using System.Globalization;
using System.Text.Json;
using Bfocus.Tests.Support;
using Xunit;

namespace Bfocus.Tests;

/// <summary>Roda <c>clients/conformance/cases.json</c> contra um servidor HTTP local (BRIEF §7).</summary>
public class ConformanceTests
{
    private static readonly Dictionary<string, Type> ErrorTypes = new()
    {
        ["authentication"] = typeof(AuthenticationException),
        ["permission_denied"] = typeof(PermissionDeniedException),
        ["not_found"] = typeof(NotFoundException),
        ["conflict"] = typeof(ConflictException),
        ["validation"] = typeof(ValidationException),
        ["rate_limit"] = typeof(RateLimitException),
        ["server"] = typeof(ServerException),
        ["network"] = typeof(NetworkException),
        ["api"] = typeof(BfocusException),
    };

    public static IEnumerable<object[]> CaseIds() =>
        Conformance.Cases.Select(c => new object[] { c.GetProperty("id").GetString()! });

    public static IEnumerable<object[]> Signatures() =>
        Conformance.Root.GetProperty("signatures").EnumerateArray().Select(s => new object[]
        {
            s.GetProperty("secret").GetString()!,
            s.GetProperty("user_external_id").GetString()!,
            s.GetProperty("customer_external_id").GetString()!,
            s.GetProperty("expected").GetString()!,
        });

    public static IEnumerable<object[]> SignaturesV2() =>
        Conformance.Root.GetProperty("signatures_v2").EnumerateArray().Select(s => new object[]
        {
            s.GetProperty("secret").GetString()!,
            s.GetProperty("user_external_id").GetString()!,
            s.GetProperty("customer_external_id").GetString()!,
            s.GetProperty("timestamp").GetInt64(),
            s.GetProperty("expected").GetString()!,
        });

    [Theory]
    [MemberData(nameof(CaseIds))]
    public async Task Case(string id)
    {
        var @case = Conformance.Case(id);
        var op = @case.GetProperty("op").GetString()!;
        if (Conformance.ExcludedOps.Contains(op))
        {
            return;
        }

        Assert.True(OpTable.Ops.TryGetValue(op, out var call), $"A op '{op}' (caso '{id}') não tem método na SDK .NET — implemente e registre em OpTable.");

        var exchanges = @case.GetProperty("exchanges").EnumerateArray().ToArray();
        using var server = new MockServer();
        var script = new ExchangeScript(exchanges, Conformance.ApiKey);
        server.Handler = script.Handle;

        using var client = new BfocusClient(Conformance.ApiKey, new BfocusClientOptions { BaseUrl = server.BaseUrl });
        var delays = new List<TimeSpan>();
        client.Http.Delay = (delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        };

        var args = new Args(@case.GetProperty("args"));
        object? result = null;
        BfocusException? error = null;
        try
        {
            result = await call!(client, args);
        }
        catch (BfocusException e)
        {
            error = e;
        }

        Assert.True(!args.Unused.Any(), $"args não usados pela tabela op → chamada: {string.Join(", ", args.Unused)}");
        Assert.True(script.Failures.Count == 0, "Trocas divergentes:\n" + string.Join("\n", script.Failures));
        Assert.True(script.Received == exchanges.Length, $"A chamada fez {script.Received} requisição(ões); o caso tem {exchanges.Length}.");
        CheckRetryDelays(exchanges, delays);

        var expect = @case.GetProperty("expect");
        if (expect.TryGetProperty("result", out var expected))
        {
            Assert.True(error is null, $"Esperava resultado, veio erro: {error}");
            var actual = JsonSerializer.SerializeToElement(result, result?.GetType() ?? typeof(object));
            var diffs = JsonCompare.Diff(expected, actual, subset: true);
            Assert.True(diffs.Count == 0, "Resultado diverge de expect.result:\n" + string.Join("\n", diffs));
            return;
        }

        var expectedError = expect.GetProperty("error");
        Assert.True(error is not null, $"Esperava erro {expectedError.GetRawText()}, veio resultado.");
        Assert.Equal(ErrorTypes[expectedError.GetProperty("type").GetString()!], error!.GetType());
        Assert.Equal(expectedError.GetProperty("code").GetString(), error.Code);
        Assert.Equal(expectedError.GetProperty("status").GetInt32(), error.Status);
        if (expectedError.TryGetProperty("request_id", out var requestId))
        {
            // "$sent" = o X-Request-Id que a SDK enviou (BRIEF §7/§10.2).
            var expectedRequestId = requestId.GetString() == "$sent" ? script.LastRequestId : requestId.GetString();
            Assert.False(string.IsNullOrEmpty(expectedRequestId), "sem X-Request-Id para comparar");
            Assert.Equal(expectedRequestId, error.RequestId);
        }

        if (expectedError.TryGetProperty("retry_after", out var retryAfter))
        {
            Assert.Equal(TimeSpan.FromSeconds(retryAfter.GetDouble()), error.RetryAfter);
        }

        if (expectedError.TryGetProperty("required_scope", out var scope))
        {
            Assert.Equal(scope.GetString(), error.RequiredScope);
        }

        if (expectedError.TryGetProperty("validation", out var validation))
        {
            var expectedValidation = validation.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
            Assert.Equal(expectedValidation.OrderBy(p => p.Key), error.Validation.OrderBy(p => p.Key));
        }

        Assert.Contains(error.Code, error.Message);
    }

    [Theory]
    [MemberData(nameof(Signatures))]
    public void Signature(string secret, string userExternalId, string customerExternalId, string expected)
    {
        Assert.Equal(expected, WidgetIdentity.Sign(secret, userExternalId, customerExternalId));
    }

    [Theory]
    [MemberData(nameof(SignaturesV2))]
    public void SignatureV2(string secret, string userExternalId, string customerExternalId, long timestamp, string expected)
    {
        Assert.Equal(expected, WidgetIdentity.SignV2(secret, userExternalId, customerExternalId, DateTimeOffset.FromUnixTimeSeconds(timestamp)));
    }

    [Fact]
    public void EveryCaseOpHasAnSdkCall()
    {
        var excluded = Conformance.ExcludedOps;
        var missing = Conformance.Cases.Select(c => c.GetProperty("op").GetString()!)
            .Where(op => !excluded.Contains(op) && !OpTable.Ops.ContainsKey(op))
            .Distinct()
            .ToList();
        Assert.True(missing.Count == 0, "ops sem método na SDK .NET: " + string.Join(", ", missing));
    }

    [Fact]
    public void EverySpecOperationHasAnSdkCall()
    {
        // Só no monorepo (o espelho público não tem a spec): toda operação pública precisa de método ou exclusão.
        var spec = Path.Combine(Repo.DotnetRoot, "..", "..", "api", "openapi", "public.json");
        if (!File.Exists(spec))
        {
            return;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(spec));
        var excluded = Conformance.ExcludedOps;
        var missing = doc.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Where(op => op.Value.ValueKind == JsonValueKind.Object && op.Value.TryGetProperty("operationId", out _))
            .Select(op => op.Value.GetProperty("operationId").GetString()!)
            .Where(op => !excluded.Contains(op) && !OpTable.Ops.ContainsKey(op))
            .ToList();
        Assert.True(missing.Count == 0, "operações de public.json sem método na SDK .NET: " + string.Join(", ", missing));
    }

    [Fact]
    public void VendoredCasesMatchMonorepo()
    {
        // No monorepo, a cópia em fixtures/ precisa ser idêntica à fonte (no espelho público a fonte não existe).
        var source = Path.Combine(Repo.DotnetRoot, "..", "conformance", "cases.json");
        if (!File.Exists(source))
        {
            return;
        }

        var vendored = Path.Combine(Repo.DotnetRoot, "tests", "Bfocus.Tests", "fixtures", "cases.json");
        Assert.True(
            File.ReadAllText(source) == File.ReadAllText(vendored),
            "tests/Bfocus.Tests/fixtures/cases.json desatualizado — rode: python3 clients/conformance/generate.py (ele escreve as cópias de todas as SDKs)");
    }

    /// <summary>
    /// Uma espera por nova tentativa: <c>Retry-After</c> quando houver (teto 60 s); senão
    /// <c>min(8, 0.5 × 2^tentativa)</c> + até 25% de jitter. Nenhuma espera real acontece (Delay substituído).
    /// </summary>
    private static void CheckRetryDelays(JsonElement[] exchanges, List<TimeSpan> delays)
    {
        var expected = new List<(double? RetryAfter, int Attempt)>();
        var attempt = 0;
        for (var i = 1; i < exchanges.Length; i++)
        {
            if (!ExchangeScript.IsRetry(exchanges[i]))
            {
                attempt = 0;
                continue;
            }

            double? retryAfter = null;
            foreach (var header in exchanges[i - 1].GetProperty("response").GetProperty("headers").EnumerateObject())
            {
                if (header.Name.Equals("Retry-After", StringComparison.OrdinalIgnoreCase))
                {
                    retryAfter = double.Parse(header.Value.GetString()!, CultureInfo.InvariantCulture);
                }
            }

            expected.Add((retryAfter, attempt++));
        }

        Assert.Equal(expected.Count, delays.Count);
        for (var k = 0; k < expected.Count; k++)
        {
            if (expected[k].RetryAfter is { } seconds)
            {
                Assert.Equal(TimeSpan.FromSeconds(Math.Min(seconds, 60)), delays[k]);
            }
            else
            {
                var baseSeconds = Math.Min(8, 0.5 * Math.Pow(2, expected[k].Attempt));
                Assert.InRange(delays[k].TotalSeconds, baseSeconds, baseSeconds * 1.25);
            }
        }
    }
}
