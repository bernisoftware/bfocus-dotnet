using System.Text.Json;
using System.Text.Json.Nodes;
using Bfocus.Tests.Support;

namespace Bfocus.Tests;

/// <summary>
/// op de <c>cases.json</c> → chamada idiomática da SDK. Caso com op fora desta tabela (e fora de
/// <c>sdk_excluded_ops</c>) FALHA: é assim que endpoint novo sem método na SDK quebra o build.
/// </summary>
internal static class OpTable
{
    public delegate Task<object?> Call(BfocusClient client, Args args);

    public static readonly IReadOnlyDictionary<string, Call> Ops = new Dictionary<string, Call>
    {
        // Clientes
        ["customers.upsert"] = async (c, a) =>
            await c.Customers.UpsertAsync(a.Str("external_id"), a.Patch<CustomerUpsert>("external_id")),
        ["customers.get"] = async (c, a) =>
            await c.Customers.GetAsync(a.Str("external_id")),
        ["customers.list"] = async (c, a) =>
            await c.Customers.ListAsync(q: a.OptStr("q"), updatedSince: a.OptDate("updated_since"), page: a.OptInt("page"), pageSize: a.OptInt("page_size")),
        ["customers.list_all"] = async (c, a) =>
            await Collect(c.Customers.ListAllAsync(q: a.OptStr("q"), updatedSince: a.OptDate("updated_since"), pageSize: a.OptInt("page_size") ?? 100)),
        ["customers.delete"] = async (c, a) =>
            await c.Customers.DeleteAsync(a.Str("external_id")),

        ["customers.contacts.list"] = async (c, a) =>
            await c.Customers.Contacts.ListAsync(a.Str("external_id")),
        ["customers.contacts.upsert"] = async (c, a) =>
            await c.Customers.Contacts.UpsertAsync(a.Str("external_id"), a.Str("contact_external_id"), a.Patch<ContactUpsert>("external_id", "contact_external_id")),
        ["customers.contacts.delete"] = async (c, a) =>
            await c.Customers.Contacts.DeleteAsync(a.Str("external_id"), a.Str("contact_external_id")),

        ["customers.products.list"] = async (c, a) =>
            await c.Customers.Products.ListAsync(a.Str("external_id")),
        ["customers.products.attach"] = async (c, a) =>
            await c.Customers.Products.AttachAsync(a.Str("external_id"), a.Str("product_slug")),
        ["customers.products.detach"] = async (c, a) =>
            await c.Customers.Products.DetachAsync(a.Str("external_id"), a.Str("product_slug")),

        ["customers.interactions.list"] = async (c, a) =>
            await c.Customers.Interactions.ListAsync(a.Str("external_id"), page: a.OptInt("page"), pageSize: a.OptInt("page_size")),
        ["customers.interactions.create"] = async (c, a) =>
            await c.Customers.Interactions.CreateAsync(a.Str("external_id"), a.Str("content"), isInternal: a.OptBool("is_internal"), authorEmail: a.OptStr("author_email")),

        ["customers.batch"] = async (c, a) =>
            await c.Customers.BatchAsync(a.Raw("items")!.Value.EnumerateArray()
                .Select(item => Args.BuildPatch<CustomerBatchItem>(JsonNode.Parse(item.GetRawText())!.AsObject()))
                .ToList()),
        ["customers.identifiers.add"] = async (c, a) =>
            await c.Customers.Identifiers.AddAsync(a.Str("external_id"), a.Str("extra_id"), label: a.OptStr("label")),
        ["customers.identifiers.remove"] = async (c, a) =>
            await c.Customers.Identifiers.RemoveAsync(a.Str("external_id"), a.Str("extra_id")),

        // Pessoas
        ["people.upsert"] = async (c, a) =>
            await c.People.UpsertAsync(a.Str("customer_external_id"), a.Str("person_external_id"), a.Patch<PersonUpsert>("customer_external_id", "person_external_id")),
        ["people.list"] = async (c, a) =>
            await c.People.ListAsync(a.Str("customer_external_id")),
        ["people.delete"] = async (c, a) =>
            await c.People.DeleteAsync(a.Str("customer_external_id"), a.Str("person_external_id")),
        ["people.batch"] = async (c, a) =>
            await c.People.BatchAsync(a.Raw("items")!.Value.EnumerateArray()
                .Select(item => Args.BuildPatch<PersonBatchItem>(JsonNode.Parse(item.GetRawText())!.AsObject()))
                .ToList()),
        ["people.identifiers.add"] = async (c, a) =>
            await c.People.Identifiers.AddAsync(a.Str("person_external_id"), a.Str("extra_id"), label: a.OptStr("label")),
        ["people.identifiers.remove"] = async (c, a) =>
            await c.People.Identifiers.RemoveAsync(a.Str("person_external_id"), a.Str("extra_id")),

        // Produtos
        ["products.list"] = async (c, a) =>
            await c.Products.ListAsync(includeInactive: a.OptBool("include_inactive")),
        ["products.get"] = async (c, a) =>
            await c.Products.GetAsync(a.Str("slug")),
        ["products.upsert"] = async (c, a) =>
            await c.Products.UpsertAsync(a.Str("slug"), a.Patch<ProductUpsert>("slug")),
        ["products.archive"] = async (c, a) =>
            await c.Products.ArchiveAsync(a.Str("slug")),

        // Release notes
        ["release_notes.list"] = async (c, a) =>
            await c.ReleaseNotes.ListAsync(a.Str("product_slug"), published: a.OptBool("published"), page: a.OptInt("page"), pageSize: a.OptInt("page_size")),
        ["release_notes.get"] = async (c, a) =>
            await c.ReleaseNotes.GetAsync(a.Str("product_slug"), a.Str("version")),
        ["release_notes.upsert"] = async (c, a) =>
            await c.ReleaseNotes.UpsertAsync(a.Str("product_slug"), a.Str("version"), a.Patch<ReleaseNoteUpsert>("product_slug", "version")),
        ["release_notes.publish"] = async (c, a) =>
            await c.ReleaseNotes.PublishAsync(a.Str("product_slug"), a.Str("version")),

        // Base de conhecimento
        ["kb.articles.list"] = async (c, a) =>
            await c.Kb.Articles.ListAsync(product: a.OptStr("product"), status: a.OptStr("status"), q: a.OptStr("q"), updatedSince: a.OptDate("updated_since"), page: a.OptInt("page"), pageSize: a.OptInt("page_size")),
        ["kb.articles.get"] = async (c, a) =>
            await c.Kb.Articles.GetAsync(a.Str("external_id")),
        ["kb.articles.upsert"] = async (c, a) =>
            await c.Kb.Articles.UpsertAsync(a.Str("external_id"), a.Patch<KbArticleUpsert>("external_id")),
        ["kb.articles.batch_upsert"] = async (c, a) =>
            await c.Kb.Articles.BatchUpsertAsync(a.Raw("articles")!.Value.EnumerateArray()
                .Select(item => Args.BuildPatch<KbBatchArticle>(JsonNode.Parse(item.GetRawText())!.AsObject()))
                .ToList()),
        ["kb.articles.publish"] = async (c, a) =>
            await c.Kb.Articles.PublishAsync(a.Str("external_id")),
        ["kb.articles.unpublish"] = async (c, a) =>
            await c.Kb.Articles.UnpublishAsync(a.Str("external_id")),
        ["kb.articles.delete"] = async (c, a) =>
            await c.Kb.Articles.DeleteAsync(a.Str("external_id")),
        ["kb.search"] = async (c, a) =>
            await c.Kb.SearchAsync(a.Str("q"), product: a.OptStr("product"), limit: a.OptInt("limit")),

        // Agentes de IA
        ["ai_agents.list"] = async (c, a) =>
            await c.AiAgents.ListAsync(),
        ["ai_agents.get"] = async (c, a) =>
            await c.AiAgents.GetAsync(a.Str("agent_id")),
        ["ai_agents.preview"] = async (c, a) =>
            await c.AiAgents.PreviewAsync(a.Str("agent_id"), a.Str("message"), history: a.Raw("history")?.EnumerateArray()
                .Select(t => new AiAgentPreviewTurn(t.GetProperty("role").GetString()!, t.GetProperty("content").GetString()!))
                .ToList()),

        // Helpers de paginação (sdk_helper_ops): percorrem as páginas da op de listagem.
        ["customers.interactions.list_all"] = async (c, a) =>
            await Collect(c.Customers.Interactions.ListAllAsync(a.Str("external_id"), pageSize: a.OptInt("page_size") ?? 100)),
        ["release_notes.list_all"] = async (c, a) =>
            await Collect(c.ReleaseNotes.ListAllAsync(a.Str("product_slug"), published: a.OptBool("published"), pageSize: a.OptInt("page_size") ?? 100)),
        ["kb.articles.list_all"] = async (c, a) =>
            await Collect(c.Kb.Articles.ListAllAsync(product: a.OptStr("product"), status: a.OptStr("status"), q: a.OptStr("q"), updatedSince: a.OptDate("updated_since"), pageSize: a.OptInt("page_size") ?? 100)),
    };

    private static async Task<List<T>> Collect<T>(IAsyncEnumerable<T> source)
    {
        var items = new List<T>();
        await foreach (var item in source)
        {
            items.Add(item);
        }

        return items;
    }
}
