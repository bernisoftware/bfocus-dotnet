using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bfocus.Internal;

namespace Bfocus;

/// <summary>Agentes de IA — <c>client.AiAgents</c>. Escopos: <c>ai_agents:read</c> / <c>ai_agents:preview</c>.</summary>
public sealed class AiAgentsResource
{
    private readonly BfocusHttp _http;

    internal AiAgentsResource(BfocusHttp http) => _http = http;

    /// <summary>Lista os agentes de IA (<c>GET /ai-agents</c>).</summary>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>Os agentes.</returns>
    public Task<IReadOnlyList<AiAgent>> ListAsync(RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _http.RequestAsync<IReadOnlyList<AiAgent>>(HttpMethod.Get, "/ai-agents", null, null, options, cancellationToken);

    /// <summary>Busca o agente (<c>GET /ai-agents/{agent_id}</c>).</summary>
    /// <param name="agentId">Id do agente (UUID).</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>O agente.</returns>
    public Task<AiAgent> GetAsync(string agentId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _http.RequestAsync<AiAgent>(HttpMethod.Get, AgentPath(agentId), null, null, options, cancellationToken);

    /// <summary>
    /// Testa a resposta do agente a uma mensagem, sem abrir atendimento (<c>POST /ai-agents/{agent_id}/preview</c>).
    /// Consome IA da conta.
    /// </summary>
    /// <param name="agentId">Id do agente (UUID).</param>
    /// <param name="message">Mensagem do cliente (1–4000).</param>
    /// <param name="history">Turnos anteriores da conversa (até 20).</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>A resposta e o diagnóstico do agente.</returns>
    /// <exception cref="ConflictException"><c>AI_DISABLED</c>.</exception>
    public Task<AiAgentPreview> PreviewAsync(string agentId, string message, IEnumerable<AiAgentPreviewTurn>? history = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        var body = new JsonObject { ["message"] = message };
        if (history is not null)
        {
            body["history"] = JsonSerializer.SerializeToNode(new List<AiAgentPreviewTurn>(history), BfocusJson.Options);
        }

        return _http.RequestAsync<AiAgentPreview>(HttpMethod.Post, AgentPath(agentId) + "/preview", null, body.ToJsonString(BfocusJson.Options), options, cancellationToken);
    }

    private static string AgentPath(string agentId) => "/ai-agents/" + PathSegment.Encode(agentId, nameof(agentId));
}
