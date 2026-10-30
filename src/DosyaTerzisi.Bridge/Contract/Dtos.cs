namespace DosyaTerzisi.Bridge.Contract;

// Web arayüzü ile uygulama arasında dolaşan her şeyin tipi burada tanımlanır.
// Bu dosya tek kaynaktır: TypeScript tipleri (web/lib/wire.generated.ts) buradan otomatik üretilir.
// Bir şey eklerken ya da değiştirirken: UPDATE_CONTRACT=1 dotnet test --filter ContractTests

public enum RenameMethod
{
    Wildcard,
    Regex,
    Translation,
}

/// <summary>Yeni adla ne yapılır: dosyanın kendisi yeniden adlandırılır (ad bir klasör yolu içeriyorsa taşınır) ya da kopya, kısayol, sabit bağ oluşturulur.</summary>
public enum Operation
{
    Rename,
    Copy,
    Shortcut,
    Hardlink,
}

/// <summary>? sayacının yazılışı: 1, 2, 10 / 1, 9, A, 1F / a, b, ... z, ba.</summary>
public enum NumberStyle
{
    Decimal,
    Hexadecimal,
    Letters,
}

public enum RowStatus
{
    Unchanged,
    Ready,
    Conflict,
    Invalid,
    Error,
    MissingTag,
}

public enum RenameDecision
{
    Cancel,
    Proceed,
    AutoNumber,
}

/// <summary>Listedeki bir dosya ya da klasör. <c>Id</c> listeyle birlikte yenilenir. <c>Modified</c> epoch milisaniye.</summary>
public sealed record FileRow(int Id, string Name, string Dir, bool IsDir, long Size, long Modified);

public sealed record ListingOptions(bool IncludeFiles, bool IncludeFolders, string Filter, bool Recursive, int Depth);

public sealed record Rule(
    RenameMethod Method,
    string Search,
    string Replace,
    bool CaseSensitive,
    int NumberStart,
    int NumberStep,
    bool ResetNumberPerFolder,
    bool AutoNumberOnConflict,
    bool UseCreatedDate,
    Operation Operation,
    string TargetFolder,
    NumberStyle NumberStyle,
    bool ResetNumberPerDate,
    string ResetNumberPerTag,
    bool ShortNames83,
    bool UseScript);

/// <summary>Favoriye ya da ön ayara kaydedilen kısım.</summary>
public sealed record SavedRule(RenameMethod Method, string Search, string Replace, bool CaseSensitive);

public sealed record Favorite(string Id, string Name, SavedRule Rule);

public sealed record BuiltInPreset(string Id, string Title, SavedRule Rule);

/// <summary><c>Destination</c>: sonuç dosyanın kendi klasöründen başka bir klasöre gidiyorsa o klasör (taşıma, kopya, kısayol...).</summary>
public sealed record PreviewRow(int Id, string NewName, RowStatus Status, string? Message, string? Destination);

/// <summary><c>Rows</c> listedeki her satır için, aynı sırayla bir önizleme içerir.</summary>
public sealed record Preview(List<PreviewRow> Rows, int Ready, int Problems, string RuleError);

public sealed record ScanResult(List<FileRow> Rows, int SkippedFolders);

public sealed record ConflictInfo(string Folder, string CurrentName, string WantedName);

public sealed record RenameRequest(int Ready, List<ConflictInfo> Conflicts, int OtherProblems);

public sealed record Failure(string Path, string Message);

public sealed record RenameOutcome(int Renamed, List<Failure> Failures);

public sealed record UndoOutcome(string Description, int Restored, List<Failure> Failures);

public sealed record HistoryItem(string Id, string Description, int Count, long Time);

/// <summary>Başlangıçta kullanıcıya söylenecek bir şey (yarım kalan işlem, okunamayan dosya...).</summary>
public sealed record Notice(string Title, string Message);

/// <summary><c>Paths</c>: programa komut satırından verilen dosyalar (Gezgin'de "Gönder" menüsü); boşsa yoktur.</summary>
public sealed record InitState(
    string Folder,
    List<BuiltInPreset> Presets,
    List<Favorite> Favorites,
    List<HistoryItem> History,
    List<Notice> Notices,
    List<string> Paths);

/// <summary>
/// <c>Available</c>: bu pencerede ayarlanabiliyor mu (deneme ortamında hayır).
/// <c>FolderMenu</c>: klasöre sağ tıklayınca "Dosya Terzisi ile aç". <c>SendTo</c>: dosyalara sağ tık, Gönder menüsü.
/// </summary>
public sealed record ShellIntegrationState(bool Available, bool FolderMenu, bool SendTo);

public sealed record SetShellIntegrationParams(bool FolderMenu, bool SendTo);

public sealed record ImportResult(int Added, List<Favorite> Favorites);

public sealed record ExportResult(bool Saved, int Count);

// ---- İstek parametreleri --------------------------------------------------------------------------------------------

/// <summary>Parametresi olmayan istekler için.</summary>
public sealed record Empty();

public sealed record PickFolderParams(string Current);

public sealed record ScanParams(string Folder, ListingOptions Options);

/// <summary><c>Selected</c> doluysa yalnızca o satırlar adlandırılır; boşsa hepsi.</summary>
/// <summary>Listede elle yazılan ad (F2): <c>Id</c> satırın yeni adı olarak <c>Name</c> kullanılır.</summary>
public sealed record NameOverride(int Id, string Name);

public sealed record PreviewParams(Rule Rule, List<int> Selected, List<NameOverride> Overrides);

/// <summary>Listeden çıkarılacak satırlar (dosyaya dokunulmaz). Cevap, kalan satırların yeni listesidir.</summary>
public sealed record RemoveRowsParams(List<int> Ids);

public sealed record ExecuteRenameParams(RenameDecision Decision, string Description);

public sealed record AddFavoriteParams(string Name, SavedRule Rule);

public sealed record UpdateFavoriteParams(Favorite Favorite);

public sealed record RemoveFavoriteParams(string Id);

public sealed record MoveFavoriteParams(string Id, int Delta);

// ---- Etiketten adlandırma -------------------------------------------------------------------------------------------

public enum TagMissing
{
    /// <summary>Etiketi olmayan dosyaya dokunma.</summary>
    Skip,

    /// <summary>Eksik etiket yerine boş metin kullan.</summary>
    UseEmpty,
}

/// <summary>
/// <c>Template</c>: <c>&lt;ID3.Artist&gt; - &lt;ID3.Track&gt;.\e</c> gibi. <c>Selected</c> boşsa hepsi.
/// </summary>
public sealed record TagPreviewParams(
    string Template,
    TagMissing Missing,
    int NumberStart,
    int NumberStep,
    bool ResetNumberPerFolder,
    bool AutoNumberOnConflict,
    bool UseCreatedDate,
    List<int> Selected,
    Operation Operation,
    string TargetFolder,
    NumberStyle NumberStyle,
    bool ResetNumberPerDate,
    string ResetNumberPerTag,
    bool ShortNames83,
    bool UseScript);

/// <summary>
/// Kullanıcının betik dosyası (VBScript ya da JScript). <c>Language</c>: "vbscript" ya da "jscript".
/// <c>Exists</c> false ise dosya henüz yok; düzenlemeye açılınca örnek içerikle oluşturulur.
/// </summary>
public sealed record ScriptInfo(string Path, bool Exists, string Language);

/// <summary>Liste sütunları için: her satırın, istenen etiketlerdeki değerleri (yoksa boş metin).</summary>
public sealed record TagColumnsParams(List<int> Ids, List<string> Names);

/// <summary><c>Rows[i][j]</c>: <c>Ids[i]</c> satırının <c>Names[j]</c> etiketi.</summary>
public sealed record TagColumnValues(List<List<string>> Rows);

/// <summary><c>Shell</c> true ise Windows kabuğunun özellikleri (<c>PROP.*</c>) da eklenir; yavaştır (dosya başına 0,1-0,3 sn), bu yüzden ayrı istenir.</summary>
public sealed record ReadTagsParams(int Id, bool Shell);

public sealed record TagEntry(string Name, string Value);

/// <summary>Bir dosyanın etiketleri; okunamadıysa <c>Error</c> dolu, liste boştur.</summary>
public sealed record TagList(List<TagEntry> Entries, string? Error);

// ---- Öznitelikler ve tarihler ---------------------------------------------------------------------------------------

public enum TriState
{
    Keep,
    Set,
    Clear,
}

public enum TimeAction
{
    Keep,
    SetTo,
    Shift,
    CopyFrom,
    PhotoTaken,
    Random,
}

public enum TimeKind
{
    Modified,
    Created,
    Accessed,
}

/// <summary><c>Value</c>: <c>SetTo</c> için tarih (epoch ms); <c>ShiftMinutes</c>: <c>Shift</c> için dakika (eksi = geriye).</summary>
/// <summary>
/// <c>Random</c>: <c>Value</c> ile <c>RandomTo</c> arasında her dosyaya ayrı bir zaman (<c>Seed</c> aynıysa aynı sonuç).
/// <c>LimitMin</c>/<c>LimitMax</c>: sonuç bu sınırların içinde tutulur; 0 = sınır yok.
/// </summary>
public sealed record TimeRule(TimeAction Action, long Value, long ShiftMinutes, TimeKind CopyFrom, long RandomTo, int Seed, long LimitMin, long LimitMax);

public sealed record AttributeRule(
    TriState ReadOnly,
    TriState Archive,
    TriState Hidden,
    TriState System,
    TimeRule Modified,
    TimeRule Created,
    TimeRule Accessed,
    bool IncludeFolders);

public sealed record AttributePreviewParams(AttributeRule Rule, List<int> Selected);

public sealed record AttrFlags(bool ReadOnly, bool Archive, bool Hidden, bool System);

public enum AttributeRowStatus
{
    Unchanged,
    Ready,
    Error,
}

public sealed record AttributeRow(
    int Id,
    AttrFlags Before,
    AttrFlags After,
    long ModifiedBefore,
    long ModifiedAfter,
    long CreatedBefore,
    long CreatedAfter,
    long AccessedBefore,
    long AccessedAfter,
    AttributeRowStatus Status,
    string? Message);

public sealed record AttributePreview(List<AttributeRow> Rows, int Changed, int Problems);

public sealed record AttributeOutcome(int Changed, List<Failure> Failures);

public sealed record AttributeUndo(int Restored, List<Failure> Failures);

// ---- Numaralı nesneler ----------------------------------------------------------------------------------------------

public enum ObjectKind
{
    EmptyFile,
    CopyOfFile,
    EmptyFolder,
    CopyOfFolder,
}

/// <summary>
/// <c>NamePattern</c>: <c>?</c> sayının yeri (<c>??</c> = 2 basamak). <c>Size</c>: <c>10K</c>, <c>1,5M</c> gibi (yalnızca boş dosya).
/// <c>SourcePath</c>: kopyalanacak dosya ya da klasör.
/// </summary>
public sealed record NumberedOptions(
    ObjectKind Kind,
    string Folder,
    string NamePattern,
    int Amount,
    int Start,
    int Step,
    string Size,
    string SourcePath,
    NumberStyle NumberStyle);

public enum PlannedStatus
{
    Ready,
    Exists,
    InvalidName,
}

public sealed record PlannedObject(string Name, PlannedStatus Status, string? Message);

/// <summary><c>Problem</c> doluysa hiçbir şey oluşturulamaz.</summary>
public sealed record NumberedPlan(List<PlannedObject> Items, string? Problem, int Ready);

public sealed record CreateOutcome(int Created, List<Failure> Failures);

// ---- Dosya seçici, sürükle-bırak, klasör ağacı ---------------------------------------------------------------------

public sealed record PickFileParams(string Title, string Filter);

/// <summary>Sürüklenen yollar. Tek bir klasörse <c>Folder</c> dolar; aksi hâlde yollar doğrudan liste olur.</summary>
public sealed record OpenPathsParams(List<string> Paths);

public sealed record OpenPathsResult(string? Folder, ScanResult? List);

public sealed record ListFoldersParams(string Path);

public sealed record FolderNode(string Name, string Path, bool HasChildren);

/// <summary><c>Path</c> boşsa kök listesidir: sık kullanılan klasörler ve sürücüler.</summary>
public sealed record FolderList(string Path, List<FolderNode> Folders);
