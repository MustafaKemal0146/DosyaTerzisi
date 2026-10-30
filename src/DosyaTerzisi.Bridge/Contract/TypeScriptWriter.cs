using System.Text;
using System.Text.Json;

namespace DosyaTerzisi.Bridge.Contract;

/// <summary>Sözleşme modelinden <c>web/lib/wire.generated.ts</c> dosyasını yazar.</summary>
public static class TypeScriptWriter
{
    public static string Write(ContractModel model)
    {
        var sb = new StringBuilder();
        sb.Append(
            """
            // Bu dosya otomatik üretilir; elle değiştirmeyin.
            // Kaynak: src/DosyaTerzisi.Bridge/Contract/Dtos.cs ve Catalog.cs
            // Yenilemek için (repo kökünde): UPDATE_CONTRACT=1 dotnet test --filter ContractTests

            """);
        sb.Append('\n');

        foreach (var def in model.Enums)
            sb.Append($"export type {def.Name} = {string.Join(" | ", def.Values.Select(v => $"\"{v}\""))};\n\n");

        foreach (var def in model.Objects)
        {
            if (def.Fields.Count == 0)
            {
                sb.Append($"export type {def.Name} = Record<string, never>;\n\n");
                continue;
            }

            sb.Append($"export interface {def.Name} {{\n");
            foreach (var field in def.Fields) sb.Append($"  {field.Name}: {TypeOf(field.Shape)};\n");
            sb.Append("}\n\n");
        }

        sb.Append("/** Arayüzün uygulamadan isteyebileceği her iş: parametre ve cevap tipi. */\n");
        sb.Append("export interface Methods {\n");
        foreach (var method in model.Methods)
            sb.Append($"  {method.Name}: {{ params: {TypeOf(method.Params)}; result: {TypeOf(method.Result)} }};\n");
        sb.Append("}\n\n");

        sb.Append("export type MethodName = keyof Methods;\n\n");
        sb.Append($"export const methodNames: MethodName[] = [{string.Join(", ", model.Methods.Select(m => $"\"{m.Name}\""))}];\n\n");
        sb.Append($"export const wireVersion = {Wire.Version};\n\n");

        sb.Append(
            """
            export type FieldShape =
              | { kind: "string" | "integer" | "number" | "boolean"; nullable?: boolean }
              | { kind: "enum"; name: string; nullable?: boolean }
              | { kind: "object"; name: string; nullable?: boolean }
              | { kind: "array"; item: FieldShape; nullable?: boolean };

            export type TypeShape =
              | { kind: "enum"; values: string[] }
              | { kind: "object"; fields: Record<string, FieldShape> };


            """);

        sb.Append("/** Her tipin alanları; gelen mesajları denetlemek için (wire.ts). */\n");
        sb.Append("export const shapes: Record<string, TypeShape> = {\n");
        foreach (var def in model.Enums)
            sb.Append($"  {def.Name}: {{ kind: \"enum\", values: [{string.Join(", ", def.Values.Select(v => $"\"{v}\""))}] }},\n");
        foreach (var def in model.Objects)
        {
            sb.Append($"  {def.Name}: {{ kind: \"object\", fields: {{");
            sb.Append(string.Join(",", def.Fields.Select(f => $" {f.Name}: {ShapeLiteral(f.Shape)}")));
            sb.Append(def.Fields.Count == 0 ? "}" : " }");
            sb.Append(" },\n");
        }
        sb.Append("};\n\n");

        sb.Append("/** Her işin parametre ve cevap tipi (denetim için). */\n");
        sb.Append("export const methodShapes: Record<MethodName, { params: FieldShape; result: FieldShape }> = {\n");
        foreach (var method in model.Methods)
            sb.Append($"  {method.Name}: {{ params: {ShapeLiteral(method.Params)}, result: {ShapeLiteral(method.Result)} }},\n");
        sb.Append("};\n");

        return sb.ToString();
    }

    private static string TypeOf(Shape shape)
    {
        string text = shape.Kind switch
        {
            "string" => "string",
            "boolean" => "boolean",
            "integer" or "number" => "number",
            "enum" or "object" => shape.Name!,
            "array" => ArrayOf(shape.Item!),
            _ => throw new InvalidOperationException($"Bilinmeyen tip: {shape.Kind}"),
        };
        return shape.Nullable ? $"{text} | null" : text;
    }

    private static string ArrayOf(Shape item)
    {
        string inner = TypeOf(item);
        return item.Nullable ? $"({inner})[]" : $"{inner}[]";
    }

    private static string ShapeLiteral(Shape shape)
    {
        var parts = new List<string> { $"kind: \"{shape.Kind}\"" };
        if (shape.Kind is "enum" or "object") parts.Add($"name: {JsonSerializer.Serialize(shape.Name)}");
        if (shape.Kind == "array") parts.Add($"item: {ShapeLiteral(shape.Item!)}");
        if (shape.Nullable) parts.Add("nullable: true");
        return $"{{ {string.Join(", ", parts)} }}";
    }
}
