using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Bfocus;

/// <summary>
/// Identidade do widget do bFocus (local, sem rede e sem chave de API). Rode no SEU backend: assine a identidade
/// do usuário logado e entregue a assinatura ao front, que abre o widget com ela.
/// </summary>
public static class WidgetIdentity
{
    /// <summary>
    /// <c>HMAC-SHA256(secret, "v1:" + userExternalId + ":" + customerExternalId)</c> (UTF-8), em hexadecimal minúsculo.
    /// </summary>
    /// <param name="secret">Segredo do widget (painel do bFocus). Nunca o envie ao navegador.</param>
    /// <param name="userExternalId"><c>external_id</c> do usuário logado no seu sistema.</param>
    /// <param name="customerExternalId"><c>external_id</c> do cliente (empresa) desse usuário.</param>
    /// <returns>Assinatura hexadecimal minúscula (64 caracteres).</returns>
    public static string Sign(string secret, string userExternalId, string customerExternalId)
    {
        Validate(secret, userExternalId, customerExternalId);
        return HmacHex(secret, "v1:" + userExternalId + ":" + customerExternalId);
    }

    /// <summary>
    /// Assinatura v2, com validade: <c>"v2.&lt;ts&gt;.&lt;hex&gt;"</c>, em que <c>ts</c> são os segundos unix (inteiros) de
    /// <paramref name="at"/> e <c>hex</c> é <c>HMAC-SHA256(secret, "v2:" + ts + ":" + userExternalId + ":" + customerExternalId)</c>
    /// (UTF-8) em hexadecimal minúsculo. A API aceita a v2 de 7 dias atrás até 5 minutos à frente: gere a cada
    /// renderização da página, nunca guarde. Vai no <c>userHash</c> do widget, no mesmo lugar da v1 (que continua aceita).
    /// </summary>
    /// <param name="secret">Segredo do widget (painel do bFocus). Nunca o envie ao navegador.</param>
    /// <param name="userExternalId"><c>external_id</c> do usuário logado no seu sistema. Não pode ter <c>:</c> (é o separador).</param>
    /// <param name="customerExternalId"><c>external_id</c> do cliente (empresa) desse usuário (pode ter <c>:</c>).</param>
    /// <param name="at">Instante da assinatura (padrão: agora). Não pode ser anterior a 1970-01-01 UTC.</param>
    /// <returns><c>v2.&lt;ts&gt;.&lt;hex&gt;</c>.</returns>
    /// <exception cref="ArgumentException">Segredo vazio, <c>:</c> no usuário ou instante antes da época unix.</exception>
    public static string SignV2(string secret, string userExternalId, string customerExternalId, DateTimeOffset? at = null)
    {
        Validate(secret, userExternalId, customerExternalId);
        if (userExternalId.IndexOf(':') >= 0)
        {
            throw new ArgumentException("O id do usuário não pode ter ':' na assinatura v2 (é o separador; a API recusa). Use '-' (ex.: 'app-77').", nameof(userExternalId));
        }

        var instant = at ?? DateTimeOffset.UtcNow;
        if (instant < DateTimeOffset.FromUnixTimeSeconds(0))
        {
            throw new ArgumentException("O instante da assinatura v2 não pode ser anterior a 1970-01-01T00:00:00Z.", nameof(at));
        }

        var ts = instant.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        return "v2." + ts + "." + HmacHex(secret, "v2:" + ts + ":" + userExternalId + ":" + customerExternalId);
    }

    private static void Validate(string secret, string userExternalId, string customerExternalId)
    {
        if (secret is null)
        {
            throw new ArgumentNullException(nameof(secret));
        }

        if (secret.Length == 0)
        {
            throw new ArgumentException("O segredo do widget não pode ser vazio.", nameof(secret));
        }

        if (userExternalId is null)
        {
            throw new ArgumentNullException(nameof(userExternalId));
        }

        if (customerExternalId is null)
        {
            throw new ArgumentNullException(nameof(customerExternalId));
        }
    }

    private static string HmacHex(string secret, string message)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var mac = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        var hex = new StringBuilder(mac.Length * 2);
        foreach (var b in mac)
        {
            hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return hex.ToString();
    }
}
