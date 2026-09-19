using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace Bfocus.Internal;

/// <summary>
/// Regra comum dos lotes de clientes e pessoas: até 500 itens por chamada, sem divisão automática (o <c>index</c> de
/// cada resultado é a posição no lote enviado); lista vazia não faz requisição.
/// </summary>
internal static class Batch
{
    internal const int MaxItems = 500;

    /// <summary>Materializa e valida o tamanho ANTES de qualquer requisição.</summary>
    internal static List<T> Take<T>(IEnumerable<T> items, string op, string paramName)
    {
        if (items is null)
        {
            throw new ArgumentNullException(paramName);
        }

        var list = items.ToList();
        if (list.Count > MaxItems)
        {
            throw new ArgumentException(
                string.Format(CultureInfo.InvariantCulture, "{0} aceita até {1} itens por chamada (recebeu {2}); divida em lotes de {1}.", op, MaxItems, list.Count),
                paramName);
        }

        return list;
    }

    internal static string Body(JsonArray items) => new JsonObject { ["items"] = items }.ToJsonString(BfocusJson.Options);
}

/// <summary>Corpo de <c>PUT …/identifiers/{extra_id}</c>: <c>{"label": …}</c> só quando veio rótulo; senão, sem corpo.</summary>
internal static class IdentifierBody
{
    internal static string? Label(string? label) =>
        label is null ? null : new JsonObject { ["label"] = label }.ToJsonString(BfocusJson.Options);
}
