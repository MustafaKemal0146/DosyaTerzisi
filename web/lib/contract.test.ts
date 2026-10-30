import { readdirSync, readFileSync } from "node:fs";
import { join, resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { conforms, validateParams, validateResult } from "./wire";
import { methodNames, methodShapes, shapes, wireVersion } from "./wire.generated";

// C# testleri `contract/fixtures` altındaki örnek dosyaları üretir; burada aynı dosyalar okunur.
// Böylece C# ve TypeScript aynı örnekler üzerinde anlaşmak zorundadır.
const fixtures = resolve(process.cwd(), "..", "contract", "fixtures");

const read = (...parts: string[]) => JSON.parse(readFileSync(join(fixtures, ...parts), "utf-8")) as unknown;
const names = (folder: string) => readdirSync(join(fixtures, folder)).map((f) => f.replace(/\.json$/, ""));

describe("sözleşme örnek dosyaları", () => {
  it("her tipin örneği var, örneği olmayan tip yok", () => {
    const objects = Object.entries(shapes).filter(([, shape]) => shape.kind === "object").map(([name]) => name);
    expect(names("types").sort()).toEqual(objects.sort());
  });

  it("her tipin örneği o tipe uyuyor", () => {
    for (const name of names("types")) {
      const problems = conforms(read("types", `${name}.json`), { kind: "object", name });
      expect(problems, name).toEqual([]);
    }
  });

  it("her işin istek ve cevap örneği var, ikisi de uyuyor", () => {
    const requests = names("messages").filter((n) => n.endsWith(".request")).map((n) => n.replace(".request", ""));
    expect(requests.sort()).toEqual([...methodNames].sort());

    for (const method of methodNames) {
      const request = read("messages", `${method}.request.json`) as Record<string, unknown>;
      expect(request.v).toBe(wireVersion);
      expect(typeof request.id).toBe("number");
      expect(request.method).toBe(method);
      expect(validateParams(method, request.params), `${method} isteği`).toEqual([]);

      const response = read("messages", `${method}.response.json`) as Record<string, unknown>;
      expect(response.v).toBe(wireVersion);
      expect(response.id).toBe(request.id);
      expect(validateResult(method, response.result), `${method} cevabı`).toEqual([]);

      if (methodShapes[method].result.nullable) {
        const empty = read("messages", `${method}.response.null.json`) as Record<string, unknown>;
        expect(validateResult(method, empty.result), `${method} boş cevabı`).toEqual([]);
      }
    }
  });

  it("hata cevabının biçimi", () => {
    const error = read("messages", "error.failed.json") as { v: number; id: number; error: { code: string; message: string } };
    expect(error.v).toBe(wireVersion);
    expect(typeof error.id).toBe("number");
    expect(error.error.code).toBe("failed");
    expect(error.error.message).toContain("Klasör bulunamadı");
  });

  it("örneklerdeki Türkçe harfler bozulmamış", () => {
    const row = read("types", "FileRow.json") as { name: string };
    expect(row.name).toBe("Öğrenci Şeması (2).xlsx");
  });
});

describe("conforms", () => {
  const row = read("types", "FileRow.json") as Record<string, unknown>;
  const check = (value: unknown) => conforms(value, { kind: "object", name: "FileRow" });

  it("iyi nesneyi kabul eder", () => expect(check(row)).toEqual([]));

  it("eksik, fazla ve yanlış tipli alanları bulur", () => {
    const { modified: _ignored, ...missing } = row;
    expect(check(missing)).toContain("$.modified: eksik");
    expect(check({ ...row, extra: 1 })).toContain("$.extra: tanımsız alan");
    expect(check({ ...row, id: "1" })).toContain("$.id: tam sayı olmalı");
    expect(check({ ...row, id: 1.5 })).toContain("$.id: tam sayı olmalı");
    expect(check({ ...row, isDir: "evet" })).toContain("$.isDir: doğru/yanlış olmalı");
    expect(check({ ...row, name: null })).toContain("$.name: boş (null) olamaz");
    expect(check([1])).toContain("$: nesne olmalı");
  });

  it("boş olabilen alanlara null verilebilir, diğerlerine verilemez", () => {
    const preview = read("types", "PreviewRow.json") as Record<string, unknown>;
    const previewCheck = (v: unknown) => conforms(v, { kind: "object", name: "PreviewRow" });

    expect(previewCheck({ ...preview, message: null })).toEqual([]);
    expect(previewCheck({ ...preview, newName: null })).toHaveLength(1);
  });

  it("bilinmeyen enum değerini ve iç içe hatayı yolu ile bildirir", () => {
    const previewRow = read("types", "PreviewRow.json") as Record<string, unknown>;
    expect(conforms({ ...previewRow, status: "hazir" }, { kind: "object", name: "PreviewRow" })[0]).toContain("$.status: şunlardan biri olmalı");

    const preview = read("types", "Preview.json") as { rows: Record<string, unknown>[] };
    const broken = { ...preview, rows: [{ ...preview.rows[0], status: 5 }] };
    expect(conforms(broken, { kind: "object", name: "Preview" })[0]).toContain("$.rows[0].status");
  });

  it("tanımsız bir iş sorulursa yanlış cevabı yakalar", () => {
    expect(validateResult("scan", { rows: "yok", skippedFolders: 0 })).not.toEqual([]);
    expect(validateParams("moveFavorite", { id: "a", delta: "bir" })).not.toEqual([]);
    expect(validateResult("undo", null)).toEqual([]);
    expect(validateResult("scan", null)).not.toEqual([]);
  });
});
