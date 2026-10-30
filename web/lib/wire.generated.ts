// Bu dosya otomatik üretilir; elle değiştirmeyin.
// Kaynak: src/DosyaTerzisi.Bridge/Contract/Dtos.cs ve Catalog.cs
// Yenilemek için (repo kökünde): UPDATE_CONTRACT=1 dotnet test --filter ContractTests

export type AttributeRowStatus = "unchanged" | "ready" | "error";

export type NumberStyle = "decimal" | "hexadecimal" | "letters";

export type ObjectKind = "emptyFile" | "copyOfFile" | "emptyFolder" | "copyOfFolder";

export type Operation = "rename" | "copy" | "shortcut" | "hardlink";

export type PlannedStatus = "ready" | "exists" | "invalidName";

export type RenameDecision = "cancel" | "proceed" | "autoNumber";

export type RenameMethod = "wildcard" | "regex" | "translation";

export type RowStatus = "unchanged" | "ready" | "conflict" | "invalid" | "error" | "missingTag";

export type TagMissing = "skip" | "useEmpty";

export type TimeAction = "keep" | "setTo" | "shift" | "copyFrom" | "photoTaken" | "random";

export type TimeKind = "modified" | "created" | "accessed";

export type TriState = "keep" | "set" | "clear";

export interface AddFavoriteParams {
  name: string;
  rule: SavedRule;
}

export interface AttrFlags {
  readOnly: boolean;
  archive: boolean;
  hidden: boolean;
  system: boolean;
}

export interface AttributeOutcome {
  changed: number;
  failures: Failure[];
}

export interface AttributePreview {
  rows: AttributeRow[];
  changed: number;
  problems: number;
}

export interface AttributePreviewParams {
  rule: AttributeRule;
  selected: number[];
}

export interface AttributeRow {
  id: number;
  before: AttrFlags;
  after: AttrFlags;
  modifiedBefore: number;
  modifiedAfter: number;
  createdBefore: number;
  createdAfter: number;
  accessedBefore: number;
  accessedAfter: number;
  status: AttributeRowStatus;
  message: string | null;
}

export interface AttributeRule {
  readOnly: TriState;
  archive: TriState;
  hidden: TriState;
  system: TriState;
  modified: TimeRule;
  created: TimeRule;
  accessed: TimeRule;
  includeFolders: boolean;
}

export interface AttributeUndo {
  restored: number;
  failures: Failure[];
}

export interface BuiltInPreset {
  id: string;
  title: string;
  rule: SavedRule;
}

export interface ConflictInfo {
  folder: string;
  currentName: string;
  wantedName: string;
}

export interface CreateOutcome {
  created: number;
  failures: Failure[];
}

export type Empty = Record<string, never>;

export interface ExecuteRenameParams {
  decision: RenameDecision;
  description: string;
}

export interface ExportResult {
  saved: boolean;
  count: number;
}

export interface Failure {
  path: string;
  message: string;
}

export interface Favorite {
  id: string;
  name: string;
  rule: SavedRule;
}

export interface FileRow {
  id: number;
  name: string;
  dir: string;
  isDir: boolean;
  size: number;
  modified: number;
}

export interface FolderList {
  path: string;
  folders: FolderNode[];
}

export interface FolderNode {
  name: string;
  path: string;
  hasChildren: boolean;
}

export interface HistoryItem {
  id: string;
  description: string;
  count: number;
  time: number;
}

export interface ImportResult {
  added: number;
  favorites: Favorite[];
}

export interface InitState {
  folder: string;
  presets: BuiltInPreset[];
  favorites: Favorite[];
  history: HistoryItem[];
  notices: Notice[];
  paths: string[];
}

export interface ListFoldersParams {
  path: string;
}

export interface ListingOptions {
  includeFiles: boolean;
  includeFolders: boolean;
  filter: string;
  recursive: boolean;
  depth: number;
}

export interface MoveFavoriteParams {
  id: string;
  delta: number;
}

export interface NameOverride {
  id: number;
  name: string;
}

export interface Notice {
  title: string;
  message: string;
}

export interface NumberedOptions {
  kind: ObjectKind;
  folder: string;
  namePattern: string;
  amount: number;
  start: number;
  step: number;
  size: string;
  sourcePath: string;
  numberStyle: NumberStyle;
}

export interface NumberedPlan {
  items: PlannedObject[];
  problem: string | null;
  ready: number;
}

export interface OpenPathsParams {
  paths: string[];
}

export interface OpenPathsResult {
  folder: string | null;
  list: ScanResult | null;
}

export interface PickFileParams {
  title: string;
  filter: string;
}

export interface PickFolderParams {
  current: string;
}

export interface PlannedObject {
  name: string;
  status: PlannedStatus;
  message: string | null;
}

export interface Preview {
  rows: PreviewRow[];
  ready: number;
  problems: number;
  ruleError: string;
}

export interface PreviewParams {
  rule: Rule;
  selected: number[];
  overrides: NameOverride[];
}

export interface PreviewRow {
  id: number;
  newName: string;
  status: RowStatus;
  message: string | null;
  destination: string | null;
}

export interface ReadTagsParams {
  id: number;
  shell: boolean;
}

export interface RemoveFavoriteParams {
  id: string;
}

export interface RemoveRowsParams {
  ids: number[];
}

export interface RenameOutcome {
  renamed: number;
  failures: Failure[];
}

export interface RenameRequest {
  ready: number;
  conflicts: ConflictInfo[];
  otherProblems: number;
}

export interface Rule {
  method: RenameMethod;
  search: string;
  replace: string;
  caseSensitive: boolean;
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

export interface SavedRule {
  method: RenameMethod;
  search: string;
  replace: string;
  caseSensitive: boolean;
}

export interface ScanParams {
  folder: string;
  options: ListingOptions;
}

export interface ScanResult {
  rows: FileRow[];
  skippedFolders: number;
}

export interface ScriptInfo {
  path: string;
  exists: boolean;
  language: string;
}

export interface SetShellIntegrationParams {
  folderMenu: boolean;
  sendTo: boolean;
}

export interface ShellIntegrationState {
  available: boolean;
  folderMenu: boolean;
  sendTo: boolean;
}

export interface TagColumnValues {
  rows: string[][];
}

export interface TagColumnsParams {
  ids: number[];
  names: string[];
}

export interface TagEntry {
  name: string;
  value: string;
}

export interface TagList {
  entries: TagEntry[];
  error: string | null;
}

export interface TagPreviewParams {
  template: string;
  missing: TagMissing;
  numberStart: number;
  numberStep: number;
  resetNumberPerFolder: boolean;
  autoNumberOnConflict: boolean;
  useCreatedDate: boolean;
  selected: number[];
  operation: Operation;
  targetFolder: string;
  numberStyle: NumberStyle;
  resetNumberPerDate: boolean;
  resetNumberPerTag: string;
  shortNames83: boolean;
  useScript: boolean;
}

export interface TimeRule {
  action: TimeAction;
  value: number;
  shiftMinutes: number;
  copyFrom: TimeKind;
  randomTo: number;
  seed: number;
  limitMin: number;
  limitMax: number;
}

export interface UndoOutcome {
  description: string;
  restored: number;
  failures: Failure[];
}

export interface UpdateFavoriteParams {
  favorite: Favorite;
}

/** Arayüzün uygulamadan isteyebileceği her iş: parametre ve cevap tipi. */
export interface Methods {
  init: { params: Empty; result: InitState };
  pickFolder: { params: PickFolderParams; result: string | null };
  scan: { params: ScanParams; result: ScanResult };
  preview: { params: PreviewParams; result: Preview };
  prepareRename: { params: Empty; result: RenameRequest };
  executeRename: { params: ExecuteRenameParams; result: RenameOutcome };
  undo: { params: Empty; result: UndoOutcome | null };
  history: { params: Empty; result: HistoryItem[] };
  addFavorite: { params: AddFavoriteParams; result: Favorite[] };
  updateFavorite: { params: UpdateFavoriteParams; result: Favorite[] };
  removeFavorite: { params: RemoveFavoriteParams; result: Favorite[] };
  moveFavorite: { params: MoveFavoriteParams; result: Favorite[] };
  importFavorites: { params: Empty; result: ImportResult };
  exportFavorites: { params: Empty; result: ExportResult };
  refresh: { params: Empty; result: ScanResult };
  openPaths: { params: OpenPathsParams; result: OpenPathsResult };
  listFolders: { params: ListFoldersParams; result: FolderList };
  pickFile: { params: PickFileParams; result: string | null };
  shellIntegration: { params: Empty; result: ShellIntegrationState };
  setShellIntegration: { params: SetShellIntegrationParams; result: ShellIntegrationState };
  removeRows: { params: RemoveRowsParams; result: ScanResult };
  tagPreview: { params: TagPreviewParams; result: Preview };
  scriptInfo: { params: Empty; result: ScriptInfo };
  editScript: { params: Empty; result: ScriptInfo };
  tagColumns: { params: TagColumnsParams; result: TagColumnValues };
  readTags: { params: ReadTagsParams; result: TagList };
  attributePreview: { params: AttributePreviewParams; result: AttributePreview };
  applyAttributes: { params: AttributePreviewParams; result: AttributeOutcome };
  undoAttributes: { params: Empty; result: AttributeUndo | null };
  numberedPreview: { params: NumberedOptions; result: NumberedPlan };
  createNumbered: { params: NumberedOptions; result: CreateOutcome };
}

export type MethodName = keyof Methods;

export const methodNames: MethodName[] = ["init", "pickFolder", "scan", "preview", "prepareRename", "executeRename", "undo", "history", "addFavorite", "updateFavorite", "removeFavorite", "moveFavorite", "importFavorites", "exportFavorites", "refresh", "openPaths", "listFolders", "pickFile", "shellIntegration", "setShellIntegration", "removeRows", "tagPreview", "scriptInfo", "editScript", "tagColumns", "readTags", "attributePreview", "applyAttributes", "undoAttributes", "numberedPreview", "createNumbered"];

export const wireVersion = 1;

export type FieldShape =
  | { kind: "string" | "integer" | "number" | "boolean"; nullable?: boolean }
  | { kind: "enum"; name: string; nullable?: boolean }
  | { kind: "object"; name: string; nullable?: boolean }
  | { kind: "array"; item: FieldShape; nullable?: boolean };

export type TypeShape =
  | { kind: "enum"; values: string[] }
  | { kind: "object"; fields: Record<string, FieldShape> };

/** Her tipin alanları; gelen mesajları denetlemek için (wire.ts). */
export const shapes: Record<string, TypeShape> = {
  AttributeRowStatus: { kind: "enum", values: ["unchanged", "ready", "error"] },
  NumberStyle: { kind: "enum", values: ["decimal", "hexadecimal", "letters"] },
  ObjectKind: { kind: "enum", values: ["emptyFile", "copyOfFile", "emptyFolder", "copyOfFolder"] },
  Operation: { kind: "enum", values: ["rename", "copy", "shortcut", "hardlink"] },
  PlannedStatus: { kind: "enum", values: ["ready", "exists", "invalidName"] },
  RenameDecision: { kind: "enum", values: ["cancel", "proceed", "autoNumber"] },
  RenameMethod: { kind: "enum", values: ["wildcard", "regex", "translation"] },
  RowStatus: { kind: "enum", values: ["unchanged", "ready", "conflict", "invalid", "error", "missingTag"] },
  TagMissing: { kind: "enum", values: ["skip", "useEmpty"] },
  TimeAction: { kind: "enum", values: ["keep", "setTo", "shift", "copyFrom", "photoTaken", "random"] },
  TimeKind: { kind: "enum", values: ["modified", "created", "accessed"] },
  TriState: { kind: "enum", values: ["keep", "set", "clear"] },
  AddFavoriteParams: { kind: "object", fields: { name: { kind: "string" }, rule: { kind: "object", name: "SavedRule" } } },
  AttrFlags: { kind: "object", fields: { readOnly: { kind: "boolean" }, archive: { kind: "boolean" }, hidden: { kind: "boolean" }, system: { kind: "boolean" } } },
  AttributeOutcome: { kind: "object", fields: { changed: { kind: "integer" }, failures: { kind: "array", item: { kind: "object", name: "Failure" } } } },
  AttributePreview: { kind: "object", fields: { rows: { kind: "array", item: { kind: "object", name: "AttributeRow" } }, changed: { kind: "integer" }, problems: { kind: "integer" } } },
  AttributePreviewParams: { kind: "object", fields: { rule: { kind: "object", name: "AttributeRule" }, selected: { kind: "array", item: { kind: "integer" } } } },
  AttributeRow: { kind: "object", fields: { id: { kind: "integer" }, before: { kind: "object", name: "AttrFlags" }, after: { kind: "object", name: "AttrFlags" }, modifiedBefore: { kind: "integer" }, modifiedAfter: { kind: "integer" }, createdBefore: { kind: "integer" }, createdAfter: { kind: "integer" }, accessedBefore: { kind: "integer" }, accessedAfter: { kind: "integer" }, status: { kind: "enum", name: "AttributeRowStatus" }, message: { kind: "string", nullable: true } } },
  AttributeRule: { kind: "object", fields: { readOnly: { kind: "enum", name: "TriState" }, archive: { kind: "enum", name: "TriState" }, hidden: { kind: "enum", name: "TriState" }, system: { kind: "enum", name: "TriState" }, modified: { kind: "object", name: "TimeRule" }, created: { kind: "object", name: "TimeRule" }, accessed: { kind: "object", name: "TimeRule" }, includeFolders: { kind: "boolean" } } },
  AttributeUndo: { kind: "object", fields: { restored: { kind: "integer" }, failures: { kind: "array", item: { kind: "object", name: "Failure" } } } },
  BuiltInPreset: { kind: "object", fields: { id: { kind: "string" }, title: { kind: "string" }, rule: { kind: "object", name: "SavedRule" } } },
  ConflictInfo: { kind: "object", fields: { folder: { kind: "string" }, currentName: { kind: "string" }, wantedName: { kind: "string" } } },
  CreateOutcome: { kind: "object", fields: { created: { kind: "integer" }, failures: { kind: "array", item: { kind: "object", name: "Failure" } } } },
  Empty: { kind: "object", fields: {} },
  ExecuteRenameParams: { kind: "object", fields: { decision: { kind: "enum", name: "RenameDecision" }, description: { kind: "string" } } },
  ExportResult: { kind: "object", fields: { saved: { kind: "boolean" }, count: { kind: "integer" } } },
  Failure: { kind: "object", fields: { path: { kind: "string" }, message: { kind: "string" } } },
  Favorite: { kind: "object", fields: { id: { kind: "string" }, name: { kind: "string" }, rule: { kind: "object", name: "SavedRule" } } },
  FileRow: { kind: "object", fields: { id: { kind: "integer" }, name: { kind: "string" }, dir: { kind: "string" }, isDir: { kind: "boolean" }, size: { kind: "integer" }, modified: { kind: "integer" } } },
  FolderList: { kind: "object", fields: { path: { kind: "string" }, folders: { kind: "array", item: { kind: "object", name: "FolderNode" } } } },
  FolderNode: { kind: "object", fields: { name: { kind: "string" }, path: { kind: "string" }, hasChildren: { kind: "boolean" } } },
  HistoryItem: { kind: "object", fields: { id: { kind: "string" }, description: { kind: "string" }, count: { kind: "integer" }, time: { kind: "integer" } } },
  ImportResult: { kind: "object", fields: { added: { kind: "integer" }, favorites: { kind: "array", item: { kind: "object", name: "Favorite" } } } },
  InitState: { kind: "object", fields: { folder: { kind: "string" }, presets: { kind: "array", item: { kind: "object", name: "BuiltInPreset" } }, favorites: { kind: "array", item: { kind: "object", name: "Favorite" } }, history: { kind: "array", item: { kind: "object", name: "HistoryItem" } }, notices: { kind: "array", item: { kind: "object", name: "Notice" } }, paths: { kind: "array", item: { kind: "string" } } } },
  ListFoldersParams: { kind: "object", fields: { path: { kind: "string" } } },
  ListingOptions: { kind: "object", fields: { includeFiles: { kind: "boolean" }, includeFolders: { kind: "boolean" }, filter: { kind: "string" }, recursive: { kind: "boolean" }, depth: { kind: "integer" } } },
  MoveFavoriteParams: { kind: "object", fields: { id: { kind: "string" }, delta: { kind: "integer" } } },
  NameOverride: { kind: "object", fields: { id: { kind: "integer" }, name: { kind: "string" } } },
  Notice: { kind: "object", fields: { title: { kind: "string" }, message: { kind: "string" } } },
  NumberedOptions: { kind: "object", fields: { kind: { kind: "enum", name: "ObjectKind" }, folder: { kind: "string" }, namePattern: { kind: "string" }, amount: { kind: "integer" }, start: { kind: "integer" }, step: { kind: "integer" }, size: { kind: "string" }, sourcePath: { kind: "string" }, numberStyle: { kind: "enum", name: "NumberStyle" } } },
  NumberedPlan: { kind: "object", fields: { items: { kind: "array", item: { kind: "object", name: "PlannedObject" } }, problem: { kind: "string", nullable: true }, ready: { kind: "integer" } } },
  OpenPathsParams: { kind: "object", fields: { paths: { kind: "array", item: { kind: "string" } } } },
  OpenPathsResult: { kind: "object", fields: { folder: { kind: "string", nullable: true }, list: { kind: "object", name: "ScanResult", nullable: true } } },
  PickFileParams: { kind: "object", fields: { title: { kind: "string" }, filter: { kind: "string" } } },
  PickFolderParams: { kind: "object", fields: { current: { kind: "string" } } },
  PlannedObject: { kind: "object", fields: { name: { kind: "string" }, status: { kind: "enum", name: "PlannedStatus" }, message: { kind: "string", nullable: true } } },
  Preview: { kind: "object", fields: { rows: { kind: "array", item: { kind: "object", name: "PreviewRow" } }, ready: { kind: "integer" }, problems: { kind: "integer" }, ruleError: { kind: "string" } } },
  PreviewParams: { kind: "object", fields: { rule: { kind: "object", name: "Rule" }, selected: { kind: "array", item: { kind: "integer" } }, overrides: { kind: "array", item: { kind: "object", name: "NameOverride" } } } },
  PreviewRow: { kind: "object", fields: { id: { kind: "integer" }, newName: { kind: "string" }, status: { kind: "enum", name: "RowStatus" }, message: { kind: "string", nullable: true }, destination: { kind: "string", nullable: true } } },
  ReadTagsParams: { kind: "object", fields: { id: { kind: "integer" }, shell: { kind: "boolean" } } },
  RemoveFavoriteParams: { kind: "object", fields: { id: { kind: "string" } } },
  RemoveRowsParams: { kind: "object", fields: { ids: { kind: "array", item: { kind: "integer" } } } },
  RenameOutcome: { kind: "object", fields: { renamed: { kind: "integer" }, failures: { kind: "array", item: { kind: "object", name: "Failure" } } } },
  RenameRequest: { kind: "object", fields: { ready: { kind: "integer" }, conflicts: { kind: "array", item: { kind: "object", name: "ConflictInfo" } }, otherProblems: { kind: "integer" } } },
  Rule: { kind: "object", fields: { method: { kind: "enum", name: "RenameMethod" }, search: { kind: "string" }, replace: { kind: "string" }, caseSensitive: { kind: "boolean" }, numberStart: { kind: "integer" }, numberStep: { kind: "integer" }, resetNumberPerFolder: { kind: "boolean" }, autoNumberOnConflict: { kind: "boolean" }, useCreatedDate: { kind: "boolean" }, operation: { kind: "enum", name: "Operation" }, targetFolder: { kind: "string" }, numberStyle: { kind: "enum", name: "NumberStyle" }, resetNumberPerDate: { kind: "boolean" }, resetNumberPerTag: { kind: "string" }, shortNames83: { kind: "boolean" }, useScript: { kind: "boolean" } } },
  SavedRule: { kind: "object", fields: { method: { kind: "enum", name: "RenameMethod" }, search: { kind: "string" }, replace: { kind: "string" }, caseSensitive: { kind: "boolean" } } },
  ScanParams: { kind: "object", fields: { folder: { kind: "string" }, options: { kind: "object", name: "ListingOptions" } } },
  ScanResult: { kind: "object", fields: { rows: { kind: "array", item: { kind: "object", name: "FileRow" } }, skippedFolders: { kind: "integer" } } },
  ScriptInfo: { kind: "object", fields: { path: { kind: "string" }, exists: { kind: "boolean" }, language: { kind: "string" } } },
  SetShellIntegrationParams: { kind: "object", fields: { folderMenu: { kind: "boolean" }, sendTo: { kind: "boolean" } } },
  ShellIntegrationState: { kind: "object", fields: { available: { kind: "boolean" }, folderMenu: { kind: "boolean" }, sendTo: { kind: "boolean" } } },
  TagColumnValues: { kind: "object", fields: { rows: { kind: "array", item: { kind: "array", item: { kind: "string" } } } } },
  TagColumnsParams: { kind: "object", fields: { ids: { kind: "array", item: { kind: "integer" } }, names: { kind: "array", item: { kind: "string" } } } },
  TagEntry: { kind: "object", fields: { name: { kind: "string" }, value: { kind: "string" } } },
  TagList: { kind: "object", fields: { entries: { kind: "array", item: { kind: "object", name: "TagEntry" } }, error: { kind: "string", nullable: true } } },
  TagPreviewParams: { kind: "object", fields: { template: { kind: "string" }, missing: { kind: "enum", name: "TagMissing" }, numberStart: { kind: "integer" }, numberStep: { kind: "integer" }, resetNumberPerFolder: { kind: "boolean" }, autoNumberOnConflict: { kind: "boolean" }, useCreatedDate: { kind: "boolean" }, selected: { kind: "array", item: { kind: "integer" } }, operation: { kind: "enum", name: "Operation" }, targetFolder: { kind: "string" }, numberStyle: { kind: "enum", name: "NumberStyle" }, resetNumberPerDate: { kind: "boolean" }, resetNumberPerTag: { kind: "string" }, shortNames83: { kind: "boolean" }, useScript: { kind: "boolean" } } },
  TimeRule: { kind: "object", fields: { action: { kind: "enum", name: "TimeAction" }, value: { kind: "integer" }, shiftMinutes: { kind: "integer" }, copyFrom: { kind: "enum", name: "TimeKind" }, randomTo: { kind: "integer" }, seed: { kind: "integer" }, limitMin: { kind: "integer" }, limitMax: { kind: "integer" } } },
  UndoOutcome: { kind: "object", fields: { description: { kind: "string" }, restored: { kind: "integer" }, failures: { kind: "array", item: { kind: "object", name: "Failure" } } } },
  UpdateFavoriteParams: { kind: "object", fields: { favorite: { kind: "object", name: "Favorite" } } },
};

/** Her işin parametre ve cevap tipi (denetim için). */
export const methodShapes: Record<MethodName, { params: FieldShape; result: FieldShape }> = {
  init: { params: { kind: "object", name: "Empty" }, result: { kind: "object", name: "InitState" } },
  pickFolder: { params: { kind: "object", name: "PickFolderParams" }, result: { kind: "string", nullable: true } },
  scan: { params: { kind: "object", name: "ScanParams" }, result: { kind: "object", name: "ScanResult" } },
  preview: { params: { kind: "object", name: "PreviewParams" }, result: { kind: "object", name: "Preview" } },
  prepareRename: { params: { kind: "object", name: "Empty" }, result: { kind: "object", name: "RenameRequest" } },
  executeRename: { params: { kind: "object", name: "ExecuteRenameParams" }, result: { kind: "object", name: "RenameOutcome" } },
  undo: { params: { kind: "object", name: "Empty" }, result: { kind: "object", name: "UndoOutcome", nullable: true } },
  history: { params: { kind: "object", name: "Empty" }, result: { kind: "array", item: { kind: "object", name: "HistoryItem" } } },
  addFavorite: { params: { kind: "object", name: "AddFavoriteParams" }, result: { kind: "array", item: { kind: "object", name: "Favorite" } } },
  updateFavorite: { params: { kind: "object", name: "UpdateFavoriteParams" }, result: { kind: "array", item: { kind: "object", name: "Favorite" } } },
  removeFavorite: { params: { kind: "object", name: "RemoveFavoriteParams" }, result: { kind: "array", item: { kind: "object", name: "Favorite" } } },
  moveFavorite: { params: { kind: "object", name: "MoveFavoriteParams" }, result: { kind: "array", item: { kind: "object", name: "Favorite" } } },
  importFavorites: { params: { kind: "object", name: "Empty" }, result: { kind: "object", name: "ImportResult" } },
  exportFavorites: { params: { kind: "object", name: "Empty" }, result: { kind: "object", name: "ExportResult" } },
  refresh: { params: { kind: "object", name: "Empty" }, result: { kind: "object", name: "ScanResult" } },
  openPaths: { params: { kind: "object", name: "OpenPathsParams" }, result: { kind: "object", name: "OpenPathsResult" } },
  listFolders: { params: { kind: "object", name: "ListFoldersParams" }, result: { kind: "object", name: "FolderList" } },
  pickFile: { params: { kind: "object", name: "PickFileParams" }, result: { kind: "string", nullable: true } },
  shellIntegration: { params: { kind: "object", name: "Empty" }, result: { kind: "object", name: "ShellIntegrationState" } },
  setShellIntegration: { params: { kind: "object", name: "SetShellIntegrationParams" }, result: { kind: "object", name: "ShellIntegrationState" } },
  removeRows: { params: { kind: "object", name: "RemoveRowsParams" }, result: { kind: "object", name: "ScanResult" } },
  tagPreview: { params: { kind: "object", name: "TagPreviewParams" }, result: { kind: "object", name: "Preview" } },
  scriptInfo: { params: { kind: "object", name: "Empty" }, result: { kind: "object", name: "ScriptInfo" } },
  editScript: { params: { kind: "object", name: "Empty" }, result: { kind: "object", name: "ScriptInfo" } },
  tagColumns: { params: { kind: "object", name: "TagColumnsParams" }, result: { kind: "object", name: "TagColumnValues" } },
  readTags: { params: { kind: "object", name: "ReadTagsParams" }, result: { kind: "object", name: "TagList" } },
  attributePreview: { params: { kind: "object", name: "AttributePreviewParams" }, result: { kind: "object", name: "AttributePreview" } },
  applyAttributes: { params: { kind: "object", name: "AttributePreviewParams" }, result: { kind: "object", name: "AttributeOutcome" } },
  undoAttributes: { params: { kind: "object", name: "Empty" }, result: { kind: "object", name: "AttributeUndo", nullable: true } },
  numberedPreview: { params: { kind: "object", name: "NumberedOptions" }, result: { kind: "object", name: "NumberedPlan" } },
  createNumbered: { params: { kind: "object", name: "NumberedOptions" }, result: { kind: "object", name: "CreateOutcome" } },
};
