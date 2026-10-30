"use client";

import { useSyncExternalStore } from "react";
import type {
  AttributeRow,
  AttributeRule,
  Bridge,
  BuiltInPreset,
  Favorite,
  FileRow,
  FolderNode,
  HistoryItem,
  ListingOptions,
  NumberedOptions,
  NumberedPlan,
  PreviewRow,
  RenameDecision,
  RenameRequest,
  Rule,
  SavedRule,
  ScanResult,
  TagList,
  NumberStyle,
  OpenPathsResult,
  Operation,
  ShellIntegrationState,
  TagMissing,
} from "./contract";
import { defaultListing, defaultRule } from "./contract";
import { operationText } from "./operation";
import { applyPrefs, defaultPrefs, loadListView, loadPrefs, saveListView, savePrefs, type Prefs } from "./prefs";
import { chunk, isValidMode, tagOfMode, type GroupMode } from "./grouping";

export type Tab = "rename" | "tags" | "attributes" | "numbered";

export type Dialog =
  | { kind: "confirm"; request: RenameRequest }
  | { kind: "prompt"; title: string; label: string; initial: string }
  | { kind: "ask"; title: string; message: string; confirm: string }
  | { kind: "failures"; title: string; lines: string[] }
  | { kind: "notice"; title: string; message: string }
  | { kind: "favorites" }
  | { kind: "history" }
  | { kind: "settings" };

export interface Toast {
  id: number;
  text: string;
  tone: "info" | "success" | "error";
}

/** Etiketten adlandırma sekmesinin ayarları. */
export interface TagSettings {
  template: string;
  missing: TagMissing;
  numberStart: number;
  numberStep: number;
  resetNumberPerFolder: boolean;
  autoNumberOnConflict: boolean;
  useCreatedDate: boolean;
  operation: Operation;
  targetFolder: string;
  numberStyle: NumberStyle;
  resetNumberPerDate: boolean;
  resetNumberPerTag: string;
  shortNames83: boolean;
  useScript: boolean;
}

export const defaultTagSettings: TagSettings = {
  template: "<ID3.Artist> - <ID3.Track>.\\e",
  missing: "skip",
  numberStart: 1,
  numberStep: 1,
  resetNumberPerFolder: false,
  autoNumberOnConflict: false,
  useCreatedDate: false,
  operation: "rename",
  targetFolder: "",
  numberStyle: "decimal",
  resetNumberPerDate: false,
  resetNumberPerTag: "",
  shortNames83: false,
  useScript: false,
};

const keep = { action: "keep", value: 0, shiftMinutes: 0, copyFrom: "modified", randomTo: 0, seed: 0, limitMin: 0, limitMax: 0 } as const;

export const defaultAttributeRule: AttributeRule = {
  readOnly: "keep",
  archive: "keep",
  hidden: "keep",
  system: "keep",
  modified: keep,
  created: keep,
  accessed: keep,
  includeFolders: true,
};

export const defaultNumbered: NumberedOptions = {
  kind: "emptyFile",
  folder: "",
  namePattern: "Yeni dosya ??.txt",
  amount: 3,
  start: 1,
  step: 1,
  size: "",
  sourcePath: "",
  numberStyle: "decimal",
};

/** Listenin nereden geldiği: bir klasör mü, sürüklenen dosyalar mı. */
export type Source = "folder" | "paths";

export interface AppState {
  ready: boolean;
  bridgeKind: Bridge["kind"];
  tab: Tab;
  folder: string;
  source: Source;
  listing: ListingOptions;
  rule: Rule;
  tag: TagSettings;
  attr: AttributeRule;
  numbered: NumberedOptions;
  rows: FileRow[];
  /** Adlandırma ve etiket sekmeleri: `rows` ile aynı sırada; henüz önizleme yoksa boş. */
  previews: PreviewRow[];
  /** Öznitelikler sekmesi: `rows` ile aynı sırada. */
  attrRows: AttributeRow[];
  attrChanged: number;
  attrProblems: number;
  attrUndoCount: number;
  numberedPlan: NumberedPlan | null;
  /** Etiketten adlandırma: seçili ilk dosyanın etiketleri. */
  fileTags: TagList | null;
  selection: ReadonlySet<number>;
  /** Listede elle yazılan yeni adlar (satır numarası → ad). Liste yenilenince silinir. */
  overrides: Readonly<Record<number, string>>;
  scanning: boolean;
  busy: boolean;
  skippedFolders: number;
  ruleError: string;
  readyCount: number;
  problemCount: number;
  presets: BuiltInPreset[];
  favorites: Favorite[];
  history: HistoryItem[];
  treeOpen: boolean;
  /** Gezinti: geri / ileri gidilebilecek klasör sayısı. */
  canBack: boolean;
  canForward: boolean;
  dropping: boolean;
  tourOpen: boolean;
  prefs: Prefs;
  /** Listeye ek sütun olarak eklenen etiketler (sıralı). */
  tagColumns: string[];
  /** Gruplama: "none" | "folder" | "date" | "ext" | "tag:<ad>". */
  groupMode: GroupMode;
  /** Etiket değerleri önbelleği: satır numarası → etiket → değer. Liste yenilenince silinir. */
  tagValues: Readonly<Record<number, Readonly<Record<string, string>>>>;
  integration: ShellIntegrationState | null;
  dialog: Dialog | null;
  toasts: Toast[];
}

const initialState: AppState = {
  ready: false,
  bridgeKind: "mock",
  tab: "rename",
  folder: "",
  source: "folder",
  listing: defaultListing,
  rule: defaultRule,
  tag: defaultTagSettings,
  attr: defaultAttributeRule,
  numbered: defaultNumbered,
  rows: [],
  previews: [],
  attrRows: [],
  attrChanged: 0,
  attrProblems: 0,
  attrUndoCount: 0,
  numberedPlan: null,
  fileTags: null,
  selection: new Set(),
  overrides: {},
  scanning: false,
  busy: false,
  skippedFolders: 0,
  ruleError: "",
  readyCount: 0,
  problemCount: 0,
  presets: [],
  favorites: [],
  history: [],
  treeOpen: false,
  canBack: false,
  canForward: false,
  dropping: false,
  tourOpen: false,
  prefs: defaultPrefs,
  tagColumns: [],
  groupMode: "none",
  tagValues: {},
  integration: null,
  dialog: null,
  toasts: [],
};

const PREVIEW_DELAY = 120;
const SCAN_DELAY = 350;
const TAG_DELAY = 150;
const TAG_CHUNK = 200;

/** Ekranın tüm durumu ve yaptığı işler. Windows uygulamasıyla konuşmayı `bridge` üstlenir. */
export class AppStore {
  private state: AppState;
  private listeners = new Set<() => void>();
  private previewTimer: ReturnType<typeof setTimeout> | undefined;
  private scanTimer: ReturnType<typeof setTimeout> | undefined;
  private previewVersion = 0;
  private scanVersion = 0;
  private backStack: string[] = [];
  private forwardStack: string[] = [];
  private dialogResolver: ((value: unknown) => void) | null = null;
  private toastId = 0;
  private tagTimer: ReturnType<typeof setTimeout> | undefined;
  private tagPending = new Set<number>();
  private tagVersion = 0;

  constructor(private readonly bridge: Bridge) {
    this.state = { ...initialState, bridgeKind: bridge.kind };
  }

  // ---- React bağlantısı -----------------------------------------------------------------------------------------

  subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  getState = () => this.state;

  private set(patch: Partial<AppState>) {
    this.state = { ...this.state, ...patch };
    this.listeners.forEach((l) => l());
  }

  // ---- Başlangıç --------------------------------------------------------------------------------------------------

  async start() {
    const prefs = loadPrefs();
    applyPrefs(prefs);
    const view = loadListView();
    this.set({ prefs, tagColumns: view.tagColumns, groupMode: view.groupMode });
    const init = await this.bridge.init({});
    this.set({
      ready: true,
      folder: init.folder,
      numbered: { ...this.state.numbered, folder: init.folder },
      presets: init.presets,
      favorites: init.favorites,
      history: init.history,
    });
    if (init.paths.length > 0) await this.openPathList(init.paths);
    else if (init.folder) await this.scan();
    for (const notice of init.notices) await this.ask<void>({ kind: "notice", title: notice.title, message: notice.message });
  }

  // ---- Sekmeler ve girdiler ---------------------------------------------------------------------------------------

  setTab(tab: Tab) {
    if (tab === this.state.tab) return;
    this.set({ tab, fileTags: null });
    this.schedulePreview();
  }

  setFolder(folder: string) {
    this.set({ folder, numbered: { ...this.state.numbered, folder } });
  }

  setListing(patch: Partial<ListingOptions>) {
    const listing = { ...this.state.listing, ...patch };
    if (listing.depth < 1) listing.depth = 1;
    if (listing.depth > 99) listing.depth = 99;
    this.set({ listing });
    this.scheduleScan(SCAN_DELAY);
  }

  setRule(patch: Partial<Rule>) {
    this.set({ rule: { ...this.state.rule, ...patch } });
    this.schedulePreview();
  }

  setTag(patch: Partial<TagSettings>) {
    this.set({ tag: { ...this.state.tag, ...patch } });
    this.schedulePreview();
  }

  setAttr(patch: Partial<AttributeRule>) {
    this.set({ attr: { ...this.state.attr, ...patch } });
    this.schedulePreview();
  }

  setNumbered(patch: Partial<NumberedOptions>) {
    this.set({ numbered: { ...this.state.numbered, ...patch } });
    this.schedulePreview();
  }

  applySavedRule(rule: SavedRule) {
    this.setRule({ method: rule.method, search: rule.search, replace: rule.replace, caseSensitive: rule.caseSensitive });
  }

  setSelection(selection: ReadonlySet<number>) {
    this.set({ selection });
    this.schedulePreview();
  }

  /** Hedef klasörü seçtirir (Adlandır ya da Etiketten sekmesi). */
  async browseTarget() {
    const tag = this.state.tab === "tags";
    const current = tag ? this.state.tag.targetFolder : this.state.rule.targetFolder;
    const picked = await this.bridge.pickFolder({ current: current || this.state.folder });
    if (!picked) return;
    if (tag) this.setTag({ targetFolder: picked });
    else this.setRule({ targetFolder: picked });
  }

  /** Seçili olmayanları seçer, seçili olanları bırakır (Ctrl+I). */
  invertSelection() {
    const next = new Set(this.state.rows.filter((r) => !this.state.selection.has(r.id)).map((r) => r.id));
    this.setSelection(next);
  }

  /** Bir satırın yeni adını elle yazar (F2). Boş ya da eski adla aynıysa elle ad kaldırılır. */
  editName(id: number, name: string) {
    const rest = { ...this.state.overrides };
    const row = this.state.rows.find((r) => r.id === id);
    if (!row || name.trim() === "" || name === row.name) delete rest[id];
    else rest[id] = name;
    this.set({ overrides: rest });
    this.schedulePreview();
  }

  /** Seçili satırları listeden çıkarır (dosyalara dokunulmaz; Yenile ile geri gelirler). */
  async removeSelected() {
    const { selection, rows, overrides } = this.state;
    if (selection.size === 0) return;
    try {
      const result = await this.bridge.removeRows({ ids: [...selection] });
      // Satır numaraları değişti: elle yazılan adlar dizin ve ada göre yeni numaralara taşınır.
      const keep: Record<number, string> = {};
      for (const [oldId, name] of Object.entries(overrides)) {
        const old = rows.find((r) => r.id === Number(oldId));
        const now = old && result.rows.find((r) => r.dir === old.dir && r.name === old.name);
        if (now) keep[now.id] = name;
      }
      this.tagVersion++;
      this.set({ rows: result.rows, previews: [], attrRows: [], selection: new Set(), overrides: keep, tagValues: {} });
      if (tagOfMode(this.state.groupMode) !== null) void this.loadAllTagValues();
      await this.preview();
      this.toast(`${selection.size} satır listeden çıkarıldı. Dosyalara dokunulmadı.`, "info");
    } catch (error) {
      this.toast(`Satırlar çıkarılamadı: ${errorText(error)}`, "error");
    }
  }

  // ---- Etiket sütunları ve gruplama -------------------------------------------------------------------------------

  private saveView() {
    saveListView({ tagColumns: this.state.tagColumns, groupMode: this.state.groupMode });
  }

  /** Listeye ek sütun olacak etiketleri belirler. Gruplamada kullanılan etiket çıkarılırsa gruplama kapanır. */
  setTagColumns(names: string[]) {
    const tagColumns = [...new Set(names)];
    const tag = tagOfMode(this.state.groupMode);
    const groupMode = tag !== null && !tagColumns.includes(tag) ? "none" : this.state.groupMode;
    this.set({ tagColumns, groupMode });
    this.saveView();
    // Görünen satırlar için FileTable yeniden ister; etiketle gruplama açıksa hepsi gerekir.
    if (tagOfMode(groupMode) !== null) void this.loadAllTagValues();
  }

  toggleTagColumn(name: string) {
    const cur = this.state.tagColumns;
    this.setTagColumns(cur.includes(name) ? cur.filter((n) => n !== name) : [...cur, name]);
  }

  setGroupMode(mode: GroupMode) {
    if (!isValidMode(mode)) mode = "none";
    const tag = tagOfMode(mode);
    const tagColumns = tag !== null && !this.state.tagColumns.includes(tag) ? [...this.state.tagColumns, tag] : this.state.tagColumns;
    this.set({ groupMode: mode, tagColumns });
    this.saveView();
    if (tag !== null) void this.loadAllTagValues();
  }

  /** Ekranda görünen satırların etiket değerlerini ister (150 ms gecikmeli, birleştirilerek). */
  requestTagValues(ids: readonly number[]) {
    if (this.state.tagColumns.length === 0) return;
    for (const id of ids) this.tagPending.add(id);
    clearTimeout(this.tagTimer);
    this.tagTimer = setTimeout(() => void this.fetchTagValues(), TAG_DELAY);
  }

  /** Önbellekte eksik etiketi olan kimlikler. */
  private missingTagIds(ids: Iterable<number>, names: string[]): number[] {
    const cache = this.state.tagValues;
    const out: number[] = [];
    for (const id of ids) {
      const have = cache[id];
      if (!have || names.some((n) => !(n in have))) out.push(id);
    }
    return out;
  }

  private async fetchTagValues() {
    const names = this.state.tagColumns;
    const ids = this.missingTagIds(this.tagPending, names);
    this.tagPending.clear();
    if (names.length === 0 || ids.length === 0) return;
    await this.fetchTagChunks(ids, names, false);
  }

  /** Etiketle gruplamada tüm satırların değeri gerekir: 200'lük parçalarla istenir, bitince tek seferde işlenir. */
  private async loadAllTagValues() {
    const names = this.state.tagColumns;
    const ids = this.missingTagIds(this.state.rows.map((r) => r.id), names);
    if (names.length === 0 || ids.length === 0) return;
    await this.fetchTagChunks(ids, names, true);
  }

  private async fetchTagChunks(ids: number[], names: string[], once: boolean) {
    const version = this.tagVersion;
    let gathered: Record<number, Record<string, string>> = {};
    const merge = () => {
      if (Object.keys(gathered).length === 0) return;
      const next = { ...this.state.tagValues };
      for (const [id, values] of Object.entries(gathered)) next[Number(id)] = { ...next[Number(id)], ...values };
      gathered = {};
      this.set({ tagValues: next });
    };
    try {
      for (const part of chunk(ids, TAG_CHUNK)) {
        const result = await this.bridge.tagColumns({ ids: part, names });
        if (version !== this.tagVersion) return; // Liste yenilendi: eski cevaplar atılır.
        part.forEach((id, i) => {
          const values: Record<string, string> = {};
          names.forEach((n, j) => (values[n] = result.rows[i]?.[j] ?? ""));
          gathered[id] = values;
        });
        if (!once) merge();
      }
      merge();
    } catch (error) {
      this.toast(`Etiket sütunları okunamadı: ${errorText(error)}`, "error");
    }
  }

  toggleTree() {
    this.set({ treeOpen: !this.state.treeOpen });
  }

  setPrefs(patch: Partial<Prefs>) {
    const prefs = { ...this.state.prefs, ...patch };
    savePrefs(prefs);
    applyPrefs(prefs);
    this.set({ prefs });
  }

  /** Görünümü ilk haline getirir: yazı tipi ve boyutu, şeritler, klasör ağacı. */
  resetLayout() {
    savePrefs(defaultPrefs);
    applyPrefs(defaultPrefs);
    this.set({ prefs: defaultPrefs, treeOpen: false });
    this.toast("Görünüm ilk haline döndürüldü.", "info");
  }

  openTour() {
    this.set({ tourOpen: true, tab: "rename" });
  }

  closeTour() {
    this.set({ tourOpen: false });
  }

  setDropping(dropping: boolean) {
    if (dropping !== this.state.dropping) this.set({ dropping });
  }

  // ---- Klasör ve liste --------------------------------------------------------------------------------------------

  async browse() {
    const picked = await this.bridge.pickFolder({ current: this.state.folder });
    if (!picked) return;
    await this.openFolder(picked);
  }

  /** Bir klasörü açar (ağaç, Gözat, yazılan yol, çift tık). Önceki klasör geri düğmesine eklenir. */
  async openFolder(path: string) {
    const previous = this.state.folder.trim();
    if (previous && previous.toLowerCase() !== path.trim().toLowerCase()) {
      this.backStack.push(previous);
      this.forwardStack = [];
      this.syncNav();
    }
    this.setFolder(path);
    await this.scan();
  }

  private syncNav() {
    this.set({ canBack: this.backStack.length > 0, canForward: this.forwardStack.length > 0 });
  }

  async goBack() {
    const target = this.backStack.pop();
    if (target === undefined) return;
    const current = this.state.folder.trim();
    if (current) this.forwardStack.push(current);
    this.syncNav();
    this.setFolder(target);
    await this.scan();
  }

  async goForward() {
    const target = this.forwardStack.pop();
    if (target === undefined) return;
    const current = this.state.folder.trim();
    if (current) this.backStack.push(current);
    this.syncNav();
    this.setFolder(target);
    await this.scan();
  }

  /** Üst klasör var mı (sürücü kökünde yok). */
  canGoUp() {
    return this.state.source === "folder" && parentFolder(this.state.folder) !== null;
  }

  async goUp() {
    const parent = parentFolder(this.state.folder);
    if (parent !== null) await this.openFolder(parent);
  }

  /** Listedeki bir klasör satırına çift tıklandı. */
  async openRow(id: number) {
    const row = this.state.rows.find((r) => r.id === id);
    if (row?.isDir) await this.openFolder(joinPath(row.dir, row.name));
  }

  /** Aynı listeyi yeniden okur (klasör ya da sürüklenen dosyalar). */
  refresh = () => (this.state.source === "paths" ? this.reload() : this.scan());

  private scheduleScan(delay: number) {
    clearTimeout(this.scanTimer);
    if (!this.state.folder.trim() || this.state.source === "paths") return;
    this.scanTimer = setTimeout(() => void this.scan(), delay);
  }

  async scan() {
    clearTimeout(this.scanTimer);
    const folder = this.state.folder.trim();
    if (!folder) return;

    await this.load(() => this.bridge.scan({ folder, options: this.state.listing }), "folder");
  }

  /** Sürüklenen dosyaların listesini yeniler (adlandırma sonrası yeni adlarıyla). */
  private reload() {
    return this.load(() => this.bridge.refresh({}), "paths");
  }

  private async load(read: () => Promise<ScanResult>, source: Source) {
    const version = ++this.scanVersion;
    this.set({ scanning: true });
    try {
      const result = await read();
      if (version !== this.scanVersion) return;
      this.tagVersion++;
      this.tagPending.clear();
      clearTimeout(this.tagTimer);
      this.set({ rows: result.rows, source, previews: [], attrRows: [], selection: new Set(), overrides: {}, skippedFolders: result.skippedFolders, tagValues: {} });
      if (tagOfMode(this.state.groupMode) !== null) void this.loadAllTagValues();
      await this.preview();
    } catch (error) {
      this.toast(`Liste okunamadı: ${errorText(error)}`, "error");
      this.set({ rows: [], previews: [], attrRows: [] });
    } finally {
      if (version === this.scanVersion) this.set({ scanning: false });
    }
  }

  /** Programa komut satırından verilen dosyalar (Gezgin'de Gönder menüsü): yolları zaten biliyoruz. */
  private async openPathList(paths: string[]) {
    try {
      await this.applyOpened(await this.bridge.openPaths({ paths }));
    } catch (error) {
      this.toast(`Dosyalar açılamadı: ${errorText(error)}`, "error");
    }
  }

  private async applyOpened(result: OpenPathsResult) {
    if (result.folder) {
      this.set({ source: "folder" });
      await this.openFolder(result.folder);
    } else if (result.list) {
      const list = result.list;
      this.set({ source: "paths", folder: "", numbered: { ...this.state.numbered, folder: "" } });
      await this.load(async () => list, "paths");
      if (list.skippedFolders > 0) this.toast(`${list.skippedFolders} öğe bulunamadı ve atlandı.`, "info");
    }
  }

  /** Gezgin menüleri ayarlar penceresi. */
  async showSettings() {
    try {
      this.set({ integration: await this.bridge.shellIntegration({}) });
    } catch (error) {
      this.toast(`Ayarlar okunamadı: ${errorText(error)}`, "error");
      return;
    }
    await this.ask<void>({ kind: "settings" });
  }

  async setIntegration(patch: { folderMenu?: boolean; sendTo?: boolean }) {
    const current = this.state.integration;
    if (!current) return;
    try {
      const next = await this.bridge.setShellIntegration({
        folderMenu: patch.folderMenu ?? current.folderMenu,
        sendTo: patch.sendTo ?? current.sendTo,
      });
      this.set({ integration: next });
    } catch (error) {
      this.toast(errorText(error), "error");
    }
  }

  /** Betik dosyasını (yoksa örnek içerikle oluşturarak) metin düzenleyicide açar. */
  async editScript() {
    try {
      const info = await this.bridge.editScript({});
      this.toast(`Betik dosyası düzenleyicide açıldı: ${info.path}`, "info");
    } catch (error) {
      this.toast(`Betik dosyası açılamadı: ${errorText(error)}`, "error");
    }
  }

  /** Pencereye bırakılan dosya ve klasörler. Yolları yalnızca Windows uygulaması görebilir (`files` ona iletilir). */
  async openDropped(files: ArrayLike<unknown>) {
    if (files.length === 0) return;
    try {
      await this.applyOpened(await this.bridge.openPaths({ paths: [] }, files));
    } catch (error) {
      this.toast(`Bırakılanlar açılamadı: ${errorText(error)}`, "error");
    }
  }

  // ---- Önizleme ---------------------------------------------------------------------------------------------------

  private schedulePreview() {
    clearTimeout(this.previewTimer);
    this.previewTimer = setTimeout(() => void this.preview(), PREVIEW_DELAY);
  }

  async preview() {
    clearTimeout(this.previewTimer);
    const version = ++this.previewVersion;
    const { tab, rule, tag, attr, numbered, folder, selection, rows } = this.state;
    const selected = [...selection];

    try {
      if (tab === "numbered") {
        const plan = await this.bridge.numberedPreview({ ...numbered, folder: numbered.folder || folder });
        if (version === this.previewVersion) this.set({ numberedPlan: plan });
        return;
      }
      if (rows.length === 0) {
        this.set({ previews: [], attrRows: [], readyCount: 0, problemCount: 0, attrChanged: 0, attrProblems: 0, ruleError: "", fileTags: null });
        return;
      }

      if (tab === "attributes") {
        const preview = await this.bridge.attributePreview({ rule: attr, selected });
        if (version !== this.previewVersion) return;
        this.set({ attrRows: preview.rows, attrChanged: preview.changed, attrProblems: preview.problems, previews: [], ruleError: "" });
        return;
      }

      const preview =
        tab === "tags"
          ? await this.bridge.tagPreview({ ...tag, selected })
          : await this.bridge.preview({ rule, selected, overrides: Object.entries(this.state.overrides).map(([id, name]) => ({ id: Number(id), name })) });
      if (version !== this.previewVersion) return;
      this.set({
        previews: preview.rows,
        readyCount: preview.ready,
        problemCount: preview.problems,
        ruleError: preview.ruleError,
      });

      if (tab === "tags") await this.readFileTags(version);
    } catch (error) {
      if (version === this.previewVersion) this.set({ ruleError: errorText(error) });
    }
  }

  /** Etiket sekmesinde, seçili (yoksa ilk) dosyanın etiketlerini gösterir. */
  private async readFileTags(version: number) {
    const { rows, selection } = this.state;
    const id = [...selection][0] ?? rows[0]?.id;
    if (id === undefined) return;

    // Önce hızlı etiketler (müzik, resim, belge); Windows özellikleri (PROP.*) yavaş olduğu için ardından gelir.
    const tags = await this.bridge.readTags({ id, shell: false });
    if (version !== this.previewVersion) return;
    this.set({ fileTags: tags });

    try {
      const all = await this.bridge.readTags({ id, shell: true });
      if (version === this.previewVersion && !all.error) this.set({ fileTags: all });
    } catch {
      // Windows özellikleri okunamadı: hızlı etiketler yeter.
    }
  }

  // ---- Yeniden adlandırma ve geri alma ----------------------------------------------------------------------------

  /** Etiketten sekmesinde onun, diğerlerinde Adlandır sekmesinin işlemi. */
  currentOperation(): Operation {
    return this.state.tab === "tags" ? this.state.tag.operation : this.state.rule.operation;
  }

  async rename() {
    if (this.state.busy) return;
    await this.preview();

    const request = await this.bridge.prepareRename({});
    if (request.ready === 0 && request.conflicts.length === 0) {
      this.toast("Adı değişecek öğe yok.", "info");
      return;
    }

    const decision = await this.ask<RenameDecision>({ kind: "confirm", request });
    if (decision === "cancel") return;

    this.set({ busy: true });
    try {
      const description = this.state.tab === "tags" ? describeTags(this.state.tag) : describeRule(this.state.rule);
      const text = operationText[this.currentOperation()];
      const outcome = await this.bridge.executeRename({ decision, description });
      if (outcome.failures.length > 0)
        await this.ask<void>({
          kind: "failures",
          title: text.failed,
          lines: outcome.failures.map((f) => `${f.path}\n    ${f.message}`),
        });
      this.toast(
        `${outcome.renamed} öğe ${text.did}.${outcome.failures.length ? ` ${outcome.failures.length} öğe yapılamadı.` : ""}`,
        outcome.failures.length ? "info" : "success",
      );
      await this.afterChange();
    } catch (error) {
      this.toast(`İşlem yapılamadı: ${errorText(error)}`, "error");
    } finally {
      this.set({ busy: false });
    }
  }

  async undo() {
    if (this.state.busy || this.state.history.length === 0) return;
    this.set({ busy: true });
    try {
      const outcome = await this.bridge.undo({});
      if (!outcome) {
        this.toast("Geri alınacak işlem yok.", "info");
      } else {
        if (outcome.failures.length > 0)
          await this.ask<void>({
            kind: "failures",
            title: "Geri alınamayanlar",
            lines: outcome.failures.map((f) => `${f.path}\n    ${f.message}`),
          });
        this.toast(`“${outcome.description}” geri alındı: ${outcome.restored} öğe eski adına döndü.`, "success");
      }
      await this.afterChange();
    } catch (error) {
      this.toast(`Geri alınamadı: ${errorText(error)}`, "error");
    } finally {
      this.set({ busy: false });
    }
  }

  private async afterChange() {
    this.set({ history: await this.bridge.history({}) });
    await this.refresh();
  }

  // ---- Öznitelikler -----------------------------------------------------------------------------------------------

  async applyAttributes() {
    if (this.state.busy || this.state.attrChanged === 0) return;
    await this.preview();

    const count = this.state.attrChanged;
    const agreed = await this.ask<boolean>({
      kind: "ask",
      title: "Öznitelikler ve tarihler değişecek",
      message:
        `${count.toLocaleString("tr-TR")} öğenin öznitelikleri ve/veya tarihleri değiştirilecek.\n\n` +
        "Geri Al bu program açıkken çalışır; program kapatılırsa bu değişiklik geri alınamaz.",
      confirm: "Değiştir",
    });
    if (!agreed) return;

    this.set({ busy: true });
    try {
      const outcome = await this.bridge.applyAttributes({ rule: this.state.attr, selected: [...this.state.selection] });
      if (outcome.failures.length > 0)
        await this.ask<void>({ kind: "failures", title: "Değiştirilemeyenler", lines: outcome.failures.map((f) => `${f.path}\n    ${f.message}`) });
      if (outcome.changed > 0) this.set({ attrUndoCount: this.state.attrUndoCount + 1 });
      this.toast(`${outcome.changed} öğe değiştirildi.${outcome.failures.length ? ` ${outcome.failures.length} öğe yapılamadı.` : ""}`, outcome.failures.length ? "info" : "success");
      await this.refresh();
    } catch (error) {
      this.toast(`Değiştirilemedi: ${errorText(error)}`, "error");
    } finally {
      this.set({ busy: false });
    }
  }

  async undoAttributes() {
    if (this.state.busy || this.state.attrUndoCount === 0) return;
    this.set({ busy: true });
    try {
      const outcome = await this.bridge.undoAttributes({});
      this.set({ attrUndoCount: Math.max(0, this.state.attrUndoCount - 1) });
      if (!outcome) {
        this.toast("Geri alınacak değişiklik yok.", "info");
      } else {
        if (outcome.failures.length > 0)
          await this.ask<void>({ kind: "failures", title: "Geri alınamayanlar", lines: outcome.failures.map((f) => `${f.path}\n    ${f.message}`) });
        this.toast(`${outcome.restored} öğenin öznitelikleri ve tarihleri eski hâline döndü.`, "success");
      }
      await this.refresh();
    } catch (error) {
      this.toast(`Geri alınamadı: ${errorText(error)}`, "error");
    } finally {
      this.set({ busy: false });
    }
  }

  // ---- Numaralı nesneler ------------------------------------------------------------------------------------------

  async createNumbered() {
    const { numbered, folder, numberedPlan, busy } = this.state;
    if (busy || !numberedPlan || numberedPlan.problem || numberedPlan.ready === 0) return;

    this.set({ busy: true });
    try {
      const target = numbered.folder || folder;
      const outcome = await this.bridge.createNumbered({ ...numbered, folder: target });
      if (outcome.failures.length > 0)
        await this.ask<void>({ kind: "failures", title: "Oluşturulamayanlar", lines: outcome.failures.map((f) => `${f.path}\n    ${f.message}`) });
      this.toast(`${outcome.created} öğe oluşturuldu.`, outcome.failures.length ? "info" : "success");
      if (target === folder) await this.refresh();
      await this.preview();
    } catch (error) {
      this.toast(`Oluşturulamadı: ${errorText(error)}`, "error");
    } finally {
      this.set({ busy: false });
    }
  }

  /** Numaralı nesne için kopyalanacak dosya ya da klasörü seçtirir. */
  async pickSource() {
    const { numbered } = this.state;
    const picked =
      numbered.kind === "copyOfFolder"
        ? await this.bridge.pickFolder({ current: numbered.sourcePath })
        : await this.bridge.pickFile({ title: "Kopyalanacak dosyayı seçin", filter: "Tüm dosyalar (*.*)|*.*" });
    if (picked) this.setNumbered({ sourcePath: picked });
  }

  // ---- Klasör ağacı -----------------------------------------------------------------------------------------------

  /** Ağaçtaki bir klasörün alt klasörleri (boş yol: kökler). */
  async listFolders(path: string): Promise<FolderNode[]> {
    return (await this.bridge.listFolders({ path })).folders;
  }

  // ---- Favoriler --------------------------------------------------------------------------------------------------

  async addFavorite() {
    const { rule } = this.state;
    const name = await this.ask<string | null>({
      kind: "prompt",
      title: "Favorilere ekle",
      label: "Bu kural için bir ad yazın:",
      initial: describeRule(rule),
    });
    if (!name?.trim()) return;

    const favorites = await this.bridge.addFavorite({
      name,
      rule: { method: rule.method, search: rule.search, replace: rule.replace, caseSensitive: rule.caseSensitive },
    });
    this.set({ favorites });
    this.toast(`“${favorites.at(-1)?.name ?? name}” favorilere eklendi.`, "success");
  }

  async removeFavorite(id: string) {
    this.set({ favorites: await this.bridge.removeFavorite({ id }) });
  }

  async renameFavorite(favorite: Favorite, name: string) {
    await this.updateFavorite({ ...favorite, name });
  }

  async updateFavorite(favorite: Favorite) {
    this.set({ favorites: await this.bridge.updateFavorite({ favorite }) });
  }

  async moveFavorite(id: string, delta: number) {
    this.set({ favorites: await this.bridge.moveFavorite({ id, delta }) });
  }

  async importFavorites() {
    try {
      const result = await this.bridge.importFavorites({});
      this.set({ favorites: result.favorites });
      this.toast(result.added === 0 ? "Yeni favori bulunamadı." : `${result.added} favori eklendi.`, result.added === 0 ? "info" : "success");
    } catch (error) {
      this.toast(`İçe aktarılamadı: ${errorText(error)}`, "error");
    }
  }

  async exportFavorites() {
    try {
      const result = await this.bridge.exportFavorites({});
      if (result.saved) this.toast(`${result.count} favori dışa aktarıldı.`, "success");
    } catch (error) {
      this.toast(`Dışa aktarılamadı: ${errorText(error)}`, "error");
    }
  }

  /** Favorileri düzenleme penceresini açar; bir favori "uygula"ya basılırsa o kural alanlara yazılır. */
  async editFavorites() {
    const chosen = await this.ask<Favorite | undefined>({ kind: "favorites" });
    if (chosen) this.applySavedRule(chosen.rule);
  }

  async showHistory() {
    this.set({ history: await this.bridge.history({}) });
    await this.ask<void>({ kind: "history" });
  }

  // ---- Pencereler ve bildirimler ----------------------------------------------------------------------------------

  /** Bir pencere açar ve kapanınca cevabı verir. */
  private ask<T>(dialog: Dialog): Promise<T> {
    return new Promise<T>((resolve) => {
      this.dialogResolver = resolve as (value: unknown) => void;
      this.set({ dialog });
    });
  }

  answer(value: unknown) {
    const resolve = this.dialogResolver;
    this.dialogResolver = null;
    this.set({ dialog: null });
    resolve?.(value);
  }

  toast(text: string, tone: Toast["tone"] = "info") {
    const id = ++this.toastId;
    this.set({ toasts: [...this.state.toasts, { id, text, tone }] });
    setTimeout(() => this.dismissToast(id), tone === "error" ? 8000 : 4500);
  }

  dismissToast(id: number) {
    this.set({ toasts: this.state.toasts.filter((t) => t.id !== id) });
  }
}

export function describeRule(rule: Rule): string {
  const method = rule.method === "wildcard" ? "Joker" : rule.method === "regex" ? "Düzenli ifade" : "Çeviri";
  const text = `${method}: ${rule.search} → ${rule.replace}`;
  return text.length <= 80 ? text : `${text.slice(0, 79)}…`;
}

export function describeTags(tag: TagSettings): string {
  const text = `Etiket: ${tag.template}`;
  return text.length <= 80 ? text : `${text.slice(0, 79)}…`;
}

function errorText(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

export function useStore<T>(store: AppStore, select: (state: AppState) => T): T {
  return useSyncExternalStore(
    store.subscribe,
    () => select(store.getState()),
    () => select(store.getState()),
  );
}

/** "C:\a\b" -> "C:\a"; sürücü kökü ("C:\\") ve boş yol için null. */
export function parentFolder(path: string): string | null {
  const trimmed = path.trim().replace(/[\\/]+$/, "");
  const cut = Math.max(trimmed.lastIndexOf("\\"), trimmed.lastIndexOf("/"));
  if (!trimmed || cut < 0) return null;
  const parent = trimmed.slice(0, cut);
  if (/^[A-Za-z]:$/.test(parent)) return parent + trimmed[cut];
  return parent === "" ? trimmed.slice(0, cut + 1) : parent;
}

export function joinPath(dir: string, name: string): string {
  const separator = dir.includes("\\") || !dir.includes("/") ? "\\" : "/";
  return dir.replace(/[\\/]+$/, "") + separator + name;
}
