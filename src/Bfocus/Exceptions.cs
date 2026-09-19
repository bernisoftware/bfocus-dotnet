using System;
using System.Collections.Generic;

namespace Bfocus;

/// <summary>
/// Erro devolvido pela API do bFocus (qualquer status fora de 2xx) ou de rede. Na sua lógica, use
/// <see cref="Code"/> — é estável (ex.: <c>CUSTOMER_NOT_FOUND</c>, <c>INTEGRATION_SCOPE_MISSING</c>); a mensagem
/// é só para leitura humana e pode mudar.
/// </summary>
public class BfocusException : Exception
{
    private static readonly IReadOnlyDictionary<string, string> NoValidation = new Dictionary<string, string>();

    /// <summary>Cria o erro (útil também para simular falhas nos testes da sua aplicação).</summary>
    /// <param name="code">Código estável do erro.</param>
    /// <param name="status">Status HTTP (<c>0</c> em erro de rede).</param>
    /// <param name="message">Texto legível.</param>
    /// <param name="requestId">Id da requisição (informe ao suporte).</param>
    /// <param name="validation">Campo → motivo, em erros de validação.</param>
    /// <param name="retryAfter">Espera pedida pela API (só em 429).</param>
    /// <param name="requiredScope">Escopo que faltou na chave (só em 403 de escopo).</param>
    /// <param name="innerException">Causa original.</param>
    public BfocusException(
        string code,
        int status,
        string message,
        string? requestId = null,
        IReadOnlyDictionary<string, string>? validation = null,
        TimeSpan? retryAfter = null,
        string? requiredScope = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Status = status;
        RequestId = requestId;
        Validation = validation ?? NoValidation;
        RetryAfter = retryAfter;
        RequiredScope = requiredScope;
    }

    /// <summary>
    /// Código estável do erro: <c>error</c> do corpo, senão <c>message</c>, senão <c>HTTP_&lt;status&gt;</c> (corpo
    /// não-JSON); <c>NETWORK_ERROR</c> em falha de rede/tempo esgotado.
    /// </summary>
    public string Code { get; }

    /// <summary>Status HTTP (<c>0</c> em erro de rede).</summary>
    public int Status { get; }

    /// <summary>
    /// Id da requisição — informe ao suporte. Vem do <c>request_id</c> do corpo, senão do header <c>X-Request-Id</c>
    /// da resposta, senão é o <c>X-Request-Id</c> que a SDK enviou (a API ecoa o do cliente). Preenchido em toda
    /// exceção lançada pela SDK, inclusive <see cref="NetworkException"/>.
    /// </summary>
    public string? RequestId { get; }

    /// <summary>Campo → motivo, em erros de validação (vazio nos demais).</summary>
    public IReadOnlyDictionary<string, string> Validation { get; }

    /// <summary>Espera pedida pela API no header <c>Retry-After</c> (só em 429).</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>Escopo que faltou na chave, do header <c>X-Required-Scope</c> (só em 403 de escopo).</summary>
    public string? RequiredScope { get; }
}

/// <summary>401 — chave ausente, inválida ou revogada.</summary>
public class AuthenticationException : BfocusException
{
    /// <inheritdoc cref="BfocusException(string, int, string, string?, IReadOnlyDictionary{string, string}?, TimeSpan?, string?, Exception?)"/>
    public AuthenticationException(string code, int status, string message, string? requestId = null, IReadOnlyDictionary<string, string>? validation = null, TimeSpan? retryAfter = null, string? requiredScope = null, Exception? innerException = null)
        : base(code, status, message, requestId, validation, retryAfter, requiredScope, innerException)
    {
    }
}

/// <summary>403 — chave desligada, IP não liberado ou escopo faltando (veja <see cref="BfocusException.RequiredScope"/>).</summary>
public class PermissionDeniedException : BfocusException
{
    /// <inheritdoc cref="BfocusException(string, int, string, string?, IReadOnlyDictionary{string, string}?, TimeSpan?, string?, Exception?)"/>
    public PermissionDeniedException(string code, int status, string message, string? requestId = null, IReadOnlyDictionary<string, string>? validation = null, TimeSpan? retryAfter = null, string? requiredScope = null, Exception? innerException = null)
        : base(code, status, message, requestId, validation, retryAfter, requiredScope, innerException)
    {
    }
}

/// <summary>404 — recurso não encontrado.</summary>
public class NotFoundException : BfocusException
{
    /// <inheritdoc cref="BfocusException(string, int, string, string?, IReadOnlyDictionary{string, string}?, TimeSpan?, string?, Exception?)"/>
    public NotFoundException(string code, int status, string message, string? requestId = null, IReadOnlyDictionary<string, string>? validation = null, TimeSpan? retryAfter = null, string? requiredScope = null, Exception? innerException = null)
        : base(code, status, message, requestId, validation, retryAfter, requiredScope, innerException)
    {
    }
}

/// <summary>409 — conflito de estado (ex.: <c>RELEASE_NOTE_CONFLICT</c>, <c>AI_DISABLED</c>).</summary>
public class ConflictException : BfocusException
{
    /// <inheritdoc cref="BfocusException(string, int, string, string?, IReadOnlyDictionary{string, string}?, TimeSpan?, string?, Exception?)"/>
    public ConflictException(string code, int status, string message, string? requestId = null, IReadOnlyDictionary<string, string>? validation = null, TimeSpan? retryAfter = null, string? requiredScope = null, Exception? innerException = null)
        : base(code, status, message, requestId, validation, retryAfter, requiredScope, innerException)
    {
    }
}

/// <summary>422 — corpo ou parâmetro inválido (detalhe em <see cref="BfocusException.Validation"/>).</summary>
public class ValidationException : BfocusException
{
    /// <inheritdoc cref="BfocusException(string, int, string, string?, IReadOnlyDictionary{string, string}?, TimeSpan?, string?, Exception?)"/>
    public ValidationException(string code, int status, string message, string? requestId = null, IReadOnlyDictionary<string, string>? validation = null, TimeSpan? retryAfter = null, string? requiredScope = null, Exception? innerException = null)
        : base(code, status, message, requestId, validation, retryAfter, requiredScope, innerException)
    {
    }
}

/// <summary>429 — limite de requisições da chave (espera sugerida em <see cref="BfocusException.RetryAfter"/>).</summary>
public class RateLimitException : BfocusException
{
    /// <inheritdoc cref="BfocusException(string, int, string, string?, IReadOnlyDictionary{string, string}?, TimeSpan?, string?, Exception?)"/>
    public RateLimitException(string code, int status, string message, string? requestId = null, IReadOnlyDictionary<string, string>? validation = null, TimeSpan? retryAfter = null, string? requiredScope = null, Exception? innerException = null)
        : base(code, status, message, requestId, validation, retryAfter, requiredScope, innerException)
    {
    }
}

/// <summary>5xx — erro do servidor (informe o <see cref="BfocusException.RequestId"/> ao suporte).</summary>
public class ServerException : BfocusException
{
    /// <inheritdoc cref="BfocusException(string, int, string, string?, IReadOnlyDictionary{string, string}?, TimeSpan?, string?, Exception?)"/>
    public ServerException(string code, int status, string message, string? requestId = null, IReadOnlyDictionary<string, string>? validation = null, TimeSpan? retryAfter = null, string? requiredScope = null, Exception? innerException = null)
        : base(code, status, message, requestId, validation, retryAfter, requiredScope, innerException)
    {
    }
}

/// <summary>Falha de conexão ou tempo esgotado (<c>Status = 0</c>, <c>Code = "NETWORK_ERROR"</c>).</summary>
public class NetworkException : BfocusException
{
    /// <summary>Código de todo erro de rede.</summary>
    public const string NetworkErrorCode = "NETWORK_ERROR";

    /// <summary>Cria o erro de rede.</summary>
    /// <param name="message">Texto legível.</param>
    /// <param name="innerException">Causa original (<c>HttpRequestException</c>, tempo esgotado…).</param>
    /// <param name="requestId">O <c>X-Request-Id</c> que a SDK enviou.</param>
    public NetworkException(string message, Exception? innerException = null, string? requestId = null)
        : base(NetworkErrorCode, 0, message, requestId, innerException: innerException)
    {
    }
}
