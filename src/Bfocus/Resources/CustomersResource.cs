using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bfocus.Internal;

namespace Bfocus;

/// <summary>Clientes — <c>client.Customers</c>. Escopos: <c>customers:read</c> / <c>customers:write</c>.</summary>
public sealed class CustomersResource
{
    private readonly BfocusHttp _http;

    internal CustomersResource(BfocusHttp http)
    {
        _http = http;
        Contacts = new CustomerContactsResource(http);
        Products = new CustomerProductsResource(http);
        Interactions = new CustomerInteractionsResource(http);
        Identifiers = new CustomerIdentifiersResource(http);
    }

    /// <summary>Máximo de itens por chamada de <see cref="BatchAsync"/> (limite da API; a SDK não divide sozinha).</summary>
    public const int MaxBatchSize = Batch.MaxItems;

    /// <summary>Contatos dos clientes.</summary>
    public CustomerContactsResource Contacts { get; }

    /// <summary>Produtos vinculados aos clientes.</summary>
    public CustomerProductsResource Products { get; }

    /// <summary>Histórico de interações dos clientes.</summary>
    public CustomerInteractionsResource Interactions { get; }

    /// <summary>Identificadores extras dos clientes (ids de outros sistemas seus ligados ao mesmo cadastro).</summary>
    public CustomerIdentifiersResource Identifiers { get; }

    /// <summary>
    /// Cria ou atualiza o cliente <paramref name="externalId"/> (<c>PUT /customers/{external_id}</c>). Só os campos
    /// informados mudam; <see cref="PatchRequest.ClearFields"/> limpa.
    /// </summary>
    /// <param name="externalId">Id do cliente no seu sistema (qualquer texto; é codificado no caminho).</param>
    /// <param name="customer">Campos a gravar.</param>
    /// <param name="options">Opções da chamada (ex.: <c>IdempotencyKey</c>).</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>O cliente gravado.</returns>
    public Task<Customer> UpsertAsync(string externalId, CustomerUpsert customer, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (customer is null)
        {
            throw new ArgumentNullException(nameof(customer));
        }

        return _http.RequestAsync<Customer>(HttpMethod.Put, CustomerPath(externalId), null, customer.ToJsonString(), options, cancellationToken);
    }

    /// <summary>Busca o cliente pelo seu <c>external_id</c> (<c>GET /customers/{external_id}</c>).</summary>
    /// <param name="externalId">Id do cliente no seu sistema.</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>O cliente.</returns>
    /// <exception cref="NotFoundException"><c>CUSTOMER_NOT_FOUND</c>.</exception>
    public Task<Customer> GetAsync(string externalId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _http.RequestAsync<Customer>(HttpMethod.Get, CustomerPath(externalId), null, null, options, cancellationToken);

    /// <summary>Lista clientes, uma página (<c>GET /customers</c>).</summary>
    /// <param name="q">Busca por nome, documento, e-mail…</param>
    /// <param name="updatedSince">Só os alterados a partir deste instante (enviado em UTC).</param>
    /// <param name="page">Página (a partir de 1).</param>
    /// <param name="pageSize">Itens por página (1–200; padrão da API: 50).</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>A página.</returns>
    public Task<Page<Customer>> ListAsync(string? q = null, DateTimeOffset? updatedSince = null, int? page = null, int? pageSize = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        var query = new Query().Add("q", q).Add("updated_since", updatedSince).Add("page", page).Add("page_size", pageSize);
        return _http.RequestPageAsync<Customer>("/customers", query, options, cancellationToken);
    }

    /// <summary>Percorre TODOS os clientes, página a página (<c>await foreach</c>).</summary>
    /// <param name="q">Busca por nome, documento, e-mail…</param>
    /// <param name="updatedSince">Só os alterados a partir deste instante — ideal para sincronização incremental.</param>
    /// <param name="pageSize">Itens por página (1–200).</param>
    /// <param name="options">Opções de cada chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>Sequência assíncrona de clientes.</returns>
    public IAsyncEnumerable<Customer> ListAllAsync(string? q = null, DateTimeOffset? updatedSince = null, int pageSize = Paging.DefaultPageSize, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Paging.CheckPageSize(pageSize);
        return Paging.All((page, ct) => ListAsync(q, updatedSince, page, pageSize, options, ct), cancellationToken);
    }

    /// <summary>Exclui o cliente (<c>DELETE /customers/{external_id}</c>).</summary>
    /// <param name="externalId">Id do cliente no seu sistema.</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns><c>{ deleted: true }</c>.</returns>
    public Task<DeleteResult> DeleteAsync(string externalId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _http.RequestAsync<DeleteResult>(HttpMethod.Delete, CustomerPath(externalId), null, null, options, cancellationToken);

    /// <summary>
    /// Cria ou atualiza até <see cref="MaxBatchSize"/> clientes numa chamada (<c>POST /customers/batch</c>). Cada item
    /// é o corpo do <see cref="UpsertAsync"/> + o seu <see cref="CustomerBatchItem.ExternalId"/>. A SDK NÃO divide:
    /// acima de <see cref="MaxBatchSize"/> lança <see cref="ArgumentException"/> antes de qualquer requisição — fatie
    /// do seu lado (o <see cref="BatchItemResult.Index"/> é a posição no lote enviado). Lista vazia devolve o
    /// resultado zerado sem requisição. Um item com erro não desfaz os outros (veja <see cref="BatchResult.Summary"/>).
    /// </summary>
    /// <param name="items">Clientes (cada um com <see cref="CustomerBatchItem.ExternalId"/>).</param>
    /// <param name="options">Opções da chamada (ex.: <c>IdempotencyKey</c> — um lote é uma chamada).</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>Um resultado por item + o resumo.</returns>
    /// <exception cref="ArgumentException">Mais de <see cref="MaxBatchSize"/> itens, item <c>null</c> ou sem <c>ExternalId</c>.</exception>
    public Task<BatchResult> BatchAsync(IEnumerable<CustomerBatchItem> items, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = Batch.Take(items, "customers.batch", nameof(items));
        if (list.Count == 0)
        {
            return Task.FromResult(new BatchResult());
        }

        var array = new JsonArray();
        for (var i = 0; i < list.Count; i++)
        {
            var item = list[i] ?? throw new ArgumentException($"O cliente #{i} do lote é null.", nameof(items));
            if (string.IsNullOrEmpty(item.ExternalId))
            {
                throw new ArgumentException($"O cliente #{i} do lote não tem ExternalId.", nameof(items));
            }

            array.Add(item.ToJsonObject());
        }

        return _http.RequestAsync<BatchResult>(HttpMethod.Post, "/customers/batch", null, Batch.Body(array), options, cancellationToken);
    }

    internal static string CustomerPath(string externalId) => "/customers/" + PathSegment.Encode(externalId, nameof(externalId));
}

/// <summary>Contatos de um cliente — <c>client.Customers.Contacts</c>.</summary>
public sealed class CustomerContactsResource
{
    private readonly BfocusHttp _http;

    internal CustomerContactsResource(BfocusHttp http) => _http = http;

    /// <summary>Lista os contatos do cliente (<c>GET /customers/{external_id}/contacts</c>).</summary>
    /// <param name="externalId">Id do cliente no seu sistema.</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>Os contatos.</returns>
    public Task<IReadOnlyList<Contact>> ListAsync(string externalId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _http.RequestAsync<IReadOnlyList<Contact>>(HttpMethod.Get, CustomersResource.CustomerPath(externalId) + "/contacts", null, null, options, cancellationToken);

    /// <summary>Cria ou atualiza o contato (<c>PUT /customers/{external_id}/contacts/{contact_external_id}</c>).</summary>
    /// <param name="externalId">Id do cliente no seu sistema.</param>
    /// <param name="contactExternalId">Id do contato no seu sistema.</param>
    /// <param name="contact">Campos a gravar.</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>O contato gravado.</returns>
    public Task<Contact> UpsertAsync(string externalId, string contactExternalId, ContactUpsert contact, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (contact is null)
        {
            throw new ArgumentNullException(nameof(contact));
        }

        return _http.RequestAsync<Contact>(HttpMethod.Put, ContactPath(externalId, contactExternalId), null, contact.ToJsonString(), options, cancellationToken);
    }

    /// <summary>Exclui o contato (<c>DELETE /customers/{external_id}/contacts/{contact_external_id}</c>).</summary>
    /// <param name="externalId">Id do cliente no seu sistema.</param>
    /// <param name="contactExternalId">Id do contato no seu sistema.</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns><c>{ deleted: true }</c>.</returns>
    public Task<DeleteResult> DeleteAsync(string externalId, string contactExternalId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _http.RequestAsync<DeleteResult>(HttpMethod.Delete, ContactPath(externalId, contactExternalId), null, null, options, cancellationToken);

    private static string ContactPath(string externalId, string contactExternalId) =>
        CustomersResource.CustomerPath(externalId) + "/contacts/" + PathSegment.Encode(contactExternalId, nameof(contactExternalId));
}

/// <summary>Produtos vinculados a um cliente — <c>client.Customers.Products</c>.</summary>
public sealed class CustomerProductsResource
{
    private readonly BfocusHttp _http;

    internal CustomerProductsResource(BfocusHttp http) => _http = http;

    /// <summary>Lista os produtos do cliente (<c>GET /customers/{external_id}/products</c>).</summary>
    /// <param name="externalId">Id do cliente no seu sistema.</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>Os produtos vinculados.</returns>
    public Task<IReadOnlyList<ProductRef>> ListAsync(string externalId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _http.RequestAsync<IReadOnlyList<ProductRef>>(HttpMethod.Get, CustomersResource.CustomerPath(externalId) + "/products", null, null, options, cancellationToken);

    /// <summary>Vincula um produto ao cliente (<c>PUT /customers/{external_id}/products/{slug}</c>). Idempotente.</summary>
    /// <param name="externalId">Id do cliente no seu sistema.</param>
    /// <param name="productSlug">Slug do produto.</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>O produto vinculado.</returns>
    public Task<ProductRef> AttachAsync(string externalId, string productSlug, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _http.RequestAsync<ProductRef>(HttpMethod.Put, LinkPath(externalId, productSlug), null, null, options, cancellationToken);

    /// <summary>Desvincula o produto do cliente (<c>DELETE /customers/{external_id}/products/{slug}</c>).</summary>
    /// <param name="externalId">Id do cliente no seu sistema.</param>
    /// <param name="productSlug">Slug do produto.</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns><c>{ deleted: true }</c>.</returns>
    /// <exception cref="NotFoundException"><c>PRODUCT_NOT_LINKED</c>.</exception>
    public Task<DeleteResult> DetachAsync(string externalId, string productSlug, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _http.RequestAsync<DeleteResult>(HttpMethod.Delete, LinkPath(externalId, productSlug), null, null, options, cancellationToken);

    private static string LinkPath(string externalId, string productSlug) =>
        CustomersResource.CustomerPath(externalId) + "/products/" + PathSegment.Encode(productSlug, nameof(productSlug));
}

/// <summary>Histórico de interações de um cliente — <c>client.Customers.Interactions</c>.</summary>
public sealed class CustomerInteractionsResource
{
    private readonly BfocusHttp _http;

    internal CustomerInteractionsResource(BfocusHttp http) => _http = http;

    /// <summary>Lista as interações do cliente, uma página (<c>GET /customers/{external_id}/interactions</c>).</summary>
    /// <param name="externalId">Id do cliente no seu sistema.</param>
    /// <param name="page">Página (a partir de 1).</param>
    /// <param name="pageSize">Itens por página (1–200).</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>A página.</returns>
    public Task<Page<Interaction>> ListAsync(string externalId, int? page = null, int? pageSize = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        var path = CustomersResource.CustomerPath(externalId) + "/interactions";
        return _http.RequestPageAsync<Interaction>(path, new Query().Add("page", page).Add("page_size", pageSize), options, cancellationToken);
    }

    /// <summary>Percorre TODAS as interações do cliente (<c>await foreach</c>).</summary>
    /// <param name="externalId">Id do cliente no seu sistema.</param>
    /// <param name="pageSize">Itens por página (1–200).</param>
    /// <param name="options">Opções de cada chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>Sequência assíncrona de interações.</returns>
    public IAsyncEnumerable<Interaction> ListAllAsync(string externalId, int pageSize = Paging.DefaultPageSize, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Paging.CheckPageSize(pageSize);
        CustomersResource.CustomerPath(externalId);
        return Paging.All((page, ct) => ListAsync(externalId, page, pageSize, options, ct), cancellationToken);
    }

    /// <summary>Registra uma interação no histórico do cliente (<c>POST /customers/{external_id}/interactions</c>).</summary>
    /// <param name="externalId">Id do cliente no seu sistema.</param>
    /// <param name="content">Texto ou HTML (1–50000).</param>
    /// <param name="isInternal">Interna (padrão da API: <c>true</c>).</param>
    /// <param name="authorEmail">E-mail de um usuário do bFocus para constar como autor.</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>A interação criada.</returns>
    public Task<Interaction> CreateAsync(string externalId, string content, bool? isInternal = null, string? authorEmail = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        var body = new JsonObject { ["content"] = content };
        if (isInternal is not null)
        {
            body["is_internal"] = isInternal.Value;
        }

        if (authorEmail is not null)
        {
            body["author_email"] = authorEmail;
        }

        var path = CustomersResource.CustomerPath(externalId) + "/interactions";
        return _http.RequestAsync<Interaction>(HttpMethod.Post, path, null, body.ToJsonString(BfocusJson.Options), options, cancellationToken);
    }
}

/// <summary>
/// Identificadores extras de um cliente — <c>client.Customers.Identifiers</c>. Liga o id de outro sistema seu ao
/// mesmo cadastro (idempotente); id que já é de outro cadastro → <see cref="ConflictException"/>
/// <c>IDENTIFIER_IN_USE</c>.
/// </summary>
public sealed class CustomerIdentifiersResource
{
    private readonly BfocusHttp _http;

    internal CustomerIdentifiersResource(BfocusHttp http) => _http = http;

    /// <summary>Liga o identificador extra ao cliente (<c>PUT /customers/{external_id}/identifiers/{extra_id}</c>). Idempotente.</summary>
    /// <param name="externalId">Id (principal ou extra) do cliente no seu sistema.</param>
    /// <param name="extraId">Identificador extra (ex.: <c>crm-88</c>).</param>
    /// <param name="label">Rótulo livre (ex.: nome do sistema). <c>null</c> = sem corpo.</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>O cliente com os identificadores extras.</returns>
    /// <exception cref="ConflictException"><c>IDENTIFIER_IN_USE</c> (o id já é de outro cadastro).</exception>
    public Task<CustomerWithIdentifiers> AddAsync(string externalId, string extraId, string? label = null, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _http.RequestAsync<CustomerWithIdentifiers>(HttpMethod.Put, IdentifierPath(externalId, extraId), null, IdentifierBody.Label(label), options, cancellationToken);

    /// <summary>Desliga o identificador extra do cliente (<c>DELETE /customers/{external_id}/identifiers/{extra_id}</c>).</summary>
    /// <param name="externalId">Id (principal ou extra) do cliente no seu sistema.</param>
    /// <param name="extraId">Identificador extra.</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>O cliente com os identificadores extras restantes.</returns>
    /// <exception cref="NotFoundException"><c>IDENTIFIER_NOT_FOUND</c>.</exception>
    public Task<CustomerWithIdentifiers> RemoveAsync(string externalId, string extraId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _http.RequestAsync<CustomerWithIdentifiers>(HttpMethod.Delete, IdentifierPath(externalId, extraId), null, null, options, cancellationToken);

    private static string IdentifierPath(string externalId, string extraId) =>
        CustomersResource.CustomerPath(externalId) + "/identifiers/" + PathSegment.Encode(extraId, nameof(extraId));
}
