using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Bfocus.Tests.Support;

/// <summary>Os casos de <c>clients/conformance/cases.json</c> (cópia em <c>fixtures/</c>).</summary>
internal static class Conformance
{
    private static readonly Lazy<JsonDocument> Document = new(() =>
        JsonDocument.Parse(File.ReadAllText(FixturePath)));

    public static string FixturePath => Path.Combine(AppContext.BaseDirectory, "fixtures", "cases.json");

    public static JsonElement Root => Document.Value.RootElement;

    public static string ApiKey => Root.GetProperty("api_key").GetString()!;

    public static IEnumerable<JsonElement> Cases => Root.GetProperty("cases").EnumerateArray();

    public static HashSet<string> ExcludedOps =>
        Root.GetProperty("sdk_excluded_ops").EnumerateArray().Select(e => e.GetString()!).ToHashSet();

    public static JsonElement Case(string id) => Cases.Single(c => c.GetProperty("id").GetString() == id);
}

/// <summary>Raízes do repositório (funciona no monorepo e no espelho público).</summary>
internal static class Repo
{
    /// <summary>A pasta <c>clients/dotnet</c> (a que tem o <c>Bfocus.sln</c>).</summary>
    public static string DotnetRoot { get; } = FindDotnetRoot();

    private static string FindDotnetRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Bfocus.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Não achei o Bfocus.sln acima de " + AppContext.BaseDirectory);
    }
}

/// <summary>
/// Os <c>args</c> neutros (snake_case) de um caso, lidos pela tabela op → chamada. Registra o que foi lido: arg que a
/// tabela não consumiu quebra o teste (parâmetro novo no caso sem suporte na SDK).
/// </summary>
internal sealed class Args
{
    private static readonly JsonSerializerOptions Strict = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private readonly JsonElement _args;
    private readonly HashSet<string> _used = new();

    public Args(JsonElement args) => _args = args;

    public IEnumerable<string> Unused => _args.EnumerateObject().Select(p => p.Name).Where(n => !_used.Contains(n));

    public string Str(string name) =>
        TryGet(name, out var v) ? v.GetString()! : throw new InvalidOperationException($"arg obrigatório ausente: {name}");

    public string? OptStr(string name) => TryGet(name, out var v) ? v.GetString() : null;

    public int? OptInt(string name) => TryGet(name, out var v) ? v.GetInt32() : null;

    public bool? OptBool(string name) => TryGet(name, out var v) ? v.GetBoolean() : null;

    public DateTimeOffset? OptDate(string name) =>
        TryGet(name, out var v) ? DateTimeOffset.Parse(v.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal) : null;

    public JsonElement? Raw(string name) => TryGet(name, out var v) ? v : null;

    /// <summary>Monta o corpo de upsert com os args restantes: valor → propriedade; <c>null</c> → <c>ClearFields</c>.</summary>
    public T Patch<T>(params string[] exclude)
        where T : PatchRequest
    {
        var fields = new JsonObject();
        foreach (var property in _args.EnumerateObject())
        {
            if (exclude.Contains(property.Name))
            {
                continue;
            }

            _used.Add(property.Name);
            fields[property.Name] = JsonNode.Parse(property.Value.GetRawText());
        }

        return BuildPatch<T>(fields);
    }

    public static T BuildPatch<T>(JsonObject fields)
        where T : PatchRequest
    {
        var nulls = fields.Where(kv => kv.Value is null).Select(kv => kv.Key).ToList();
        foreach (var name in nulls)
        {
            fields.Remove(name);
        }

        var request = fields.Deserialize<T>(Strict)!;
        foreach (var name in nulls)
        {
            request.ClearFields.Add(name);
        }

        return request;
    }

    private bool TryGet(string name, out JsonElement value)
    {
        _used.Add(name);
        return _args.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null;
    }
}

/// <summary>
/// Confere cada troca de um caso como o BRIEF §7 manda e responde com o que o caso diz.
/// </summary>
internal sealed class ExchangeScript
{
    private static readonly Regex ClientHeader = new("^bfocus-dotnet/" + Regex.Escape(BfocusClient.Version) + "$");
    private static readonly Regex RequestId = new("^[0-9a-f]{32}$");
    private readonly JsonElement[] _exchanges;
    private readonly string _apiKey;
    private RecordedRequest? _previous;

    public ExchangeScript(JsonElement[] exchanges, string apiKey)
    {
        _exchanges = exchanges;
        _apiKey = apiKey;
    }

    public List<string> Failures { get; } = new();

    public int Received { get; private set; }

    /// <summary>O <c>X-Request-Id</c> da última requisição recebida (o <c>"$sent"</c> dos casos).</summary>
    public string? LastRequestId => _previous?.Header("X-Request-Id");

    /// <summary>
    /// <c>"retry": true</c> = nova tentativa da troca anterior (mesmos X-Request-Id e Idempotency-Key);
    /// <c>false</c> = chamada lógica nova (ids novos).
    /// </summary>
    public static bool IsRetry(JsonElement exchange) => exchange.GetProperty("retry").GetBoolean();

    public MockResponse Handle(RecordedRequest actual)
    {
        var i = Received++;
        if (i >= _exchanges.Length)
        {
            Failures.Add($"requisição a mais (#{i + 1}): {actual.Method} {actual.Path}");
            return new MockResponse(500, "{}");
        }

        var tag = $"troca #{i + 1}";
        var expected = _exchanges[i].GetProperty("request");
        var method = expected.GetProperty("method").GetString()!;

        Check(actual.Method == method, $"{tag}: método {actual.Method}, esperado {method}");
        var path = expected.GetProperty("path").GetString()!;
        Check(actual.Path == path, $"{tag}: caminho {actual.Path}, esperado {path}");

        var expectedQuery = expected.GetProperty("query").EnumerateObject()
            .Select(p => $"{p.Name}={p.Value.GetString()}").OrderBy(s => s, StringComparer.Ordinal).ToList();
        var actualQuery = actual.Query.Select(p => $"{p.Key}={p.Value}").OrderBy(s => s, StringComparer.Ordinal).ToList();
        Check(expectedQuery.SequenceEqual(actualQuery), $"{tag}: query [{string.Join("&", actualQuery)}], esperado [{string.Join("&", expectedQuery)}]");

        var body = expected.GetProperty("body");
        if (body.ValueKind == JsonValueKind.Null)
        {
            Check(actual.Body.Length == 0, $"{tag}: esperava requisição sem corpo, veio {actual.Body}");
        }
        else
        {
            try
            {
                using var doc = JsonDocument.Parse(actual.Body);
                foreach (var diff in JsonCompare.Diff(body, doc.RootElement, subset: false))
                {
                    Failures.Add($"{tag}: corpo {diff}");
                }
            }
            catch (JsonException)
            {
                Failures.Add($"{tag}: corpo não é JSON: '{actual.Body}'");
            }

            Check(actual.Header("Content-Type")?.StartsWith("application/json", StringComparison.Ordinal) == true, $"{tag}: Content-Type {actual.Header("Content-Type")}");
        }

        Check(actual.Header("Authorization") == "Bearer " + _apiKey, $"{tag}: Authorization {actual.Header("Authorization")}");
        Check(ClientHeader.IsMatch(actual.Header("X-Bfocus-Client") ?? string.Empty), $"{tag}: X-Bfocus-Client {actual.Header("X-Bfocus-Client")}");
        Check(actual.Header("User-Agent") == actual.Header("X-Bfocus-Client"), $"{tag}: User-Agent {actual.Header("User-Agent")}");
        Check(actual.Header("Accept")?.Contains("application/json") == true, $"{tag}: Accept {actual.Header("Accept")}");
        Check(RequestId.IsMatch(actual.Header("X-Request-Id") ?? string.Empty), $"{tag}: X-Request-Id {actual.Header("X-Request-Id")}");

        var isWrite = method is "POST" or "PUT" or "PATCH" or "DELETE";
        if (isWrite)
        {
            Check(!string.IsNullOrEmpty(actual.Header("Idempotency-Key")), $"{tag}: escrita sem Idempotency-Key");
        }
        else
        {
            Check(actual.Header("Idempotency-Key") is null, $"{tag}: leitura com Idempotency-Key");
        }

        var retry = IsRetry(_exchanges[i]);
        if (_previous is null)
        {
            Check(!retry, $"{tag}: a primeira troca de uma chamada não pode ser retry");
        }
        else if (retry)
        {
            Check(actual.Header("X-Request-Id") == _previous.Header("X-Request-Id"), $"{tag}: nova tentativa com outro X-Request-Id");
            Check(actual.Header("Idempotency-Key") == _previous.Header("Idempotency-Key"), $"{tag}: nova tentativa com outra Idempotency-Key");
        }
        else
        {
            Check(actual.Header("X-Request-Id") != _previous.Header("X-Request-Id"), $"{tag}: chamada nova reutilizou o X-Request-Id");
            if (isWrite && _previous.Header("Idempotency-Key") is not null)
            {
                Check(actual.Header("Idempotency-Key") != _previous.Header("Idempotency-Key"), $"{tag}: chamada nova reutilizou a Idempotency-Key");
            }
        }

        _previous = actual;

        var response = _exchanges[i].GetProperty("response");
        var headers = response.GetProperty("headers").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
        var responseBody = response.GetProperty("body");
        return responseBody.ValueKind == JsonValueKind.String
            ? new MockResponse(response.GetProperty("status").GetInt32(), responseBody.GetString()!, headers, "text/plain; charset=utf-8")
            : new MockResponse(response.GetProperty("status").GetInt32(), responseBody.GetRawText(), headers);
    }

    private void Check(bool ok, string failure)
    {
        if (!ok)
        {
            Failures.Add(failure);
        }
    }
}
