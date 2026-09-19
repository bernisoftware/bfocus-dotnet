using System;
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

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var mac = hmac.ComputeHash(Encoding.UTF8.GetBytes("v1:" + userExternalId + ":" + customerExternalId));
        var hex = new StringBuilder(mac.Length * 2);
        foreach (var b in mac)
        {
            hex.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return hex.ToString();
    }
}
