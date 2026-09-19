using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Bfocus.Internal;

internal static class BfocusJson
{
    /// <summary>
    /// Nomes vêm dos <c>[JsonPropertyName]</c> dos modelos; campos desconhecidos na resposta são ignorados
    /// (a API ganha campos sem aviso). Acentos saem sem escape; só os caracteres sensíveis a HTML são escapados.
    /// </summary>
    internal static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    internal static T ReadData<T>(Envelope envelope)
    {
        if (!envelope.Root.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
        {
            throw ErrorFactory.InvalidResponse(envelope.Status, envelope.RequestId, "a resposta não trouxe 'data'", null);
        }

        return Deserialize<T>(data, envelope);
    }

    internal static Page<T> ReadPage<T>(Envelope envelope)
    {
        var items = ReadData<List<T>>(envelope);
        int page = 1, pageSize = items.Count, total = items.Count, pages = 1;
        if (envelope.Root.TryGetProperty("pagination", out var p) && p.ValueKind == JsonValueKind.Object)
        {
            page = Int(p, "page", page);
            pageSize = Int(p, "page_size", pageSize);
            total = Int(p, "total", total);
            pages = Int(p, "pages", pages);
        }

        return new Page<T>(items, page, pageSize, total, pages);
    }

    private static T Deserialize<T>(JsonElement element, Envelope envelope)
    {
        try
        {
            var value = element.Deserialize<T>(Options);
            if (value is null)
            {
                throw ErrorFactory.InvalidResponse(envelope.Status, envelope.RequestId, "a resposta não trouxe 'data'", null);
            }

            return value;
        }
        catch (JsonException ex)
        {
            throw ErrorFactory.InvalidResponse(envelope.Status, envelope.RequestId, "formato inesperado em 'data': " + ex.Message, ex);
        }
        catch (NotSupportedException ex)
        {
            throw ErrorFactory.InvalidResponse(envelope.Status, envelope.RequestId, "formato inesperado em 'data': " + ex.Message, ex);
        }
    }

    private static int Int(JsonElement obj, string name, int fallback) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : fallback;
}
