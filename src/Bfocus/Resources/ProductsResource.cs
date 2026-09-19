using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bfocus.Internal;

namespace Bfocus;

/// <summary>Catálogo de produtos — <c>client.Products</c>. Escopos: <c>products:read</c> / <c>products:write</c>.</summary>
public sealed class ProductsResource
{
    private readonly BfocusHttp _http;

    internal ProductsResource(BfocusHttp http) => _http = http;

    /// <summary>Lista os produtos (<c>GET /products</c>).</summary>
    /// <param name="includeInactive">Inclui os arquivados.</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>Os produtos.</returns>
    public Task<IReadOnlyList<Product>> ListAsync(bool? includeInactive = null, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _http.RequestAsync<IReadOnlyList<Product>>(HttpMethod.Get, "/products", new Query().Add("include_inactive", includeInactive), null, options, cancellationToken);

    /// <summary>Busca o produto pelo slug (<c>GET /products/{slug}</c>).</summary>
    /// <param name="slug">Slug do produto.</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>O produto.</returns>
    public Task<Product> GetAsync(string slug, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _http.RequestAsync<Product>(HttpMethod.Get, ProductPath(slug), null, null, options, cancellationToken);

    /// <summary>Cria ou atualiza o produto (<c>PUT /products/{slug}</c>). Só os campos informados mudam.</summary>
    /// <param name="slug">Slug (minúsculas, números, <c>-</c> e <c>_</c>).</param>
    /// <param name="product">Campos a gravar.</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>O produto gravado.</returns>
    public Task<Product> UpsertAsync(string slug, ProductUpsert product, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (product is null)
        {
            throw new ArgumentNullException(nameof(product));
        }

        return _http.RequestAsync<Product>(HttpMethod.Put, ProductPath(slug), null, product.ToJsonString(), options, cancellationToken);
    }

    /// <summary>Arquiva o produto (<c>DELETE /products/{slug}</c>) — não apaga; some das listas padrão.</summary>
    /// <param name="slug">Slug do produto.</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>O produto arquivado.</returns>
    public Task<Product> ArchiveAsync(string slug, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _http.RequestAsync<Product>(HttpMethod.Delete, ProductPath(slug), null, null, options, cancellationToken);

    internal static string ProductPath(string slug) => "/products/" + PathSegment.Encode(slug, nameof(slug));
}
