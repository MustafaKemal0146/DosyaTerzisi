using System.Text.Json;
using System.Text.Json.Nodes;
using DosyaTerzisi.Bridge.Contract;

namespace DosyaTerzisi.Tests;

internal static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    public static string Contract => Path.Combine(Root, "contract", "fixtures");

    public static string GeneratedTypeScript => Path.Combine(Root, "web", "lib", "wire.generated.ts");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "DosyaTerzisi.sln"))) return dir.FullName;

        throw new InvalidOperationException("DosyaTerzisi.sln bulunamadı.");
    }
}

/// <summary>
/// Sözleşmenin iki yüzünü birbirine bağlar: C# tipleri → üretilen TypeScript dosyası ve <c>contract/fixtures</c> altındaki
/// örnek JSON dosyaları. TypeScript testleri aynı örnekleri okur. Bir şey değiştiyse bu testler kırmızı olur;
/// bilerek değiştirdiyseniz yenileyin:  <c>UPDATE_CONTRACT=1 dotnet test --filter ContractTests</c>
/// </summary>
public class ContractTests
{
    private static readonly bool Update = Environment.GetEnvironmentVariable("UPDATE_CONTRACT") == "1";
    private static readonly ContractModel Model = ContractModel.Default;

    // ---- Üretilen TypeScript -----------------------------------------------------------------------------------

    [Fact]
    public void GeneratedTypeScript_IsUpToDate()
    {
        string expected = TypeScriptWriter.Write(Model).ReplaceLineEndings("\n");

        if (Update) File.WriteAllText(RepoPaths.GeneratedTypeScript, expected);

        Assert.True(File.Exists(RepoPaths.GeneratedTypeScript), "web/lib/wire.generated.ts yok. UPDATE_CONTRACT=1 ile üretin.");
        Assert.Equal(expected, File.ReadAllText(RepoPaths.GeneratedTypeScript).ReplaceLineEndings("\n"));
    }

    // ---- Örnek dosyalar ----------------------------------------------------------------------------------------

    private static IEnumerable<(string Path, string Json)> ExpectedFixtures()
    {
        foreach (var def in Model.Objects)
            yield return (Path.Combine("types", $"{def.Name}.json"), Serialize(ContractSamples.For(def.Name), Model.TypeOf(def.Name)));

        int id = 1;
        foreach (var spec in Catalog.Methods)
        {
            yield return (Path.Combine("messages", $"{spec.Name}.request.json"), Envelope(id, "params", ContractSamples.ForType(spec.Params), spec.Params, spec.Name));
            yield return (Path.Combine("messages", $"{spec.Name}.response.json"), Envelope(id, "result", ContractSamples.ForType(spec.Result), spec.Result));
            if (spec.NullableResult) yield return (Path.Combine("messages", $"{spec.Name}.response.null.json"), $"{{\n  \"v\": {Wire.Version},\n  \"id\": {id},\n  \"result\": null\n}}");
            id++;
        }

        yield return (Path.Combine("messages", "error.failed.json"),
            $"{{\n  \"v\": {Wire.Version},\n  \"id\": 7,\n  \"error\": {{\n    \"code\": \"{ErrorCodes.Failed}\",\n    \"message\": \"Klasör bulunamadı: C:\\\\yok\"\n  }}\n}}");
    }

    private static string Serialize(object value, Type type) =>
        JsonSerializer.Serialize(value, type, Wire.Indented);

    private static string Envelope(int id, string key, object value, Type type, string? method = null)
    {
        var node = new JsonObject { ["v"] = Wire.Version, ["id"] = id };
        if (method is not null) node["method"] = method;
        node[key] = JsonSerializer.SerializeToNode(value, type, Wire.Options);
        return node.ToJsonString(Wire.Indented);
    }

    private static string Envelope(int id, string key, object value, Type type) => Envelope(id, key, value, type, null);

    [Fact]
    public void FixtureFiles_AreUpToDate_AndNoneAreLeftOver()
    {
        var expected = ExpectedFixtures().ToList();

        if (Update)
        {
            if (Directory.Exists(RepoPaths.Contract)) Directory.Delete(RepoPaths.Contract, recursive: true);
            foreach (var (path, json) in expected)
            {
                string full = Path.Combine(RepoPaths.Contract, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, json + "\n");
            }
        }

        foreach (var (path, json) in expected)
        {
            string full = Path.Combine(RepoPaths.Contract, path);
            Assert.True(File.Exists(full), $"{path} yok. UPDATE_CONTRACT=1 ile üretin.");
            Assert.True(
                JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(File.ReadAllText(full))),
                $"{path} güncel değil. Bilerek değiştirdiyseniz UPDATE_CONTRACT=1 ile yenileyin.");
        }

        var onDisk = Directory.EnumerateFiles(RepoPaths.Contract, "*.json", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(RepoPaths.Contract, f))
            .Order();
        Assert.Equal(expected.Select(e => e.Path).Order(), onDisk);
    }

    [Fact]
    public void EveryTypeFixture_PassesTheValidator_AndRoundTrips()
    {
        var validator = new ContractValidator(Model);

        foreach (var def in Model.Objects)
        {
            string json = File.ReadAllText(Path.Combine(RepoPaths.Contract, "types", $"{def.Name}.json"));
            using var document = JsonDocument.Parse(json);

            var errors = validator.Validate(document.RootElement, new Shape("object", def.Name));
            Assert.True(errors.Count == 0, $"{def.Name}: {string.Join("; ", errors)}");

            // JSON → C# → JSON aynı kalmalı.
            var type = Model.TypeOf(def.Name);
            var back = JsonSerializer.Deserialize(json, type, Wire.Options)!;
            Assert.True(
                JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(JsonSerializer.Serialize(back, type, Wire.Options))),
                $"{def.Name}: gidiş-dönüş farklı çıktı");
        }
    }

    [Fact]
    public void EveryMessageFixture_PassesTheValidator()
    {
        var validator = new ContractValidator(Model);

        foreach (var spec in Catalog.Methods)
        {
            using var request = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Contract, "messages", $"{spec.Name}.request.json")));
            Assert.Equal(spec.Name, request.RootElement.GetProperty("method").GetString());
            Assert.Empty(validator.ValidateParams(spec.Name, request.RootElement.GetProperty("params")));

            using var response = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Contract, "messages", $"{spec.Name}.response.json")));
            Assert.Empty(validator.ValidateResult(spec.Name, response.RootElement.GetProperty("result")));

            if (spec.NullableResult)
            {
                using var empty = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Contract, "messages", $"{spec.Name}.response.null.json")));
                Assert.Empty(validator.ValidateResult(spec.Name, empty.RootElement.GetProperty("result")));
            }
        }
    }

    [Fact]
    public void Samples_FillEveryField_IncludingTheOptionalOnes()
    {
        foreach (var def in Model.Objects)
        {
            var node = JsonSerializer.SerializeToNode(ContractSamples.For(def.Name), Model.TypeOf(def.Name), Wire.Options)!.AsObject();
            foreach (var field in def.Fields)
                Assert.True(node[field.Name] is not null, $"{def.Name}.{field.Name} örnekte boş; örneği doldurun.");
        }
    }

    // ---- Model -------------------------------------------------------------------------------------------------

    [Fact]
    public void Model_KnowsTheTypesAndNullability()
    {
        Assert.Equal(["wildcard", "regex", "translation"], Model.Enum("RenameMethod").Values);
        Assert.Equal(["unchanged", "ready", "conflict", "invalid", "error", "missingTag"], Model.Enum("RowStatus").Values);
        Assert.Equal(["cancel", "proceed", "autoNumber"], Model.Enum("RenameDecision").Values);

        var row = Model.Object("PreviewRow").Fields.ToDictionary(f => f.Name, f => f.Shape);
        Assert.True(row["message"].Nullable);
        Assert.False(row["newName"].Nullable);
        Assert.Equal("enum", row["status"].Kind);

        var preview = Model.Object("Preview").Fields.First(f => f.Name == "rows").Shape;
        Assert.Equal("array", preview.Kind);
        Assert.Equal("PreviewRow", preview.Item!.Name);

        Assert.True(Model.Methods.First(m => m.Name == "undo").Result.Nullable);
        Assert.True(Model.Methods.First(m => m.Name == "pickFolder").Result.Nullable);
        Assert.False(Model.Methods.First(m => m.Name == "scan").Result.Nullable);
    }

    [Fact]
    public void EveryMethod_HasUniqueName_AndCamelCase()
    {
        var names = Catalog.Methods.Select(m => m.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.All(names, n => Assert.Matches("^[a-z][A-Za-z]+$", n));
    }

    // ---- Doğrulayıcı -------------------------------------------------------------------------------------------

    private static IReadOnlyList<string> Check(string json, string type)
    {
        using var document = JsonDocument.Parse(json);
        return ContractValidator.Default.Validate(document.RootElement, new Shape("object", type));
    }

    private const string GoodRow = """{"id":1,"name":"a.txt","dir":"C:\\x","isDir":false,"size":10,"modified":1760956200000}""";

    [Fact]
    public void Validator_AcceptsAGoodObject() => Assert.Empty(Check(GoodRow, "FileRow"));

    [Theory]
    [InlineData("""{"id":1,"name":"a.txt","dir":"C:\\x","isDir":false,"size":10}""", "modified: eksik")]
    [InlineData("""{"id":1,"name":"a.txt","dir":"C:\\x","isDir":false,"size":10,"modified":1,"extra":1}""", "extra: tanımsız alan")]
    [InlineData("""{"id":"1","name":"a.txt","dir":"C:\\x","isDir":false,"size":10,"modified":1}""", "id: tam sayı olmalı")]
    [InlineData("""{"id":1.5,"name":"a.txt","dir":"C:\\x","isDir":false,"size":10,"modified":1}""", "id: tam sayı olmalı")]
    [InlineData("""{"id":1,"name":null,"dir":"C:\\x","isDir":false,"size":10,"modified":1}""", "name: boş (null) olamaz")]
    [InlineData("""{"id":1,"name":"a","dir":"C:\\x","isDir":"evet","size":10,"modified":1}""", "isDir: doğru/yanlış olmalı")]
    [InlineData("""{"id":1,"name":5,"dir":"C:\\x","isDir":false,"size":10,"modified":1}""", "name: metin olmalı")]
    public void Validator_RejectsABadObject(string json, string expected) =>
        Assert.Contains(Check(json, "FileRow"), e => e.Contains(expected));

    [Fact]
    public void Validator_RejectsUnknownEnumValues_AndWrongShapes()
    {
        Assert.Contains(
            Check("""{"id":1,"newName":"a","status":"hazir","message":null}""", "PreviewRow"),
            e => e.Contains("status") && e.Contains("şunlardan biri"));
        Assert.Contains(Check("""[1]""", "FileRow"), e => e.Contains("nesne olmalı"));
        Assert.Contains(
            Check("""{"rows":{"a":1},"ready":0,"problems":0,"ruleError":""}""", "Preview"),
            e => e.Contains("rows") && e.Contains("liste olmalı"));
        Assert.Contains(
            Check("""{"rows":[{"id":1,"newName":"a","status":"ready"}],"ready":0,"problems":0,"ruleError":""}""", "Preview"),
            e => e.Contains("rows[0].message") && e.Contains("eksik"));
    }

    [Fact]
    public void Validator_AllowsNullOnlyWhereTheContractSaysSo()
    {
        Assert.Empty(Check("""{"id":1,"newName":"a","status":"ready","message":null,"destination":null}""", "PreviewRow"));
        Assert.Contains(Check("""{"id":1,"newName":null,"status":"ready","message":null,"destination":null}""", "PreviewRow"), e => e.Contains("newName"));
    }
}
