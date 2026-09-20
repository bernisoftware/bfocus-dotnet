using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Bfocus.Internal;

/// <summary>
/// Transporte HTTP: headers, novas tentativas (com o MESMO X-Request-Id/Idempotency-Key), tempo limite por
/// tentativa e conversão de erro em <see cref="BfocusException"/>.
/// </summary>
internal sealed class BfocusHttp : IDisposable
{
    internal const string ApiPrefix = "/api/v1/integration";
    internal static readonly string ClientHeader = "bfocus-dotnet/" + BfocusClient.Version;

    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);
    private static readonly object RandomLock = new object();
    private static readonly Random Jitter = new Random();

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _authorization;
    private readonly string _baseUrl;
    private readonly TimeSpan _timeout;
    private readonly int _maxRetries;

    internal BfocusHttp(string apiKey, BfocusClientOptions options)
    {
        var baseUrl = (options.BaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            throw new ArgumentException($"BaseUrl inválida: '{options.BaseUrl}'. Use uma URL absoluta http(s), ex.: {BfocusClient.DefaultBaseUrl}.", nameof(options));
        }

        if (options.Timeout <= TimeSpan.Zero && options.Timeout != System.Threading.Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Timeout precisa ser positivo.");
        }

        if (options.MaxRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxRetries não pode ser negativo.");
        }

        _authorization = "Bearer " + apiKey;
        _baseUrl = baseUrl;
        _timeout = options.Timeout;
        _maxRetries = options.MaxRetries;

        if (options.HttpClient is not null)
        {
            _http = options.HttpClient;
            _ownsHttp = false;
        }
        else
        {
            _http = options.HttpMessageHandler is not null
                ? new HttpClient(options.HttpMessageHandler, disposeHandler: false)
                : new HttpClient(CreateDefaultHandler(), disposeHandler: true);

            // O tempo limite é controlado por tentativa (ver SendAsync); o do HttpClient fica desligado.
            _http.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
            _ownsHttp = true;
        }
    }

    /// <summary>Espera entre tentativas. Substituível nos testes (a suíte não pode dormir de verdade).</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = static (delay, ct) => Task.Delay(delay, ct);

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    internal async Task<T> RequestAsync<T>(HttpMethod method, string path, Query? query, string? body, RequestOptions? options, CancellationToken cancellationToken)
    {
        using var envelope = await SendAsync(method, path, query, body, options, cancellationToken).ConfigureAwait(false);
        return BfocusJson.ReadData<T>(envelope);
    }

    internal async Task<Page<T>> RequestPageAsync<T>(string path, Query? query, RequestOptions? options, CancellationToken cancellationToken)
    {
        using var envelope = await SendAsync(HttpMethod.Get, path, query, null, options, cancellationToken).ConfigureAwait(false);
        return BfocusJson.ReadPage<T>(envelope);
    }

    private async Task<Envelope> SendAsync(HttpMethod method, string path, Query? query, string? body, RequestOptions? options, CancellationToken cancellationToken)
    {
        var url = _baseUrl + ApiPrefix + path + (query?.ToString() ?? string.Empty);

        // Gerados UMA vez por chamada lógica e repetidos em toda nova tentativa: é o que torna a repetição
        // segura (a API devolve a resposta original com Idempotent-Replayed: true).
        var requestId = Guid.NewGuid().ToString("N");
        string? idempotencyKey = null;
        if (IsWrite(method))
        {
            idempotencyKey = string.IsNullOrEmpty(options?.IdempotencyKey) ? Guid.NewGuid().ToString() : options!.IdempotencyKey;
        }

        var timeout = options?.Timeout ?? _timeout;

        for (var attempt = 0; ; attempt++)
        {
            BfocusException error;
            TimeSpan? retryAfter = null;

            using (var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                attemptCts.CancelAfter(timeout);
                try
                {
                    using var request = BuildRequest(method, url, body, requestId, idempotencyKey);
                    using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, attemptCts.Token).ConfigureAwait(false);
                    var text = await ReadBodyAsync(response.Content, attemptCts.Token).ConfigureAwait(false);
                    var status = (int)response.StatusCode;
                    // request_id de erro: corpo → header X-Request-Id → o que a SDK enviou (a API ecoa o do cliente).
                    if (status >= 200 && status < 300)
                    {
                        return Envelope.Parse(text, status, HeaderValue(response.Headers, "X-Request-Id") ?? requestId);
                    }

                    retryAfter = ParseRetryAfter(response.Headers);
                    error = ErrorFactory.FromResponse(status, text, response.Headers, retryAfter, requestId);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException ex)
                {
                    error = new NetworkException($"Tempo esgotado após {timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} s em {method.Method} {path} (request_id {requestId}).", ex, requestId);
                }
                catch (HttpRequestException ex)
                {
                    error = new NetworkException($"Falha de conexão em {method.Method} {path}: {ex.Message} (request_id {requestId})", ex, requestId);
                }
                catch (IOException ex)
                {
                    error = new NetworkException($"Falha de conexão em {method.Method} {path}: {ex.Message} (request_id {requestId})", ex, requestId);
                }
            }

            if (attempt >= _maxRetries || !IsRetryable(error))
            {
                throw error;
            }

            await Delay(BackoffDelay(attempt, retryAfter), cancellationToken).ConfigureAwait(false);
        }
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string url, string? body, string requestId, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Authorization", _authorization);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("X-Bfocus-Client", ClientHeader);
        request.Headers.TryAddWithoutValidation("User-Agent", ClientHeader);
        request.Headers.TryAddWithoutValidation("X-Request-Id", requestId);
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        if (body is not null)
        {
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Content = content;
        }

        return request;
    }

    /// <summary>Espera antes da nova tentativa <paramref name="attempt"/> (0 = a primeira nova tentativa).</summary>
    internal static TimeSpan BackoffDelay(int attempt, TimeSpan? retryAfter)
    {
        if (retryAfter is { } fromServer)
        {
            if (fromServer < TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            return fromServer > MaxRetryAfter ? MaxRetryAfter : fromServer;
        }

        var seconds = Math.Min(8.0, 0.5 * Math.Pow(2, attempt));
        double jitter;
        lock (RandomLock)
        {
            jitter = Jitter.NextDouble() * 0.25 * seconds;
        }

        return TimeSpan.FromSeconds(seconds + jitter);
    }

    private static bool IsWrite(HttpMethod method) =>
        method == HttpMethod.Post || method == HttpMethod.Put || method == HttpMethod.Delete || method.Method == "PATCH";

    private static bool IsRetryable(BfocusException error) =>
        error is NetworkException || error.Status == 429 || error.Status == 502 || error.Status == 503 || error.Status == 504;

    private static Task<string> ReadBodyAsync(HttpContent? content, CancellationToken cancellationToken)
    {
        if (content is null)
        {
            return Task.FromResult(string.Empty);
        }
#if NET5_0_OR_GREATER
        return content.ReadAsStringAsync(cancellationToken);
#else
        return content.ReadAsStringAsync();
#endif
    }

    private static HttpMessageHandler CreateDefaultHandler()
    {
#if NET5_0_OR_GREATER
        // Recicla conexões para acompanhar mudanças de DNS num cliente de vida longa.
        return new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
#else
        return new HttpClientHandler();
#endif
    }

    internal static string? HeaderValue(HttpResponseHeaders headers, string name)
    {
        if (headers.TryGetValues(name, out var values))
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
        }

        return null;
    }

    /// <summary><c>Retry-After</c> em segundos (inteiro ou decimal) ou data HTTP.</summary>
    internal static TimeSpan? ParseRetryAfter(HttpResponseHeaders headers)
    {
        var raw = HeaderValue(headers, "Retry-After");
        if (raw is null)
        {
            return null;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0 && !double.IsInfinity(seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
        {
            var delta = date - DateTimeOffset.UtcNow;
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        return null;
    }
}

/// <summary>Envelope de sucesso <c>{"code", "data", "message"[, "pagination"]}</c>.</summary>
internal sealed class Envelope : IDisposable
{
    private readonly JsonDocument _document;

    private Envelope(JsonDocument document, int status, string? requestId)
    {
        _document = document;
        Status = status;
        RequestId = requestId;
    }

    internal int Status { get; }

    internal string? RequestId { get; }

    internal JsonElement Root => _document.RootElement;

    internal static Envelope Parse(string text, int status, string? requestId)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            throw ErrorFactory.InvalidResponse(status, requestId, "o corpo da resposta não é JSON", ex);
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw ErrorFactory.InvalidResponse(status, requestId, "o corpo da resposta não é um objeto JSON", null);
        }

        // request_id: o do corpo, se houver; senão o que o chamador resolveu (header → enviado pela SDK).
        if (document.RootElement.TryGetProperty("request_id", out var bodyRequestId)
            && bodyRequestId.ValueKind == JsonValueKind.String
            && !string.IsNullOrEmpty(bodyRequestId.GetString()))
        {
            requestId = bodyRequestId.GetString();
        }

        return new Envelope(document, status, requestId);
    }

    public void Dispose() => _document.Dispose();
}

/// <summary>Converte uma resposta de erro no <see cref="BfocusException"/> certo (BRIEF §4).</summary>
internal static class ErrorFactory
{
    internal const string InvalidResponseCode = "INVALID_RESPONSE";

    internal static BfocusException FromResponse(int status, string text, HttpResponseHeaders headers, TimeSpan? retryAfter, string sentRequestId)
    {
        string? error = null;
        string? bodyMessage = null;
        string? requestId = null;
        var validation = new Dictionary<string, string>();
        var errorData = new Dictionary<string, JsonElement>();

        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    error = StringOrNull(root, "error");
                    bodyMessage = StringOrNull(root, "message");
                    requestId = StringOrNull(root, "request_id");
                    // `data`: o detalhe estruturado do erro. A API também o repete em `validation`, mas quem lê
                    // o erro precisa alcançá-lo sem depender dessa duplicação. Clone() porque o JsonDocument
                    // é descartado ao sair daqui.
                    if (root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var field in d.EnumerateObject())
                        {
                            errorData[field.Name] = field.Value.Clone();
                        }
                    }

                    if (root.TryGetProperty("validation", out var v) && v.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var field in v.EnumerateObject())
                        {
                            validation[field.Name] = field.Value.ValueKind == JsonValueKind.String ? field.Value.GetString() ?? string.Empty : field.Value.GetRawText();
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Corpo não-JSON (proxy, balanceador…): cai no HTTP_<status> abaixo.
            }
        }

        var code = error ?? bodyMessage ?? "HTTP_" + status.ToString(CultureInfo.InvariantCulture);

        // Corpo → header X-Request-Id → o que a SDK enviou (BRIEF §10.2).
        requestId ??= BfocusHttp.HeaderValue(headers, "X-Request-Id") ?? sentRequestId;
        var requiredScope = BfocusHttp.HeaderValue(headers, "X-Required-Scope");
        var exposedRetryAfter = status == 429 ? retryAfter : null;

        var message = new StringBuilder(code);
        if (bodyMessage is not null && bodyMessage != code)
        {
            message.Append(": ").Append(bodyMessage);
        }

        message.Append(" (HTTP ").Append(status.ToString(CultureInfo.InvariantCulture));
        if (requiredScope is not null)
        {
            message.Append("; escopo exigido: ").Append(requiredScope);
        }

        foreach (var pair in validation)
        {
            message.Append("; ").Append(pair.Key).Append(": ").Append(pair.Value);
        }

        if (exposedRetryAfter is { } wait)
        {
            message.Append("; tente de novo em ").Append(wait.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)).Append(" s");
        }

        if (requestId is not null)
        {
            message.Append("; request_id ").Append(requestId);
        }

        message.Append(')');
        var text2 = message.ToString();

        return status switch
        {
            401 => new AuthenticationException(code, status, text2, requestId, validation, exposedRetryAfter, requiredScope, errorData: errorData),
            403 => new PermissionDeniedException(code, status, text2, requestId, validation, exposedRetryAfter, requiredScope, errorData: errorData),
            404 => new NotFoundException(code, status, text2, requestId, validation, exposedRetryAfter, requiredScope, errorData: errorData),
            409 => new ConflictException(code, status, text2, requestId, validation, exposedRetryAfter, requiredScope, errorData: errorData),
            422 => new ValidationException(code, status, text2, requestId, validation, exposedRetryAfter, requiredScope, errorData: errorData),
            429 => new RateLimitException(code, status, text2, requestId, validation, exposedRetryAfter, requiredScope, errorData: errorData),
            >= 500 and <= 599 => new ServerException(code, status, text2, requestId, validation, exposedRetryAfter, requiredScope, errorData: errorData),
            _ => new BfocusException(code, status, text2, requestId, validation, exposedRetryAfter, requiredScope, errorData: errorData),
        };
    }

    internal static BfocusException InvalidResponse(int status, string? requestId, string reason, Exception? inner) =>
        new BfocusException(
            InvalidResponseCode,
            status,
            $"{InvalidResponseCode}: {reason} (HTTP {status.ToString(CultureInfo.InvariantCulture)}{(requestId is null ? string.Empty : "; request_id " + requestId)})",
            requestId,
            innerException: inner);

    private static string? StringOrNull(JsonElement obj, string name)
    {
        if (obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var s = value.GetString();
            return string.IsNullOrEmpty(s) ? null : s;
        }

        return null;
    }
}
