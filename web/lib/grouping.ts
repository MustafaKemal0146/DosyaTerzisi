/** Dosya listesini gruplar halinde göstermek için saf yardımcılar (sanallaştırma için satır konumları dahil). */

import type { FileRow } from "./contract";

/** "none" | "folder" | "date" | "ext" | "tag:<Etiket adı>" */
export type GroupMode = string;

export const TAG_PREFIX = "tag:";

/** Seçilebilir hazır etiketler (başlık menüsü). */
export const knownTags = [
  "ID3.Artist",
  "ID3.Album",
  "ID3.Track",
  "ID3.Year",
  "EXIF.OriginalTime",
  "EXIF.Model",
  "DOC.Title",
  "DOC.Author",
  "HTML.Title",
] as const;

export const NO_TAG_VALUE = "(boş)";
export const NO_EXT = "(uzantısız)";
export const FOLDERS = "(klasörler)";

export const tagMode = (name: string): GroupMode => TAG_PREFIX + name;
export const tagOfMode = (mode: GroupMode): string | null => (mode.startsWith(TAG_PREFIX) ? mode.slice(TAG_PREFIX.length) : null);

export function isValidMode(mode: unknown): mode is GroupMode {
  return mode === "none" || mode === "folder" || mode === "date" || mode === "ext" || (typeof mode === "string" && mode.length > TAG_PREFIX.length && mode.startsWith(TAG_PREFIX));
}

/** Etiket değeri aranırken kullanılan işlev: satır numarası ve etiket adı verilir. */
export type TagLookup = (id: number, tag: string) => string | undefined;

const pad = (n: number) => String(n).padStart(2, "0");

/** Yerel saate göre gün: "2026-10-28". */
export function dayKey(epochMs: number): string {
  const d = new Date(epochMs);
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

export function extensionKey(row: FileRow): string {
  if (row.isDir) return FOLDERS;
  const dot = row.name.lastIndexOf(".");
  return dot <= 0 || dot === row.name.length - 1 ? NO_EXT : row.name.slice(dot + 1).toLowerCase();
}

/** Satırın grup anahtarı. Boş anahtar "değeri yok" demektir (en sona dizilir). */
export function groupKey(row: FileRow, mode: GroupMode, tags?: TagLookup): string {
  if (mode === "folder") return row.dir;
  if (mode === "date") return dayKey(row.modified);
  if (mode === "ext") return extensionKey(row);
  const tag = tagOfMode(mode);
  if (tag !== null) return (tags?.(row.id, tag) ?? "").trim();
  return "";
}

/** Grup başlığında görünen ad. */
export function groupLabel(key: string, mode: GroupMode): string {
  if (key === "") return mode === "folder" ? "(klasör yok)" : NO_TAG_VALUE;
  if (mode === "date") {
    const [y, m, d] = key.split("-");
    return `${d}.${m}.${y}`;
  }
  return key;
}

const collator = new Intl.Collator("tr", { numeric: true, sensitivity: "base" });

export type LayoutItem =
  | { kind: "header"; key: string; label: string; count: number; ids: number[] }
  | { kind: "row"; row: FileRow };

export interface Layout {
  items: LayoutItem[];
  /** `offsets[i]` = i. öğenin üst konumu; son eleman toplam yükseklik (uzunluk = items.length + 1). */
  offsets: number[];
  grouped: boolean;
}

/**
 * Satırları grup anahtarına göre kararlı biçimde dizer ve her grubun başına başlık ekler.
 * "none" ise satırlar olduğu gibi kalır. Grup içinde özgün sıra korunur.
 */
export function buildLayout(rows: readonly FileRow[], mode: GroupMode, rowHeight: number, tags?: TagLookup): Layout {
  if (mode === "none" || !isValidMode(mode)) {
    const items: LayoutItem[] = rows.map((row) => ({ kind: "row", row }));
    return { items, offsets: computeOffsets(items, rowHeight), grouped: false };
  }

  const groups = new Map<string, FileRow[]>();
  for (const row of rows) {
    const key = groupKey(row, mode, tags);
    const list = groups.get(key);
    if (list) list.push(row);
    else groups.set(key, [row]);
  }

  const keys = [...groups.keys()].sort((a, b) => {
    if (a === b) return 0;
    if (a === "") return 1; // Boş değerler en sonda.
    if (b === "") return -1;
    if (mode === "date") return a < b ? -1 : 1;
    return collator.compare(a, b) || (a < b ? -1 : 1);
  });

  const items: LayoutItem[] = [];
  for (const key of keys) {
    const members = groups.get(key)!;
    items.push({ kind: "header", key, label: groupLabel(key, mode), count: members.length, ids: members.map((r) => r.id) });
    for (const row of members) items.push({ kind: "row", row });
  }
  return { items, offsets: computeOffsets(items, rowHeight), grouped: true };
}

/** Her öğe tek satır yüksekliğindedir; yine de konumlar bir ofset dizisinden okunur. */
export function computeOffsets(items: readonly unknown[], rowHeight: number): number[] {
  const offsets = new Array<number>(items.length + 1);
  for (let i = 0; i <= items.length; i++) offsets[i] = i * rowHeight;
  return offsets;
}

/** `offsets[i] <= y` olan en büyük i (ikili arama). Dizi boşsa 0. */
export function indexAt(offsets: readonly number[], y: number): number {
  const count = offsets.length - 1;
  if (count <= 0) return 0;
  let lo = 0;
  let hi = count - 1;
  while (lo < hi) {
    const mid = (lo + hi + 1) >> 1;
    if (offsets[mid]! <= y) lo = mid;
    else hi = mid - 1;
  }
  return lo;
}

/** Ekranda görünen öğe aralığı [first, last) (tampon dahil). */
export function visibleRange(offsets: readonly number[], scrollTop: number, viewport: number, overscan: number): [number, number] {
  const count = offsets.length - 1;
  const first = Math.max(0, indexAt(offsets, scrollTop) - overscan);
  const last = Math.min(count, indexAt(offsets, scrollTop + viewport) + 1 + overscan);
  return [first, last];
}

/** `from` konumundan `step` (±1) yönünde ilk satır öğesi (başlıklar atlanır); yoksa null. */
export function nextRowIndex(items: readonly LayoutItem[], from: number, step: 1 | -1): number | null {
  for (let i = from + step; i >= 0 && i < items.length; i += step) if (items[i]!.kind === "row") return i;
  return null;
}

/** Bu satır numarasının öğe dizisindeki konumu; yoksa -1. */
export function indexOfRow(items: readonly LayoutItem[], id: number): number {
  return items.findIndex((it) => it.kind === "row" && it.row.id === id);
}

/** İki öğe konumu arasındaki (dahil) satır kimlikleri. */
export function rowIdsBetween(items: readonly LayoutItem[], a: number, b: number): number[] {
  const [from, to] = [Math.min(a, b), Math.max(a, b)];
  const ids: number[] = [];
  for (let i = from; i <= to; i++) {
    const it = items[i];
    if (it?.kind === "row") ids.push(it.row.id);
  }
  return ids;
}

/** Kimlik listesini en çok `size` elemanlı parçalara böler. */
export function chunk<T>(list: readonly T[], size: number): T[][] {
  const out: T[][] = [];
  for (let i = 0; i < list.length; i += size) out.push(list.slice(i, i + size));
  return out;
}
