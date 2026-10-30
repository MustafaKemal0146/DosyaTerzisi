using System.Text.Json;

namespace DosyaTerzisi.Bridge.Contract;

/// <summary>
/// Gelen JSON'un sözleşmeye uyup uymadığını denetler: her alan var, tipi doğru, fazladan alan yok, sayılar tam sayı,
/// metinler bilinen bir değer. TypeScript tarafındaki <c>conforms</c> ile aynı kuralları uygular.
/// </summary>
public sealed class ContractValidator(ContractModel model)
{
    public static readonly ContractValidator Default = new(ContractModel.Default);

    public IReadOnlyList<string> Validate(JsonElement value, Shape shape)
    {
        var errors = new List<string>();
        Check(value, shape, "$", errors);
        return errors;
    }

    public IReadOnlyList<string> ValidateParams(string method, JsonElement value) =>
        Validate(value, model.Methods.First(m => m.Name == method).Params);

    public IReadOnlyList<string> ValidateResult(string method, JsonElement value) =>
        Validate(value, model.Methods.First(m => m.Name == method).Result);

    private void Check(JsonElement value, Shape shape, string path, List<string> errors)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            if (!shape.Nullable) errors.Add($"{path}: boş (null) olamaz");
            return;
        }

        switch (shape.Kind)
        {
            case "string":
                if (value.ValueKind != JsonValueKind.String) errors.Add($"{path}: metin olmalı");
                break;

            case "boolean":
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) errors.Add($"{path}: doğru/yanlış olmalı");
                break;

            case "number":
                if (value.ValueKind != JsonValueKind.Number) errors.Add($"{path}: sayı olmalı");
                break;

            case "integer":
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out _)) errors.Add($"{path}: tam sayı olmalı");
                break;

            case "enum":
                if (value.ValueKind != JsonValueKind.String || !model.Enum(shape.Name!).Values.Contains(value.GetString()!))
                    errors.Add($"{path}: şunlardan biri olmalı: {string.Join(", ", model.Enum(shape.Name!).Values)}");
                break;

            case "array":
                if (value.ValueKind != JsonValueKind.Array)
                {
                    errors.Add($"{path}: liste olmalı");
                    break;
                }
                int index = 0;
                foreach (var item in value.EnumerateArray()) Check(item, shape.Item!, $"{path}[{index++}]", errors);
                break;

            case "object":
                CheckObject(value, model.Object(shape.Name!), path, errors);
                break;
        }
    }

    private void CheckObject(JsonElement value, ObjectDef def, string path, List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{path}: nesne olmalı");
            return;
        }

        var known = def.Fields.Select(f => f.Name).ToHashSet();
        foreach (var property in value.EnumerateObject())
            if (!known.Contains(property.Name)) errors.Add($"{path}.{property.Name}: tanımsız alan");

        foreach (var field in def.Fields)
        {
            if (value.TryGetProperty(field.Name, out var child)) Check(child, field.Shape, $"{path}.{field.Name}", errors);
            else errors.Add($"{path}.{field.Name}: eksik");
        }
    }
}
