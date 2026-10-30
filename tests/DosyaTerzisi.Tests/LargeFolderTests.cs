using System.Diagnostics;
using DosyaTerzisi.Bridge.Contract;

namespace DosyaTerzisi.Tests;

/// <summary>Çok dosyalı klasör: listeleme, önizleme, adlandırma ve geri alma makul sürede bitmeli.</summary>
public class LargeFolderTests
{
    [Fact]
    public async Task TwentyThousandFiles_AreListedPreviewedRenamedAndUndone_InReasonableTime()
    {
        using var h = new BridgeHarness();
        for (int i = 0; i < 20_000; i++) File.WriteAllText(Path.Combine(h.Root, $"IMG_{i:00000} - foto.jpg"), "");

        var clock = Stopwatch.StartNew();
        var list = await h.Scan();
        long scan = clock.ElapsedMilliseconds;
        Assert.Equal(20_000, list.Rows.Count);

        clock.Restart();
        var preview = await h.Preview(BridgeHarness.Rule(RenameMethod.Wildcard, "* - *.*", @"\1_\2.\3"));
        long previewMs = clock.ElapsedMilliseconds;
        Assert.Equal(20_000, preview.Ready);

        clock.Restart();
        var outcome = await h.Rename();
        long rename = clock.ElapsedMilliseconds;
        Assert.Equal(20_000, outcome.Renamed);
        Assert.Empty(outcome.Failures);

        clock.Restart();
        var undone = await h.Call<UndoOutcome?>("undo", new Empty());
        long undo = clock.ElapsedMilliseconds;
        Assert.Equal(20_000, undone!.Restored);
        Assert.True(File.Exists(Path.Combine(h.Root, "IMG_00000 - foto.jpg")));

        // Bilgi için; sınırlar bilerek cömert (yavaş disklerde de geçsin), asıl amaç takılma ve üstel yavaşlamayı yakalamak.
        Console.WriteLine($"20k dosya: liste {scan} ms, önizleme {previewMs} ms, adlandırma {rename} ms, geri alma {undo} ms");
        Assert.True(scan < 20_000, $"liste {scan} ms");
        Assert.True(previewMs < 20_000, $"önizleme {previewMs} ms");
        Assert.True(rename < 60_000, $"adlandırma {rename} ms");
        Assert.True(undo < 60_000, $"geri alma {undo} ms");
    }

    [Fact]
    public async Task FiveThousandFiles_AttributesAndTagPreview_AreFast()
    {
        using var h = new BridgeHarness();
        for (int i = 0; i < 5_000; i++) File.WriteAllText(Path.Combine(h.Root, $"d{i:0000}.txt"), "x");
        await h.Scan();

        var clock = Stopwatch.StartNew();
        var rule = new AttributeRule(
            TriState.Keep, TriState.Keep, TriState.Set, TriState.Keep,
            new TimeRule(TimeAction.Shift, 0, 90, TimeKind.Modified, 0, 0, 0, 0),
            new TimeRule(TimeAction.Keep, 0, 0, TimeKind.Modified, 0, 0, 0, 0),
            new TimeRule(TimeAction.Keep, 0, 0, TimeKind.Modified, 0, 0, 0, 0), true);
        var preview = await h.Call<AttributePreview>("attributePreview", new AttributePreviewParams(rule, []));
        long previewMs = clock.ElapsedMilliseconds;
        Assert.Equal(5_000, preview.Changed);

        clock.Restart();
        var applied = await h.Call<AttributeOutcome>("applyAttributes", new AttributePreviewParams(rule, []));
        long applyMs = clock.ElapsedMilliseconds;
        Assert.Equal(5_000, applied.Changed);

        clock.Restart();
        var undone = await h.Call<AttributeUndo?>("undoAttributes", new Empty());
        long undoMs = clock.ElapsedMilliseconds;
        Assert.Equal(5_000, undone!.Restored);

        clock.Restart();
        var tags = await h.Call<Preview>("tagPreview", new TagPreviewParams(@"<DOC.Title>.\e", TagMissing.UseEmpty, 1, 1, false, false, false, [], Operation.Rename, "", NumberStyle.Decimal, false, "", false, false));
        long tagMs = clock.ElapsedMilliseconds;
        Assert.Equal(5_000, tags.Rows.Count);

        Console.WriteLine($"5k dosya: öznitelik önizleme {previewMs} ms, uygulama {applyMs} ms, geri alma {undoMs} ms, etiket önizleme {tagMs} ms");
        Assert.True(previewMs < 15_000 && applyMs < 30_000 && undoMs < 30_000 && tagMs < 30_000);
    }
}
