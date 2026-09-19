# bFocus — SDK oficial .NET

SDK oficial da [API pública do bFocus](https://api.bfocus.com.br) para .NET: clientes, contatos, produtos,
release notes, base de conhecimento e agentes de IA — com novas tentativas idempotentes, erros tipados e
assinatura de identidade do widget. Alvos `netstandard2.0` (.NET Framework 4.6.2+, .NET 6/7…) e `net8.0`.

```bash
dotnet add package Bfocus --version 0.1.0
```

```csharp
using Bfocus;

using var bfocus = new BfocusClient(Environment.GetEnvironmentVariable("BFOCUS_API_KEY")!);

var cliente = await bfocus.Customers.UpsertAsync("ERP 1042", new CustomerUpsert
{
    Name = "Padaria Estrela",
    Email = "contato@padaria.example",
});

Console.WriteLine($"{cliente.Name} → {cliente.Id}");
```

`UpsertAsync` cria o cliente se o `external_id` (o id dele no **seu** sistema) ainda não existe e atualiza
se já existe — rode quantas vezes quiser.

---

## Autenticação

Crie a chave em **Integrações → Chaves de API** no bFocus, só com os escopos que a integração precisa. Ela
vai em `Authorization: Bearer <chave>` em toda requisição — a SDK cuida disso.

| Escopo | Libera |
| --- | --- |
| `customers:read` / `customers:write` | Clientes, contatos, produtos vinculados e interações |
| `products:read` / `products:write` | Catálogo de produtos |
| `release_notes:read` / `release_notes:write` | Release notes |
| `kb:read` / `kb:write` | Base de conhecimento |
| `ai_agents:read` / `ai_agents:preview` | Agentes de IA (o preview consome IA da conta) |

A chave legada (`bf_sk_…`) só alcança clientes. Faltou escopo → `PermissionDeniedException` com
`RequiredScope` dizendo qual.

```csharp
var bfocus = new BfocusClient(apiKey, new BfocusClientOptions
{
    BaseUrl = "https://api.bfocus.com.br",   // padrão; em dev: http://localhost:8000
    Timeout = TimeSpan.FromSeconds(30),      // por tentativa
    MaxRetries = 2,                          // novas tentativas além da primeira; 0 desliga
});
```

Crie **um** `BfocusClient` por chave e reutilize (é thread-safe e mantém o pool de conexões). O construtor
não faz chamada de rede; chave vazia lança `ArgumentException` na hora. Com injeção de dependência, passe o
`HttpClient` do `IHttpClientFactory` — a SDK não o descarta:

```csharp
builder.Services.AddHttpClient("bfocus");
builder.Services.AddSingleton(sp => new BfocusClient(
    builder.Configuration["Bfocus:ApiKey"]!,
    new BfocusClientOptions { HttpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("bfocus") }));
```

## "Só o que veio muda": omitido × `null`

Todo upsert é **parcial**: a API só altera os campos presentes no corpo, e `null` explícito **limpa** o campo.
Na SDK:

- propriedade `null` (o padrão) = **omitida** — o campo não vai no corpo e fica como está;
- para **limpar**, ponha o campo em `ClearFields` (pelo `nameof` ou pelo nome JSON) — ele vai como `null`.

```csharp
// Muda o nome, limpa telefone e site; e-mail, documento e o resto ficam intactos.
await bfocus.Customers.UpsertAsync("ERP 1042", new CustomerUpsert
{
    Name = "Padaria Estrela Ltda.",
    ClearFields = { nameof(CustomerUpsert.Phone), "website" },
});
// Corpo enviado: {"name":"Padaria Estrela Ltda.","phone":null,"website":null}
```

Nome desconhecido em `ClearFields` lança `ArgumentException` na hora. Se o campo tem valor **e** está em
`ClearFields`, vale o valor.

## Recursos

Todos os métodos são assíncronos, terminam em `Async` e aceitam por último `RequestOptions? options`
(`IdempotencyKey`, `Timeout`) e `CancellationToken cancellationToken`.

### Clientes — `bfocus.Customers`

```csharp
var cliente = await bfocus.Customers.GetAsync("ERP 1042");

var pagina = await bfocus.Customers.ListAsync(q: "padaria", page: 1, pageSize: 50);
Console.WriteLine($"{pagina.Total} clientes em {pagina.Pages} páginas");

// Todos, página a página (sincronização incremental com updatedSince):
await foreach (var c in bfocus.Customers.ListAllAsync(updatedSince: ultimaSincronizacao))
{
    Console.WriteLine(c.ExternalId);
}

// Campos personalizados: a lista enviada SUBSTITUI a atual.
await bfocus.Customers.UpsertAsync("ERP 1042", new CustomerUpsert
{
    CustomFields = new List<CustomFieldInput> { new("plano", "ouro") { Label = "Plano" } },
});

// Contatos
await bfocus.Customers.Contacts.UpsertAsync("ERP 1042", "CT-1", new ContactUpsert
{
    Name = "Ana Souza", Role = "Financeiro", Email = "ana@padaria.example", IsPrimary = true,
});
var contatos = await bfocus.Customers.Contacts.ListAsync("ERP 1042");
await bfocus.Customers.Contacts.DeleteAsync("ERP 1042", "CT-1");

// Produtos vinculados
await bfocus.Customers.Products.AttachAsync("ERP 1042", "erp-cloud");
var produtos = await bfocus.Customers.Products.ListAsync("ERP 1042");
await bfocus.Customers.Products.DetachAsync("ERP 1042", "erp-cloud");

// Histórico de interações
await bfocus.Customers.Interactions.CreateAsync("ERP 1042", "Pedido 1042 faturado.", authorEmail: "carla@suaempresa.com");
await foreach (var i in bfocus.Customers.Interactions.ListAllAsync("ERP 1042")) { /* … */ }

await bfocus.Customers.DeleteAsync("ERP 1042");
```

### Produtos — `bfocus.Products`

```csharp
await bfocus.Products.UpsertAsync("erp-cloud", new ProductUpsert { Name = "ERP Cloud", Color = "#6366F1" });
var produtos = await bfocus.Products.ListAsync(includeInactive: true);
var produto = await bfocus.Products.GetAsync("erp-cloud");
await bfocus.Products.ArchiveAsync("erp-cloud");   // arquiva (não apaga)
```

### Release notes — publicar direto do CI

Com `Publish = true`, o upsert grava e publica na mesma chamada. Rodar de novo com a mesma versão atualiza a
nota (o pipeline pode ser reexecutado sem medo):

```csharp
// dotnet run -- v2.3.0   (no GitHub Actions: GITHUB_REF_NAME)
var versao = (Environment.GetEnvironmentVariable("GITHUB_REF_NAME") ?? args[0]).TrimStart('v');

var nota = await bfocus.ReleaseNotes.UpsertAsync("erp-cloud", versao, new ReleaseNoteUpsert
{
    Title = $"Versão {versao}",
    DescriptionMarkdown = File.ReadAllText($"release-notes/{versao}.md"),
    Audience = "external",          // internal | external | both
    Publish = true,
});
Console.WriteLine($"Publicada em {nota.PublishedAt}");
```

```csharp
var publicadas = await bfocus.ReleaseNotes.ListAsync("erp-cloud", published: true);
var nota = await bfocus.ReleaseNotes.GetAsync("erp-cloud", "2.3.0");
await bfocus.ReleaseNotes.PublishAsync("erp-cloud", "2.4.0");
```

### Base de conhecimento — sincronizar a partir de arquivos Markdown

`BatchUpsertAsync` aceita **qualquer quantidade** de artigos: a SDK divide em lotes de 100 (limite da API),
envia em sequência e devolve um resultado único (resultados na ordem, contadores somados). Lista vazia devolve
o resultado zerado sem requisição. Com `IdempotencyKey` própria, o 1º lote usa a chave como veio e os seguintes
`<chave>:2`, `<chave>:3`… (sem ela, cada lote gera a sua). O `external_id` não aceita `/` — use `:` para
hierarquia:

```csharp
var pasta = Path.GetFullPath("docs");
var artigos = Directory.EnumerateFiles(pasta, "*.md", SearchOption.AllDirectories)
    .Select(arquivo =>
    {
        var markdown = File.ReadAllText(arquivo);
        var relativo = Path.ChangeExtension(Path.GetRelativePath(pasta, arquivo), null);
        var titulo = markdown.Split('\n').FirstOrDefault(l => l.StartsWith("# "))?[2..].Trim()
                     ?? Path.GetFileNameWithoutExtension(arquivo);
        return new KbBatchArticle("git:" + relativo.Replace(Path.DirectorySeparatorChar, ':'))
        {
            Title = titulo,
            BodyMarkdown = markdown,
            Product = "erp-cloud",   // ou ClearFields = { "product" } para um artigo global
        };
    })
    .ToList();

var resultado = await bfocus.Kb.Articles.BatchUpsertAsync(artigos);
Console.WriteLine($"{resultado.Created} criados, {resultado.Updated} alterados, " +
                  $"{resultado.Unchanged} sem mudança, {resultado.Failed} com erro");

foreach (var r in resultado.Results)
{
    if (!r.Ok)
    {
        Console.Error.WriteLine($"{r.ExternalId}: {r.Error}");   // ex.: KB_ARTICLE_TITLE_REQUIRED
    }
    else if (r.Article?.Status != "published")
    {
        await bfocus.Kb.Articles.PublishAsync(r.ExternalId);
    }
}
```

```csharp
await bfocus.Kb.Articles.UpsertAsync("notion:emitir-nfse", new KbArticleUpsert
{
    Title = "Como emitir NFS-e", BodyMarkdown = "# Passo a passo\n\n1. Abra o menu **Fiscal**", Status = "published",
});
var artigo = await bfocus.Kb.Articles.GetAsync("notion:emitir-nfse");
await foreach (var a in bfocus.Kb.Articles.ListAllAsync(product: "erp-cloud", status: "published")) { /* … */ }
await bfocus.Kb.Articles.UnpublishAsync("notion:emitir-nfse");
await bfocus.Kb.Articles.DeleteAsync("notion:emitir-nfse");

var achados = await bfocus.Kb.SearchAsync("emitir nota fiscal", product: "erp-cloud", limit: 3);
```

### Agentes de IA — `bfocus.AiAgents`

```csharp
var agentes = await bfocus.AiAgents.ListAsync();
var resposta = await bfocus.AiAgents.PreviewAsync(agentes[0].Id, "Como emito uma NFS-e?", history: new[]
{
    AiAgentPreviewTurn.Customer("Oi"),
    AiAgentPreviewTurn.Bot("Olá! Como posso ajudar?"),
});
Console.WriteLine($"{resposta.Action}: {resposta.AnswerHtml} (confiança {resposta.Confidence}, assunto {resposta.Topic})");
// Diagnóstico tipado: Escalated, Refused, HandoffReason, Guards, Citations, Sources, Collected, Missing
```

### Paginação

`ListAsync` devolve uma `Page<T>` (`Items`, `PageNumber`, `PageSize`, `Total`, `Pages`, `HasNextPage`) —
`PageNumber` porque em C# um membro não pode ter o nome da classe; no JSON continua `page`. `ListAllAsync`
(clientes, interações, release notes e artigos) devolve `IAsyncEnumerable<T>` e busca as páginas sob demanda
(`await foreach`; aceita `.WithCancellation(token)`), com `pageSize` padrão 100; para na última página ou numa
página vazia. Cada página é uma chamada nova (com `X-Request-Id` próprio).

As listagens de artigos (e o resultado do lote) trazem `KbArticleSummary`, sem corpo; `GetAsync`, `UpsertAsync`,
`PublishAsync` e `UnpublishAsync` devolvem `KbArticle`, que acrescenta `BodyHtml`.

## Erros

Qualquer status fora de 2xx vira uma `BfocusException` (namespace `Bfocus`):

| Exceção | Quando |
| --- | --- |
| `AuthenticationException` | 401 — chave ausente, inválida ou revogada |
| `PermissionDeniedException` | 403 — chave desligada, IP não liberado, escopo faltando (`RequiredScope`) |
| `NotFoundException` | 404 |
| `ConflictException` | 409 (ex.: `RELEASE_NOTE_CONFLICT`, `KB_ARTICLE_EMPTY`, `AI_DISABLED`) |
| `ValidationException` | 422 — detalhe por campo em `Validation` |
| `RateLimitException` | 429 — espera sugerida em `RetryAfter` |
| `ServerException` | 5xx |
| `NetworkException` | falha de conexão ou tempo esgotado (`Status = 0`, `Code = "NETWORK_ERROR"`) |
| `BfocusException` | qualquer outro status — e resposta 2xx que não é o envelope JSON da API (`Code = "INVALID_RESPONSE"`) |

**Na sua lógica, use `Code`** — é estável (`CUSTOMER_NOT_FOUND`, `INTEGRATION_SCOPE_MISSING`…); a mensagem é só
para gente ler. Ao falar com o suporte, informe o `RequestId`: vem do `request_id` do corpo, senão do header
`X-Request-Id` da resposta, senão é o `X-Request-Id` que a SDK enviou (a API ecoa o do cliente, então bate com o
log dela) — preenchido em toda exceção da SDK, inclusive `NetworkException`.

```csharp
try
{
    await bfocus.Customers.GetAsync("ERP 9999");
}
catch (BfocusException e) when (e.Code == "CUSTOMER_NOT_FOUND")
{
    // cria, ignora…
}
catch (ValidationException e)
{
    foreach (var (campo, motivo) in e.Validation) Console.Error.WriteLine($"{campo}: {motivo}");
}
catch (BfocusException e)
{
    Console.Error.WriteLine($"{e.Code} (HTTP {e.Status}) — request_id {e.RequestId}");
    throw;
}
```

Argumento inválido (chave vazia; parâmetro de caminho vazio, `"."` ou `".."`; `/` no `external_id` de artigo;
campo desconhecido em `ClearFields`) lança `ArgumentException` **antes** de qualquer requisição. Cancelar pelo `CancellationToken` lança
`OperationCanceledException` (não é erro de rede e não gera nova tentativa).

## Novas tentativas e idempotência

A SDK tenta de novo, sozinha, em erro de rede/tempo esgotado, `429`, `502`, `503` e `504` — nada mais (um `500`
ou `4xx` volta na hora). A espera respeita o `Retry-After` da API (teto de 60 s); sem ele,
`min(8, 0,5 × 2^tentativa)` s + até 25% de variação. `MaxRetries = 0` desliga.

Toda chamada leva um `X-Request-Id` e, nas escritas (POST/PUT/DELETE), uma `Idempotency-Key` — os **mesmos** em
todas as tentativas. Se a primeira tentativa chegou a ser executada, a API devolve a resposta original
(`Idempotent-Replayed: true`) em vez de executar de novo. Para deduplicar também um reenvio feito pela **sua**
aplicação (um job reexecutado), passe a sua chave:

```csharp
await bfocus.Customers.Interactions.CreateAsync("ERP 1042", "Pedido 1042 faturado.",
    options: new RequestOptions { IdempotencyKey = "pedido-1042-faturado" });
```

## Identidade do widget

O widget do bFocus identifica o usuário logado por uma assinatura HMAC que o **seu backend** gera com o
segredo do widget (nunca o envie ao navegador). Não precisa de chave de API nem de rede:

```csharp
var assinatura = WidgetIdentity.Sign(
    secret: configuration["Bfocus:WidgetSecret"]!,
    userExternalId: usuario.Id,          // o usuário logado, no seu sistema
    customerExternalId: usuario.EmpresaId);
// HMAC-SHA256(secret, "v1:" + userExternalId + ":" + customerExternalId), hex minúsculo
```

## Fixe a versão exata

Cada release declara se muda a superfície pública. Fixe a versão e suba de pin em pin, lendo a nota:

```xml
<PackageReference Include="Bfocus" Version="[0.1.0]" />
```

A versão em uso vai no header `X-Bfocus-Client: bfocus-dotnet/0.1.0` de toda requisição — é por ele que o bFocus
sabe avisar você quando uma correção exigir atualizar a SDK.

## Compatibilidade

- **`net8.0`**: sem dependências.
- **`netstandard2.0`** (.NET Framework 4.6.2+, .NET Core 2.x/3.x, .NET 5–7, Mono/Xamarin/Unity): depende só de
  `System.Text.Json` 8.x. `IAsyncEnumerable<T>` (dos `ListAllAsync`) vem do `Microsoft.Bcl.AsyncInterfaces`, que já
  chega como dependência do `System.Text.Json` — nenhum pacote extra. Em C# 7.3 (sem `await foreach`), percorra
  com `GetAsyncEnumerator()`/`MoveNextAsync()` ou pagine com `ListAsync(page: …)`.

## Desenvolvimento

Sem o .NET SDK instalado, use Docker:

```bash
docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test
docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet test tests/Bfocus.Tests -p:BfocusTargetFramework=netstandard2.0   # a suíte contra o build netstandard2.0
docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet pack src/Bfocus -c Release -o artifacts
```

A suíte roda os casos de conformidade compartilhados por todas as SDKs do bFocus
(`tests/Bfocus.Tests/fixtures/cases.json`, cópia de `clients/conformance/cases.json` do monorepo) contra um
servidor HTTP local, mais testes unitários.

## Licença

MIT © Berni Software
