// Sahte köprünün tarayıcıda denenebilir olması için: etiketler, öznitelikler, numaralı nesneler ve klasör ağacı.
// Gerçek işleri C# tarafı yapar; burada yalnızca aynı sözleşmeye uyan, basit bir benzeri var.

import type {
  AttrFlags,
  AttributeRow,
  AttributeRule,
  FolderNode,
  NumberedOptions,
  NumberedPlan,
  PlannedObject,
  TagEntry,
  TimeRule,
} from "../contract";
import { checkName, extensionOf, formatCounter, titleOf, type MockEntry, type MockTags } from "./engine";

// ---- Etiketler ------------------------------------------------------------------------------------------------------

/** Dosya adından uydurulmuş etiketler: "Şarkı - Sanatçı.mp3" ve "IMG_20260814_143005.jpg". */
export function tagsOf(entry: MockEntry): MockTags {
  const tags: MockTags = new Map();
  if (entry.isDir) return tags;

  const ext = extensionOf(entry).toLowerCase();
  if (ext === "mp3") {
    const match = /^(.*) - (.*)\.mp3$/i.exec(entry.name);
    if (match) {
      tags.set("ID3.Track", { text: match[1]! });
      tags.set("ID3.Title", { text: match[1]! });
      tags.set("ID3.Artist", { text: match[2]! });
      tags.set("ID3.Album", { text: "Albüm" });
      tags.set("ID3.Genre", { text: "Pop" });
      tags.set("ID3.Year", { text: "1991", number: 1991 });
      const track = (entry.name.length % 12) + 1;
      tags.set("ID3.TrackNumber", { text: String(track), number: track });
    }
  } else if (ext === "jpg") {
    const match = /^IMG_(\d{4})(\d{2})(\d{2})_(\d{2})(\d{2})(\d{2})/.exec(entry.name);
    tags.set("EXIF.Maker", { text: "Canon" });
    tags.set("EXIF.Model", { text: "EOS 5D" });
    if (match) {
      const [, y, mo, d, h, mi, s] = match.map(Number) as [number, number, number, number, number, number, number];
      const date = new Date(y, mo - 1, d, h, mi, s);
      tags.set("EXIF.OriginalTime", { text: date.toISOString(), date });
    }
  }
  return tags;
}

export function tagList(entry: MockEntry): TagEntry[] {
  return [...tagsOf(entry)]
    .map(([name, tag]) => ({ name, value: tag.text }))
    .sort((a, b) => a.name.localeCompare(b.name, "en"));
}

/** Liste sütunu için tek etiketin değeri (yoksa boş). Belge ve HTML etiketleri yalnızca bu sütunlar için uydurulur. */
export function tagColumnValue(entry: MockEntry, name: string): string {
  const own = tagsOf(entry).get(name);
  if (own) return own.text;
  if (entry.isDir) return "";

  const ext = extensionOf(entry).toLowerCase();
  const base = entry.name.slice(0, entry.name.length - (ext ? ext.length + 1 : 0));
  if ((ext === "docx" || ext === "doc") && name === "DOC.Title") return base;
  if ((ext === "docx" || ext === "doc") && name === "DOC.Author") return "Musa Hoca";
  if ((ext === "html" || ext === "htm") && name === "HTML.Title") return base;
  return "";
}

// ---- Öznitelikler ---------------------------------------------------------------------------------------------------

export const defaultAttrs: AttrFlags = { readOnly: false, archive: true, hidden: false, system: false };

export const attrsOf = (entry: MockEntry): AttrFlags => entry.attrs ?? defaultAttrs;

const minTime = Date.UTC(1601, 0, 2);

/** Aynı tohum ve dosya her zaman aynı sayıyı verir (önizleme uygulananla aynı olsun). */
function seeded(text: string): number {
  let h = 2166136261;
  for (let i = 0; i < text.length; i++) h = Math.imul(h ^ text.charCodeAt(i), 16777619);
  return (h >>> 0) / 4294967296;
}

function applyTime(entry: MockEntry, rule: TimeRule, current: number, taken: Date | undefined): number {
  const result = baseTime(entry, rule, current, taken);
  if (rule.action === "keep") return result;
  let limited = result;
  if (rule.limitMin !== 0 && limited < rule.limitMin) limited = rule.limitMin;
  if (rule.limitMax !== 0 && limited > rule.limitMax) limited = rule.limitMax;
  return limited;
}

function baseTime(entry: MockEntry, rule: TimeRule, current: number, taken: Date | undefined): number {
  switch (rule.action) {
    case "random": {
      const [from, to] = rule.randomTo >= rule.value ? [rule.value, rule.randomTo] : [rule.randomTo, rule.value];
      const seconds = Math.floor((to - from) / 1000);
      return from + Math.floor(seeded(`${rule.seed}|${entry.dir}\${entry.name}`.toLowerCase()) * (seconds + 1)) * 1000;
    }
    case "setTo":
      return rule.value;
    case "shift":
      return current + rule.shiftMinutes * 60_000;
    case "copyFrom":
      return rule.copyFrom === "created" ? entry.created : rule.copyFrom === "accessed" ? (entry.accessed ?? entry.modified) : entry.modified;
    case "photoTaken":
      return taken ? taken.getTime() : current;
    default:
      return current;
  }
}

function tri(flag: boolean, state: AttributeRule["readOnly"]): boolean {
  return state === "set" ? true : state === "clear" ? false : flag;
}

export interface AttributePlan {
  rows: AttributeRow[];
  changed: number;
  problems: number;
}

export function planAttributes(all: MockEntry[], targets: MockEntry[], rule: AttributeRule): AttributePlan {
  const chosen = new Set(targets.filter((e) => rule.includeFolders || !e.isDir).map((e) => e.id));
  let changed = 0;
  let problems = 0;

  const rows = all.map((entry): AttributeRow => {
    const before = attrsOf(entry);
    const accessed = entry.accessed ?? entry.modified;
    const untouched: AttributeRow = {
      id: entry.id, before, after: before,
      modifiedBefore: entry.modified, modifiedAfter: entry.modified,
      createdBefore: entry.created, createdAfter: entry.created,
      accessedBefore: accessed, accessedAfter: accessed,
      status: "unchanged", message: null,
    };
    if (!chosen.has(entry.id)) return untouched;

    const needsPhoto = [rule.modified, rule.created, rule.accessed].some((r) => r.action === "photoTaken");
    const taken = needsPhoto ? tagsOf(entry).get("EXIF.OriginalTime")?.date : undefined;

    const after: AttrFlags = {
      readOnly: tri(before.readOnly, rule.readOnly),
      archive: tri(before.archive, rule.archive),
      hidden: tri(before.hidden, rule.hidden),
      system: tri(before.system, rule.system),
    };
    const modifiedAfter = applyTime(entry, rule.modified, entry.modified, taken);
    const createdAfter = applyTime(entry, rule.created, entry.created, taken);
    const accessedAfter = applyTime(entry, rule.accessed, accessed, taken);

    const row = { ...untouched, after, modifiedAfter, createdAfter, accessedAfter };
    const tooEarly = [modifiedAfter, createdAfter, accessedAfter].some((t) => t < minTime);
    const different =
      JSON.stringify(after) !== JSON.stringify(before) || modifiedAfter !== entry.modified || createdAfter !== entry.created || accessedAfter !== accessed;

    if (tooEarly) {
      problems++;
      return { ...row, status: "error", message: "Yeni tarih geçersiz (en erken 1601 olabilir)." };
    }
    if (different) changed++;
    const message = needsPhoto && !taken ? "Çekim tarihi bulunamadı." : null;
    return { ...row, status: different ? "ready" : "unchanged", message };
  });

  return { rows, changed, problems };
}

// ---- Numaralı nesneler ----------------------------------------------------------------------------------------------

export function parseSize(text: string): number | null {
  const clean = text.trim().replace(/\s/g, "").replace(/b$/i, "");
  if (!clean) return 0;
  const match = /^(\d+(?:[.,]\d+)?)([kmgt]?)$/i.exec(clean);
  if (!match) return null;
  const units: Record<string, number> = { "": 1, k: 1024, m: 1024 ** 2, g: 1024 ** 3, t: 1024 ** 4 };
  return Math.floor(Number(match[1]!.replace(",", ".")) * units[match[2]!.toLowerCase()]!);
}

function numberedName(o: NumberedOptions, number: number, width: number): string {
  const pattern = o.namePattern.trim();
  const named = pattern.replace(/\?+/g, (run) => formatCounter(number, run.length, o.numberStyle));
  if (pattern.includes("?") || o.amount === 1) return named;

  const isFile = o.kind === "emptyFile" || o.kind === "copyOfFile";
  const dot = isFile ? named.lastIndexOf(".") : -1;
  const counter = formatCounter(number, width, o.numberStyle);
  return dot > 0 ? `${named.slice(0, dot)}${counter}${named.slice(dot)}` : named + counter;
}

export function planNumbered(o: NumberedOptions, exists: (dir: string, name: string) => boolean, folderExists: boolean): NumberedPlan {
  const problem = (text: string): NumberedPlan => ({ items: [], problem: text, ready: 0 });

  if (o.kind === "emptyFile" && parseSize(o.size) === null) return problem("Boyut anlaşılamadı. Örnek: 512, 10K, 1,5M, 2G");
  if (!folderExists) return problem("Hedef klasör bulunamadı.");
  if (o.amount < 1 || o.amount > 100_000) return problem("Adet 1 ile 100.000 arasında olmalı.");
  if (o.start < 0) return problem("Başlangıç sayısı eksi olamaz.");
  if (o.step < 1) return problem("Adım en az 1 olmalı.");
  if (!o.namePattern.trim()) return problem("Bir ad yazın.");
  if ((o.kind === "copyOfFile" || o.kind === "copyOfFolder") && !o.sourcePath.trim()) return problem("Kopyalanacak öğeyi seçin.");

  const last = o.start + (o.amount - 1) * o.step;
  const width = Math.max(String(o.start).length, String(last).length);
  const seen = new Set<string>();
  const items: PlannedObject[] = [];
  for (let i = 0; i < o.amount; i++) {
    const name = numberedName(o, o.start + i * o.step, width);
    const invalid = checkName(name);
    if (invalid) items.push({ name, status: "invalidName", message: invalid });
    else if (!seen.add(name.toLowerCase()) || exists(o.folder, name)) items.push({ name, status: "exists", message: "Bu adda bir öğe zaten var." });
    else items.push({ name, status: "ready", message: null });
  }
  return { items, problem: null, ready: items.filter((i) => i.status === "ready").length };
}

// ---- Klasör ağacı ---------------------------------------------------------------------------------------------------

/** `C:\Users\Musa` → `C:\Users`; `C:\Users` → `C:\` (sürücü kökü). Kökün üstü yok: kendisini verir. */
function parentOf(path: string): string {
  const parent = path.replace(/[\\/][^\\/]+[\\/]?$/, "");
  return /^[A-Za-z]:$/.test(parent) ? `${parent}\\` : parent;
}
const nameOf = (path: string) => path.split(/[\\/]/).filter(Boolean).at(-1) ?? path;

/** Dosyaların bulunduğu klasörlerden ve üst klasörlerinden bir ağaç kurar. */
export function folderTree(entries: MockEntry[]): Map<string, string[]> {
  const children = new Map<string, Set<string>>();
  const add = (path: string) => {
    for (let p = path; ; ) {
      const parent = parentOf(p);
      if (!parent || parent === p) return;
      if (!children.has(parent)) children.set(parent, new Set());
      children.get(parent)!.add(p);
      p = parent;
    }
  };
  for (const e of entries) {
    add(e.dir);
    if (e.isDir) add(`${e.dir}\\${e.name}`);
  }
  return new Map([...children].map(([k, v]) => [k, [...v].sort((a, b) => a.localeCompare(b, "tr", { numeric: true }))]));
}

export function listFolderNodes(tree: Map<string, string[]>, path: string, roots: FolderNode[]): FolderNode[] {
  if (path === "") return roots;
  return (tree.get(path) ?? []).map((p) => ({ name: nameOf(p), path: p, hasChildren: (tree.get(p) ?? []).length > 0 }));
}

export { titleOf };
