using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bfocus.Internal;
using Bfocus.Tests.Support;
using Xunit;

namespace Bfocus.Tests;

public class UnitTests
{
    // ── batch_upsert em lotes de 100 ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BatchUpsert_SplitsIntoChunksOf100_AndAggregatesInOrder()
    {
        using var server = new MockServer { Handler = EchoBatch };
        using var client = Client(server);

        var articles = Enumerable.Range(1, 250).Select(i => new KbBatchArticle($"git:doc-{i:000}") { Title = $"Doc {i}" }).ToList();
        var result = await client.Kb.Articles.BatchUpsertAsync(articles);

        var requests = server.Requests;
        Assert.Equal(3, requests.Count);
        Assert.All(requests, r => Assert.Equal("/api/v1/integration/kb/articles/batch", r.Path));
        Assert.Equal(new[] { 100, 100, 50 }, requests.Select(r => BatchIds(r).Count));
        Assert.Equal(articles.Select(a => a.ExternalId), requests.SelectMany(BatchIds));

        Assert.Equal(articles.Select(a => a.ExternalId), result.Results.Select(r => r.ExternalId));
        Assert.Equal(250, result.Created);
        Assert.Equal(3, result.Updated);   // o eco devolve updated = 1 por lote: somados
        Assert.Equal(0, result.Failed);

        // Cada lote é uma chamada lógica: ids próprios.
        Assert.Equal(3, requests.Select(r => r.Header("X-Request-Id")).Distinct().Count());
        Assert.Equal(3, requests.Select(r => r.Header("Idempotency-Key")).Distinct().Count());
    }

    [Fact]
    public async Task BatchUpsert_OwnIdempotencyKey_IsDerivedPerChunk()
    {
        using var server = new MockServer { Handler = EchoBatch };
        using var client = Client(server);

        var articles = Enumerable.Range(1, 201).Select(i => new KbBatchArticle($"a{i}") { Title = "t" });
        await client.Kb.Articles.BatchUpsertAsync(articles, new RequestOptions { IdempotencyKey = "sync-42" });

        Assert.Equal(new[] { "sync-42", "sync-42:2", "sync-42:3" }, server.Requests.Select(r => r.Header("Idempotency-Key")));
    }

    [Fact]
    public async Task BatchUpsert_Empty_MakesNoRequest()
    {
        using var server = new MockServer { Handler = EchoBatch };
        using var client = Client(server);

        var result = await client.Kb.Articles.BatchUpsertAsync(Array.Empty<KbBatchArticle>());

        Assert.Empty(result.Results);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task BatchUpsert_RejectsItemsWithoutExternalIdOrWithSlash_BeforeAnyRequest()
    {
        using var server = new MockServer { Handler = EchoBatch };
        using var client = Client(server);

        await Assert.ThrowsAsync<ArgumentException>(() => client.Kb.Articles.BatchUpsertAsync(new[] { new KbBatchArticle("ok"), new KbBatchArticle() }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Kb.Articles.BatchUpsertAsync(new[] { new KbBatchArticle("guia/instalacao") }));
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task BatchUpsert_ExplicitNullProduct_MakesArticleGlobal()
    {
        using var server = new MockServer { Handler = EchoBatch };
        using var client = Client(server);

        await client.Kb.Articles.BatchUpsertAsync(new[]
        {
            new KbBatchArticle("git:global") { Title = "Global", ClearFields = { nameof(KbBatchArticle.Product) } },
            new KbBatchArticle("git:intacto") { Title = "Intacto" },
        });

        using var body = JsonDocument.Parse(server.Requests.Single().Body);
        var items = body.RootElement.GetProperty("articles").EnumerateArray().ToList();
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("product").ValueKind);
        Assert.False(items[1].TryGetProperty("product", out _));
    }

    // ── lotes de clientes e pessoas (até 500, sem divisão) ─────────────────────────────────────────────────

    [Fact]
    public async Task CustomersBatch_Over500_ThrowsArgumentException_BeforeAnyRequest()
    {
        using var server = new MockServer { Handler = EchoItems };
        using var client = Client(server);

        var items = Enumerable.Range(1, 501).Select(i => new CustomerBatchItem($"erp-{i}") { Name = $"Cliente {i}" });
        var error = Assert.Throws<ArgumentException>(() => { _ = client.Customers.BatchAsync(items); });

        Assert.Equal("items", error.ParamName);
        Assert.StartsWith("customers.batch aceita até 500 itens por chamada (recebeu 501); divida em lotes de 500.", error.Message);
        await Task.Yield();
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task PeopleBatch_Over500_ThrowsArgumentException_BeforeAnyRequest()
    {
        using var server = new MockServer { Handler = EchoItems };
        using var client = Client(server);

        var items = Enumerable.Range(1, 501).Select(i => new PersonBatchItem("erp-1042", $"app-{i}") { Name = $"Pessoa {i}" });
        var error = Assert.Throws<ArgumentException>(() => { _ = client.People.BatchAsync(items); });

        Assert.StartsWith("people.batch aceita até 500 itens por chamada (recebeu 501); divida em lotes de 500.", error.Message);
        await Task.Yield();
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task CustomersBatch_500_IsOneRequest_WithEveryItem()
    {
        using var server = new MockServer { Handler = EchoItems };
        using var client = Client(server);

        var items = Enumerable.Range(1, CustomersResource.MaxBatchSize).Select(i => new CustomerBatchItem($"erp-{i}") { Name = $"Cliente {i}" }).ToList();
        var result = await client.Customers.BatchAsync(items, new RequestOptions { IdempotencyKey = "carga-1" });

        var request = server.Requests.Single();
        Assert.Equal("/api/v1/integration/customers/batch", request.Path);
        Assert.Equal("carga-1", request.Header("Idempotency-Key"));
        using var body = JsonDocument.Parse(request.Body);
        var sent = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(500, sent.Count);
        Assert.Equal("erp-500", sent[499].GetProperty("external_id").GetString());
        Assert.Equal(500, result.Results.Count);
        Assert.Equal(500, result.Summary.Created);
    }

    [Fact]
    public async Task PeopleBatch_500_IsOneRequest_WithCustomerOutsideAndIdInsidePerson()
    {
        using var server = new MockServer { Handler = EchoItems };
        using var client = Client(server);

        var items = Enumerable.Range(1, PeopleResource.MaxBatchSize)
            .Select(i => new PersonBatchItem("erp-1042", $"app-{i}") { Name = $"Pessoa {i}", ClearFields = { nameof(PersonUpsert.Phone) } })
            .ToList();
        await client.People.BatchAsync(items);

        var request = server.Requests.Single();
        Assert.Equal("/api/v1/integration/people/batch", request.Path);
        using var body = JsonDocument.Parse(request.Body);
        var sent = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(500, sent.Count);
        Assert.Equal("erp-1042", sent[0].GetProperty("customer_external_id").GetString());
        var person = sent[0].GetProperty("person");
        Assert.Equal("app-1", person.GetProperty("external_id").GetString());
        Assert.Equal(JsonValueKind.Null, person.GetProperty("phone").ValueKind);
        Assert.False(person.TryGetProperty("customer_external_id", out _));
        Assert.False(person.TryGetProperty("email", out _));
    }

    [Fact]
    public async Task Batches_RejectItemsWithoutIds_AndEmptyMakesNoRequest()
    {
        using var server = new MockServer { Handler = EchoItems };
        using var client = Client(server);

        Assert.Throws<ArgumentException>(() => { _ = client.Customers.BatchAsync(new[] { new CustomerBatchItem() }); });
        Assert.Throws<ArgumentException>(() => { _ = client.People.BatchAsync(new[] { new PersonBatchItem { ExternalId = "app-1" } }); });
        Assert.Throws<ArgumentException>(() => { _ = client.People.BatchAsync(new[] { new PersonBatchItem { CustomerExternalId = "erp-1" } }); });
        Assert.Throws<ArgumentException>(() => new CustomerBatchItem { ClearFields = { "external_id" } });

        var empty = await client.People.BatchAsync(Array.Empty<PersonBatchItem>());
        Assert.Empty(empty.Results);
        Assert.Equal(0, empty.Summary.Created + empty.Summary.Updated + empty.Summary.Unchanged + empty.Summary.Error);
        Assert.Empty(server.Requests);
    }

    // ── identidade do widget v2 ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SignV2_WithoutInstant_UsesNow()
    {
        var signature = WidgetIdentity.SignV2("bf_whs_x", "app-77", "erp-1042");

        var match = Regex.Match(signature, "^v2\\.(\\d+)\\.([0-9a-f]{64})$");
        Assert.True(match.Success, signature);
        var ts = long.Parse(match.Groups[1].Value);
        Assert.InRange(ts, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 5, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 5);
        Assert.Equal(signature, WidgetIdentity.SignV2("bf_whs_x", "app-77", "erp-1042", DateTimeOffset.FromUnixTimeSeconds(ts)));
    }

    [Fact]
    public void SignV2_FloorsFractionalSeconds_AndAcceptsAnyOffset()
    {
        var at = new DateTimeOffset(2026, 9, 19, 9, 0, 0, 999, TimeSpan.FromHours(-3));
        var expectedTs = at.ToUnixTimeSeconds();

        Assert.StartsWith($"v2.{expectedTs}.", WidgetIdentity.SignV2("s", "u", "c", at));
        Assert.Equal(WidgetIdentity.SignV2("s", "u", "c", at), WidgetIdentity.SignV2("s", "u", "c", at.ToUniversalTime().AddMilliseconds(-999)));
    }

    [Fact]
    public void SignV2_RejectsColonInUser_EmptySecret_AndInstantBeforeEpoch()
    {
        Assert.Throws<ArgumentException>(() => WidgetIdentity.SignV2("s", "app:77", "erp-1042"));
        Assert.Throws<ArgumentException>(() => WidgetIdentity.SignV2("", "app-77", "erp-1042"));
        Assert.Throws<ArgumentException>(() => WidgetIdentity.SignV2("s", "app-77", "erp-1042", DateTimeOffset.FromUnixTimeSeconds(-1)));

        // O id do CLIENTE pode ter ':'; a v1 não muda.
        Assert.StartsWith("v2.", WidgetIdentity.SignV2("s", "app-77", "legado:1042"));
        Assert.Matches("^[0-9a-f]{64}$", WidgetIdentity.Sign("s", "app:77", "erp-1042"));
    }

    // ── ListAll percorrendo páginas ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAll_WalksEveryPage()
    {
        using var server = new MockServer();
        server.Handler = req =>
        {
            var page = int.Parse(req.Q("page")!);
            var items = page switch { 1 => new[] { "i1", "i2" }, 2 => new[] { "i3", "i4" }, _ => new[] { "i5" } };
            return MockResponse.Ok(
                items.Select(id => new { id, content = "x", is_internal = true, author_kind = "human" }),
                new { page, page_size = 2, total = 5, pages = 3 });
        };
        using var client = Client(server);

        var ids = new List<string>();
        await foreach (var interaction in client.Customers.Interactions.ListAllAsync("ERP 1042", pageSize: 2))
        {
            ids.Add(interaction.Id);
        }

        Assert.Equal(new[] { "i1", "i2", "i3", "i4", "i5" }, ids);
        Assert.Equal(new[] { "1", "2", "3" }, server.Requests.Select(r => r.Q("page")));
        Assert.All(server.Requests, r => Assert.Equal("2", r.Q("page_size")));
        Assert.All(server.Requests, r => Assert.Equal("/api/v1/integration/customers/ERP%201042/interactions", r.Path));
    }

    [Fact]
    public async Task ListAll_StopsOnEmptyPage()
    {
        using var server = new MockServer();
        server.Handler = _ => MockResponse.Ok(Array.Empty<object>(), new { page = 1, page_size = 100, total = 0, pages = 7 });
        using var client = Client(server);

        var all = new List<KbArticleSummary>();
        await foreach (var article in client.Kb.Articles.ListAllAsync(status: "published"))
        {
            all.Add(article);
        }

        Assert.Empty(all);
        Assert.Single(server.Requests);
        Assert.Equal("100", server.Requests[0].Q("page_size"));
    }

    // ── versão ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Version_MatchesCsproj()
    {
        var csproj = File.ReadAllText(Path.Combine(Repo.DotnetRoot, "src", "Bfocus", "Bfocus.csproj"));
        var match = Regex.Match(csproj, "<Version>([^<]+)</Version>");
        Assert.True(match.Success, "Bfocus.csproj sem <Version>");
        Assert.Equal(BfocusClient.Version, match.Groups[1].Value.Trim());

        // E o assembly testado (projeto ou pacote) foi construído com essa versão.
        var informational = typeof(BfocusClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.Equal(BfocusClient.Version, informational.Split('+')[0]);
    }

    [Fact]
    public async Task ClientHeader_CarriesVersion()
    {
        using var server = new MockServer { Handler = _ => MockResponse.Ok(Array.Empty<object>()) };
        using var client = Client(server);

        await client.AiAgents.ListAsync();

        Assert.Equal("bfocus-dotnet/" + BfocusClient.Version, server.Requests.Single().Header("X-Bfocus-Client"));
    }

    // ── rede ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NetworkError_WhenServerIsDown_AfterRetries()
    {
        var port = MockServer.FreePort();
        using var client = new BfocusClient("bf_live_x", new BfocusClientOptions { BaseUrl = $"http://127.0.0.1:{port}", Timeout = TimeSpan.FromSeconds(5) });
        var delays = NoSleep(client);

        var error = await Assert.ThrowsAsync<NetworkException>(() => client.Products.ListAsync());

        Assert.Equal("NETWORK_ERROR", error.Code);
        Assert.Equal(0, error.Status);
        Assert.Matches("^[0-9a-f]{32}$", error.RequestId);   // o X-Request-Id enviado (BRIEF §10.2)
        Assert.Equal(2, delays.Count);   // MaxRetries padrão
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    public async Task PathParams_EmptyOrDotSegments_AreRejectedBeforeAnyRequest(string value)
    {
        using var server = new MockServer();
        using var client = Client(server);

        Assert.ThrowsAny<ArgumentException>(() => { _ = client.Customers.GetAsync(value); });
        Assert.ThrowsAny<ArgumentException>(() => { _ = client.Customers.Contacts.DeleteAsync("ERP 1042", value); });
        Assert.ThrowsAny<ArgumentException>(() => { _ = client.Products.GetAsync(value); });
        Assert.ThrowsAny<ArgumentException>(() => { _ = client.ReleaseNotes.GetAsync("erp-cloud", value); });
        Assert.ThrowsAny<ArgumentException>(() => { _ = client.Kb.Articles.DeleteAsync(value); });
        Assert.ThrowsAny<ArgumentException>(() => { _ = client.AiAgents.GetAsync(value); });
        await Task.Yield();
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task Timeout_IsNetworkError()
    {
        using var server = new MockServer();
        server.Handler = _ =>
        {
            Thread.Sleep(1500);
            return MockResponse.Ok(Array.Empty<object>());
        };
        using var client = new BfocusClient("bf_live_x", new BfocusClientOptions { BaseUrl = server.BaseUrl, Timeout = TimeSpan.FromMilliseconds(200), MaxRetries = 0 });

        var error = await Assert.ThrowsAsync<NetworkException>(() => client.Products.ListAsync());

        Assert.Equal("NETWORK_ERROR", error.Code);
    }

    [Fact]
    public async Task CallerCancellation_IsNotANetworkError_AndIsNotRetried()
    {
        using var server = new MockServer();
        server.Handler = _ =>
        {
            Thread.Sleep(1500);
            return MockResponse.Ok(Array.Empty<object>());
        };
        using var client = Client(server);
        var delays = NoSleep(client);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.Products.ListAsync(cancellationToken: cts.Token));
        Assert.Empty(delays);
    }

    // ── cliente ────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyApiKey_ThrowsArgumentException_NotBfocusException(string key)
    {
        var error = Assert.ThrowsAny<ArgumentException>(() => new BfocusClient(key));
        Assert.IsNotAssignableFrom<BfocusException>(error);
    }

    [Fact]
    public void InjectedHttpClient_IsNotDisposed()
    {
        using var http = new HttpClient();
        new BfocusClient("bf_live_x", new BfocusClientOptions { HttpClient = http }).Dispose();
        Assert.Equal(TimeSpan.FromSeconds(100), http.Timeout);   // ainda utilizável e intocado
    }

    // ── omitido × null ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Upsert_OmitsNullProperties_AndSendsNullOnlyForClearFields()
    {
        var body = new CustomerUpsert
        {
            Name = "Padaria",
            ClearFields = { nameof(CustomerUpsert.Phone), "website" },
        }.ToJsonString();

        Assert.Equal("""{"name":"Padaria","phone":null,"website":null}""", body);
        Assert.Equal("{}", new ProductUpsert().ToJsonString());
    }

    [Fact]
    public void ClearFields_ValueWins_AndCanBeRemoved()
    {
        var request = new ContactUpsert { Email = "a@b.example", ClearFields = { "email", "IsPrimary" } };
        Assert.Equal("""{"email":"a@b.example","is_primary":null}""", request.ToJsonString());

        Assert.True(request.ClearFields.Remove(nameof(ContactUpsert.IsPrimary)));
        Assert.Equal(new[] { "email" }, request.ClearFields);
    }

    [Fact]
    public void ClearFields_RejectsUnknownAndNonNullableFields()
    {
        Assert.Throws<ArgumentException>(() => new CustomerUpsert { ClearFields = { "telefone" } });
        Assert.Throws<ArgumentException>(() => new ReleaseNoteUpsert { ClearFields = { nameof(ReleaseNoteUpsert.Publish) } });
        Assert.Throws<ArgumentException>(() => new KbBatchArticle("x") { ClearFields = { "external_id" } });
    }

    [Fact]
    public void CustomFields_OmitNullSubfields()
    {
        var body = new CustomerUpsert
        {
            CustomFields = new List<CustomFieldInput> { new("plano", "ouro"), new("limite", 1500) { Type = "number" } },
        }.ToJsonString();

        Assert.Equal("""{"custom_fields":[{"key":"plano","value":"ouro"},{"key":"limite","type":"number","value":1500}]}""", body);
    }

    // ── caminho e query ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PathSegments_ArePercentEncoded()
    {
        using var server = new MockServer { Handler = _ => MockResponse.Ok(new { deleted = true }) };
        using var client = Client(server);

        await client.Customers.Contacts.DeleteAsync("ERP/10 ç", "CT 1?x");

        Assert.Equal("/api/v1/integration/customers/ERP%2F10%20%C3%A7/contacts/CT%201%3Fx", server.Requests.Single().Path);
    }

    [Fact]
    public async Task KbExternalIdWithSlash_IsRejected_BeforeAnyRequest()
    {
        using var server = new MockServer();
        using var client = Client(server);

        // Validação síncrona: estoura na chamada, antes de existir Task (e requisição).
        Assert.Throws<ArgumentException>(() => { _ = client.Kb.Articles.GetAsync("guia/instalacao"); });
        Assert.Throws<ArgumentException>(() => { _ = client.Customers.GetAsync(""); });
        await Task.Yield();
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task Dates_AreSentInUtcWithZ()
    {
        using var server = new MockServer { Handler = _ => MockResponse.Ok(Array.Empty<object>(), new { page = 1, page_size = 50, total = 0, pages = 0 }) };
        using var client = Client(server);

        await client.Customers.ListAsync(updatedSince: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(-3)));

        Assert.Equal("2026-09-01T03:00:00Z", server.Requests.Single().Q("updated_since"));
        Assert.Equal("2026-09-01T03:00:00.25Z", Query.FormatDate(new DateTimeOffset(2026, 9, 1, 0, 0, 0, 250, TimeSpan.FromHours(-3))));
    }

    [Fact]
    public async Task Booleans_AreLowercase_AndUnsetParamsAreOmitted()
    {
        using var server = new MockServer { Handler = _ => MockResponse.Ok(Array.Empty<object>()) };
        using var client = Client(server);

        await client.Products.ListAsync(includeInactive: false);
        await client.Products.ListAsync();

        Assert.Equal("false", server.Requests[0].Q("include_inactive"));
        Assert.Empty(server.Requests[1].Query);
    }

    // ── novas tentativas e idempotência ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RetryAfter_IsCappedAt60Seconds()
    {
        var calls = 0;
        using var server = new MockServer();
        server.Handler = _ => ++calls == 1
            ? MockResponse.Json(429, new { code = 429, error = "RATE_LIMITED", message = "RATE_LIMITED" }, new Dictionary<string, string> { ["Retry-After"] = "120" })
            : MockResponse.Ok(Array.Empty<object>());
        using var client = Client(server);
        var delays = NoSleep(client);

        await client.AiAgents.ListAsync();

        Assert.Equal(new[] { TimeSpan.FromSeconds(60) }, delays);
    }

    [Fact]
    public async Task MaxRetriesZero_DoesNotRetry()
    {
        using var server = new MockServer { Handler = _ => new MockResponse(503, "Service Unavailable", ContentType: "text/plain") };
        using var client = new BfocusClient("bf_live_x", new BfocusClientOptions { BaseUrl = server.BaseUrl, MaxRetries = 0 });

        var error = await Assert.ThrowsAsync<ServerException>(() => client.Products.ListAsync());

        Assert.Equal("HTTP_503", error.Code);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task OwnIdempotencyKey_IsSent_AndReadsHaveNone()
    {
        using var server = new MockServer
        {
            Handler = req => req.Method == "GET" ? MockResponse.Ok(Array.Empty<object>()) : MockResponse.Ok(new { deleted = true }),
        };
        using var client = Client(server);

        await client.Customers.DeleteAsync("ERP 1042", new RequestOptions { IdempotencyKey = "minha-chave" });
        await client.Customers.Contacts.ListAsync("ERP 1042");

        Assert.Equal("minha-chave", server.Requests[0].Header("Idempotency-Key"));
        Assert.Null(server.Requests[1].Header("Idempotency-Key"));
    }

    // ── erros ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ErrorWithoutErrorField_UsesMessageAsCode_AndUnmappedStatusIsBaseType()
    {
        using var server = new MockServer { Handler = _ => MockResponse.Json(400, new { code = 400, data = (object?)null, message = "ALGO_ERRADO" }) };
        using var client = Client(server);

        var error = await Assert.ThrowsAsync<BfocusException>(() => client.Products.GetAsync("erp-cloud"));

        Assert.Equal(typeof(BfocusException), error.GetType());
        Assert.Equal("ALGO_ERRADO", error.Code);
        Assert.Equal(400, error.Status);
        Assert.Empty(error.Validation);
    }

    [Fact]
    public async Task UnknownResponseFields_AreIgnored()
    {
        using var server = new MockServer
        {
            Handler = _ => MockResponse.Ok(new { id = "p1", slug = "erp", name = "ERP", is_active = true, novo_campo = new { x = 1 }, sort_order = 0, current_version = "1.0.0", ai_level = "off" }),
        };
        using var client = Client(server);

        var product = await client.Products.GetAsync("erp");

        Assert.Equal("erp", product.Slug);
    }

    // ── auxiliares ─────────────────────────────────────────────────────────────────────────────────────

    private static BfocusClient Client(MockServer server)
    {
        var client = new BfocusClient("bf_live_x", new BfocusClientOptions { BaseUrl = server.BaseUrl });
        NoSleep(client);
        return client;
    }

    private static List<TimeSpan> NoSleep(BfocusClient client)
    {
        var delays = new List<TimeSpan>();
        client.Http.Delay = (delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        };
        return delays;
    }

    private static List<string?> BatchIds(RecordedRequest request)
    {
        using var doc = JsonDocument.Parse(request.Body);
        return doc.RootElement.GetProperty("articles").EnumerateArray().Select(a => a.GetProperty("external_id").GetString()).ToList();
    }

    private static MockResponse EchoItems(RecordedRequest request)
    {
        using var doc = JsonDocument.Parse(request.Body);
        var count = doc.RootElement.GetProperty("items").GetArrayLength();
        return MockResponse.Ok(new
        {
            results = Enumerable.Range(0, count).Select(i => new { index = i, status = "created", external_id = (string?)null, merged_into = (string?)null, error = (string?)null, code = (int?)null }),
            summary = new { created = count, updated = 0, unchanged = 0, error = 0 },
        });
    }

    private static MockResponse EchoBatch(RecordedRequest request)
    {
        var ids = BatchIds(request);
        return MockResponse.Ok(new
        {
            results = ids.Select(id => new { external_id = id, ok = true, action = "created", error = (string?)null, article = (object?)null }),
            created = ids.Count,
            updated = 1,
            unchanged = 0,
            failed = 0,
        });
    }
}
