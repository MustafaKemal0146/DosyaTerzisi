import { describe, expect, it } from "vitest";
import type { FileRow } from "./contract";
import { buildLayout, chunk, dayKey, groupKey, indexAt, nextRowIndex, rowIdsBetween, tagMode, visibleRange } from "./grouping";

const row = (id: number, name: string, dir = "C:\\a", modified = new Date(2026, 9, 28, 12).getTime(), isDir = false): FileRow => ({ id, name, dir, isDir, size: 1, modified });

describe("grouping", () => {
  const rows = [row(0, "b.TXT"), row(1, "a.mp3", "C:\\z"), row(2, "c.txt", "C:\\a"), row(3, "README", "C:\\z"), row(4, "sub", "C:\\a", 0, true)];

  it("grup anahtarları", () => {
    expect(groupKey(rows[0]!, "ext")).toBe("txt");
    expect(groupKey(rows[3]!, "ext")).toBe("(uzantısız)");
    expect(groupKey(rows[4]!, "ext")).toBe("(klasörler)");
    expect(groupKey(rows[0]!, "folder")).toBe("C:\\a");
    expect(groupKey(rows[0]!, "date")).toBe("2026-10-28");
    expect(dayKey(new Date(2026, 0, 5, 23, 59).getTime())).toBe("2026-01-05");
    expect(groupKey(rows[0]!, tagMode("ID3.Artist"), () => " Sezen ")).toBe("Sezen");
    expect(groupKey(rows[0]!, tagMode("ID3.Artist"), () => undefined)).toBe("");
  });

  it("Yok: satırlar olduğu gibi, başlık yok", () => {
    const l = buildLayout(rows, "none", 36);
    expect(l.grouped).toBe(false);
    expect(l.items.map((i) => (i.kind === "row" ? i.row.id : -1))).toEqual([0, 1, 2, 3, 4]);
    expect(l.offsets).toEqual([0, 36, 72, 108, 144, 180]);
  });

  it("uzantıya göre: kararlı sıra, başlıklar, sayılar", () => {
    const l = buildLayout(rows, "ext", 36);
    const flat = l.items.map((i) => (i.kind === "header" ? `#${i.label}(${i.count})` : i.row.id));
    expect(flat).toEqual(["#(klasörler)(1)", 4, "#(uzantısız)(1)", 3, "#mp3(1)", 1, "#txt(2)", 0, 2]);
    expect(l.offsets).toHaveLength(l.items.length + 1);
    const head = l.items[6]!;
    expect(head.kind === "header" && head.ids).toEqual([0, 2]);
  });

  it("klasöre göre ve boş etiket sona", () => {
    const f = buildLayout(rows, "folder", 36).items.filter((i) => i.kind === "header").map((i) => (i.kind === "header" ? i.key : ""));
    expect(f).toEqual(["C:\\a", "C:\\z"]);

    const values: Record<number, string> = { 0: "Zeki", 1: "Ajda", 2: "Ajda" };
    const t = buildLayout(rows, tagMode("ID3.Artist"), 36, (id) => values[id]);
    const labels = t.items.filter((i) => i.kind === "header").map((i) => (i.kind === "header" ? i.label : ""));
    expect(labels).toEqual(["Ajda", "Zeki", "(boş)"]);
  });

  it("tarihe göre artan", () => {
    const l = buildLayout([row(0, "x", "d", new Date(2026, 5, 2).getTime()), row(1, "y", "d", new Date(2025, 11, 31).getTime())], "date", 36);
    const labels = l.items.filter((i) => i.kind === "header").map((i) => (i.kind === "header" ? i.label : ""));
    expect(labels).toEqual(["31.12.2025", "02.06.2026"]);
  });

  it("ikili arama ve görünür aralık", () => {
    const offsets = Array.from({ length: 101 }, (_, i) => i * 36);
    expect(indexAt(offsets, 0)).toBe(0);
    expect(indexAt(offsets, 35)).toBe(0);
    expect(indexAt(offsets, 36)).toBe(1);
    expect(indexAt(offsets, 10_000)).toBe(99);
    expect(visibleRange(offsets, 360, 360, 2)).toEqual([8, 23]);
    expect(visibleRange(offsets, 0, 360, 0)).toEqual([0, 11]);
    expect(visibleRange([0], 0, 100, 5)).toEqual([0, 0]);
  });

  it("gezinti başlıkları atlar", () => {
    const items = buildLayout(rows, "ext", 36).items;
    expect(nextRowIndex(items, 1, 1)).toBe(3); // 4 numaralı klasör satırı -> sonraki başlığı atla
    expect(nextRowIndex(items, 3, -1)).toBe(1);
    expect(nextRowIndex(items, 1, -1)).toBeNull();
    expect(nextRowIndex(items, items.length - 1, 1)).toBeNull();
    expect(rowIdsBetween(items, 1, 5)).toEqual([4, 3, 1]);
  });

  it("parçalara böler", () => {
    expect(chunk([1, 2, 3, 4, 5], 2)).toEqual([[1, 2], [3, 4], [5]]);
    expect(chunk([], 200)).toEqual([]);
  });
});
