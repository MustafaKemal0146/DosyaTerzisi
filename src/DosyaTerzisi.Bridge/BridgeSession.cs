using System.Globalization;
using DosyaTerzisi.Bridge.Contract;
using Core = DosyaTerzisi.Core;

namespace DosyaTerzisi.Bridge;

/// <summary>
/// Arayüzün yaptığı işlerin uygulaması. Çekirdeği (<c>DosyaTerzisi.Core</c>) çağırır, sonucu sözleşme tiplerine çevirir.
/// Listenin ve son önizlemenin durumunu tutar: arayüz satırları <c>Id</c> ile (listedeki sıra) belirtir.
/// </summary>
public sealed class BridgeSession
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private readonly Core.RenameService _renames;
    private readonly Core.FavoriteStore _favorites;
    private readonly INativeDialogs _dialogs;
    private readonly string? _presetFile;
    private readonly string _initialFolder;
    private readonly IReadOnlyList<string> _initialPaths;
    private readonly IShellIntegration? _integration;
    private readonly CachedTagReader _tags = new(Core.FileTagReader.Default);
    private readonly CachedTagReader _shellTags = new(Core.FileTagReader.WithShellProperties);
    private readonly Core.ScriptEngine _script;

    private IReadOnlyList<Core.FileEntry> _entries = [];
    private bool _noticesGiven;

    // Listenin kaynağı: bir klasör (seçeneklerle) ya da sürüklenen belirli yollar.
    private ScanParams? _folderSource;
    private List<string>? _pathsSource;

    /// <summary>Son önizlemenin planı; adlandırma bunu yeniden hesaplayıp uygular. Parametre: çakışmalara numara eklensin mi.</summary>
    private Func<bool, IReadOnlyList<(int Id, Core.RenameItem Item)>>? _planner;

    private AttributePreviewParams? _lastAttributes;
    private readonly Stack<Core.AttributeJournal> _attributeJournals = new();

    public BridgeSession(
        Core.RenameService renames,
        Core.FavoriteStore favorites,
        INativeDialogs dialogs,
        string? presetFile = null,
        string initialFolder = "",
        IReadOnlyList<string>? initialPaths = null,
        IShellIntegration? integration = null,
        string? scriptFolder = null)
    {
        _script = new Core.ScriptEngine(scriptFolder);
        _initialPaths = initialPaths ?? [];
        _integration = integration;
        _renames = renames;
        _favorites = favorites;
        _dialogs = dialogs;
        _presetFile = presetFile;
        _initialFolder = initialFolder;
    }

    // ---- Başlangıç --------------------------------------------------------------------------------------------------

    public Task<InitState> InitAsync()
    {
        var notices = new List<Notice>();
        if (!_noticesGiven)
        {
            // Bildirimler yalnızca bir kez verilir: sayfa yenilenince aynı uyarı tekrar çıkmasın.
            _noticesGiven = true;
            if (_renames.History.LoadWarning is { } history) notices.Add(new Notice("Dosya Terzisi", history));
            if (_favorites.LoadWarning is { } favorites) notices.Add(new Notice("Dosya Terzisi", favorites));
            notices.AddRange(_renames.RecoverInterrupted().Select(Recovered));
        }

        return Task.FromResult(new InitState(
            _initialFolder, LoadPresets(), FavoriteList(), HistoryList(), notices, [.. _initialPaths]));
    }

    private static Notice Recovered(Core.RecoveryReport report)
    {
        string text =
            $"Önceki yeniden adlandırma ({report.Entry.Time:dd.MM.yyyy HH:mm}) yarıda kalmış.\n\n" +
            $"{report.Planned} öğeden {report.Done} tanesi yeni adını almıştı" +
            (report.Restored > 0 ? $", {report.Restored} tanesi geçici adından eski adına döndürüldü" : "") +
            ".\nGeri Al düğmesiyle yapılanları geri alabilirsiniz.";
        if (report.Problems.Count > 0) text += "\n\nSorunlu öğeler:\n" + string.Join("\n", report.Problems.Take(8));

        return new Notice("Yarım kalan işlem", text);
    }

    private List<BuiltInPreset> LoadPresets()
    {
        if (_presetFile is null || !File.Exists(_presetFile)) return [];

        try
        {
            return Core.PresetFile.Load(_presetFile)
                .Select((p, i) => (Preset: p, Index: i))
                .Select(x => new BuiltInPreset(
                    $"p{x.Index + 1}",
                    x.Preset.Name,
                    new SavedRule(Map(x.Preset.Method), x.Preset.Search, x.Preset.Replace, false)))
                .ToList();
        }
        catch (IOException)
        {
            return [];
        }
    }

    public Task<string?> PickFolderAsync(PickFolderParams p) =>
        _dialogs.PickFolderAsync(Directory.Exists(p.Current) ? p.Current : null);

    public Task<string?> PickFileAsync(PickFileParams p) => _dialogs.PickOpenFileAsync(p.Title, p.Filter);

    // ---- Gezgin menüleri -------------------------------------------------------------------------------------------

    public ShellIntegrationState ShellIntegration() => _integration?.Get() ?? new ShellIntegrationState(false, false, false);

    public ShellIntegrationState SetShellIntegration(SetShellIntegrationParams p)
    {
        if (_integration is null) throw BridgeException.Failed("Gezgin menüleri bu pencerede ayarlanamaz.");

        try
        {
            return _integration.Set(p.FolderMenu, p.SendTo);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw BridgeException.Failed($"Gezgin menüleri ayarlanamadı: {ex.Message}");
        }
    }

    // ---- Liste ---------------------------------------------------------------------------------------------------

    public async Task<ScanResult> ScanAsync(ScanParams p)
    {
        string folder = p.Folder.Trim().Trim('"');
        if (!Directory.Exists(folder)) throw BridgeException.Failed($"Klasör bulunamadı: {folder}");

        var options = new Core.ScanOptions
        {
            IncludeFiles = p.Options.IncludeFiles,
            IncludeFolders = p.Options.IncludeFolders,
            MaxDepth = p.Options.Recursive ? Math.Clamp(p.Options.Depth, 1, 99) : 0,
            Filter = p.Options.Filter,
            Culture = Turkish,
        };

        var result = await Task.Run(() => Core.FileScanner.Scan(folder, options));
        _folderSource = p with { Folder = folder };
        _pathsSource = null;
        return SetEntries(result.Entries, result.Errors.Count);
    }

    /// <summary>Sürüklenen yollar: tek bir klasörse onu aç, aksi hâlde dosyaları olduğu gibi listele.</summary>
    public async Task<OpenPathsResult> OpenPathsAsync(OpenPathsParams p)
    {
        var paths = p.Paths.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (paths.Count == 0) throw BridgeException.BadRequest("Hiçbir yol verilmedi.");
        if (paths.Count == 1 && Directory.Exists(paths[0])) return new OpenPathsResult(Path.GetFullPath(paths[0]), null);

        var result = await Task.Run(() => Core.FileScanner.FromPaths(paths));
        _pathsSource = result.Entries.Select(e => e.FullPath).ToList();
        _folderSource = null;
        return new OpenPathsResult(null, SetEntries(result.Entries, result.Errors.Count));
    }

    /// <summary>Aynı listeyi yeniden okur: adlandırma ya da geri almadan sonra.</summary>
    public async Task<ScanResult> RefreshAsync()
    {
        if (_folderSource is { } folder) return await ScanAsync(folder);

        if (_pathsSource is { } paths)
        {
            var result = await Task.Run(() => Core.FileScanner.FromPaths(paths));
            _pathsSource = result.Entries.Select(e => e.FullPath).ToList();
            return SetEntries(result.Entries, result.Errors.Count);
        }

        throw BridgeException.Failed("Önce bir klasör seçin.");
    }

    private ScanResult SetEntries(IReadOnlyList<Core.FileEntry> entries, int skipped)
    {
        _entries = entries;
        _planner = null;
        _lastAttributes = null;
        _tags.Clear();
        _shellTags.Clear();

        var rows = entries
            .Select((e, id) => new FileRow(id, e.Name, e.Directory, e.IsDirectory, e.Size, Millis(e.Modified)))
            .ToList();
        return new ScanResult(rows, skipped);
    }

    /// <summary>Seçili satırları listeden çıkarır; dosyalar yerinde kalır. Klasör listesi yenilenirse geri gelirler.</summary>
    public Task<ScanResult> RemoveRowsAsync(RemoveRowsParams p)
    {
        var drop = p.Ids.ToHashSet();
        var kept = _entries.Where((_, id) => !drop.Contains(id)).ToList();
        if (_pathsSource is not null) _pathsSource = kept.Select(e => e.FullPath).ToList();
        return Task.FromResult(SetEntries(kept, 0));
    }

    /// <summary>Yolla verilen listede, yapılan adlandırmaları izler (yeniden okuyunca dosyalar bulunsun).</summary>
    private void FollowMoves(IEnumerable<Core.RenameMove> moves)
    {
        if (_pathsSource is not { } paths) return;

        // Copies, shortcuts and links leave the original where it is; only renames and moves change a path.
        var map = moves
            .Where(m => m.Kind == Core.TransferKind.Rename)
            .ToDictionary(m => Path.Combine(m.Directory, m.OldName), m => Path.Combine(m.TargetDirectory ?? m.Directory, m.NewName), StringComparer.OrdinalIgnoreCase);
        _pathsSource = paths.Select(path => map.GetValueOrDefault(path, path)).ToList();
    }

    // ---- Klasör ağacı -----------------------------------------------------------------------------------------------

    public Task<FolderList> ListFoldersAsync(ListFoldersParams p) => Task.Run(() => ListFolders(p.Path));

    private static FolderList ListFolders(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return new FolderList("", Roots());

        if (!Directory.Exists(path)) throw BridgeException.Failed($"Klasör bulunamadı: {path}");

        try
        {
            var folders = new DirectoryInfo(path)
                .EnumerateDirectories()
                .Where(d => (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                .OrderBy(d => d.Name, new Core.NaturalComparer(Turkish))
                .Take(2000)
                .Select(d => new FolderNode(d.Name, d.FullName, HasSubfolders(d.FullName)))
                .ToList();
            return new FolderList(path, folders);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw BridgeException.Failed($"Klasör okunamadı: {ex.Message}");
        }
    }

    private static List<FolderNode> Roots()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var quick = new (string Name, string Path)[]
        {
            ("Masaüstü", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            ("Belgeler", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            ("İndirilenler", Path.Combine(profile, "Downloads")),
            ("Resimler", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            ("Müzik", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
            ("Videolar", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
        };

        var nodes = quick
            .Where(q => q.Path.Length > 0 && Directory.Exists(q.Path))
            .Select(q => new FolderNode(q.Name, q.Path, HasSubfolders(q.Path)))
            .ToList();

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Yerel Disk" : drive.VolumeLabel;
                nodes.Add(new FolderNode($"{label} ({drive.Name.TrimEnd('\\')})", drive.RootDirectory.FullName, HasSubfolders(drive.RootDirectory.FullName)));
            }
            catch (IOException)
            {
            }
        }
        return nodes;
    }

    private static bool HasSubfolders(string path)
    {
        try
        {
            return new DirectoryInfo(path).EnumerateDirectories()
                .Any(d => (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    // ---- Önizleme (adlandırma ve etiketten adlandırma) --------------------------------------------------------------

    public async Task<Preview> PreviewAsync(PreviewParams p)
    {
        var rule = p.Rule;
        var selected = p.Selected.ToHashSet();
        var typed = p.Overrides
            .Where(o => o.Id >= 0 && o.Id < _entries.Count)
            .GroupBy(o => _entries[o.Id].FullPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Name, StringComparer.OrdinalIgnoreCase);
        _planner = autoNumber => PlanItems(rule, selected, autoNumber, typed);

        return ToPreview(await Task.Run(() => _planner(false)));
    }

    public async Task<Preview> TagPreviewAsync(TagPreviewParams p)
    {
        var selected = p.Selected.ToHashSet();
        _planner = autoNumber => PlanTags(p, selected, autoNumber);

        return ToPreview(await Task.Run(() => _planner(false)));
    }

    private Preview ToPreview(IReadOnlyList<(int Id, Core.RenameItem Item)> planned)
    {
        var byId = planned.ToDictionary(x => x.Id, x => x.Item);
        var rows = new List<PreviewRow>(_entries.Count);
        int ready = 0, problems = 0;
        string ruleError = "";

        for (int id = 0; id < _entries.Count; id++)
        {
            if (!byId.TryGetValue(id, out var item))
            {
                rows.Add(new PreviewRow(id, _entries[id].Name, RowStatus.Unchanged, null, null));
                continue;
            }

            if (item.Status == Core.RenameStatus.Ready) ready++;
            else if (item.Status is Core.RenameStatus.Conflict or Core.RenameStatus.InvalidName or Core.RenameStatus.Error) problems++;
            if (item.Status == Core.RenameStatus.Error && ruleError.Length == 0) ruleError = item.Message ?? "";

            rows.Add(new PreviewRow(id, item.NewName, Map(item.Status), item.Message, item.DestinationDirectory));
        }

        return new Preview(rows, ready, problems, ruleError);
    }

    private List<int> Targets(HashSet<int> selected) =>
        Enumerable.Range(0, _entries.Count).Where(id => selected.Count == 0 || selected.Contains(id)).ToList();

    /// <summary>Adlandırılacak satırlar: seçili olanlar, seçim yoksa hepsi.</summary>
    private IReadOnlyList<(int Id, Core.RenameItem Item)> PlanItems(Rule rule, HashSet<int> selected, bool autoNumber, IReadOnlyDictionary<string, string>? typed = null)
    {
        var targets = Targets(selected);
        if (targets.Count == 0) return [];

        var options = new Core.RenameOptions
        {
            Method = Map(rule.Method),
            Search = rule.Search,
            Replace = rule.Replace,
            CaseSensitive = rule.CaseSensitive,
            NumberStart = rule.NumberStart,
            NumberStep = rule.NumberStep,
            ResetNumberPerFolder = rule.ResetNumberPerFolder,
            ResetNumberPerDate = rule.ResetNumberPerDate,
            ResetNumberPerTag = rule.ResetNumberPerTag,
            TagReader = _tags,
            DateSource = rule.UseCreatedDate ? Core.DateSource.Created : Core.DateSource.Modified,
            Culture = Turkish,
            NameOverrides = typed,
            Kind = Map(rule.Operation),
            TargetFolder = rule.TargetFolder,
            ShortNames83 = rule.ShortNames83,
            UseScript = rule.UseScript,
            Script = _script,
            NumberFormat = Map(rule.NumberStyle),
        };
        var plan = new Core.PlanOptions
        {
            Conflict = autoNumber || rule.AutoNumberOnConflict ? Core.ConflictPolicy.AutoNumber : Core.ConflictPolicy.Report,
        };

        var items = Core.RenamePlanner.Build(targets.Select(id => _entries[id]).ToList(), options, plan);
        return targets.Zip(items, (id, item) => (id, item)).ToList();
    }

    private IReadOnlyList<(int Id, Core.RenameItem Item)> PlanTags(TagPreviewParams p, HashSet<int> selected, bool autoNumber)
    {
        var targets = Targets(selected);
        if (targets.Count == 0) return [];

        var options = new Core.TagRenameOptions
        {
            Template = p.Template,
            Missing = p.Missing == TagMissing.UseEmpty ? Core.MissingTagPolicy.UseEmpty : Core.MissingTagPolicy.Skip,
            NumberStart = p.NumberStart,
            NumberStep = p.NumberStep,
            ResetNumberPerFolder = p.ResetNumberPerFolder,
            ResetNumberPerDate = p.ResetNumberPerDate,
            ResetNumberPerTag = p.ResetNumberPerTag,
            DateSource = p.UseCreatedDate ? Core.DateSource.Created : Core.DateSource.Modified,
            Culture = Turkish,
            Kind = Map(p.Operation),
            TargetFolder = p.TargetFolder,
            ShortNames83 = p.ShortNames83,
            UseScript = p.UseScript,
            Script = _script,
            NumberFormat = Map(p.NumberStyle),
        };
        var plan = new Core.PlanOptions
        {
            Conflict = autoNumber || p.AutoNumberOnConflict ? Core.ConflictPolicy.AutoNumber : Core.ConflictPolicy.Report,
        };

        var items = Core.TagRenamePlanner.Build(targets.Select(id => _entries[id]).ToList(), options, p.Template.Contains("<PROP.", StringComparison.OrdinalIgnoreCase) ? _shellTags : _tags, plan);
        return targets.Zip(items, (id, item) => (id, item)).ToList();
    }

    public Task<ScriptInfo> ScriptInfoAsync() => Task.FromResult(CurrentScript());

    /// <summary>Betik dosyası yoksa örnek içerikle oluşturur, sonra düzenleyicide açar.</summary>
    public async Task<ScriptInfo> EditScriptAsync()
    {
        try
        {
            string path = _script.EnsureScriptFile();
            await _dialogs.OpenInEditorAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            throw BridgeException.Failed($"Betik dosyası açılamadı: {ex.Message}");
        }
        return CurrentScript();
    }

    private ScriptInfo CurrentScript() => new(_script.ScriptPath, _script.Exists, _script.Language);

    public Task<TagColumnValues> TagColumnsAsync(TagColumnsParams p) =>
        Task.Run(() =>
        {
            var reader = p.Names.Any(n => n.StartsWith("PROP.", StringComparison.OrdinalIgnoreCase)) ? _shellTags : _tags;
            var rows = new List<List<string>>(p.Ids.Count);
            foreach (int id in p.Ids)
            {
                if (id < 0 || id >= _entries.Count) throw BridgeException.BadRequest($"Geçersiz satır: {id}");
                var values = reader.Read(_entries[id]).Values;
                rows.Add(p.Names.Select(n => values.TryGetValue(n, out var v) ? v.Text : "").ToList());
            }
            return new TagColumnValues(rows);
        });

    public Task<TagList> ReadTagsAsync(ReadTagsParams p)
    {
        if (p.Id < 0 || p.Id >= _entries.Count) throw BridgeException.BadRequest($"Geçersiz satır: {p.Id}");

        return Task.Run(() =>
        {
            var read = (p.Shell ? _shellTags : _tags).Read(_entries[p.Id]);
            var entries = read.Values
                .OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase)
                .Select(v => new TagEntry(v.Key, v.Value.Text))
                .ToList();
            return new TagList(entries, read.Error);
        });
    }

    // ---- Yeniden adlandırma ve geri alma ----------------------------------------------------------------------------

    public async Task<RenameRequest> PrepareRenameAsync()
    {
        if (_planner is not { } planner) return new RenameRequest(0, [], 0);

        var planned = await Task.Run(() => planner(false));
        return new RenameRequest(
            planned.Count(x => x.Item.Status == Core.RenameStatus.Ready),
            planned
                .Where(x => x.Item.Status == Core.RenameStatus.Conflict)
                .Select(x => new ConflictInfo(x.Item.Entry.Directory, x.Item.Entry.Name, x.Item.NewName))
                .ToList(),
            planned.Count(x => x.Item.Status is Core.RenameStatus.InvalidName or Core.RenameStatus.Error));
    }

    public async Task<RenameOutcome> ExecuteRenameAsync(ExecuteRenameParams p)
    {
        if (p.Decision == RenameDecision.Cancel || _planner is not { } planner) return new RenameOutcome(0, []);

        var planned = await Task.Run(() => planner(p.Decision == RenameDecision.AutoNumber));
        var items = planned.Select(x => x.Item).ToList();

        var result = await Task.Run(() => _renames.Execute(items, p.Description));
        FollowMoves(result.Journal.Moves);
        _planner = null;

        return new RenameOutcome(
            result.Journal.Moves.Count,
            result.Failures.Select(f => new Failure(f.Path, $"→ {f.NewName}: {f.Message}")).ToList());
    }

    public async Task<UndoOutcome?> UndoAsync()
    {
        var undone = await Task.Run(() => _renames.UndoLatest());
        if (undone is not { } outcome) return null;

        FollowMoves(outcome.Result.Journal.Moves);
        _planner = null;
        return new UndoOutcome(
            outcome.Entry.Description,
            outcome.Result.Journal.Moves.Count,
            outcome.Result.Failures.Select(f => new Failure(f.Path, $"→ {f.NewName}: {f.Message}")).ToList());
    }

    public Task<List<HistoryItem>> HistoryAsync() => Task.FromResult(HistoryList());

    private List<HistoryItem> HistoryList() => _renames.History.Entries
        .Where(e => e.State == Core.OperationState.Completed)
        .Reverse()
        .Select(e => new HistoryItem(e.Id.ToString("N"), e.Description, e.Moves.Count, Millis(e.Time)))
        .ToList();

    // ---- Öznitelikler ve tarihler -----------------------------------------------------------------------------------

    public async Task<AttributePreview> AttributePreviewAsync(AttributePreviewParams p)
    {
        _lastAttributes = p;
        var planned = await Task.Run(() => PlanAttributes(p));

        var byId = planned.ToDictionary(x => x.Id, x => x.Item);
        var rows = new List<AttributeRow>(_entries.Count);
        int changed = 0, problems = 0;

        for (int id = 0; id < _entries.Count; id++)
        {
            var entry = _entries[id];
            if (!byId.TryGetValue(id, out var item))
            {
                rows.Add(AttributeRowOf(id, entry, entry.Attributes, entry.Modified, entry.Created, entry.Accessed, AttributeRowStatus.Unchanged, null));
                continue;
            }

            var status = item.Status switch
            {
                Core.AttributeStatus.Ready => AttributeRowStatus.Ready,
                Core.AttributeStatus.Error => AttributeRowStatus.Error,
                _ => AttributeRowStatus.Unchanged,
            };
            if (status == AttributeRowStatus.Ready) changed++;
            else if (status == AttributeRowStatus.Error) problems++;

            rows.Add(AttributeRowOf(id, entry, item.NewAttributes, item.NewModified, item.NewCreated, item.NewAccessed, status, item.Message));
        }

        return new AttributePreview(rows, changed, problems);
    }

    private static AttributeRow AttributeRowOf(
        int id, Core.FileEntry entry, FileAttributes after, DateTime modified, DateTime created, DateTime accessed,
        AttributeRowStatus status, string? message) => new(
        id, Flags(entry.Attributes), Flags(after),
        Millis(entry.Modified), Millis(modified), Millis(entry.Created), Millis(created), Millis(entry.Accessed), Millis(accessed),
        status, message);

    private static AttrFlags Flags(FileAttributes a) => new(
        a.HasFlag(FileAttributes.ReadOnly), a.HasFlag(FileAttributes.Archive), a.HasFlag(FileAttributes.Hidden), a.HasFlag(FileAttributes.System));

    /// <param name="fresh">Dosyaların şimdiki hâlini diskten oku (uygularken): listeden sonra başka bir değişiklik olmuş olabilir.</param>
    private IReadOnlyList<(int Id, Core.AttributeItem Item)> PlanAttributes(AttributePreviewParams p, bool fresh = false)
    {
        var rule = new Core.AttributeRule
        {
            ReadOnly = Map(p.Rule.ReadOnly),
            Archive = Map(p.Rule.Archive),
            Hidden = Map(p.Rule.Hidden),
            System = Map(p.Rule.System),
            Modified = Map(p.Rule.Modified),
            Created = Map(p.Rule.Created),
            Accessed = Map(p.Rule.Accessed),
            IncludeFolders = p.Rule.IncludeFolders,
        };

        var selected = p.Selected.ToHashSet();
        var targets = Targets(selected).Where(id => p.Rule.IncludeFolders || !_entries[id].IsDirectory).ToList();
        var entries = targets
            .Select(id => fresh ? Core.FileEntry.FromPath(_entries[id].FullPath) ?? _entries[id] : _entries[id])
            .ToList();
        var items = Core.AttributePlanner.Build(entries, rule, _tags);
        return targets.Zip(items, (id, item) => (id, item)).ToList();
    }

    public async Task<AttributeOutcome> ApplyAttributesAsync(AttributePreviewParams p)
    {
        var items = (await Task.Run(() => PlanAttributes(p, fresh: true))).Select(x => x.Item).ToList();
        var result = await Task.Run(() => Core.AttributeExecutor.Apply(items));

        if (result.Journal.Items.Count > 0) _attributeJournals.Push(result.Journal);
        _lastAttributes = null;
        return new AttributeOutcome(
            result.Journal.Items.Count,
            result.Failures.Select(f => new Failure(f.Path, f.Message)).ToList());
    }

    public async Task<AttributeUndo?> UndoAttributesAsync()
    {
        if (!_attributeJournals.TryPop(out var journal)) return null;

        var result = await Task.Run(() => Core.AttributeExecutor.Undo(journal));
        _lastAttributes = null;
        return new AttributeUndo(
            journal.Items.Count - result.Failures.Count,
            result.Failures.Select(f => new Failure(f.Path, f.Message)).ToList());
    }

    // ---- Numaralı nesneler ------------------------------------------------------------------------------------------

    public Task<NumberedPlan> NumberedPreviewAsync(NumberedOptions p) => Task.Run(() => PlanNumbered(p).Plan);

    public async Task<CreateOutcome> CreateNumberedAsync(NumberedOptions p)
    {
        var (options, core, plan) = await Task.Run(() => PlanNumbered(p));
        if (core is null || plan.Problem is not null) throw BridgeException.Failed(plan.Problem ?? "Oluşturulamadı.");

        var result = await Task.Run(() => Core.NumberedObjectCreator.Create(options!, core));
        return new CreateOutcome(result.Created.Count, result.Failures.Select(f => new Failure(f.Path, f.Message)).ToList());
    }

    private static (Core.NumberedObjectOptions? Options, Core.NumberedObjectPlan? Core, NumberedPlan Plan) PlanNumbered(NumberedOptions p)
    {
        long size = 0;
        if (p.Kind == ObjectKind.EmptyFile && p.Size.Trim().Length > 0 && !Core.SizeParser.TryParse(p.Size, out size))
            return (null, null, new NumberedPlan([], "Boyut anlaşılamadı. Örnek: 512, 10K, 1,5M, 2G", 0));

        var options = new Core.NumberedObjectOptions
        {
            Kind = p.Kind switch
            {
                ObjectKind.CopyOfFile => Core.ObjectKind.CopyOfFile,
                ObjectKind.EmptyFolder => Core.ObjectKind.EmptyFolder,
                ObjectKind.CopyOfFolder => Core.ObjectKind.CopyOfFolder,
                _ => Core.ObjectKind.EmptyFile,
            },
            Folder = p.Folder,
            NamePattern = p.NamePattern,
            Amount = p.Amount,
            Start = p.Start,
            Step = p.Step,
            NumberFormat = Map(p.NumberStyle),
            SizeBytes = size,
            SourcePath = p.SourcePath,
        };

        var core = Core.NumberedObjectPlanner.Build(options);
        var items = core.Items
            .Select(i => new PlannedObject(i.Name, i.Status switch
            {
                Core.ObjectStatus.Exists => PlannedStatus.Exists,
                Core.ObjectStatus.InvalidName => PlannedStatus.InvalidName,
                _ => PlannedStatus.Ready,
            }, i.Message))
            .ToList();
        return (options, core, new NumberedPlan(items, core.Problem, items.Count(i => i.Status == PlannedStatus.Ready)));
    }

    // ---- Favoriler --------------------------------------------------------------------------------------------------

    public Task<List<Favorite>> AddFavoriteAsync(AddFavoriteParams p)
    {
        _favorites.Add(p.Name, Map(p.Rule.Method), p.Rule.Search, p.Rule.Replace, p.Rule.CaseSensitive);
        return Task.FromResult(FavoriteList());
    }

    public Task<List<Favorite>> UpdateFavoriteAsync(UpdateFavoriteParams p)
    {
        var f = p.Favorite;
        var updated = _favorites.Update(new Core.Favorite(
            ParseId(f.Id), f.Name, Map(f.Rule.Method), f.Rule.Search, f.Rule.Replace, f.Rule.CaseSensitive));
        if (updated is null) throw BridgeException.Failed("Favori bulunamadı.");

        return Task.FromResult(FavoriteList());
    }

    public Task<List<Favorite>> RemoveFavoriteAsync(RemoveFavoriteParams p)
    {
        _favorites.Remove(ParseId(p.Id));
        return Task.FromResult(FavoriteList());
    }

    public Task<List<Favorite>> MoveFavoriteAsync(MoveFavoriteParams p)
    {
        _favorites.Move(ParseId(p.Id), p.Delta);
        return Task.FromResult(FavoriteList());
    }

    public async Task<ImportResult> ImportFavoritesAsync()
    {
        string? path = await _dialogs.PickOpenFileAsync(
            "Favorileri içe aktar", "Favori dosyaları (*.json;*.txt)|*.json;*.txt|Tümü (*.*)|*.*");
        if (path is null) return new ImportResult(0, FavoriteList());

        try
        {
            return new ImportResult(_favorites.Import(path), FavoriteList());
        }
        catch (InvalidDataException ex)
        {
            throw BridgeException.Failed(ex.Message);
        }
    }

    public async Task<ExportResult> ExportFavoritesAsync()
    {
        if (_favorites.Items.Count == 0) return new ExportResult(false, 0);

        string? path = await _dialogs.PickSaveFileAsync(
            "Favorileri dışa aktar", "Favori dosyası (*.json)|*.json", "favoriler.json");
        if (path is null) return new ExportResult(false, 0);

        try
        {
            _favorites.Export(path);
            return new ExportResult(true, _favorites.Items.Count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw BridgeException.Failed(ex.Message);
        }
    }

    private List<Favorite> FavoriteList() => _favorites.Items
        .Select(f => new Favorite(f.Id.ToString("N"), f.Name, new SavedRule(Map(f.Method), f.Search, f.Replace, f.CaseSensitive)))
        .ToList();

    private static Guid ParseId(string id) =>
        Guid.TryParse(id, out var guid) ? guid : throw BridgeException.BadRequest($"Geçersiz kimlik: {id}");

    // ---- Çeviriler --------------------------------------------------------------------------------------------------

    private static long Millis(DateTime local) => new DateTimeOffset(local).ToUnixTimeMilliseconds();

    private static DateTime FromMillis(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;

    private static Core.Tri Map(TriState state) => state switch
    {
        TriState.Set => Core.Tri.Set,
        TriState.Clear => Core.Tri.Clear,
        _ => Core.Tri.Keep,
    };

    private static Core.TimestampRule Map(TimeRule rule)
    {
        var mapped = rule.Action switch
        {
            TimeAction.SetTo => Core.TimestampRule.SetTo(FromMillis(rule.Value)),
            TimeAction.Shift => Core.TimestampRule.ShiftBy(TimeSpan.FromMinutes(rule.ShiftMinutes)),
            TimeAction.CopyFrom => Core.TimestampRule.CopyOf(rule.CopyFrom switch
            {
                TimeKind.Created => Core.TimestampKind.Created,
                TimeKind.Accessed => Core.TimestampKind.Accessed,
                _ => Core.TimestampKind.Modified,
            }),
            TimeAction.PhotoTaken => Core.TimestampRule.FromPhotoTaken(),
            TimeAction.Random => Core.TimestampRule.RandomBetween(FromMillis(rule.Value), FromMillis(rule.RandomTo), rule.Seed),
            _ => new Core.TimestampRule(),
        };
        if (rule.LimitMin != 0) mapped.Min = FromMillis(rule.LimitMin);
        if (rule.LimitMax != 0) mapped.Max = FromMillis(rule.LimitMax);
        return mapped;
    }

    private static Core.NumberFormat Map(NumberStyle style) => style switch
    {
        NumberStyle.Hexadecimal => Core.NumberFormat.Hexadecimal,
        NumberStyle.Letters => Core.NumberFormat.Letters,
        _ => Core.NumberFormat.Decimal,
    };

    private static Core.TransferKind Map(Operation operation) => operation switch
    {
        Operation.Copy => Core.TransferKind.Copy,
        Operation.Shortcut => Core.TransferKind.Shortcut,
        Operation.Hardlink => Core.TransferKind.Hardlink,
        _ => Core.TransferKind.Rename,
    };

    private static Core.RenameMethod Map(RenameMethod method) => method switch
    {
        RenameMethod.Wildcard => Core.RenameMethod.Wildcard,
        RenameMethod.Regex => Core.RenameMethod.RegularExpression,
        _ => Core.RenameMethod.Translation,
    };

    private static RenameMethod Map(Core.RenameMethod method) => method switch
    {
        Core.RenameMethod.Wildcard => RenameMethod.Wildcard,
        Core.RenameMethod.RegularExpression => RenameMethod.Regex,
        _ => RenameMethod.Translation,
    };

    private static RowStatus Map(Core.RenameStatus status) => status switch
    {
        Core.RenameStatus.Ready => RowStatus.Ready,
        Core.RenameStatus.Conflict => RowStatus.Conflict,
        Core.RenameStatus.InvalidName => RowStatus.Invalid,
        Core.RenameStatus.Error => RowStatus.Error,
        Core.RenameStatus.MissingTag => RowStatus.MissingTag,
        _ => RowStatus.Unchanged,
    };
}

/// <summary>
/// Etiketleri bir kez okur ve hatırlar: kural her değiştiğinde binlerce müzik/resim dosyası yeniden açılmasın.
/// Liste yenilenince (<see cref="Clear"/>) unutulur.
/// </summary>
internal sealed class CachedTagReader(Core.ITagReader inner) : Core.ITagReader
{
    private readonly Dictionary<string, Core.TagReadResult> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public Core.TagReadResult Read(Core.FileEntry entry)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(entry.FullPath, out var known)) return known;
        }

        var result = inner.Read(entry);
        lock (_gate) _cache[entry.FullPath] = result;
        return result;
    }

    public void Clear()
    {
        lock (_gate) _cache.Clear();
    }
}
