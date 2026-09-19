using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Bfocus.Internal;

/// <summary>Parâmetros de caminho: percent-encoding por segmento (<c>ERP 1042</c> → <c>ERP%201042</c>).</summary>
internal static class PathSegment
{
    internal static string Encode(string value, string paramName)
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName);
        }

        if (value.Length == 0)
        {
            throw new ArgumentException($"'{paramName}' não pode ser vazio.", paramName);
        }

        if (value == "." || value == "..")
        {
            throw new ArgumentException($"'{paramName}' não pode ser '.' nem '..'.", paramName);
        }

        return Uri.EscapeDataString(value);
    }

    /// <summary><c>external_id</c> de artigo não aceita <c>/</c> (a API recusa) — use <c>:</c> para hierarquia.</summary>
    internal static string KbExternalId(string value, string paramName)
    {
        if (value is not null && value.IndexOf('/') >= 0)
        {
            throw new ArgumentException($"'{paramName}' de artigo não aceita '/' — use ':' para hierarquia (ex.: 'git:guia:instalacao').", paramName);
        }

        return Encode(value!, paramName);
    }
}

/// <summary>Query string: omite o que não foi informado; booleanos <c>true</c>/<c>false</c>; datas ISO 8601 UTC com <c>Z</c>.</summary>
internal sealed class Query
{
    private readonly List<KeyValuePair<string, string>> _pairs = new List<KeyValuePair<string, string>>();

    internal Query Add(string name, string? value)
    {
        if (value is not null)
        {
            _pairs.Add(new KeyValuePair<string, string>(name, value));
        }

        return this;
    }

    internal Query Add(string name, int? value) =>
        value is null ? this : Add(name, value.Value.ToString(CultureInfo.InvariantCulture));

    internal Query Add(string name, bool? value) =>
        value is null ? this : Add(name, value.Value ? "true" : "false");

    internal Query Add(string name, DateTimeOffset? value) =>
        value is null ? this : Add(name, FormatDate(value.Value));

    /// <summary><c>2026-09-01T03:00:00Z</c> (fração de segundo só quando houver).</summary>
    internal static string FormatDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);

    public override string ToString()
    {
        if (_pairs.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var pair in _pairs)
        {
            sb.Append(sb.Length == 0 ? '?' : '&')
              .Append(Uri.EscapeDataString(pair.Key))
              .Append('=')
              .Append(Uri.EscapeDataString(pair.Value));
        }

        return sb.ToString();
    }
}
