import { describe, expect, it } from "vitest";
import { defaultRule, type Rule } from "../contract";
import { checkName, plan, type MockEntry } from "./engine";

let nextId = 0;

function entry(name: string, dir = "C:\\Belgeler\\Evrak", isDir = false): MockEntry {
  const time = new Date(2026, 6, 1, 15, 33, 7).getTime();
  return { id: nextId++, name, dir, isDir, size: 1, modified: time, created: time };
}

const rule = (patch: Partial<Rule>): Rule => ({ ...defaultRule, ...patch });

function names(entries: MockEntry[], r: Rule) {
  return plan(entries, entries, r).rows.map((row) => row.newName);
}

describe("sahte motor", () => {
  it("joker karakter: tire etrafındaki boşlukları siler", () => {
    const e = entry("Ek I-4.9.1 - Karar Alma Süreçleri.pdf");
    expect(names([e], rule({ method: "wildcard", search: "* - *", replace: "\\1-\\2" }))).toEqual(["Ek I-4.9.1-Karar Alma Süreçleri.pdf"]);
  });

  it("joker karakter: eşleşmeyen ad değişmez", () => {
    const e = entry("rapor.docx");
    expect(plan([e], [e], rule({ method: "wildcard", search: "* - *", replace: "\\1-\\2" })).ready).toBe(0);
  });

  it("joker karakter: iki yanı değiştirir", () => {
    expect(names([entry("a-b.txt")], rule({ method: "wildcard", search: "*-*.*", replace: "\\2-\\1.\\3" }))).toEqual(["b-a.txt"]);
  });

  it("düzenli ifade: (n) siler, yalnızca eşleşen kısmı değiştirir", () => {
    expect(names([entry("report(2).txt")], rule({ method: "regex", search: "(.*)\\(\\d+\\)(.*)", replace: "\\1\\2" }))).toEqual(["report.txt"]);
    expect(names([entry("PHOTO.JPG")], rule({ method: "regex", search: "(\\..*)$", replace: "\\L\\1" }))).toEqual(["PHOTO.jpg"]);
  });

  it("düzenli ifade: bozuksa hata verir ve hiçbir şey değişmez", () => {
    const e = entry("a.txt");
    const result = plan([e], [e], rule({ method: "regex", search: "(", replace: "x" }));
    expect(result.ruleError).not.toBe("");
    expect(result.ready).toBe(0);
    expect(result.rows[0]!.status).toBe("error");
  });

  it("çeviri: Türkçe harfleri çevirir, uzantıya dokunmaz", () => {
    const r = rule({ method: "translation", search: "ÜüĞğİıŞşÇçÖö ", replace: "UuGgIiSsCcOo_" });
    expect(names([entry("Öğrenci Şeması.docx"), entry("özet.TXT")], r)).toEqual(["Ogrenci_Semasi.docx", "ozet.TXT"]);
  });

  it("çeviri: aralıklarla büyük harf", () => {
    expect(names([entry("abc.txt")], rule({ method: "translation", search: "a-z", replace: "A-Z" }))).toEqual(["ABC.txt"]);
  });

  it("sayaç: sıfırla doldurur, başlangıç ve adım uygulanır", () => {
    const r = rule({ method: "wildcard", search: "*.*", replace: "???_\\1.\\2", numberStart: 5, numberStep: 5 });
    expect(names([entry("a.txt"), entry("b.txt")], r)).toEqual(["005_a.txt", "010_b.txt"]);
  });

  it("sayaç: her klasörde yeniden başlayabilir", () => {
    const r = rule({ method: "wildcard", search: "*.*", replace: "??.\\2", resetNumberPerFolder: true });
    expect(names([entry("a.jpg", "C:\\x"), entry("b.jpg", "C:\\x"), entry("c.jpg", "C:\\y")], r)).toEqual(["01.jpg", "02.jpg", "01.jpg"]);
  });

  it("değişkenler: tarih ve ad parçaları", () => {
    expect(names([entry("foto.jpg")], rule({ method: "wildcard", search: "*.*", replace: "\\Y\\m\\d\\H\\M\\S.\\2" }))).toEqual(["20260701153307.jpg"]);
    expect(names([entry("a.txt")], rule({ method: "wildcard", search: "*", replace: "\\F_\\f_\\t_\\e" }))).toEqual(["Belgeler_Evrak_a_txt"]);
  });

  it("büyük/küçük harf değişkenleri", () => {
    expect(names([entry("abc.txt")], rule({ method: "wildcard", search: "*.*", replace: "\\U\\1\\E.\\2" }))).toEqual(["ABC.txt"]);
    expect(names([entry("istanbul.txt")], rule({ method: "wildcard", search: "*.*", replace: "\\u\\1.\\2" }))).toEqual(["İstanbul.txt"]);
  });

  it("çakışma: ikinci öğe çakışma olur, numara eklenirse çözülür", () => {
    const files = [entry("a.txt"), entry("b.txt"), entry("c.txt")];
    const r = rule({ method: "wildcard", search: "*.*", replace: "x.\\2" });

    const reported = plan(files, files, r);
    expect(reported.rows.map((x) => x.status)).toEqual(["ready", "conflict", "conflict"]);
    expect(reported.problems).toBe(2);

    const numbered = plan(files, files, r, true);
    expect(numbered.rows.map((x) => x.newName)).toEqual(["x.txt", "x(01).txt", "x(02).txt"]);
  });

  it("çakışma: takas serbest, diskteki dosyayla çakışma yasak", () => {
    const a = entry("a-b.txt");
    const b = entry("b-a.txt");
    const swap = plan([a, b], [a, b], rule({ method: "wildcard", search: "*-*.*", replace: "\\2-\\1.\\3" }));
    expect(swap.rows.map((x) => x.status)).toEqual(["ready", "ready"]);

    const existing = entry("taken.txt");
    const clash = plan([a, existing], [a], rule({ method: "wildcard", search: "*", replace: "taken.txt" }));
    expect(clash.rows[0]!.status).toBe("conflict");
  });

  it("yalnızca büyük/küçük harf değişimi çakışma sayılmaz", () => {
    const a = entry("abc.txt");
    expect(plan([a], [a], rule({ method: "translation", search: "a-z", replace: "A-Z" })).rows[0]!.status).toBe("ready");
  });

  it("hedef olmayanlar değişmez", () => {
    const a = entry("a.txt");
    const b = entry("b.txt");
    const result = plan([a, b], [b], rule({ method: "wildcard", search: "*", replace: "x_\\1" }));
    expect(result.rows.map((x) => x.newName)).toEqual(["a.txt", "x_b.txt"]);
    expect(result.ready).toBe(1);
  });

  it("geçersiz adlar", () => {
    expect(checkName("")).not.toBeNull();
    expect(checkName("a/b")).not.toBeNull();
    expect(checkName("CON.txt")).not.toBeNull();
    expect(checkName("bitti.")).not.toBeNull();
    expect(checkName("Öğrenci Şeması (GÜNCEL).docx")).toBeNull();

    const e = entry("a.txt");
    expect(plan([e], [e], rule({ method: "wildcard", search: "*", replace: "CON" })).rows[0]!.status).toBe("invalid");
  });
});

describe("sayaç yazılışı", () => {
  it("ondalık, onaltılık ve harf", async () => {
    const { formatCounter } = await import("./engine");
    expect(formatCounter(7, 2, "decimal")).toBe("07");
    expect(formatCounter(255, 1, "hexadecimal")).toBe("FF");
    expect(formatCounter(26, 1, "letters")).toBe("ba");
    expect(formatCounter(1, 2, "letters")).toBe("ab");
  });
});
