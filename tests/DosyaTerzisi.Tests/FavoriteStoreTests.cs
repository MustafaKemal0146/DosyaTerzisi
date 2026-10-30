using DosyaTerzisi.Core;

namespace DosyaTerzisi.Tests;

public class FavoriteStoreTests
{
    private sealed class TempDir : IDisposable
    {
        public string Path_ { get; } = Path.Combine(Path.GetTempPath(), "dosyaterzisi-fav-" + Guid.NewGuid().ToString("N"));

        public string File(string name) => Path.Combine(Path_, name);

        public void Dispose()
        {
            if (Directory.Exists(Path_)) Directory.Delete(Path_, recursive: true);
        }
    }

    [Fact]
    public void Favorites_SurviveRestart_InTheSameOrder()
    {
        using var dir = new TempDir();
        var first = new FavoriteStore(dir.File("fav.json"));
        first.Add("Türkçe → ASCII", RenameMethod.Translation, "ÜüĞğ ", "UuGg_");
        first.Add("Tire düzelt", RenameMethod.Wildcard, "* - *", @"\1-\2", caseSensitive: true);

        var second = new FavoriteStore(dir.File("fav.json"));

        Assert.Equal(["Türkçe → ASCII", "Tire düzelt"], second.Items.Select(f => f.Name));
        Assert.Equal(RenameMethod.Translation, second.Items[0].Method);
        Assert.Equal(@"\1-\2", second.Items[1].Replace);
        Assert.True(second.Items[1].CaseSensitive);
    }

    [Fact]
    public void WithoutAPath_ItWorksInMemory()
    {
        var store = new FavoriteStore();

        store.Add("a", RenameMethod.Wildcard, "*", @"\1");

        Assert.Single(store.Items);
    }

    [Fact]
    public void Names_AreMadeUnique_IgnoringCase()
    {
        var store = new FavoriteStore();

        store.Add("Rapor", RenameMethod.Wildcard, "a", "b");
        store.Add("rapor", RenameMethod.Wildcard, "c", "d");
        store.Add("RAPOR", RenameMethod.Wildcard, "e", "f");
        store.Add("  ", RenameMethod.Wildcard, "g", "h");

        Assert.Equal(["Rapor", "rapor (2)", "RAPOR (3)", "Adsız"], store.Items.Select(f => f.Name));
    }

    [Fact]
    public void Update_ChangesTheRule_AndKeepsOwnNameFree()
    {
        var store = new FavoriteStore();
        var a = store.Add("A", RenameMethod.Wildcard, "x", "y");
        var b = store.Add("B", RenameMethod.Wildcard, "x", "y");

        var same = store.Update(a with { Replace = "z" });
        var clash = store.Update(b with { Name = "A" });

        Assert.Equal("A", same!.Name);
        Assert.Equal("z", store.Items[0].Replace);
        Assert.Equal("A (2)", clash!.Name);
        Assert.Null(store.Update(a with { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void Remove_AndMove()
    {
        var store = new FavoriteStore();
        var a = store.Add("A", RenameMethod.Wildcard, "1", "1");
        var b = store.Add("B", RenameMethod.Wildcard, "2", "2");
        var c = store.Add("C", RenameMethod.Wildcard, "3", "3");

        Assert.True(store.Move(c.Id, -2));
        Assert.Equal(["C", "A", "B"], store.Items.Select(f => f.Name));

        Assert.False(store.Move(c.Id, -1));
        Assert.False(store.Move(b.Id, 1));
        Assert.False(store.Move(a.Id, 0));

        Assert.True(store.Remove(a.Id));
        Assert.False(store.Remove(a.Id));
        Assert.Equal(["C", "B"], store.Items.Select(f => f.Name));
    }

    [Fact]
    public void Changed_IsRaisedForEveryEdit()
    {
        var store = new FavoriteStore();
        int raised = 0;
        store.Changed += (_, _) => raised++;

        var a = store.Add("A", RenameMethod.Wildcard, "1", "1");
        store.Add("B", RenameMethod.Wildcard, "2", "2");
        store.Update(a with { Search = "3" });
        store.Move(a.Id, 1);
        store.Remove(a.Id);

        Assert.Equal(5, raised);
    }

    [Fact]
    public void ExportThenImport_RoundTrips_AndSkipsRulesAlreadyThere()
    {
        using var dir = new TempDir();
        var source = new FavoriteStore();
        source.Add("Bir", RenameMethod.Translation, "a-z", "A-Z");
        source.Add("İki", RenameMethod.RegularExpression, @"\d+", "#");
        source.Export(dir.File("out.json"));

        var target = new FavoriteStore();
        target.Add("Zaten var", RenameMethod.Translation, "a-z", "A-Z");

        int added = target.Import(dir.File("out.json"));

        Assert.Equal(1, added);
        Assert.Equal(["Zaten var", "İki"], target.Items.Select(f => f.Name));
        Assert.Equal(0, target.Import(dir.File("out.json")));
    }

    [Fact]
    public void Import_ReadsThePresetList()
    {
        var store = new FavoriteStore();

        int added = store.Import(TestHelpers.PresetPath);

        Assert.True(added >= 15);
        var dsc = store.Items.Single(f => f.Name == "“DSC” yerine “IMG” yaz");
        Assert.Equal(RenameMethod.Wildcard, dsc.Method);
        Assert.Equal(@"\1IMG\2", dsc.Replace);
    }

    [Fact]
    public void Import_RejectsFilesItCannotRead()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Path_);
        File.WriteAllText(dir.File("bad.json"), "{ not json");
        var store = new FavoriteStore();

        Assert.Throws<InvalidDataException>(() => store.Import(dir.File("bad.json")));
        Assert.Throws<InvalidDataException>(() => store.Import(dir.File("missing.json")));
        Assert.Empty(store.Items);
    }

    [Fact]
    public void CorruptFile_IsSetAside_AndStartsEmpty()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Path_);
        File.WriteAllText(dir.File("fav.json"), "garbage");

        var store = new FavoriteStore(dir.File("fav.json"));

        Assert.Empty(store.Items);
        Assert.NotNull(store.LoadWarning);
        Assert.Single(Directory.GetFiles(dir.Path_, "fav.json.bad-*"));
    }

    [Fact]
    public void ToOptions_CarriesTheRule()
    {
        var favorite = new FavoriteStore().Add("A", RenameMethod.RegularExpression, "x", "y", caseSensitive: true);

        var options = favorite.ToOptions();

        Assert.Equal(RenameMethod.RegularExpression, options.Method);
        Assert.Equal("x", options.Search);
        Assert.Equal("y", options.Replace);
        Assert.True(options.CaseSensitive);
    }
}
