import { describe, expect, it } from "vitest";
import { diffNames } from "./diff";

const text = (parts: ReturnType<typeof diffNames>, kind: string) => parts.filter((p) => p.kind === kind).map((p) => p.text).join("");

describe("diffNames", () => {
  it("aynı adı tek parça olarak verir", () => {
    expect(diffNames("rapor.docx", "rapor.docx")).toEqual([{ text: "rapor.docx", kind: "same" }]);
  });

  it("eklenen harfleri işaretler", () => {
    const parts = diffNames("rapor.docx", "01_rapor.docx");
    expect(text(parts, "added")).toBe("01_");
    expect(text(parts, "removed")).toBe("");
    expect(text(parts, "same")).toBe("rapor.docx");
  });

  it("silinen harfleri işaretler", () => {
    const parts = diffNames("a - b.txt", "a-b.txt");
    expect(text(parts, "removed")).toBe("  ");
    expect(text(parts, "added")).toBe("");
  });

  it("değişen harfi hem silinen hem eklenen olarak verir", () => {
    const parts = diffNames("Şu.txt", "Su.txt");
    expect(text(parts, "removed")).toBe("Ş");
    expect(text(parts, "added")).toBe("S");
    expect(text(parts, "same")).toBe("u.txt");
  });

  it("parçaları birleştirince iki adı da yeniden kurar", () => {
    for (const [a, b] of [["Öğrenci Şeması.xlsx", "Ogrenci_Semasi.xlsx"], ["", "x"], ["x", ""], ["abc", "cba"]] as const) {
      const parts = diffNames(a, b);
      expect(parts.filter((p) => p.kind !== "added").map((p) => p.text).join("")).toBe(a);
      expect(parts.filter((p) => p.kind !== "removed").map((p) => p.text).join("")).toBe(b);
    }
  });
});
