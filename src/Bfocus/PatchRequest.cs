using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Bfocus.Internal;

namespace Bfocus;

/// <summary>
/// Base dos corpos de upsert. Os upserts do bFocus são PARCIAIS: só muda o que vai no corpo, e <c>null</c>
/// explícito LIMPA o campo. Aqui:
/// <list type="bullet">
/// <item>propriedade <c>null</c> (o padrão) = <b>omitida</b> — o campo não vai no corpo e fica como está;</item>
/// <item>para <b>limpar</b> (enviar <c>null</c>), adicione o campo em <see cref="ClearFields"/>, pelo nome C#
/// (<c>nameof</c>) ou pelo nome JSON.</item>
/// </list>
/// Se o campo tiver valor E estiver em <see cref="ClearFields"/>, vale o valor.
/// </summary>
/// <example>
/// <code>
/// // Muda o nome e limpa o telefone; e-mail, documento etc. ficam como estão.
/// await bfocus.Customers.UpsertAsync("ERP 1042", new CustomerUpsert
/// {
///     Name = "Padaria Estrela",
///     ClearFields = { nameof(CustomerUpsert.Phone) },
/// });
/// </code>
/// </example>
public abstract class PatchRequest
{
    private readonly ClearFieldSet _clearFields;

    private protected PatchRequest()
    {
        _clearFields = new ClearFieldSet(GetType());
    }

    /// <summary>
    /// Campos a LIMPAR (enviados como <c>null</c>). Aceita o nome da propriedade (<c>nameof(CustomerUpsert.Phone)</c>)
    /// ou o nome JSON (<c>"phone"</c>); nome desconhecido ou campo que não pode ser nulo lança
    /// <see cref="ArgumentException"/> na hora.
    /// </summary>
    [JsonIgnore]
    public ICollection<string> ClearFields => _clearFields;

    internal JsonObject ToJsonObject()
    {
        var body = new JsonObject();
        foreach (var field in FieldMap.For(GetType()).Fields)
        {
            var value = field.Property.GetValue(this);
            if (value is not null)
            {
                body[field.WireName] = JsonSerializer.SerializeToNode(value, field.Property.PropertyType, BfocusJson.Options);
            }
            else if (_clearFields.ContainsWireName(field.WireName))
            {
                body[field.WireName] = null;
            }
        }

        return body;
    }

    internal string ToJsonString() => ToJsonObject().ToJsonString(BfocusJson.Options);
}

/// <summary>Marca um campo de upsert que a API não aceita como <c>null</c>.</summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class NotClearableAttribute : Attribute
{
}

internal sealed class FieldMap
{
    private static readonly ConcurrentDictionary<Type, FieldMap> Cache = new ConcurrentDictionary<Type, FieldMap>();
    private readonly Dictionary<string, Field> _byName = new Dictionary<string, Field>(StringComparer.OrdinalIgnoreCase);
    private readonly Type _type;

    private FieldMap(Type type)
    {
        _type = type;
        var fields = new List<Field>();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var name = property.GetCustomAttribute<JsonPropertyNameAttribute>();
            if (name is null)
            {
                continue;
            }

            var field = new Field(property, name.Name, property.GetCustomAttribute<NotClearableAttribute>() is null);
            fields.Add(field);
            _byName[field.WireName] = field;
            _byName[property.Name] = field;
        }

        Fields = fields;
    }

    internal IReadOnlyList<Field> Fields { get; }

    internal static FieldMap For(Type type) => Cache.GetOrAdd(type, static t => new FieldMap(t));

    internal bool TryResolve(string name, out Field field) => _byName.TryGetValue(name ?? string.Empty, out field!);

    internal Field Resolve(string name)
    {
        if (!TryResolve(name, out var field))
        {
            var known = string.Join(", ", Fields.Where(f => f.Clearable).Select(f => f.WireName));
            throw new ArgumentException($"'{name}' não é um campo de {_type.Name}. Campos que podem ser limpos: {known}.", nameof(name));
        }

        return field;
    }

    internal sealed class Field
    {
        internal Field(PropertyInfo property, string wireName, bool clearable)
        {
            Property = property;
            WireName = wireName;
            Clearable = clearable;
        }

        internal PropertyInfo Property { get; }

        internal string WireName { get; }

        internal bool Clearable { get; }
    }
}

/// <summary>Conjunto de campos a limpar, normalizado para o nome JSON e validado ao adicionar.</summary>
internal sealed class ClearFieldSet : ICollection<string>
{
    private readonly Type _owner;
    private readonly HashSet<string> _wireNames = new HashSet<string>(StringComparer.Ordinal);

    internal ClearFieldSet(Type owner)
    {
        _owner = owner;
    }

    public int Count => _wireNames.Count;

    public bool IsReadOnly => false;

    public void Add(string item)
    {
        var field = FieldMap.For(_owner).Resolve(item);
        if (!field.Clearable)
        {
            throw new ArgumentException($"O campo '{field.WireName}' de {_owner.Name} não pode ser limpo (a API não aceita null nele).", nameof(item));
        }

        _wireNames.Add(field.WireName);
    }

    public bool Contains(string item) => FieldMap.For(_owner).TryResolve(item, out var field) && _wireNames.Contains(field.WireName);

    public bool Remove(string item) => FieldMap.For(_owner).TryResolve(item, out var field) && _wireNames.Remove(field.WireName);

    public void Clear() => _wireNames.Clear();

    public void CopyTo(string[] array, int arrayIndex) => _wireNames.CopyTo(array, arrayIndex);

    public IEnumerator<string> GetEnumerator() => _wireNames.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal bool ContainsWireName(string wireName) => _wireNames.Contains(wireName);
}
