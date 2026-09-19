using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bfocus.Internal;

namespace Bfocus;

/// <summary>Release notes — <c>client.ReleaseNotes</c>. Escopos: <c>release_notes:read</c> / <c>release_notes:write</c>.</summary>
public sealed class ReleaseNotesResource
{
    private readonly BfocusHttp _http;

    internal ReleaseNotesResource(BfocusHttp http) => _http = http;

    /// <summary>Lista as release notes do produto, uma página (<c>GET /products/{slug}/release-notes</c>).</summary>
    /// <param name="productSlug">Slug do produto.</param>
    /// <param name="published"><c>true</c> só publicadas; <c>false</c> só rascunhos; <c>null</c> todas.</param>
    /// <param name="page">Página (a partir de 1).</param>
    /// <param name="pageSize">Itens por página (1–200).</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>A página.</returns>
    public Task<Page<ReleaseNote>> ListAsync(string productSlug, bool? published = null, int? page = null, int? pageSize = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        var query = new Query().Add("published", published).Add("page", page).Add("page_size", pageSize);
        return _http.RequestPageAsync<ReleaseNote>(NotesPath(productSlug), query, options, cancellationToken);
    }

    /// <summary>Percorre TODAS as release notes do produto (<c>await foreach</c>).</summary>
    /// <param name="productSlug">Slug do produto.</param>
    /// <param name="published"><c>true</c> só publicadas; <c>false</c> só rascunhos; <c>null</c> todas.</param>
    /// <param name="pageSize">Itens por página (1–200).</param>
    /// <param name="options">Opções de cada chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>Sequência assíncrona de release notes.</returns>
    public IAsyncEnumerable<ReleaseNote> ListAllAsync(string productSlug, bool? published = null, int pageSize = Paging.DefaultPageSize, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Paging.CheckPageSize(pageSize);
        NotesPath(productSlug);
        return Paging.All((page, ct) => ListAsync(productSlug, published, page, pageSize, options, ct), cancellationToken);
    }

    /// <summary>Busca a release note da versão (<c>GET /products/{slug}/release-notes/{version}</c>).</summary>
    /// <param name="productSlug">Slug do produto.</param>
    /// <param name="version">Versão SemVer <c>X.Y.Z</c> (aceita <c>v</c> na frente).</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>A release note.</returns>
    public Task<ReleaseNote> GetAsync(string productSlug, string version, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _http.RequestAsync<ReleaseNote>(HttpMethod.Get, NotePath(productSlug, version), null, null, options, cancellationToken);

    /// <summary>
    /// Cria ou atualiza a release note da versão (<c>PUT /products/{slug}/release-notes/{version}</c>). Com
    /// <c>Publish = true</c>, publica na mesma chamada — o jeito de publicar direto do CI.
    /// </summary>
    /// <param name="productSlug">Slug do produto.</param>
    /// <param name="version">Versão SemVer <c>X.Y.Z</c> (aceita <c>v</c> na frente).</param>
    /// <param name="releaseNote">Campos a gravar.</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>A release note gravada.</returns>
    /// <exception cref="ConflictException"><c>RELEASE_NOTE_CONFLICT</c> (gravação concorrente da mesma versão).</exception>
    public Task<ReleaseNote> UpsertAsync(string productSlug, string version, ReleaseNoteUpsert releaseNote, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (releaseNote is null)
        {
            throw new ArgumentNullException(nameof(releaseNote));
        }

        return _http.RequestAsync<ReleaseNote>(HttpMethod.Put, NotePath(productSlug, version), null, releaseNote.ToJsonString(), options, cancellationToken);
    }

    /// <summary>Publica a release note (<c>POST /products/{slug}/release-notes/{version}/publish</c>).</summary>
    /// <param name="productSlug">Slug do produto.</param>
    /// <param name="version">Versão SemVer.</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>A release note publicada.</returns>
    public Task<ReleaseNote> PublishAsync(string productSlug, string version, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _http.RequestAsync<ReleaseNote>(HttpMethod.Post, NotePath(productSlug, version) + "/publish", null, null, options, cancellationToken);

    private static string NotesPath(string productSlug) => ProductsResource.ProductPath(productSlug) + "/release-notes";

    private static string NotePath(string productSlug, string version) =>
        NotesPath(productSlug) + "/" + PathSegment.Encode(version, nameof(version));
}
