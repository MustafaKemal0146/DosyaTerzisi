using DosyaTerzisi.Bridge.Contract;

namespace DosyaTerzisi.Tests;

/// <summary>
/// Sözleşmedeki her tip için örnek bir değer. Bunlardan <c>contract/fixtures</c> altındaki örnek dosyalar üretilir;
/// TypeScript testleri aynı dosyaları okur. Her alan dolu olmalı (boş bırakılabilen alanlar da).
/// </summary>
internal static class ContractSamples
{
    public static readonly SavedRule SavedRule = new(RenameMethod.Regex, @"(.*)\(\d+\)(.*)", @"\1\2", true);

    public static readonly Favorite Favorite = new("0f8fad5bd9cb469fa16570867728950e", "(n) sil", SavedRule);

    public static readonly Rule Rule = new(RenameMethod.Wildcard, "* - *", @"\1-\2", false, 1, 1, false, true, false, Operation.Copy, @"D:\Yedek", NumberStyle.Hexadecimal, true, "ID3.Album", true, true);

    public static readonly FileRow FileRow = new(3, "Öğrenci Şeması (2).xlsx", @"C:\Belgeler\Evrak", false, 55_296, 1_760_956_200_000);

    public static readonly ListingOptions ListingOptions = new(true, false, "*.jpg;*.png", true, 10);

    public static readonly PreviewRow PreviewRow = new(3, "Öğrenci Şeması .xlsx", RowStatus.Ready, "Örnek ileti", @"D:\Yedek");

    public static readonly Failure Failure = new(@"C:\Belgeler\Evrak\a.txt", "→ b.txt: Dosya başka bir işlem tarafından kullanılıyor.");

    public static readonly HistoryItem HistoryItem = new("9b2e1c4f7a6d4e0f8c3b5a1d2e4f6a7b", "Çeviri: ÜüĞğ → UuGg", 12, 1_760_956_300_000);

    /// <summary>Sözleşme tipinin adından örnek değeri verir.</summary>
    public static object For(string typeName) => typeName switch
    {
        nameof(SavedRule) => SavedRule,
        nameof(Favorite) => Favorite,
        nameof(Rule) => Rule,
        nameof(FileRow) => FileRow,
        nameof(ListingOptions) => ListingOptions,
        nameof(PreviewRow) => PreviewRow,
        nameof(Failure) => Failure,
        nameof(HistoryItem) => HistoryItem,
        nameof(BuiltInPreset) => new BuiltInPreset("p8", "(n) sil", SavedRule),
        nameof(Preview) => new Preview([PreviewRow, new PreviewRow(4, "Rapor.docx", RowStatus.Unchanged, null, null)], 1, 0, ""),
        nameof(ScanResult) => new ScanResult([FileRow], 2),
        nameof(ConflictInfo) => new ConflictInfo(@"C:\Belgeler\Evrak", "a.txt", "b.txt"),
        nameof(RenameRequest) => new RenameRequest(5, [(ConflictInfo)For(nameof(ConflictInfo))], 1),
        nameof(RenameOutcome) => new RenameOutcome(11, [Failure]),
        nameof(UndoOutcome) => new UndoOutcome("Çeviri: ÜüĞğ → UuGg", 11, [Failure]),
        nameof(Notice) => new Notice("Yarım kalan işlem", "Önceki yeniden adlandırma yarıda kalmış."),
        nameof(InitState) => new InitState(
            @"C:\Belgeler\Evrak",
            [(BuiltInPreset)For(nameof(BuiltInPreset))],
            [Favorite],
            [HistoryItem],
            [(Notice)For(nameof(Notice))],
            [@"C:\Belgeler.txt", @"C:\Belgeler.txt"]),
        nameof(ShellIntegrationState) => new ShellIntegrationState(true, true, false),
        nameof(SetShellIntegrationParams) => new SetShellIntegrationParams(true, false),
        nameof(ImportResult) => new ImportResult(2, [Favorite]),
        nameof(ExportResult) => new ExportResult(true, 4),
        nameof(TagPreviewParams) => new TagPreviewParams(@"<ID3.Artist> - <ID3.Track>.\e", TagMissing.Skip, 1, 1, true, false, true, [2, 4], Operation.Rename, "", NumberStyle.Letters, false, "ID3.Album", false, true),
        nameof(ScriptInfo) => new ScriptInfo(@"C:\Users\Musa\AppData\Roaming\DosyaTerzisi\Script.vbs", true, "vbscript"),
        nameof(TagColumnsParams) => new TagColumnsParams([1, 2], ["ID3.Artist", "ID3.Album"]),
        nameof(TagColumnValues) => new TagColumnValues([["Sezen Aksu", "Gülümse"], ["", ""]]),
        nameof(ReadTagsParams) => new ReadTagsParams(3, true),
        nameof(TagEntry) => new TagEntry("ID3.Artist", "Sezen Aksu"),
        nameof(TagList) => new TagList([(TagEntry)For(nameof(TagEntry))], "Dosya bozuk"),
        nameof(TimeRule) => new TimeRule(TimeAction.Random, 1_760_956_200_000, -90, TimeKind.Created, 1_763_548_200_000, 7, 1_700_000_000_000, 1_800_000_000_000),
        nameof(AttributeRule) => new AttributeRule(
            TriState.Set, TriState.Keep, TriState.Clear, TriState.Keep,
            (TimeRule)For(nameof(TimeRule)), new TimeRule(TimeAction.PhotoTaken, 0, 0, TimeKind.Modified, 0, 0, 0, 0),
            new TimeRule(TimeAction.CopyFrom, 0, 0, TimeKind.Accessed, 0, 0, 0, 0), true),
        nameof(AttributePreviewParams) => new AttributePreviewParams((AttributeRule)For(nameof(AttributeRule)), [1, 2]),
        nameof(AttrFlags) => new AttrFlags(true, false, true, false),
        nameof(AttributeRow) => new AttributeRow(
            3, new AttrFlags(false, true, false, false), new AttrFlags(true, true, false, false),
            1_760_956_200_000, 1_760_956_800_000, 1_760_000_000_000, 1_760_000_600_000, 1_760_956_300_000, 1_760_956_300_000,
            AttributeRowStatus.Ready, "Çekim tarihi bulunamadı."),
        nameof(AttributePreview) => new AttributePreview([(AttributeRow)For(nameof(AttributeRow))], 1, 0),
        nameof(AttributeOutcome) => new AttributeOutcome(2, [Failure]),
        nameof(AttributeUndo) => new AttributeUndo(2, [Failure]),
        nameof(NumberedOptions) => new NumberedOptions(ObjectKind.EmptyFile, @"C:\Belgeler\Evrak", "Hafta ??.txt", 3, 1, 1, "10K", @"C:\Belgeler\sablon.docx", NumberStyle.Decimal),
        nameof(PlannedObject) => new PlannedObject("Hafta 01.txt", PlannedStatus.Exists, "Bu adda bir öğe zaten var."),
        nameof(NumberedPlan) => new NumberedPlan([(PlannedObject)For(nameof(PlannedObject))], "Hedef klasör bulunamadı.", 0),
        nameof(CreateOutcome) => new CreateOutcome(3, [Failure]),
        nameof(PickFileParams) => new PickFileParams("Dosya seçin", "Tümü (*.*)|*.*"),
        nameof(OpenPathsParams) => new OpenPathsParams([@"C:\Belgeler\Evrak", @"C:\Belgeler.txt"]),
        nameof(OpenPathsResult) => new OpenPathsResult(@"C:\Belgeler\Evrak", new ScanResult([FileRow], 0)),
        nameof(ListFoldersParams) => new ListFoldersParams(@"C:\Belgeler"),
        nameof(FolderNode) => new FolderNode("Evrak", @"C:\Belgeler\Evrak", true),
        nameof(FolderList) => new FolderList(@"C:\Belgeler", [(FolderNode)For(nameof(FolderNode))]),
        nameof(Empty) => new Empty(),
        nameof(PickFolderParams) => new PickFolderParams(@"C:\Belgeler"),
        nameof(ScanParams) => new ScanParams(@"C:\Belgeler\Evrak", ListingOptions),
        nameof(NameOverride) => new NameOverride(3, "Özel ad.txt"),
        nameof(RemoveRowsParams) => new RemoveRowsParams([2, 4]),
        nameof(PreviewParams) => new PreviewParams(Rule, [1, 3, 5], [(NameOverride)For(nameof(NameOverride))]),
        nameof(ExecuteRenameParams) => new ExecuteRenameParams(RenameDecision.AutoNumber, "Joker: * - * → \\1-\\2"),
        nameof(AddFavoriteParams) => new AddFavoriteParams("Tire düzelt", SavedRule),
        nameof(UpdateFavoriteParams) => new UpdateFavoriteParams(Favorite),
        nameof(RemoveFavoriteParams) => new RemoveFavoriteParams(Favorite.Id),
        nameof(MoveFavoriteParams) => new MoveFavoriteParams(Favorite.Id, -1),
        _ => throw new ArgumentException($"{typeName} için örnek yok. ContractSamples'a ekleyin."),
    };

    /// <summary>Bir işin parametresi ya da cevabı için örnek (liste ve metin tipleri de dahil).</summary>
    public static object ForType(Type type)
    {
        if (type == typeof(string)) return @"C:\Belgeler\Evrak";
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var list = (System.Collections.IList)Activator.CreateInstance(type)!;
            list.Add(For(type.GetGenericArguments()[0].Name));
            return list;
        }
        return For(type.Name);
    }
}
