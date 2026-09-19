using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Bfocus;

/// <summary>
/// Corpo de <see cref="CustomersResource.UpsertAsync"/> (<c>PUT /customers/{external_id}</c>). Parcial: propriedade
/// <c>null</c> = omitida; para limpar, use <see cref="PatchRequest.ClearFields"/>.
/// </summary>
public sealed class CustomerUpsert : PatchRequest
{
    /// <summary>Nome (até 500).</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>CPF/CNPJ ou outro documento (até 50).</summary>
    [JsonPropertyName("document")]
    public string? Document { get; set; }

    /// <summary>E-mail (até 255).</summary>
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>Telefone (até 50).</summary>
    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    /// <summary>Site (até 500).</summary>
    [JsonPropertyName("website")]
    public string? Website { get; set; }

    /// <summary>Observações.</summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>Campos personalizados. Quando enviada, a lista SUBSTITUI a atual (lista vazia apaga todos).</summary>
    [JsonPropertyName("custom_fields")]
    public IList<CustomFieldInput>? CustomFields { get; set; }
}

/// <summary>Campo personalizado enviado em <see cref="CustomerUpsert.CustomFields"/>.</summary>
public sealed class CustomFieldInput
{
    /// <summary>Cria um campo vazio (preencha <see cref="Key"/>).</summary>
    public CustomFieldInput()
    {
    }

    /// <summary>Cria o campo com chave e valor.</summary>
    /// <param name="key">Chave (1–80).</param>
    /// <param name="value">Valor (texto, número, booleano…).</param>
    public CustomFieldInput(string key, object? value = null)
    {
        Key = key;
        Value = value;
    }

    /// <summary>Chave estável do campo (1–80).</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>Rótulo exibido (até 200).</summary>
    [JsonPropertyName("label")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; set; }

    /// <summary>
    /// Tipo: <c>text</c> (padrão), <c>textarea</c>, <c>email</c>, <c>phone</c>, <c>url</c>, <c>number</c>, <c>date</c>,
    /// <c>datetime</c>, <c>bool</c>, <c>select</c> ou <c>file</c>.
    /// </summary>
    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; set; }

    /// <summary>Valor (qualquer tipo JSON).</summary>
    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Value { get; set; }

    /// <summary>Opções, para o tipo <c>select</c>.</summary>
    [JsonPropertyName("options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IList<string>? Options { get; set; }
}

/// <summary>
/// Corpo de <see cref="CustomerContactsResource.UpsertAsync"/>. Parcial: propriedade <c>null</c> = omitida; para
/// limpar, use <see cref="PatchRequest.ClearFields"/>.
/// </summary>
public sealed class ContactUpsert : PatchRequest
{
    /// <summary>Nome (1–255).</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Cargo/função (até 120).</summary>
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    /// <summary>E-mail válido.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>Telefone (até 50).</summary>
    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    /// <summary>Observações.</summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>Contato principal do cliente.</summary>
    [JsonPropertyName("is_primary")]
    public bool? IsPrimary { get; set; }
}

/// <summary>
/// Corpo de <see cref="ProductsResource.UpsertAsync"/>. Parcial: propriedade <c>null</c> = omitida; para limpar,
/// use <see cref="PatchRequest.ClearFields"/>.
/// </summary>
public sealed class ProductUpsert : PatchRequest
{
    /// <summary>Nome (1–255).</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Descrição.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Cor (ex.: <c>#6366F1</c>, até 16).</summary>
    [JsonPropertyName("color")]
    public string? Color { get; set; }

    /// <summary>Ícone (até 64).</summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    /// <summary>Ativo (<c>false</c> arquiva).</summary>
    [JsonPropertyName("is_active")]
    public bool? IsActive { get; set; }

    /// <summary>Ordem de exibição.</summary>
    [JsonPropertyName("sort_order")]
    public int? SortOrder { get; set; }
}

/// <summary>
/// Corpo de <see cref="ReleaseNotesResource.UpsertAsync"/>. Parcial: propriedade <c>null</c> = omitida; para limpar,
/// use <see cref="PatchRequest.ClearFields"/>.
/// </summary>
public sealed class ReleaseNoteUpsert : PatchRequest
{
    /// <summary>Título (1–255).</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Descrição em HTML (use este OU <see cref="DescriptionMarkdown"/>).</summary>
    [JsonPropertyName("description_html")]
    public string? DescriptionHtml { get; set; }

    /// <summary>Descrição em Markdown (a API converte para HTML).</summary>
    [JsonPropertyName("description_markdown")]
    public string? DescriptionMarkdown { get; set; }

    /// <summary>Público: <c>internal</c>, <c>external</c> ou <c>both</c>.</summary>
    [JsonPropertyName("audience")]
    public string? Audience { get; set; }

    /// <summary>Exige ciência da equipe interna.</summary>
    [JsonPropertyName("require_ack_internal")]
    public bool? RequireAckInternal { get; set; }

    /// <summary>Exige ciência dos clientes (widget).</summary>
    [JsonPropertyName("require_ack_external")]
    public bool? RequireAckExternal { get; set; }

    /// <summary><c>true</c> publica na mesma chamada (ideal no CI). Não pode ser limpo.</summary>
    [JsonPropertyName("publish")]
    [NotClearable]
    public bool? Publish { get; set; }
}

/// <summary>
/// Corpo de <see cref="KbArticlesResource.UpsertAsync"/>. Parcial: propriedade <c>null</c> = omitida; para limpar,
/// use <see cref="PatchRequest.ClearFields"/> — em especial, <c>ClearFields = { "product" }</c> torna o artigo global.
/// </summary>
public class KbArticleUpsert : PatchRequest
{
    /// <summary>Título (1–200).</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Corpo em HTML (use este OU <see cref="BodyMarkdown"/>).</summary>
    [JsonPropertyName("body_html")]
    public string? BodyHtml { get; set; }

    /// <summary>Corpo em Markdown (a API converte para HTML).</summary>
    [JsonPropertyName("body_markdown")]
    public string? BodyMarkdown { get; set; }

    /// <summary>Slug do produto. Limpe (<see cref="PatchRequest.ClearFields"/>) para tornar o artigo global.</summary>
    [JsonPropertyName("product")]
    public string? Product { get; set; }

    /// <summary><c>draft</c> ou <c>published</c>.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

/// <summary>Item de <see cref="KbArticlesResource.BatchUpsertAsync"/>: um <see cref="KbArticleUpsert"/> com o seu <c>external_id</c>.</summary>
public sealed class KbBatchArticle : KbArticleUpsert
{
    /// <summary>Cria o item (preencha <see cref="ExternalId"/>).</summary>
    public KbBatchArticle()
    {
    }

    /// <summary>Cria o item para o artigo <paramref name="externalId"/>.</summary>
    /// <param name="externalId"><c>external_id</c> do artigo no seu sistema (sem <c>/</c>; use <c>:</c> para hierarquia).</param>
    public KbBatchArticle(string externalId)
    {
        ExternalId = externalId;
    }

    /// <summary><c>external_id</c> do artigo no seu sistema (obrigatório; sem <c>/</c> — use <c>:</c> para hierarquia).</summary>
    [JsonPropertyName("external_id")]
    [NotClearable]
    public string? ExternalId { get; set; }
}

/// <summary>Turno anterior da conversa em <see cref="AiAgentsResource.PreviewAsync"/>.</summary>
public sealed class AiAgentPreviewTurn
{
    /// <summary>Cria o turno.</summary>
    /// <param name="role"><c>customer</c> ou <c>bot</c>.</param>
    /// <param name="content">Texto (até 4000).</param>
    [JsonConstructor]
    public AiAgentPreviewTurn(string role, string content)
    {
        Role = role ?? throw new ArgumentNullException(nameof(role));
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    /// <summary><c>customer</c> ou <c>bot</c>.</summary>
    [JsonPropertyName("role")]
    public string Role { get; }

    /// <summary>Texto (até 4000).</summary>
    [JsonPropertyName("content")]
    public string Content { get; }

    /// <summary>Turno do cliente.</summary>
    /// <param name="content">Texto.</param>
    /// <returns>O turno.</returns>
    public static AiAgentPreviewTurn Customer(string content) => new AiAgentPreviewTurn("customer", content);

    /// <summary>Turno do agente.</summary>
    /// <param name="content">Texto.</param>
    /// <returns>O turno.</returns>
    public static AiAgentPreviewTurn Bot(string content) => new AiAgentPreviewTurn("bot", content);
}
