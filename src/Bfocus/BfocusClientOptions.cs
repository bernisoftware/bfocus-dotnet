using System;
using System.Net.Http;

namespace Bfocus;

/// <summary>Opções do <see cref="BfocusClient"/>.</summary>
public sealed class BfocusClientOptions
{
    /// <summary>URL base da API, sem barra final. Padrão: <c>https://api.bfocus.com.br</c>. Dev: <c>http://localhost:8000</c>.</summary>
    public string BaseUrl { get; set; } = BfocusClient.DefaultBaseUrl;

    /// <summary>Tempo limite de CADA tentativa (não da chamada inteira). Padrão: 30 s.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Novas tentativas além da primeira em erro de rede/tempo esgotado, 429, 502, 503 e 504. Padrão: 2.
    /// <c>0</c> desliga.
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// <c>HttpClient</c> próprio (ex.: vindo do <c>IHttpClientFactory</c>). A SDK não o descarta e respeita também o
    /// <c>HttpClient.Timeout</c> dele. Tem precedência sobre <see cref="HttpMessageHandler"/>.
    /// </summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>Handler próprio (proxy, certificados, testes). A SDK não o descarta.</summary>
    public HttpMessageHandler? HttpMessageHandler { get; set; }
}
