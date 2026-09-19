using System;

namespace Bfocus;

/// <summary>Opções de UMA chamada.</summary>
public sealed class RequestOptions
{
    /// <summary>
    /// <c>Idempotency-Key</c> própria para escritas (POST/PUT/DELETE). Sem ela, a SDK gera uma por chamada e a
    /// repete nas novas tentativas. Informe a sua para que um reenvio feito pela SUA aplicação (ex.: job
    /// reexecutado) também seja deduplicado pela API. Ignorada em leituras.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>Tempo limite por tentativa só desta chamada (sobrepõe <see cref="BfocusClientOptions.Timeout"/>).</summary>
    public TimeSpan? Timeout { get; set; }

    internal RequestOptions WithIdempotencyKey(string? key) => new RequestOptions { IdempotencyKey = key, Timeout = Timeout };
}
