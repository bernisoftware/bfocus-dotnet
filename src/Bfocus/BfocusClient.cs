using System;
using Bfocus.Internal;

namespace Bfocus;

/// <summary>
/// Cliente da API pública do bFocus. Crie uma instância por chave de API e reutilize-a (é thread-safe e
/// mantém o pool de conexões); descarte com <see cref="Dispose"/> ao encerrar a aplicação.
/// </summary>
/// <example>
/// <code>
/// using var bfocus = new BfocusClient(Environment.GetEnvironmentVariable("BFOCUS_API_KEY")!);
/// var cliente = await bfocus.Customers.UpsertAsync("ERP 1042", new CustomerUpsert { Name = "Padaria Estrela" });
/// </code>
/// </example>
public sealed class BfocusClient : IDisposable
{
    /// <summary>Versão desta SDK (enviada no header <c>X-Bfocus-Client</c> de toda requisição).</summary>
    public const string Version = "0.2.2";

    /// <summary>URL da API de produção, usada quando <see cref="BfocusClientOptions.BaseUrl"/> não é informado.</summary>
    public const string DefaultBaseUrl = "https://api.bfocus.com.br";

    /// <summary>Cria o cliente com as opções padrão. Não faz nenhuma chamada de rede.</summary>
    /// <param name="apiKey">Chave de API (Integrações → Chaves de API).</param>
    /// <exception cref="ArgumentException">Chave vazia.</exception>
    public BfocusClient(string apiKey)
        : this(apiKey, new BfocusClientOptions())
    {
    }

    /// <summary>Cria o cliente com opções. Não faz nenhuma chamada de rede.</summary>
    /// <param name="apiKey">Chave de API (Integrações → Chaves de API).</param>
    /// <param name="options">URL base, tempo limite, novas tentativas e <c>HttpClient</c>/handler próprios.</param>
    /// <exception cref="ArgumentException">Chave vazia ou opção inválida.</exception>
    public BfocusClient(string apiKey, BfocusClientOptions options)
    {
        if (apiKey is null)
        {
            throw new ArgumentNullException(nameof(apiKey), "Informe a chave de API do bFocus.");
        }

        if (apiKey.Trim().Length == 0)
        {
            throw new ArgumentException("A chave de API do bFocus não pode ser vazia.", nameof(apiKey));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        Http = new BfocusHttp(apiKey, options);
        Customers = new CustomersResource(Http);
        People = new PeopleResource(Http);
        Products = new ProductsResource(Http);
        ReleaseNotes = new ReleaseNotesResource(Http);
        Kb = new KbResource(Http);
        AiAgents = new AiAgentsResource(Http);
    }

    /// <summary>Clientes (e seus contatos, produtos vinculados e interações).</summary>
    public CustomersResource Customers { get; }

    /// <summary>Pessoas dos clientes (quem abre o widget/portal), lotes e identificadores extras.</summary>
    public PeopleResource People { get; }

    /// <summary>Catálogo de produtos.</summary>
    public ProductsResource Products { get; }

    /// <summary>Release notes por produto.</summary>
    public ReleaseNotesResource ReleaseNotes { get; }

    /// <summary>Base de conhecimento (artigos e busca).</summary>
    public KbResource Kb { get; }

    /// <summary>Agentes de IA.</summary>
    public AiAgentsResource AiAgents { get; }

    internal BfocusHttp Http { get; }

    /// <summary>Libera o <c>HttpClient</c> interno (um <c>HttpClient</c> injetado nas opções não é descartado).</summary>
    public void Dispose() => Http.Dispose();
}
