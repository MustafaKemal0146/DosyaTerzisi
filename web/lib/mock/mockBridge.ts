import type {
  AddFavoriteParams,
  AttributeOutcome,
  AttributePreview,
  AttributePreviewParams,
  AttributeUndo,
  AttrFlags,
  Bridge,
  CreateOutcome,
  ExecuteRenameParams,
  ExportResult,
  Favorite,
  FolderList,
  HistoryItem,
  ImportResult,
  InitState,
  ListFoldersParams,
  NumberedOptions,
  NumberedPlan,
  OpenPathsParams,
  OpenPathsResult,
  PickFileParams,
  Preview,
  PreviewParams,
  RemoveRowsParams,
  ScriptInfo,
  TagColumnsParams,
  TagColumnValues,
  SetShellIntegrationParams,
  ShellIntegrationState,
  ReadTagsParams,
  RenameOutcome,
  RenameRequest,
  ScanParams,
  ScanResult,
  TagList,
  TagPreviewParams,
  UndoOutcome,
} from "../contract";
import { checkName, plan, planTags, type MockEntry, type PlanResult } from "./engine";
import { attrsOf, folderTree, listFolderNodes, parseSize, planAttributes, planNumbered, tagColumnValue, tagList, tagsOf } from "./features";
import { builtInPresets } from "./presets";
import { bigEntries, sampleEntries, sampleRoot } from "./sample";

interface Move {
  id: number;
  from: string;
  to: string;
}

interface Operation extends HistoryItem {
  moves: Move[];
}

interface AttributeSnapshot {
  id: number;
  attrs: AttrFlags;
  modified: number;
  created: number;
  accessed: number;
}

const pause = (ms = 40) => new Promise((resolve) => setTimeout(resolve, ms));

const fullPath = (e: { dir: string; name: string }) => `${e.dir}\\${e.name}`;

/** Windows uygulaması olmadan çalışan sahte köprü: bellekte bir dosya sistemi tutar. */
export class MockBridge implements Bridge {
  readonly kind = "mock" as const;

  private entries: MockEntry[];
  private listed: MockEntry[] = [];
  private source: { folder: string; params: ScanParams } | { paths: string[] } | null = null;
  /** Son önizlemenin planı; adlandırma bunu yeniden hesaplayıp uygular. */
  private planner: ((autoNumber: boolean) => PlanResult) | null = null;
  private operations: Operation[] = [];
  private attributeUndo: AttributeSnapshot[][] = [];
  private favorites: Favorite[] = [
    { id: "f1", name: "Türkçe → ASCII", rule: { method: "translation", search: "ÜüĞğİıŞşÇçÖö ", replace: "UuGgIiSsCcOo_", caseSensitive: false } },
    { id: "f2", name: "Tire boşluklarını düzelt", rule: { method: "wildcard", search: "* - *", replace: "\\1-\\2", caseSensitive: false } },
  ];
  private nextId = 100;
  private root: string;

  constructor(big = 0) {
    this.entries = big > 0 ? bigEntries(big) : sampleEntries();
    this.root = big > 0 ? `${sampleRoot}\\Büyük` : sampleRoot;
  }

  // ---- Başlangıç ve liste -----------------------------------------------------------------------------------------

  init = async (): Promise<InitState> => {
    await pause();
    return { folder: this.root, presets: builtInPresets, favorites: this.favorites, history: this.historyItems(), notices: [], paths: [] };
  };

  pickFolder = async ({ current }: { current: string }): Promise<string | null> => {
    await pause();
    return current || this.root;
  };

  // Dosya seçme pencereleri tarayıcıda yok; gerçek uygulamada Windows'un kendi pencereleri açılır.
  pickFile = async (_params: PickFileParams): Promise<string | null> => {
    await pause();
    return `${this.root}\\sablon.docx`;
  };

  scan = async (params: ScanParams): Promise<ScanResult> => {
    await pause(80);
    const { folder, options } = params;
    const base = folder.replace(/[\\/]+$/, "").toLowerCase();
    const depthOf = (dir: string) => dir.toLowerCase().slice(base.length).split(/[\\/]/).filter(Boolean).length;
    const filters = options.filter
      .split(";")
      .map((f) => f.trim())
      .filter((f) => f && f !== "*" && f !== "*.*")
      .map((f) => new RegExp(`^${f.replace(/[.+^${}()|[\]\\]/g, "\\$&").replace(/\*/g, ".*").replace(/\?/g, ".")}$`, "i"));

    const inFolder = this.entries.filter((e) => {
      const dir = e.dir.toLowerCase();
      if (dir !== base && !(options.recursive && dir.startsWith(base + "\\") && depthOf(e.dir) <= options.depth)) return false;
      if (e.isDir ? !options.includeFolders : !options.includeFiles) return false;
      return filters.length === 0 || filters.some((f) => f.test(e.name));
    });

    const collator = new Intl.Collator("tr-TR", { numeric: true, sensitivity: "base" });
    inFolder.sort((a, b) => {
      if (a.dir !== b.dir) return a.dir === folder ? -1 : b.dir === folder ? 1 : collator.compare(a.dir, b.dir);
      if (a.isDir !== b.isDir) return a.isDir ? -1 : 1;
      return collator.compare(a.name, b.name);
    });

    this.source = { folder, params };
    return this.setListed(inFolder, 0);
  };

  /** Gerçek uygulamada sürüklenen dosyaların yolları gelir; tarayıcıda bunlar bilinmez, yollar sahte dosya sistemiyle eşlenir. */
  openPaths = async (params: OpenPathsParams): Promise<OpenPathsResult> => {
    await pause(60);
    let paths = params.paths;
    // Tarayıcıda bırakılan dosyaların yolu bilinmez; gösterim için ilk birkaç dosya bırakılmış gibi davranılır.
    if (paths.length === 0) paths = this.entries.filter((e) => !e.isDir).slice(0, 5).map(fullPath);
    const lower = paths.map((p) => p.toLowerCase());
    const folder = this.entries.find((e) => e.isDir && lower.length === 1 && fullPath(e).toLowerCase() === lower[0]);
    if (folder) return { folder: fullPath(folder), list: null };
    if (lower.length === 1 && lower[0] === this.root.toLowerCase()) return { folder: this.root, list: null };

    const found = this.entries.filter((e) => lower.includes(fullPath(e).toLowerCase()));
    this.source = { paths: found.map(fullPath) };
    return { folder: null, list: this.setListed(found, paths.length - found.length) };
  };

  refresh = async (): Promise<ScanResult> => {
    if (!this.source) throw new Error("Önce bir klasör seçin.");
    if ("folder" in this.source) return this.scan(this.source.params);

    const known = this.source.paths.map((p) => p.toLowerCase());
    const found = this.entries.filter((e) => known.includes(fullPath(e).toLowerCase()));
    return this.setListed(found, 0);
  };

  private setListed(entries: MockEntry[], skippedFolders: number): ScanResult {
    this.listed = entries.map((e, id) => ({ ...e, id }));
    this.planner = null;
    return {
      rows: this.listed.map(({ id, name, dir, isDir, size, modified }) => ({ id, name, dir, isDir, size, modified })),
      skippedFolders,
    };
  }

  private integration: ShellIntegrationState = { available: true, folderMenu: false, sendTo: false };

  shellIntegration = async (): Promise<ShellIntegrationState> => {
    await pause(10);
    return this.integration;
  };

  setShellIntegration = async ({ folderMenu, sendTo }: SetShellIntegrationParams): Promise<ShellIntegrationState> => {
    await pause(30);
    this.integration = { available: true, folderMenu, sendTo };
    return this.integration;
  };

  /** Liste sütunları: `rows[i][j]` = ids[i] satırının names[j] etiketi (yoksa boş metin). */
  tagColumns = async ({ ids, names }: TagColumnsParams): Promise<TagColumnValues> => ({
    rows: ids.map((id) => {
      const entry = this.listed[id];
      return names.map((name) => (entry ? tagColumnValue(entry, name) : ""));
    }),
  });

  private scriptExists = false;

  scriptInfo = async (): Promise<ScriptInfo> => ({
    path: "C:\\Users\\Musa\\AppData\\Roaming\\DosyaTerzisi\\Script.vbs",
    exists: this.scriptExists,
    language: "vbscript",
  });

  /** Gerçek köprü dosyayı (yoksa örnek içerikle oluşturup) Not Defteri'nde açar; burada yalnızca "var" olur. */
  editScript = async (): Promise<ScriptInfo> => {
    this.scriptExists = true;
    return this.scriptInfo();
  };

  removeRows = async ({ ids }: RemoveRowsParams): Promise<ScanResult> => {
    await pause(10);
    const drop = new Set(ids);
    const kept = this.listed.filter((e) => !drop.has(e.id));
    if (this.source && "paths" in this.source) this.source.paths = kept.map(fullPath);
    return this.setListed(kept, 0);
  };

  listFolders = async ({ path }: ListFoldersParams): Promise<FolderList> => {
    await pause(30);
    const tree = folderTree(this.entries);
    const user = "C:\\Users\\Musa";
    const roots = [
      { name: "Masaüstü", path: `${user}\\Desktop` },
      { name: "Belgeler", path: `${user}\\Belgeler` },
      { name: "Yerel Disk (C:)", path: "C:\\" },
    ].map((r) => ({ ...r, hasChildren: (tree.get(r.path) ?? []).length > 0 }));

    return { path, folders: listFolderNodes(tree, path, roots) };
  };

  // ---- Önizleme ve adlandırma -------------------------------------------------------------------------------------

  private targets(selected: number[]): MockEntry[] {
    const chosen = new Set(selected);
    return chosen.size > 0 ? this.listed.filter((e) => chosen.has(e.id)) : this.listed;
  }

  preview = async ({ rule, selected, overrides }: PreviewParams): Promise<Preview> => {
    const targets = this.targets(selected);
    const typed = new Map(overrides.map((o) => [o.id, o.name]));
    this.planner = (auto) => plan(this.listed, targets, rule, auto || rule.autoNumberOnConflict, typed);
    return this.planner(false);
  };

  tagPreview = async (p: TagPreviewParams): Promise<Preview> => {
    const targets = this.targets(p.selected);
    this.planner = (auto) =>
      planTags(this.listed, targets, { ...p, missing: p.missing === "useEmpty" ? "useEmpty" : "skip" }, tagsOf, auto || p.autoNumberOnConflict);
    return this.planner(false);
  };

  readTags = async ({ id }: ReadTagsParams): Promise<TagList> => {
    const entry = this.listed[id];
    if (!entry) throw new Error(`Geçersiz satır: ${id}`);
    return { entries: tagList(entry), error: null };
  };

  prepareRename = async (): Promise<RenameRequest> => {
    await pause();
    if (!this.planner) return { ready: 0, conflicts: [], otherProblems: 0 };
    const result = this.planner(false);
    const byId = new Map(this.listed.map((e) => [e.id, e]));
    return {
      ready: result.ready,
      conflicts: result.rows
        .filter((r) => r.status === "conflict")
        .map((r) => ({ folder: byId.get(r.id)!.dir, currentName: byId.get(r.id)!.name, wantedName: r.newName })),
      otherProblems: result.rows.filter((r) => r.status === "invalid" || r.status === "error").length,
    };
  };

  executeRename = async ({ decision, description }: ExecuteRenameParams): Promise<RenameOutcome> => {
    await pause(120);
    if (decision === "cancel" || !this.planner) return { renamed: 0, failures: [] };

    const result = this.planner(decision === "autoNumber");
    const moves: Move[] = [];
    for (const row of result.rows) {
      if (row.status !== "ready") continue;
      const listed = this.listed[row.id]!;
      const entry = this.entries.find((e) => e.dir === listed.dir && e.name === listed.name);
      if (!entry || checkName(row.newName)) continue;
      moves.push({ id: entry.id, from: entry.name, to: row.newName });
      this.rename(entry, row.newName);
    }

    if (moves.length > 0)
      this.operations.push({ id: `h${this.nextId++}`, description, count: moves.length, time: Date.now(), moves });
    this.planner = null;
    return { renamed: moves.length, failures: [] };
  };

  undo = async (): Promise<UndoOutcome | null> => {
    await pause(120);
    const operation = this.operations.pop();
    if (!operation) return null;
    for (const move of operation.moves) {
      const entry = this.entries.find((e) => e.id === move.id);
      if (entry) this.rename(entry, move.from);
    }
    this.planner = null;
    return { description: operation.description, restored: operation.moves.length, failures: [] };
  };

  /** Adı değiştirir; yolla verilen bir listedeyse listenin yolunu da izler. */
  private rename(entry: MockEntry, name: string) {
    if (this.source && "paths" in this.source) {
      const old = fullPath(entry).toLowerCase();
      this.source.paths = this.source.paths.map((p) => (p.toLowerCase() === old ? `${entry.dir}\\${name}` : p));
    }
    entry.name = name;
  }

  history = async (): Promise<HistoryItem[]> => this.historyItems();

  private historyItems(): HistoryItem[] {
    return this.operations.map(({ id, description, count, time }) => ({ id, description, count, time })).reverse();
  }

  // ---- Öznitelikler -----------------------------------------------------------------------------------------------

  attributePreview = async ({ rule, selected }: AttributePreviewParams): Promise<AttributePreview> => {
    await pause(20);
    return planAttributes(this.listed, this.targets(selected), rule);
  };

  applyAttributes = async ({ rule, selected }: AttributePreviewParams): Promise<AttributeOutcome> => {
    await pause(80);
    const { rows } = planAttributes(this.listed, this.targets(selected), rule);
    const snapshots: AttributeSnapshot[] = [];

    for (const row of rows) {
      if (row.status !== "ready") continue;
      const listed = this.listed[row.id]!;
      const entry = this.entries.find((e) => e.dir === listed.dir && e.name === listed.name);
      if (!entry) continue;

      snapshots.push({ id: entry.id, attrs: attrsOf(entry), modified: entry.modified, created: entry.created, accessed: entry.accessed ?? entry.modified });
      entry.attrs = row.after;
      entry.modified = row.modifiedAfter;
      entry.created = row.createdAfter;
      entry.accessed = row.accessedAfter;
    }

    if (snapshots.length > 0) this.attributeUndo.push(snapshots);
    return { changed: snapshots.length, failures: [] };
  };

  undoAttributes = async (): Promise<AttributeUndo | null> => {
    await pause(60);
    const snapshots = this.attributeUndo.pop();
    if (!snapshots) return null;

    for (const s of snapshots) {
      const entry = this.entries.find((e) => e.id === s.id);
      if (!entry) continue;
      entry.attrs = s.attrs;
      entry.modified = s.modified;
      entry.created = s.created;
      entry.accessed = s.accessed;
    }
    return { restored: snapshots.length, failures: [] };
  };

  // ---- Numaralı nesneler ------------------------------------------------------------------------------------------

  numberedPreview = async (o: NumberedOptions): Promise<NumberedPlan> => {
    await pause(20);
    return this.numberedPlan(o);
  };

  private numberedPlan(o: NumberedOptions): NumberedPlan {
    const exists = (dir: string, name: string) => this.entries.some((e) => e.dir.toLowerCase() === dir.toLowerCase() && e.name.toLowerCase() === name.toLowerCase());
    const folderExists = this.entries.some((e) => e.dir.toLowerCase() === o.folder.toLowerCase()) || o.folder.toLowerCase() === this.root.toLowerCase();
    return planNumbered(o, exists, folderExists);
  }

  createNumbered = async (o: NumberedOptions): Promise<CreateOutcome> => {
    await pause(100);
    const plan = this.numberedPlan(o);
    if (plan.problem) throw new Error(plan.problem);

    const size = o.kind === "emptyFile" ? (parseSize(o.size) ?? 0) : 0;
    const now = Date.now();
    let created = 0;
    for (const item of plan.items) {
      if (item.status !== "ready") continue;
      const id = Math.max(0, ...this.entries.map((e) => e.id)) + 1;
      this.entries.push({
        id, name: item.name, dir: o.folder, isDir: o.kind === "emptyFolder" || o.kind === "copyOfFolder",
        size, modified: now, created: now,
      });
      created++;
    }
    return { created, failures: [] };
  };

  // ---- Favoriler --------------------------------------------------------------------------------------------------

  addFavorite = async ({ name, rule }: AddFavoriteParams): Promise<Favorite[]> => {
    let unique = name.trim() || "Adsız";
    for (let n = 2; this.favorites.some((f) => f.name.toLowerCase() === unique.toLowerCase()); n++) unique = `${name.trim()} (${n})`;
    this.favorites = [...this.favorites, { id: `f${this.nextId++}`, name: unique, rule }];
    return this.favorites;
  };

  updateFavorite = async ({ favorite }: { favorite: Favorite }): Promise<Favorite[]> => {
    this.favorites = this.favorites.map((f) => (f.id === favorite.id ? favorite : f));
    return this.favorites;
  };

  removeFavorite = async ({ id }: { id: string }): Promise<Favorite[]> => {
    this.favorites = this.favorites.filter((f) => f.id !== id);
    return this.favorites;
  };

  moveFavorite = async ({ id, delta }: { id: string; delta: number }): Promise<Favorite[]> => {
    const from = this.favorites.findIndex((f) => f.id === id);
    const to = from + delta;
    if (from < 0 || to < 0 || to >= this.favorites.length) return this.favorites;
    const copy = [...this.favorites];
    const [item] = copy.splice(from, 1);
    copy.splice(to, 0, item!);
    this.favorites = copy;
    return copy;
  };

  importFavorites = async (): Promise<ImportResult> => ({ added: 0, favorites: this.favorites });

  exportFavorites = async (): Promise<ExportResult> => ({ saved: false, count: 0 });
}
