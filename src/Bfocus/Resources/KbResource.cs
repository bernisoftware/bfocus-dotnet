using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bfocus.Internal;

namespace Bfocus;

/// <summary>Base de conhecimento — <c>client.Kb</c>. Escopos: <c>kb:read</c> / <c>kb:write</c>.</summary>
public sealed class KbResource
{
    private readonly BfocusHttp _http;

    internal KbResource(BfocusHttp http)
    {
        _http = http;
        Articles = new KbArticlesResource(http);
    }

    /// <summary>Artigos.</summary>
    public KbArticlesResource Articles { get; }

    /// <summary>Busca na base de conhecimento (<c>GET /kb/search</c>) — a mesma busca que os agentes de IA usam.</summary>
    /// <param name="q">Texto buscado (1–500).</param>
    /// <param name="product">Só artigos deste produto (e os globais).</param>
    /// <param name="limit">Máximo de resultados (1–20; padrão da API: 5).</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>Os artigos mais relevantes.</returns>
    public Task<IReadOnlyList<KbSearchHit>> SearchAsync(string q, string? product = null, int? limit = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (q is null)
        {
            throw new ArgumentNullException(nameof(q));
        }

        var query = new Query().Add("q", q).Add("product", product).Add("limit", limit);
        return _http.RequestAsync<IReadOnlyList<KbSearchHit>>(HttpMethod.Get, "/kb/search", query, null, options, cancellationToken);
    }
}

/// <summary>Artigos da base de conhecimento — <c>client.Kb.Articles</c>.</summary>
public sealed class KbArticlesResource
{
    /// <summary>Máximo de artigos por requisição de lote (limite da API).</summary>
    public const int BatchSize = 100;

    private readonly BfocusHttp _http;

    internal KbArticlesResource(BfocusHttp http) => _http = http;

    /// <summary>Lista artigos, uma página (<c>GET /kb/articles</c>). Os itens vêm sem corpo (<see cref="KbArticleSummary"/>).</summary>
    /// <param name="product">Só os artigos deste produto.</param>
    /// <param name="status"><c>draft</c> ou <c>published</c>.</param>
    /// <param name="q">Busca no título e no texto.</param>
    /// <param name="updatedSince">Só os alterados a partir deste instante (enviado em UTC).</param>
    /// <param name="page">Página (a partir de 1).</param>
    /// <param name="pageSize">Itens por página (1–100).</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>A página.</returns>
    public Task<Page<KbArticleSummary>> ListAsync(string? product = null, string? status = null, string? q = null, DateTimeOffset? updatedSince = null, int? page = null, int? pageSize = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        var query = new Query()
            .Add("product", product)
            .Add("status", status)
            .Add("q", q)
            .Add("updated_since", updatedSince)
            .Add("page", page)
            .Add("page_size", pageSize);
        return _http.RequestPageAsync<KbArticleSummary>("/kb/articles", query, options, cancellationToken);
    }

    /// <summary>Percorre TODOS os artigos, sem corpo (<c>await foreach</c>).</summary>
    /// <param name="product">Só os artigos deste produto.</param>
    /// <param name="status"><c>draft</c> ou <c>published</c>.</param>
    /// <param name="q">Busca no título e no texto.</param>
    /// <param name="updatedSince">Só os alterados a partir deste instante.</param>
    /// <param name="pageSize">Itens por página (1–100).</param>
    /// <param name="options">Opções de cada chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>Sequência assíncrona de artigos.</returns>
    public IAsyncEnumerable<KbArticleSummary> ListAllAsync(string? product = null, string? status = null, string? q = null, DateTimeOffset? updatedSince = null, int pageSize = Paging.DefaultPageSize, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Paging.CheckPageSize(pageSize);
        return Paging.All((page, ct) => ListAsync(product, status, q, updatedSince, page, pageSize, options, ct), cancellationToken);
    }

    /// <summary>Busca o artigo pelo seu <c>external_id</c> (<c>GET /kb/articles/{external_id}</c>).</summary>
    /// <param name="externalId">Id do artigo no seu sistema (sem <c>/</c>).</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>O artigo completo, com <see cref="KbArticle.BodyHtml"/>.</returns>
    public Task<KbArticle> GetAsync(string externalId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _http.RequestAsync<KbArticle>(HttpMethod.Get, ArticlePath(externalId), null, null, options, cancellationToken);

    /// <summary>
    /// Cria ou atualiza o artigo (<c>PUT /kb/articles/{external_id}</c>). Só os campos informados mudam;
    /// <c>ClearFields = { "product" }</c> torna o artigo global.
    /// </summary>
    /// <param name="externalId">Id do artigo no seu sistema (sem <c>/</c>; use <c>:</c> para hierarquia).</param>
    /// <param name="article">Campos a gravar.</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>O artigo gravado.</returns>
    /// <exception cref="ConflictException"><c>KB_ARTICLE_EMPTY</c> (corpo vazio).</exception>
    public Task<KbArticle> UpsertAsync(string externalId, KbArticleUpsert article, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (article is null)
        {
            throw new ArgumentNullException(nameof(article));
        }

        return _http.RequestAsync<KbArticle>(HttpMethod.Put, ArticlePath(externalId), null, article.ToJsonString(), options, cancellationToken);
    }

    /// <summary>
    /// Cria ou atualiza QUALQUER quantidade de artigos (<c>POST /kb/articles/batch</c>): a SDK divide em lotes de
    /// <see cref="BatchSize"/>, envia em sequência e devolve UM resultado agregado (<c>Results</c> na ordem
    /// enviada, contadores somados). Lista vazia devolve o resultado zerado sem requisição. Falha de um artigo não
    /// derruba o lote (veja <see cref="KbBatchItemResult.Error"/>); um erro HTTP interrompe os lotes seguintes (os
    /// anteriores já foram gravados — rodar de novo é seguro).
    /// </summary>
    /// <param name="articles">Artigos (cada um com <see cref="KbBatchArticle.ExternalId"/>).</param>
    /// <param name="options">
    /// Opções de cada lote. Com <c>IdempotencyKey</c> própria, o 1º lote usa a chave como veio e os seguintes
    /// <c>{chave}:2</c>, <c>{chave}:3</c>…; sem ela, cada lote gera a sua.
    /// </param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>O resultado agregado.</returns>
    public async Task<KbBatchResult> BatchUpsertAsync(IEnumerable<KbBatchArticle> articles, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (articles is null)
        {
            throw new ArgumentNullException(nameof(articles));
        }

        var list = articles.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var article = list[i] ?? throw new ArgumentException($"O artigo #{i} do lote é null.", nameof(articles));
            if (string.IsNullOrEmpty(article.ExternalId))
            {
                throw new ArgumentException($"O artigo #{i} do lote não tem ExternalId.", nameof(articles));
            }

            if (article.ExternalId!.IndexOf('/') >= 0)
            {
                throw new ArgumentException($"O ExternalId '{article.ExternalId}' (artigo #{i}) não aceita '/' — use ':' para hierarquia.", nameof(articles));
            }
        }

        var results = new List<KbBatchItemResult>(list.Count);
        var aggregate = new KbBatchResult { Results = results };
        for (int start = 0, chunk = 0; start < list.Count; start += BatchSize, chunk++)
        {
            var items = new JsonArray();
            for (var i = start; i < Math.Min(start + BatchSize, list.Count); i++)
            {
                items.Add(list[i].ToJsonObject());
            }

            var body = new JsonObject { ["articles"] = items }.ToJsonString(BfocusJson.Options);
            var chunkOptions = options;
            if (chunk > 0 && !string.IsNullOrEmpty(options?.IdempotencyKey))
            {
                chunkOptions = options!.WithIdempotencyKey(options.IdempotencyKey + ":" + (chunk + 1).ToString(CultureInfo.InvariantCulture));
            }

            var result = await _http.RequestAsync<KbBatchResult>(HttpMethod.Post, "/kb/articles/batch", null, body, chunkOptions, cancellationToken).ConfigureAwait(false);
            results.AddRange(result.Results);
            aggregate.Created += result.Created;
            aggregate.Updated += result.Updated;
            aggregate.Unchanged += result.Unchanged;
            aggregate.Failed += result.Failed;
        }

        return aggregate;
    }

    /// <summary>Publica o artigo (<c>POST /kb/articles/{external_id}/publish</c>).</summary>
    /// <param name="externalId">Id do artigo no seu sistema.</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>O artigo publicado.</returns>
    public Task<KbArticle> PublishAsync(string externalId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _http.RequestAsync<KbArticle>(HttpMethod.Post, ArticlePath(externalId) + "/publish", null, null, options, cancellationToken);

    /// <summary>Volta o artigo para rascunho (<c>POST /kb/articles/{external_id}/unpublish</c>).</summary>
    /// <param name="externalId">Id do artigo no seu sistema.</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>O artigo em rascunho.</returns>
    public Task<KbArticle> UnpublishAsync(string externalId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _http.RequestAsync<KbArticle>(HttpMethod.Post, ArticlePath(externalId) + "/unpublish", null, null, options, cancellationToken);

    /// <summary>Exclui o artigo (<c>DELETE /kb/articles/{external_id}</c>).</summary>
    /// <param name="externalId">Id do artigo no seu sistema.</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns><c>{ deleted: true }</c>.</returns>
    public Task<DeleteResult> DeleteAsync(string externalId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _http.RequestAsync<DeleteResult>(HttpMethod.Delete, ArticlePath(externalId), null, null, options, cancellationToken);

    private static string ArticlePath(string externalId) => "/kb/articles/" + PathSegment.KbExternalId(externalId, nameof(externalId));
}
