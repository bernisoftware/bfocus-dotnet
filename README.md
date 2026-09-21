# bFocus — SDK oficial .NET

SDK oficial da [API pública do bFocus](https://api.bfocus.com.br) para .NET: clientes, pessoas, contatos,
produtos, release notes, base de conhecimento e agentes de IA — com lotes, identificadores extras, novas
tentativas idempotentes, erros tipados e assinatura de identidade do widget. Alvos `netstandard2.0` (.NET Framework 4.6.2+, .NET 6/7…) e `net8.0`.

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
| `customers:read` / `customers:write` | Clientes, pessoas, contatos, produtos vinculados, interações, lotes e identificadores extras |
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

// Lote (até 500) e identificadores extras — detalhes abaixo
var lote = await bfocus.Customers.BatchAsync(new[] { new CustomerBatchItem("erp-1042") { Name = "Padaria Estrela" } });
await bfocus.Customers.Identifiers.AddAsync("erp-1042", "crm-88", label: "CRM");
await bfocus.Customers.Identifiers.RemoveAsync("erp-1042", "crm-88");
```

### Pessoas — `bfocus.People`

As pessoas de um cliente são quem abre o widget/portal. O id da pessoa é o do usuário no **seu** sistema (o
mesmo `user.externalId` que o seu backend assina para o widget).

```csharp
var paula = await bfocus.People.UpsertAsync("erp-1042", "app-77", new PersonUpsert
{
    Name = "Paula Reis",
    Email = "paula@padaria.example",
    Role = "Financeiro",
    IsPrimary = true,
    ExtraEmails = new List<string> { "paula.reis@pessoal.example" },   // somam aos que já existem
});
Console.WriteLine(paula.Status);   // created | updated | unchanged

var pessoas = await bfocus.People.ListAsync("erp-1042");   // com e sem acesso (Access)

// Retira o acesso: a pessoa continua no histórico (chamados, conversas).
var semAcesso = await bfocus.People.DeleteAsync("erp-1042", "app-77");   // semAcesso.Access == false

// Devolve o acesso:
await bfocus.People.UpsertAsync("erp-1042", "app-77", new PersonUpsert { Access = true });
```

- **Sem duplicar**: o e-mail (ou o telefone) acha a pessoa que já chegou por e-mail ou por outro sistema — ela é
  **adotada** pelo seu id, nunca duplicada.
- **Outro cliente**: a mesma pessoa enviada com outro cliente NÃO é transferida — fica **ligada**
  também a ele (`Linked = true`). O cadastro é único e a mesma pessoa circula por vários clientes.
- **O acesso é do vínculo.** `DeleteAsync` (e `Access = false`) tira o acesso dela NESTE cliente, não
  nos outros: `PersonRevokeResult.Unlinked = true` quer dizer que ela segue ativa em algum outro.
- Parcial como todo upsert: `null` = omitido; `ClearFields = { nameof(PersonUpsert.Phone) }` envia `null`.
- Erros comuns (`Code`): `PERSON_EMAIL_STAFF` (e-mail de alguém da sua equipe), `PERSON_EMAIL_TAKEN`,
  `PERSON_PHONE_TAKEN`, `PERSON_CONTACT_OTHER_CUSTOMER`, `PERSON_CLEAR_FIELD_INVALID`,
  `PERSON_CLEAR_NOT_OWN_RECORD`, `PERSON_DOCUMENT_INVALID`, `PERSON_DOCUMENT_CONFLICT`,
  `NAME_REQUIRED` (ao criar), `PERSON_NOT_FOUND`,
  `CUSTOMER_NOT_FOUND`.

#### Campos personalizados da pessoa

`PersonUpsert.CustomFields` leva o que só existe no seu sistema (matrícula, centro de custo, filial). É a
**exceção** ao "só o que vier muda": a lista enviada **substitui a lista inteira** — campo que
ficar de fora é **removido**. Mande sempre a lista que o seu sistema tem hoje; deixar a propriedade `null` não mexe
em nada, como em qualquer outro campo.

A `visibility` é decidida no bFocus e **preservada entre sincronizações** — por isso ela não vai
no envio, só volta na resposta: o seu ERP não rebaixa nem promove a exposição de um dado sem
querer.

Vale no upsert de pessoa, no lote de pessoas e na listagem de pessoas do cliente.

```csharp
var p = await bfocus.People.UpsertAsync("erp-1042", "app-77", new PersonUpsert
{
    CustomFields = new List<CustomFieldInput>    // a lista INTEIRA do seu sistema
    {
        new("matricula", "4471") { Label = "Matrícula" },
        new("filial", "Centro") { Label = "Filial" },
    },
});
foreach (var campo in p.CustomFields)
{
    Console.WriteLine($"{campo.Key} {campo.Value} {campo.Visibility}");   // Visibility vem do bFocus
}
```

#### Apagar o e-mail ou o telefone da pessoa

Um contato gravado errado ficava preso para sempre: enquanto a ficha errada segurasse o telefone, nenhum
reenvio o soltava. `PersonUpsert.Clear` apaga.

```csharp
await bfocus.People.UpsertAsync("erp-1042", "app-77", new PersonUpsert
{
    Clear = new List<string> { "phone" },          // ou { "email", "phone" }
});
```

Três regras que parecem contraintuitivas e são de propósito:

- **Apagar é explícito.** `Clear` é o ÚNICO jeito de apagar. `ClearFields = { nameof(PersonUpsert.Phone) }`
  manda `phone: null`, e em pessoa `null` (como a lista vazia e não preencher) quer dizer **"não mexe"** — a
  SDK não traduz `null` em `Clear`. Fazer o `null` apagar teria apagado, em silêncio e na primeira carga
  seguinte, o dado de todo sistema que manda `null` para "não tenho esse valor".
- **Campo fora da lista é recusado, não ignorado**: hoje só `"email"` e `"phone"`; qualquer outro devolve 422
  `PERSON_CLEAR_FIELD_INVALID` (`ValidationException`).
- **Só se limpa a própria ficha.** Se você alcançou a pessoa por um identificador **extra**, a API recusa com
  409 `PERSON_CLEAR_NOT_OWN_RECORD` (`ConflictException`): apagar o contato de uma ficha alcançada por apelido
  seria apagar dado de outro sistema. Para saber se o id que você tem em mãos é o principal ou um extra, use
  `People.Identifiers.ListAsync(...)`.

Vale no `People.UpsertAsync` e no `People.BatchAsync` (`Clear` no item).

#### CPF: a pessoa é única

`document` é o CPF da pessoa. É por ele que dois sistemas que conhecem a mesma pessoa por ids
diferentes chegam ao MESMO cadastro.

```csharp
var p = await bfocus.People.UpsertAsync("erp-1042", "app-91", new PersonUpsert
{
    Name = "Paula Reis",
    Document = "529.982.247-25",
});
// p.Document == "52998224725"; p.MergedInto == "app-77" se o CPF já era de outra ficha
```

Regras (valem no upsert e no lote):

- **A pessoa é única.** O mesmo CPF é sempre o mesmo cadastro, em qualquer produto e cliente. Mande
  com ou sem máscara; a resposta traz só os 11 dígitos em `document`.
- **Id desconhecido + CPF que já existe** → a API acha a ficha, o seu id vira identificador extra
  dela e a resposta vem com `merged_into` = o id principal. Guarde esse id do seu lado.
- **Id de uma ficha + CPF de OUTRA** → as duas são mescladas na hora; `merged_into` = a que tinha o CPF.
- **`null` (nem com `ClearFields`) NÃO apaga** o CPF (e `document` não é campo do `clear`). Omitir é o mesmo que "não mexe".
- Erros: 422 `PERSON_DOCUMENT_INVALID` (CPF inválido, `ValidationException`) e 409 `PERSON_DOCUMENT_CONFLICT` (a
  ficha já tem OUTRO CPF — a API nunca troca sozinho; `ConflictException`).

No lote: `Document` no `PersonBatchItem`.

#### Contato já usado: um 409 que você consegue resolver

`PERSON_EMAIL_TAKEN` e `PERSON_PHONE_TAKEN` (409) não são "tente de novo": o e-mail (ou o
telefone) já é de outra pessoa da conta. O erro diz **de quem**, em `ErrorData` (a API repete o mesmo
detalhe em `Validation`, por compatibilidade):

| campo | o que é |
| --- | --- |
| `field` | `email` ou `phone` — qual contato está tomado |
| `owner_external_id` | o identificador da pessoa que já usa esse contato |
| `owner_name` | o nome dela |
| `owner_customer_external_id` | o cliente a que ela pertence |

**É o `owner_customer_external_id` que decide a ação**, e os dois casos pedem coisas opostas:

- **mesmo cliente que você enviou** → é quase sempre a MESMA pessoa em dois sistemas. Uma pessoa
  tem **N identificadores**: registre o seu como **extra** dela. A partir daí o seu id encontra
  essa pessoa.
- **outro cliente** → ninguém decide sozinho a quem a pessoa pertence. Não force: registre o caso
  e leve para quem conhece o cadastro. Unificar dois clientes é decisão de gente, não de um
  casamento por e-mail.

```csharp
try
{
    await bfocus.People.UpsertAsync("erp-1042", "app-77",
        new PersonUpsert { Name = "Paula Reis", Email = "paula@padaria.example" });
}
catch (ConflictException e) when (e.Code is "PERSON_EMAIL_TAKEN" or "PERSON_PHONE_TAKEN")
{
    var dono = e.ErrorData;
    if (dono["owner_customer_external_id"].GetString() == "erp-1042")
    {
        // A mesma pessoa, com dois ids: o seu vira mais um identificador dela.
        await bfocus.People.Identifiers.AddAsync(dono["owner_external_id"].GetString()!, "app-77", "ERP");
    }
    else
    {
        // Dono em OUTRO cliente: não decida sozinho — registre e leve para o cadastro.
        await AvisarCadastroAsync(e.Code, dono);
    }
}
```

`PERSON_CONTACT_OTHER_CUSTOMER` (409) é o mesmo assunto pelo outro lado: o e-mail (ou o telefone) é
de uma pessoa de **outro cliente**. A API **não liga duas fichas sozinha** só porque o contato casou
— dois cadastros podem dividir um e-mail ou um telefone, e ligar por palpite já destruiu fichas.
Repetir a chamada não resolve. Se for **mesmo a mesma pessoa** (confirme antes), o erro traz o dono
nos dados, como os outros dois conflitos: registre o seu id como identificador extra da ficha dele
(`owner_external_id`) e o próximo envio **liga** a pessoa ao seu cliente (`linked: true`), sem
sobrescrever os dados da outra ficha.

### Lotes — `Customers.BatchAsync` e `People.BatchAsync`

Até **500** itens por chamada (`CustomersResource.MaxBatchSize` / `PeopleResource.MaxBatchSize`). Acima disso a SDK
lança `ArgumentException` **antes** de qualquer requisição — ela **não** divide sozinha (diferente do
`Kb.Articles.BatchUpsertAsync`), para que o `Index` de cada resultado seja sempre a posição no lote que **você**
enviou. Fatie assim:

```csharp
var clientes = minhasEmpresas.Select(e => new CustomerBatchItem("erp-" + e.Id)
{
    Name = e.RazaoSocial,
    Document = e.Cnpj,
    Email = e.Email,
}).ToList();

for (var inicio = 0; inicio < clientes.Count; inicio += CustomersResource.MaxBatchSize)
{
    var fatia = clientes.GetRange(inicio, Math.Min(CustomersResource.MaxBatchSize, clientes.Count - inicio));
    var resultado = await bfocus.Customers.BatchAsync(fatia);   // no .NET 6+: clientes.Chunk(500)

    Console.WriteLine($"{resultado.Summary.Created} criados, {resultado.Summary.Updated} alterados, " +
                      $"{resultado.Summary.Unchanged} sem mudança, {resultado.Summary.Error} com erro");
    foreach (var r in resultado.Results.Where(r => r.Status == "error"))
    {
        Console.Error.WriteLine($"{fatia[r.Index].ExternalId}: {r.Error} (HTTP {r.Code})");   // ex.: NAME_REQUIRED
    }
}

// Pessoas: cada item diz o cliente e o id da pessoa (no fio: {"customer_external_id", "person": {...}}).
var resultadoPessoas = await bfocus.People.BatchAsync(new[]
{
    new PersonBatchItem("erp-1042", "app-77") { Name = "Paula Reis", Email = "paula@padaria.example" },
    new PersonBatchItem("erp-1042", "app-78") { Name = "Rui Lima", Access = false },
});
```

Cada resultado (`BatchItemResult`) traz `Index`, `Status` (`created`, `updated`, `unchanged` ou `error`),
`ExternalId`, `MergedInto` (o id enviado é um identificador extra — este é o principal do cadastro), `Error`
(código estável) e `Code` (o status HTTP que o item teria sozinho); `Summary` soma por status. **Um item com erro
não desfaz os outros.** Lista vazia devolve o resultado zerado sem requisição. O lote é **uma** chamada: aceita
`IdempotencyKey` como qualquer escrita.

### Identificadores extras — `Customers.Identifiers` e `People.Identifiers`

Liga o id de **outro** sistema seu (CRM, loja, app…) ao **mesmo** cadastro: depois disso, qualquer um dos ids
acha o cliente/pessoa. É idempotente; se o id já pertence a outro cadastro, a API responde 409
`IDENTIFIER_IN_USE` (`ConflictException`).

```csharp
var cliente = await bfocus.Customers.Identifiers.AddAsync("erp-1042", "crm-88", label: "CRM");
foreach (var id in cliente.Identifiers) Console.WriteLine($"{id.ExternalId} ({id.Label}, via {id.Source})");
await bfocus.Customers.Identifiers.RemoveAsync("erp-1042", "crm-88");

var ids = await bfocus.People.Identifiers.AddAsync("app-77", "loja-5531");   // sem rótulo
await bfocus.People.Identifiers.RemoveAsync("app-77", "loja-5531");
```

#### Ler os identificadores da pessoa (para reconciliar)

`People.ListAsync(...)` mostra só o identificador **principal** de cada pessoa. Quando dois cadastros seus
eram a mesma pessoa, um dos ids virou **extra** — e some da listagem sem ter sumido do cadastro. É isso que
faz a sua conferência fechar "633 de 636" sem explicar os 3.

`People.Identifiers.ListAsync` é a fonte de verdade dessa conferência, e é **leitura**: antes dela era preciso
ESCREVER (tentar um `AddAsync`) para descobrir o que tinha acontecido. Aceita no caminho o id principal **ou
qualquer um dos extras**.

```csharp
var ficha = await bfocus.People.Identifiers.ListAsync("crm-p5");   // o id extra que "sumiu" da listagem
Console.WriteLine(ficha.ExternalId);                               // "app-77" — o principal do cadastro
foreach (var id in ficha.Identifiers) Console.WriteLine($"{id.ExternalId} ({id.Label}, via {id.Source})");
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

## Sincronizar clientes e usuários do seu sistema

**Ids com o prefixo do sistema, sem `:`.** Use `-` como separador — `erp-1042` para clientes, `app-77` para
pessoas — ou UUIDs puros: vários sistemas seus convivem no mesmo bFocus sem colisão. Nada de `:` (a assinatura
do widget recusa `:` no id do usuário, e é mais simples usar a mesma regra em tudo).

**Carga inicial (no deploy):** clientes em fatias de 500 → vincule cada cliente ao produto → pessoas em fatias de
500. Confira `Summary.Error` e registre os itens com erro.

```csharp
foreach (var fatia in Fatias(clientes, CustomersResource.MaxBatchSize))
{
    Registrar(fatia, await bfocus.Customers.BatchAsync(fatia));
}

foreach (var c in clientes)
{
    await bfocus.Customers.Products.AttachAsync(c.ExternalId!, "erp-cloud");   // idempotente
}

foreach (var fatia in Fatias(pessoas, PeopleResource.MaxBatchSize))
{
    Registrar(fatia, await bfocus.People.BatchAsync(fatia));
}

static IEnumerable<List<T>> Fatias<T>(List<T> itens, int tamanho)
{
    for (var i = 0; i < itens.Count; i += tamanho)
        yield return itens.GetRange(i, Math.Min(tamanho, itens.Count - i));
}

static void Registrar<T>(List<T> fatia, BatchResult resultado)
{
    foreach (var r in resultado.Results)
    {
        if (r.Status == "error") Console.Error.WriteLine($"item {r.Index}: {r.Error}");
        else if (r.MergedInto is not null) Console.WriteLine($"item {r.Index}: o principal agora é {r.MergedInto}");
    }
}
```

**Depois, no dia a dia**, espelhe cada evento do seu sistema:

| No seu sistema | No bFocus |
| --- | --- |
| criou/alterou cliente | `Customers.UpsertAsync` (e `Customers.Products.AttachAsync` para vinculá-lo ao produto) |
| criou/alterou usuário | `People.UpsertAsync` |
| excluiu/desativou usuário | `People.DeleteAsync` (retira o acesso; o histórico fica) |
| excluiu cliente | `Customers.DeleteAsync` |

Se um resultado de lote trouxer `MergedInto`, atualize o id do seu lado.

**Nunca bloqueie a requisição do seu usuário esperando o bFocus.** Grave no seu banco, enfileire (job/outbox) e
deixe um worker sincronizar, tentando de novo com backoff. A SDK já repete `429`/`5xx` com a mesma
`Idempotency-Key`; a fila cobre indisponibilidades longas.

```csharp
// record SincronizarUsuario(string Id, string UsuarioId, string EmpresaId);   // Id: único por evento
// No endpoint do seu sistema: salva e enfileira — sem chamar o bFocus aqui.
await outbox.EnqueueAsync(new SincronizarUsuario(Guid.NewGuid().ToString("N"), usuario.Id, usuario.EmpresaId));

// No worker da fila (Hangfire, MassTransit, BackgroundService…), que repete com backoff se lançar:
public async Task HandleAsync(SincronizarUsuario job, CancellationToken ct)
{
    var u = await db.Usuarios.FindAsync(job.UsuarioId);
    var opcoes = new RequestOptions { IdempotencyKey = job.Id };   // mesma chave em toda repetição do job
    if (u is { Ativo: true })
    {
        await bfocus.People.UpsertAsync("erp-" + job.EmpresaId, "app-" + job.UsuarioId,
            new PersonUpsert { Name = u.Nome, Email = u.Email, Access = true }, opcoes, ct);
    }
    else
    {
        try { await bfocus.People.DeleteAsync("erp-" + job.EmpresaId, "app-" + job.UsuarioId, opcoes, ct); }
        catch (NotFoundException) { /* já não existe: nada a fazer */ }
    }
}
```

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

Além de `Code`, `Status`, `RequestId`, `Validation`, `RetryAfter` e `RequiredScope`, a exceção tem
**`ErrorData`** (`IReadOnlyDictionary<string, JsonElement>`): o `data` do corpo, com o detalhe estruturado que
alguns erros trazem (vazio quando não há). Chama-se `ErrorData` porque `Exception.Data` já existe em toda exceção
do .NET. É por ele que um 409 de contato tomado diz de **quem** é o contato — veja
[Pessoas](#pessoas--bfocuspeople).

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
campo desconhecido em `ClearFields`; lote de clientes/pessoas com mais de 500 itens; `:` no id do usuário da
assinatura v2) lança `ArgumentException` **antes** de qualquer requisição. Cancelar pelo `CancellationToken` lança
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

### Identidade do widget v2 (com validade)

A v2 carrega o instante da assinatura e **expira**: a API a aceita de 7 dias atrás até 5 minutos à frente. Gere a
cada renderização da página — nunca guarde. Vai no `userHash` do widget, no mesmo lugar da v1 (que continua
aceita):

```csharp
var userHash = WidgetIdentity.SignV2(
    secret: configuration["Bfocus:WidgetSecret"]!,
    userExternalId: "app-" + usuario.Id,          // NÃO pode ter ':' (é o separador) → ArgumentException
    customerExternalId: "erp-" + usuario.EmpresaId);
// "v2.<ts>.<hex>": ts = segundos unix de agora;
// hex = HMAC-SHA256(secret, "v2:" + ts + ":" + userExternalId + ":" + customerExternalId), minúsculo

// Instante fixo (testes): WidgetIdentity.SignV2(secret, usuario, cliente, DateTimeOffset.FromUnixTimeSeconds(1789000000))
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
