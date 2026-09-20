using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bfocus;

// Modelos de resposta. Campos desconhecidos são ignorados (a API ganha campos sem aviso); onde a spec permite
// campos extras, eles ficam em AdditionalProperties.

/// <summary>Cliente.</summary>
public class Customer
{
    /// <summary>Id no bFocus (UUID).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Id do cliente no seu sistema.</summary>
    [JsonPropertyName("external_id")]
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>Nome.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Documento.</summary>
    [JsonPropertyName("document")]
    public string? Document { get; set; }

    /// <summary>E-mail.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>Telefone.</summary>
    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    /// <summary>Site.</summary>
    [JsonPropertyName("website")]
    public string? Website { get; set; }

    /// <summary>Observações.</summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>Campos personalizados.</summary>
    [JsonPropertyName("custom_fields")]
    public IReadOnlyList<CustomField> CustomFields { get; set; } = Array.Empty<CustomField>();

    /// <summary>Ativo.</summary>
    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; }

    /// <summary>Criado em.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Alterado em.</summary>
    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Cliente com os identificadores extras (<see cref="CustomerIdentifiersResource"/>).</summary>
public sealed class CustomerWithIdentifiers : Customer
{
    /// <summary>Identificadores extras (o principal é <see cref="Customer.ExternalId"/>).</summary>
    [JsonPropertyName("identifiers")]
    public IReadOnlyList<Identifier> Identifiers { get; set; } = Array.Empty<Identifier>();
}

/// <summary>Identificador extra de um cliente ou pessoa (o id de outro sistema seu ligado ao mesmo cadastro).</summary>
public sealed class Identifier
{
    /// <summary>O identificador extra.</summary>
    [JsonPropertyName("external_id")]
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>Rótulo livre (ex.: nome do sistema).</summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>Quem ligou: <c>api</c>, <c>panel</c>, <c>import</c>…</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;
}

/// <summary>Campo personalizado de um cliente ou de uma pessoa.</summary>
public sealed class CustomField
{
    /// <summary>Chave.</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>Rótulo.</summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>Tipo (<c>text</c>, <c>number</c>, <c>select</c>…).</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    /// <summary>Valor, como veio no JSON (use <c>GetString()</c>, <c>GetDecimal()</c>…).</summary>
    [JsonPropertyName("value")]
    public JsonElement? Value { get; set; }

    /// <summary>Visibilidade (<c>interno</c>…).</summary>
    [JsonPropertyName("visibility")]
    public string Visibility { get; set; } = "interno";

    /// <summary>Campos extras que a API enviar.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

/// <summary>Contato de um cliente.</summary>
public sealed class Contact
{
    /// <summary>Id no bFocus (UUID).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Id do contato no seu sistema.</summary>
    [JsonPropertyName("external_id")]
    public string? ExternalId { get; set; }

    /// <summary>Nome.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Cargo/função.</summary>
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    /// <summary>E-mail.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>Telefone.</summary>
    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    /// <summary>Observações.</summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>Contato principal.</summary>
    [JsonPropertyName("is_primary")]
    public bool IsPrimary { get; set; }

    /// <summary>Criado em.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Alterado em.</summary>
    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Pessoa de um cliente (quem abre o widget/portal).</summary>
public class Person
{
    /// <summary>Id da pessoa no seu sistema (<c>null</c> = contato do cliente sem acesso, sem identificador).</summary>
    [JsonPropertyName("external_id")]
    public string? ExternalId { get; set; }

    /// <summary>Nome.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>E-mail.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>Telefone.</summary>
    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    /// <summary>Cargo/função no cliente.</summary>
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    /// <summary>Pode abrir o widget/portal do cliente.</summary>
    [JsonPropertyName("access")]
    public bool Access { get; set; }

    /// <summary>Contato principal do cliente.</summary>
    [JsonPropertyName("is_primary")]
    public bool IsPrimary { get; set; }

    /// <summary><c>external_id</c> principal do cliente a que a pessoa pertence.</summary>
    [JsonPropertyName("customer_external_id")]
    public string CustomerExternalId { get; set; } = string.Empty;

    /// <summary>Campos personalizados da pessoa (a visibilidade de cada um é definida no bFocus).</summary>
    [JsonPropertyName("custom_fields")]
    public IReadOnlyList<CustomField> CustomFields { get; set; } = Array.Empty<CustomField>();
}

/// <summary>Resultado de <see cref="PeopleResource.UpsertAsync"/>: a pessoa + o que aconteceu.</summary>
public sealed class PersonUpsertResult : Person
{
    /// <summary><c>created</c>, <c>updated</c> ou <c>unchanged</c>.</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

/// <summary>Identificadores extras de uma pessoa (<see cref="PersonIdentifiersResource"/>).</summary>
public sealed class PersonIdentifiers
{
    /// <summary>Identificador principal da pessoa.</summary>
    [JsonPropertyName("external_id")]
    public string? ExternalId { get; set; }

    /// <summary>Identificadores extras.</summary>
    [JsonPropertyName("identifiers")]
    public IReadOnlyList<Identifier> Identifiers { get; set; } = Array.Empty<Identifier>();
}

/// <summary>
/// Resultado de <see cref="CustomersResource.BatchAsync"/> e <see cref="PeopleResource.BatchAsync"/>: um resultado por
/// item + o resumo. Um item com erro não desfaz os outros.
/// </summary>
public sealed class BatchResult
{
    /// <summary>Um resultado por item, na ordem enviada (<see cref="BatchItemResult.Index"/> = posição no lote).</summary>
    [JsonPropertyName("results")]
    public IReadOnlyList<BatchItemResult> Results { get; set; } = Array.Empty<BatchItemResult>();

    /// <summary>Contagem por status.</summary>
    [JsonPropertyName("summary")]
    public BatchSummary Summary { get; set; } = new BatchSummary();
}

/// <summary>Resultado de um item do lote.</summary>
public sealed class BatchItemResult
{
    /// <summary>Posição do item no lote enviado (0 = primeiro).</summary>
    [JsonPropertyName("index")]
    public int Index { get; set; }

    /// <summary><c>created</c>, <c>updated</c>, <c>unchanged</c> ou <c>error</c>.</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>Identificador do item (o principal, depois do upsert).</summary>
    [JsonPropertyName("external_id")]
    public string? ExternalId { get; set; }

    /// <summary>O id enviado é um identificador extra: este é o principal do cadastro único — atualize o seu lado.</summary>
    [JsonPropertyName("merged_into")]
    public string? MergedInto { get; set; }

    /// <summary>Código estável do erro do item (só com <see cref="Status"/> = <c>error</c>; ex.: <c>NAME_REQUIRED</c>).</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>Status HTTP que o item teria sozinho (só em erro).</summary>
    [JsonPropertyName("code")]
    public int? Code { get; set; }
}

/// <summary>Resumo de um lote: quantos itens por status.</summary>
public sealed class BatchSummary
{
    /// <summary>Criados.</summary>
    [JsonPropertyName("created")]
    public int Created { get; set; }

    /// <summary>Alterados.</summary>
    [JsonPropertyName("updated")]
    public int Updated { get; set; }

    /// <summary>Sem mudança.</summary>
    [JsonPropertyName("unchanged")]
    public int Unchanged { get; set; }

    /// <summary>Com erro (veja <see cref="BatchItemResult.Error"/>).</summary>
    [JsonPropertyName("error")]
    public int Error { get; set; }
}

/// <summary>Referência resumida a um produto.</summary>
public sealed class ProductRef
{
    /// <summary>Id no bFocus (UUID).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Slug.</summary>
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    /// <summary>Nome.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Ativo.</summary>
    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; }
}

/// <summary>Produto do catálogo.</summary>
public sealed class Product
{
    /// <summary>Id no bFocus (UUID).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Slug.</summary>
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    /// <summary>Nome.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Descrição.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Cor.</summary>
    [JsonPropertyName("color")]
    public string? Color { get; set; }

    /// <summary>Ícone.</summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    /// <summary>Ativo (<c>false</c> = arquivado).</summary>
    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; }

    /// <summary>Ordem de exibição.</summary>
    [JsonPropertyName("sort_order")]
    public int SortOrder { get; set; }

    /// <summary>Versão atual (a da última release note publicada).</summary>
    [JsonPropertyName("current_version")]
    public string CurrentVersion { get; set; } = string.Empty;

    /// <summary>Nível de IA configurado para o produto.</summary>
    [JsonPropertyName("ai_level")]
    public string AiLevel { get; set; } = string.Empty;

    /// <summary>Criado em.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Alterado em.</summary>
    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Interação registrada no histórico de um cliente.</summary>
public sealed class Interaction
{
    /// <summary>Id no bFocus (UUID).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Conteúdo (HTML).</summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>Interna (não visível ao cliente).</summary>
    [JsonPropertyName("is_internal")]
    public bool IsInternal { get; set; }

    /// <summary>Tipo do autor (<c>human</c>…).</summary>
    [JsonPropertyName("author_kind")]
    public string AuthorKind { get; set; } = string.Empty;

    /// <summary>Nome do autor.</summary>
    [JsonPropertyName("author_name")]
    public string? AuthorName { get; set; }

    /// <summary>Criada em.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }
}

/// <summary>Release note de um produto.</summary>
public sealed class ReleaseNote
{
    /// <summary>Id no bFocus (UUID).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Slug do produto.</summary>
    [JsonPropertyName("product")]
    public string Product { get; set; } = string.Empty;

    /// <summary>Versão SemVer.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>Título.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Descrição em HTML.</summary>
    [JsonPropertyName("description_html")]
    public string DescriptionHtml { get; set; } = string.Empty;

    /// <summary>Público: <c>internal</c>, <c>external</c> ou <c>both</c>.</summary>
    [JsonPropertyName("audience")]
    public string Audience { get; set; } = string.Empty;

    /// <summary>Publicada.</summary>
    [JsonPropertyName("is_published")]
    public bool IsPublished { get; set; }

    /// <summary>Exige ciência da equipe interna.</summary>
    [JsonPropertyName("require_ack_internal")]
    public bool RequireAckInternal { get; set; }

    /// <summary>Exige ciência dos clientes.</summary>
    [JsonPropertyName("require_ack_external")]
    public bool RequireAckExternal { get; set; }

    /// <summary>Publicada em.</summary>
    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>Criada em.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Alterada em.</summary>
    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Artigo da base de conhecimento SEM o corpo — como vem nas listagens (<see cref="KbArticlesResource.ListAsync"/>) e
/// no resultado do lote. O artigo completo é <see cref="KbArticle"/>.
/// </summary>
public class KbArticleSummary
{
    /// <summary>Id no bFocus (UUID).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Id do artigo no seu sistema.</summary>
    [JsonPropertyName("external_id")]
    public string? ExternalId { get; set; }

    /// <summary>Slug do produto (<c>null</c> = global).</summary>
    [JsonPropertyName("product")]
    public string? Product { get; set; }

    /// <summary>Título.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Resumo em texto.</summary>
    [JsonPropertyName("excerpt")]
    public string Excerpt { get; set; } = string.Empty;

    /// <summary><c>draft</c> ou <c>published</c>.</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>Origem (<c>manual</c>…).</summary>
    [JsonPropertyName("origin")]
    public string Origin { get; set; } = string.Empty;

    /// <summary>Publicado em.</summary>
    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>Criado em.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Alterado em.</summary>
    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Artigo completo (get, upsert, publish, unpublish): o resumo + <see cref="BodyHtml"/>.</summary>
public sealed class KbArticle : KbArticleSummary
{
    /// <summary>Corpo em HTML.</summary>
    [JsonPropertyName("body_html")]
    public string BodyHtml { get; set; } = string.Empty;
}

/// <summary>Resultado agregado de <see cref="KbArticlesResource.BatchUpsertAsync"/> (todos os lotes).</summary>
public sealed class KbBatchResult
{
    /// <summary>Um resultado por artigo, na ordem enviada.</summary>
    [JsonPropertyName("results")]
    public IReadOnlyList<KbBatchItemResult> Results { get; set; } = Array.Empty<KbBatchItemResult>();

    /// <summary>Artigos criados.</summary>
    [JsonPropertyName("created")]
    public int Created { get; set; }

    /// <summary>Artigos alterados.</summary>
    [JsonPropertyName("updated")]
    public int Updated { get; set; }

    /// <summary>Artigos sem mudança.</summary>
    [JsonPropertyName("unchanged")]
    public int Unchanged { get; set; }

    /// <summary>Artigos com erro (veja <see cref="KbBatchItemResult.Error"/>).</summary>
    [JsonPropertyName("failed")]
    public int Failed { get; set; }
}

/// <summary>Resultado de um artigo no lote.</summary>
public sealed class KbBatchItemResult
{
    /// <summary><c>external_id</c> do artigo.</summary>
    [JsonPropertyName("external_id")]
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>Deu certo.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    /// <summary><c>created</c>, <c>updated</c> ou <c>unchanged</c> (<c>null</c> em erro).</summary>
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    /// <summary>Código do erro (ex.: <c>KB_ARTICLE_TITLE_REQUIRED</c>).</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>O artigo gravado, sem corpo (<c>null</c> em erro).</summary>
    [JsonPropertyName("article")]
    public KbArticleSummary? Article { get; set; }
}

/// <summary>Resultado da busca na base de conhecimento.</summary>
public sealed class KbSearchHit
{
    /// <summary>Id no bFocus (UUID).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Id do artigo no seu sistema.</summary>
    [JsonPropertyName("external_id")]
    public string? ExternalId { get; set; }

    /// <summary>Título.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Resumo.</summary>
    [JsonPropertyName("excerpt")]
    public string Excerpt { get; set; } = string.Empty;
}

/// <summary>Agente de IA.</summary>
public sealed class AiAgent
{
    /// <summary>Id no bFocus (UUID).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Nome.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Produto que o agente atende.</summary>
    [JsonPropertyName("product")]
    public ProductRef Product { get; set; } = new ProductRef();

    /// <summary>Ativo.</summary>
    [JsonPropertyName("active")]
    public bool Active { get; set; }

    /// <summary>Persona.</summary>
    [JsonPropertyName("persona")]
    public string? Persona { get; set; }

    /// <summary>Escopo de atuação.</summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    /// <summary>URL do avatar.</summary>
    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }

    /// <summary>Criado em.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Alterado em.</summary>
    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Resposta de teste de um agente de IA (<see cref="AiAgentsResource.PreviewAsync"/>).</summary>
public sealed class AiAgentPreview
{
    /// <summary>O que o agente decidiu (<c>answer</c>, <c>escalate</c>…).</summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    /// <summary>Resposta em HTML.</summary>
    [JsonPropertyName("answer_html")]
    public string? AnswerHtml { get; set; }

    /// <summary>Encaminharia para um humano.</summary>
    [JsonPropertyName("escalated")]
    public bool Escalated { get; set; }

    /// <summary>Recusou (fora do escopo).</summary>
    [JsonPropertyName("refused")]
    public bool Refused { get; set; }

    /// <summary>Motivo do encaminhamento.</summary>
    [JsonPropertyName("handoff_reason")]
    public string? HandoffReason { get; set; }

    /// <summary>Confiança (0–1).</summary>
    [JsonPropertyName("confidence")]
    public double? Confidence { get; set; }

    /// <summary>Assunto identificado na mensagem.</summary>
    [JsonPropertyName("topic")]
    public string? Topic { get; set; }

    /// <summary>Guardas de segurança que dispararam.</summary>
    [JsonPropertyName("guards")]
    public IReadOnlyList<string> Guards { get; set; } = Array.Empty<string>();

    /// <summary>Referências citadas na resposta (índices em <see cref="Sources"/>, como vieram).</summary>
    [JsonPropertyName("citations")]
    public IReadOnlyList<JsonElement> Citations { get; set; } = Array.Empty<JsonElement>();

    /// <summary>Fontes usadas (objetos JSON, ex.: <c>{"type": "kb_article", "id": "…", "title": "…"}</c>).</summary>
    [JsonPropertyName("sources")]
    public IReadOnlyList<JsonElement> Sources { get; set; } = Array.Empty<JsonElement>();

    /// <summary>Dados que o agente já coletou do cliente (campo → valor).</summary>
    [JsonPropertyName("collected")]
    public IReadOnlyDictionary<string, JsonElement> Collected { get; set; } = new Dictionary<string, JsonElement>();

    /// <summary>Dados que ainda faltam coletar.</summary>
    [JsonPropertyName("missing")]
    public IReadOnlyList<JsonElement> Missing { get; set; } = Array.Empty<JsonElement>();

    /// <summary>Demais campos do diagnóstico que a API enviar, como vieram.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

/// <summary>Resultado de uma exclusão.</summary>
public sealed class DeleteResult
{
    /// <summary>Excluído.</summary>
    [JsonPropertyName("deleted")]
    public bool Deleted { get; set; } = true;
}
