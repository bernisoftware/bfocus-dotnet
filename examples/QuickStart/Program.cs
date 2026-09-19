// QuickStart da SDK .NET do bFocus.
//
//   export BFOCUS_API_KEY=bf_live_…            # Integrações → Chaves de API (escopos customers:write e kb:read)
//   export BFOCUS_BASE_URL=http://localhost:8000   # opcional (dev)
//   dotnet run --project examples/QuickStart
using Bfocus;

var apiKey = Environment.GetEnvironmentVariable("BFOCUS_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Defina BFOCUS_API_KEY com uma chave de API do bFocus (Integrações → Chaves de API).");
    return 1;
}

var options = new BfocusClientOptions();
var baseUrl = Environment.GetEnvironmentVariable("BFOCUS_BASE_URL");
if (!string.IsNullOrWhiteSpace(baseUrl))
{
    options.BaseUrl = baseUrl;
}

using var bfocus = new BfocusClient(apiKey, options);

try
{
    // 1. Hello world: cria (ou atualiza) um cliente pelo id dele no SEU sistema.
    var customer = await bfocus.Customers.UpsertAsync("SDK-DOTNET-EXEMPLO", new CustomerUpsert
    {
        Name = "Cliente de exemplo (SDK .NET)",
        Email = "exemplo@example.com",
        CustomFields = new List<CustomFieldInput> { new("origem", "quickstart-dotnet") },
    });
    Console.WriteLine($"Cliente {customer.ExternalId}: {customer.Name} (id {customer.Id})");

    // 2. Uma página de clientes.
    var page = await bfocus.Customers.ListAsync(pageSize: 5);
    Console.WriteLine($"{page.Total} cliente(s) na conta; os primeiros:");
    foreach (var c in page.Items)
    {
        Console.WriteLine($"  - {c.ExternalId}: {c.Name}");
    }

    // 3. Busca na base de conhecimento (precisa do escopo kb:read).
    try
    {
        foreach (var hit in await bfocus.Kb.SearchAsync("como emitir nota fiscal", limit: 3))
        {
            Console.WriteLine($"  KB: {hit.Title}");
        }
    }
    catch (PermissionDeniedException e)
    {
        Console.WriteLine($"  (busca na KB pulada: a chave não tem o escopo {e.RequiredScope})");
    }

    return 0;
}
catch (BfocusException e)
{
    // A lógica usa e.Code (estável); informe e.RequestId ao suporte.
    Console.Error.WriteLine($"Erro {e.Code} (HTTP {e.Status}, request_id {e.RequestId ?? "-"}): {e.Message}");
    return 1;
}
