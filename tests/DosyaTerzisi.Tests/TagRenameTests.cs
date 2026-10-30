using System.Globalization;
using DosyaTerzisi.Core;
using static DosyaTerzisi.Tests.MediaFixtures;
using static DosyaTerzisi.Tests.TestHelpers;

namespace DosyaTerzisi.Tests;

public class TagRenameTests
{
    private static string NewName(FileEntry entry, string template, ITagReader reader, MissingTagPolicy missing = MissingTagPolicy.Skip) =>
        TagRenamePlanner.Build([entry], new TagRenameOptions { Template = template, Missing = missing }, reader, NoDisk)[0].NewName;

    private static readonly FakeTags Song = new(
        Text("ID3.Artist", "Queen"),
        Text("ID3.Track", "Bohemian Rhapsody"),
        Number("ID3.TrackNumber", 3),
        Number("ID3.Year", 1975),
        Date("EXIF.OriginalTime", new DateTime(2019, 8, 17, 14, 30, 5)));

    // ---- Template ---------------------------------------------------------------------------------------------

    [Fact]
    public void TagsAndVariables_MixInOneTemplate()
    {
        Assert.Equal(
            "Queen - Bohemian Rhapsody.mp3",
            NewName(File("x.mp3"), @"<ID3.Artist> - <ID3.Track>.\e", Song));
    }

    [Fact]
    public void Numbers_CanBePadded()
    {
        Assert.Equal("03 Bohemian Rhapsody.mp3", NewName(File("x.mp3"), @"<ID3.TrackNumber:00> <ID3.Track>.\e", Song));
        Assert.Equal("3 Bohemian Rhapsody.mp3", NewName(File("x.mp3"), @"<ID3.TrackNumber> <ID3.Track>.\e", Song));
    }

    [Fact]
    public void Dates_HaveAFileSafeDefault_AndCanBeFormatted()
    {
        Assert.Equal("2019-08-17 14.30.05.jpg", NewName(File("x.jpg"), @"<EXIF.OriginalTime>.\e", Song));
        Assert.Equal("20190817_143005.jpg", NewName(File("x.jpg"), @"<EXIF.OriginalTime:yyyyMMdd_HHmmss>.\e", Song));
        // A colon in the format would be illegal in a name; it is replaced.
        Assert.Equal("14_30.jpg", NewName(File("x.jpg"), @"<EXIF.OriginalTime:HH:mm>.\e", Song));
    }

    [Fact]
    public void CharactersThatCannotBeInANames_AreReplaced()
    {
        var tags = new FakeTags(Text("ID3.Artist", "AC/DC"), Text("ID3.Track", "Who Made Who?"));

        Assert.Equal("AC_DC - Who Made Who_.mp3", NewName(File("x.mp3"), @"<ID3.Artist> - <ID3.Track>.\e", tags));
    }

    [Fact]
    public void Tag_NamesIgnoreCase()
    {
        Assert.Equal("Queen.mp3", NewName(File("x.mp3"), @"<id3.artist>.\e", Song));
    }

    [Fact]
    public void CaseChanges_ApplyToTagText()
    {
        Assert.Equal("QUEEN.mp3", NewName(File("x.mp3"), @"\U<ID3.Artist>\E.\e", Song));
        Assert.Equal("Queen.mp3", NewName(File("x.mp3"), @"\u\L<ID3.Artist>\E.\e", new FakeTags(Text("ID3.Artist", "QUEEN"))));
    }

    [Fact]
    public void Counters_Work_AndRestartPerFolder()
    {
        var options = new TagRenameOptions { Template = @"??-<ID3.Track>.\e", ResetNumberPerFolder = true };

        var plan = TagRenamePlanner.Build(
            [File("a.mp3", @"C:\x"), File("b.mp3", @"C:\x"), File("c.mp3", @"C:\y")], options, Song, NoDisk);

        // The same title three times: every folder numbers from 1, so "01-" and "02-" never clash.
        Assert.Equal(["01-Bohemian Rhapsody.mp3", "02-Bohemian Rhapsody.mp3", "01-Bohemian Rhapsody.mp3"], plan.Select(i => i.NewName));
    }

    [Theory]
    [InlineData("<b>")]
    [InlineData("<a b.c>")]
    [InlineData("<.x>")]
    [InlineData("<x.>")]
    [InlineData("<ID3.Artist")]
    [InlineData("1 < 2")]
    public void Things_ThatLookLikeTagsButAreNot_StayLiteral(string template)
    {
        var text = ReplacementTemplate.Expand(
            template,
            new ExpansionContext(File("x.mp3"), [], 1, DateSource.Modified, CultureInfo.InvariantCulture, new Dictionary<string, TagValue>()));

        Assert.Equal(template, text);
    }

    [Fact]
    public void WithoutTags_AngleBracketsAreOrdinaryCharacters()
    {
        var text = ReplacementTemplate.Expand(
            "<ID3.Artist>", new ExpansionContext(File("x.mp3"), [], 1, DateSource.Modified, CultureInfo.InvariantCulture));

        Assert.Equal("<ID3.Artist>", text);
    }

    // ---- Missing tags -----------------------------------------------------------------------------------------

    [Fact]
    public void MissingTag_SkipsTheFile_AndSaysWhichTag()
    {
        var onlyArtist = new FakeTags(Text("ID3.Artist", "Queen"));

        var item = TagRenamePlanner.Build([File("x.mp3")], new TagRenameOptions { Template = @"<ID3.Artist> - <ID3.Track> <ID3.Album>.\e" }, onlyArtist, NoDisk)[0];

        Assert.Equal(RenameStatus.MissingTag, item.Status);
        Assert.Equal("x.mp3", item.NewName);
        Assert.Contains("ID3.Album", item.Message);
        Assert.Contains("ID3.Track", item.Message);
        Assert.DoesNotContain("ID3.Artist", item.Message);
        Assert.False(item.WillRename);
    }

    [Fact]
    public void MissingTag_CanBeLeftEmptyInstead()
    {
        var onlyTrack = new FakeTags(Text("ID3.Track", "Song"));

        Assert.Equal("Song.mp3", NewName(File("x.mp3"), @"<ID3.Artist><ID3.Track>.\e", onlyTrack, MissingTagPolicy.UseEmpty));
    }

    [Fact]
    public void FoldersAreLeftAlone()
    {
        var item = TagRenamePlanner.Build([Folder("Albüm")], new TagRenameOptions { Template = "<ID3.Artist>" }, Song, NoDisk).Single();

        Assert.Equal(RenameStatus.Unchanged, item.Status);
    }

    [Fact]
    public void TheTagsOfAFile_AreReadOnlyOnce()
    {
        var reader = new FakeTags(Text("ID3.Artist", "Queen"));

        TagRenamePlanner.Build([File("a.mp3"), File("b.mp3")], new TagRenameOptions { Template = @"<ID3.Artist> <ID3.Artist>.\e" }, reader, NoDisk);

        Assert.Equal(2, reader.Reads);
    }

    // ---- Conflicts and problems ------------------------------------------------------------------------------

    [Fact]
    public void TwoFilesWithTheSameTags_Clash_UnlessNumbered()
    {
        var files = new[] { File("a.mp3"), File("b.mp3") };
        var options = new TagRenameOptions { Template = @"<ID3.Artist> - <ID3.Track>.\e" };

        var reported = TagRenamePlanner.Build(files, options, Song, NoDisk);
        var numbered = TagRenamePlanner.Build(files, options, Song, new PlanOptions { Conflict = ConflictPolicy.AutoNumber, ListDirectory = _ => [] });

        Assert.Equal([RenameStatus.Ready, RenameStatus.Conflict], reported.Select(i => i.Status));
        Assert.Equal(
            ["Queen - Bohemian Rhapsody.mp3", "Queen - Bohemian Rhapsody(01).mp3"], numbered.Select(i => i.NewName));
    }

    [Fact]
    public void AnIllegalResult_IsMarked()
    {
        var item = TagRenamePlanner.Build([File("x.mp3")], new TagRenameOptions { Template = @"CON.\e" }, Song, NoDisk).Single();

        Assert.Equal(RenameStatus.InvalidName, item.Status);
    }

    [Fact]
    public void TheSameNameAsBefore_IsUnchanged()
    {
        Assert.Equal(
            RenameStatus.Unchanged,
            TagRenamePlanner.Build([File("Queen.mp3")], new TagRenameOptions { Template = @"<ID3.Artist>.\e" }, Song, NoDisk)[0].Status);
    }

    // ---- Real files -------------------------------------------------------------------------------------------

    [Fact]
    public void Mp3_TagsAreReadFromTheFile()
    {
        using var tmp = new TempFolder();
        var path = WriteMp3(Path.Combine(tmp.Root, "01.mp3"), tag =>
        {
            tag.Performers = ["Sezen Aksu"];
            tag.AlbumArtists = ["Sezen Aksu & Co"];
            tag.Title = "Şarkı Bitti";
            tag.Album = "Gülümse";
            tag.Track = 11;
            tag.Year = 1991;
            tag.Genres = ["Pop"];
            tag.Comment = "güzel";
        });

        var result = FileTagReader.Default.Read(FileEntry.FromPath(path)!);

        Assert.Null(result.Error);
        var v = result.Values;
        Assert.Equal("Sezen Aksu", v["ID3.Artist"].Text);
        Assert.Equal("Şarkı Bitti", v["ID3.Track"].Text);
        Assert.Equal("Şarkı Bitti", v["id3.title"].Text);
        Assert.Equal("Gülümse", v["ID3.Album"].Text);
        Assert.Equal(11, v["ID3.TrackNumber"].Number);
        Assert.Equal(1991, v["ID3.Year"].Number);
        Assert.Equal("Pop", v["ID3.Genre"].Text);
        Assert.Equal("güzel", v["ID3.Comment"].Text);
        Assert.Equal("Şarkı Bitti", v["ID3v2.TIT2"].Text);
        Assert.Equal("Sezen Aksu", v["ID3v2.TPE1"].Text);
        Assert.False(v.ContainsKey("ID3.Composer"));
    }

    [Fact]
    public void Mp3_IsRenamedFromItsTags_OnDisk()
    {
        using var tmp = new TempFolder();
        WriteMp3(Path.Combine(tmp.Root, "track01.mp3"), t => { t.Performers = ["AC/DC"]; t.Title = "Back In Black"; t.Track = 6; });
        WriteMp3(Path.Combine(tmp.Root, "track02.mp3"), t => { t.Performers = ["Queen"]; t.Title = "Radio Ga Ga"; t.Track = 7; });
        WriteMp3(Path.Combine(tmp.Root, "untagged.mp3"));
        var entries = FileScanner.Scan(tmp.Root, new ScanOptions { Filter = "*.mp3" }).Entries;

        var plan = TagRenamePlanner.Build(entries, new TagRenameOptions { Template = @"<ID3.TrackNumber:00> <ID3.Artist> - <ID3.Track>.\e" });
        var result = RenameExecutor.Execute(plan);

        Assert.Empty(result.Failures);
        Assert.Equal(
            ["06 AC_DC - Back In Black.mp3", "07 Queen - Radio Ga Ga.mp3", "untagged.mp3"],
            tmp.Tree());
        Assert.Equal(RenameStatus.MissingTag, plan.Single(i => i.Entry.Name == "untagged.mp3").Status);
    }

    [Fact]
    public void Jpeg_ExifAndIptcAreRead()
    {
        using var tmp = new TempFolder();
        var metadata = new Dictionary<string, object>(PhotoMetadata)
        {
            ["/app13/irb/8bimiptc/iptc/object name"] = "Piknik",
            ["/app13/irb/8bimiptc/iptc/city"] = "İzmir",
            ["/app13/irb/8bimiptc/iptc/caption"] = "Deniz kenarında",
        };
        var path = WriteJpeg(Path.Combine(tmp.Root, "IMG_0001.jpg"), metadata);

        var result = FileTagReader.Default.Read(FileEntry.FromPath(path)!);

        Assert.Null(result.Error);
        var v = result.Values;
        Assert.Equal("Canon", v["EXIF.Maker"].Text);
        Assert.Equal("EOS 5D", v["EXIF.Model"].Text);
        Assert.Equal("Aile piknigi", v["EXIF.ImageDescription"].Text);
        Assert.Equal("Ayse Yilmaz", v["EXIF.Artist"].Text);
        Assert.Equal(new DateTime(2019, 8, 17, 14, 30, 5), v["EXIF.OriginalTime"].Date);
        Assert.Equal("Piknik", v["IPTC.ObjectName"].Text);
        Assert.Equal("İzmir", v["IPTC.City"].Text);
        Assert.Equal("Deniz kenarında", v["IPTC.Caption"].Text);
    }

    [Fact]
    public void Jpeg_IsRenamedByTheDateItWasTaken()
    {
        using var tmp = new TempFolder();
        WriteJpeg(Path.Combine(tmp.Root, "IMG_0001.jpg"), PhotoMetadata);
        WriteJpeg(Path.Combine(tmp.Root, "IMG_0002.jpg"), new Dictionary<string, object>());
        var entries = FileScanner.Scan(tmp.Root, new ScanOptions { Filter = "*.jpg" }).Entries;

        var plan = TagRenamePlanner.Build(entries, new TagRenameOptions { Template = @"<EXIF.OriginalTime:yyyyMMdd_HHmmss> <EXIF.Model>.\e" });
        RenameExecutor.Execute(plan);

        Assert.Equal(["20190817_143005 EOS 5D.jpg", "IMG_0002.jpg"], tmp.Tree());
    }

    [Fact]
    public void OtherFiles_AndFolders_HaveNoTags()
    {
        using var tmp = new TempFolder();
        var text = FileEntry.FromPath(tmp.File("not.txt"))!;
        var folder = FileEntry.FromPath(tmp.Dir("klasör"))!;

        Assert.Empty(FileTagReader.Default.Read(text).Values.Keys.Where(k => !k.StartsWith("PROP.", StringComparison.OrdinalIgnoreCase)));
        Assert.Empty(FileTagReader.Default.Read(folder).Values);
        Assert.Null(FileTagReader.Default.Read(text).Error);
    }

    [Fact]
    public void ADamagedFile_IsReported_NotThrown()
    {
        using var tmp = new TempFolder();
        var path = tmp.File("bozuk.jpg", "bu bir resim değil");
        var entry = FileEntry.FromPath(path)!;

        var result = FileTagReader.Default.Read(entry);
        var item = TagRenamePlanner.Build([entry], new TagRenameOptions { Template = @"<EXIF.Model>.\e" }, plan: NoDisk).Single();

        Assert.NotNull(result.Error);
        Assert.Empty(result.Values);
        Assert.Equal(RenameStatus.MissingTag, item.Status);
        Assert.Contains("EXIF.Model", item.Message);
    }

    [Fact]
    public void AFileThatVanished_IsReported_NotThrown()
    {
        var gone = File("yok.mp3", @"C:\bu\klasor\yok");

        var result = FileTagReader.Default.Read(gone);

        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ReadingTags_DoesNotLockOrChangeTheFile()
    {
        using var tmp = new TempFolder();
        var path = WriteMp3(Path.Combine(tmp.Root, "a.mp3"), t => t.Title = "x");
        var before = File2.ReadAllBytes(path);

        FileTagReader.Default.Read(FileEntry.FromPath(path)!);

        System.IO.File.Move(path, Path.Combine(tmp.Root, "b.mp3"));
        Assert.Equal(before, File2.ReadAllBytes(Path.Combine(tmp.Root, "b.mp3")));
    }

    private static class File2
    {
        public static byte[] ReadAllBytes(string path) => System.IO.File.ReadAllBytes(path);
    }

    // ---- Photo date for Attributes ---------------------------------------------------------------------------

    [Fact]
    public void Attributes_CanSetTimesToThePhotoTakenDate()
    {
        var entry = new FileEntry(@"C:\x", "a.jpg", false, new DateTime(2024, 1, 1), new DateTime(2024, 1, 2));
        var rule = new AttributeRule { Modified = TimestampRule.FromPhotoTaken(), Created = TimestampRule.FromPhotoTaken() };

        var item = AttributePlanner.Build([entry], rule, Song).Single();

        var taken = new DateTime(2019, 8, 17, 14, 30, 5);
        Assert.Equal(taken, item.NewModified);
        Assert.Equal(taken, item.NewCreated);
        Assert.Equal(AttributeStatus.Ready, item.Status);
        Assert.Null(item.Message);
        Assert.False(item.ChangeAccessed);
    }

    [Fact]
    public void Attributes_WithoutAPhotoDate_KeepTheTime_AndSayWhy()
    {
        var entry = new FileEntry(@"C:\x", "a.jpg", false, new DateTime(2024, 1, 1), new DateTime(2024, 1, 2));
        var noExif = new FakeTags(Text("EXIF.Model", "x"));

        var item = AttributePlanner.Build([entry], new AttributeRule { Modified = TimestampRule.FromPhotoTaken() }, noExif).Single();

        Assert.Equal(entry.Modified, item.NewModified);
        Assert.Equal(AttributeStatus.Unchanged, item.Status);
        Assert.Equal("Çekim tarihi bulunamadı.", item.Message);
    }

    [Fact]
    public void Attributes_DoNotReadTagsWhenTheRuleDoesNotNeedThem()
    {
        var entry = new FileEntry(@"C:\x", "a.jpg", false, new DateTime(2024, 1, 1), new DateTime(2024, 1, 2));
        var reader = new FakeTags();

        AttributePlanner.Build([entry], new AttributeRule { Hidden = Tri.Set }, reader);

        Assert.Equal(0, reader.Reads);
    }

    [Fact]
    public void Attributes_PhotoDateReachesTheDisk_AndCanBeUndone()
    {
        using var tmp = new TempFolder();
        var path = WriteJpeg(Path.Combine(tmp.Root, "IMG_0001.jpg"), PhotoMetadata);
        var before = FileEntry.FromPath(path)!;
        var entries = FileScanner.Scan(tmp.Root, new ScanOptions()).Entries;
        var rule = new AttributeRule { Modified = TimestampRule.FromPhotoTaken() };

        var applied = AttributeExecutor.Apply(AttributePlanner.Build(entries, rule));

        Assert.Empty(applied.Failures);
        Assert.Equal(new DateTime(2019, 8, 17, 14, 30, 5), FileEntry.FromPath(path)!.Modified);

        AttributeExecutor.Undo(applied.Journal);

        Assert.Equal(before.Modified, FileEntry.FromPath(path)!.Modified);
    }
}
