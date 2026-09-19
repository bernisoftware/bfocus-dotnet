using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Bfocus;

/// <summary>Uma página de uma listagem paginada.</summary>
/// <typeparam name="T">Tipo do item.</typeparam>
public sealed class Page<T>
{
    /// <summary>Cria a página (útil para simular respostas nos testes da sua aplicação).</summary>
    /// <param name="items">Itens desta página.</param>
    /// <param name="pageNumber">Número desta página (a partir de 1).</param>
    /// <param name="pageSize">Tamanho da página.</param>
    /// <param name="total">Total de itens em todas as páginas.</param>
    /// <param name="pages">Total de páginas.</param>
    public Page(IReadOnlyList<T> items, int pageNumber, int pageSize, int total, int pages)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
        PageNumber = pageNumber;
        PageSize = pageSize;
        Total = total;
        Pages = pages;
    }

    /// <summary>Itens desta página.</summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<T> Items { get; }

    /// <summary>Número desta página (a partir de 1). JSON: <c>page</c>.</summary>
    [JsonPropertyName("page")]
    public int PageNumber { get; }

    /// <summary>Tamanho da página.</summary>
    [JsonPropertyName("page_size")]
    public int PageSize { get; }

    /// <summary>Total de itens em todas as páginas.</summary>
    [JsonPropertyName("total")]
    public int Total { get; }

    /// <summary>Total de páginas.</summary>
    [JsonPropertyName("pages")]
    public int Pages { get; }

    /// <summary>Há página depois desta.</summary>
    [JsonIgnore]
    public bool HasNextPage => Items.Count > 0 && PageNumber < Pages;
}
