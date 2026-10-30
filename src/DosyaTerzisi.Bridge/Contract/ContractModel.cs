using System.Collections;
using System.Reflection;
using System.Text.Json;

namespace DosyaTerzisi.Bridge.Contract;

/// <summary>Bir alanın tipi. <see cref="Kind"/>: string, integer, number, boolean, enum, array, object.</summary>
public sealed record Shape(string Kind, string? Name = null, Shape? Item = null, bool Nullable = false);

public sealed record ObjectDef(string Name, IReadOnlyList<FieldDef> Fields);

public sealed record FieldDef(string Name, Shape Shape);

public sealed record EnumDef(string Name, IReadOnlyList<string> Values);

public sealed record MethodShape(string Name, Shape Params, Shape Result);

/// <summary>
/// <see cref="Dtos"/> içindeki tiplerden çıkarılan sözleşme modeli. TypeScript üreticisi ve gelen mesajların
/// doğrulayıcısı aynı modeli kullanır; böylece iki taraf aynı şeyi söyler.
/// </summary>
public sealed class ContractModel
{
    private readonly SortedDictionary<string, ObjectDef> _objects = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, EnumDef> _enums = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Type> _types = new();
    private readonly List<MethodShape> _methods = [];

    public IReadOnlyCollection<ObjectDef> Objects => _objects.Values;
    public IReadOnlyCollection<EnumDef> Enums => _enums.Values;
    public IReadOnlyList<MethodShape> Methods => _methods;

    public Type TypeOf(string name) => _types[name];

    public ObjectDef Object(string name) => _objects[name];

    public EnumDef Enum(string name) => _enums[name];

    public static readonly ContractModel Default = Build(Catalog.Methods);

    public static ContractModel Build(IEnumerable<MethodSpec> specs)
    {
        var model = new ContractModel();
        foreach (var spec in specs)
        {
            var parameters = model.ShapeOf(spec.Params, nullable: false);
            var result = model.ShapeOf(spec.Result, spec.NullableResult);
            model._methods.Add(new MethodShape(spec.Name, parameters, result));
        }
        return model;
    }

    private Shape ShapeOf(Type type, bool nullable)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            type = underlying;
            nullable = true;
        }

        if (type == typeof(string)) return new Shape("string", Nullable: nullable);
        if (type == typeof(bool)) return new Shape("boolean", Nullable: nullable);
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
            return new Shape("integer", Nullable: nullable);
        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal))
            return new Shape("number", Nullable: nullable);

        if (type.IsEnum)
        {
            RegisterEnum(type);
            return new Shape("enum", type.Name, Nullable: nullable);
        }

        var item = ElementType(type);
        if (item is not null) return new Shape("array", Item: ShapeOf(item, nullable: false), Nullable: nullable);

        RegisterObject(type);
        return new Shape("object", type.Name, Nullable: nullable);
    }

    private static Type? ElementType(Type type)
    {
        if (type.IsArray) return type.GetElementType();
        if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type)) return null;

        return type.IsGenericType ? type.GetGenericArguments()[0] : null;
    }

    private void RegisterEnum(Type type)
    {
        if (_enums.ContainsKey(type.Name)) return;
        _types[type.Name] = type;
        _enums[type.Name] = new EnumDef(type.Name, System.Enum.GetNames(type).Select(n => JsonNamingPolicy.CamelCase.ConvertName(n)).ToList());
    }

    private void RegisterObject(Type type)
    {
        if (_objects.ContainsKey(type.Name)) return;
        _types[type.Name] = type;

        // Önce yer aç: kendine başvuran tipler sonsuz döngüye girmesin.
        _objects[type.Name] = new ObjectDef(type.Name, []);

        var context = new NullabilityInfoContext();
        var fields = new List<FieldDef>();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            bool nullable = property.PropertyType.IsValueType
                ? Nullable.GetUnderlyingType(property.PropertyType) is not null
                : context.Create(property).WriteState == NullabilityState.Nullable;

            fields.Add(new FieldDef(JsonNamingPolicy.CamelCase.ConvertName(property.Name), ShapeOf(property.PropertyType, nullable)));
        }
        _objects[type.Name] = new ObjectDef(type.Name, fields);
    }
}
