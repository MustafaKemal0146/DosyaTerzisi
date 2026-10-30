// Tarayıcıda denemek için küçük bir yeniden adlandırma motoru.
// Gerçek motor C# tarafındadır (DosyaTerzisi.Core); bu yalnızca sahte köprünün önizleme üretmesi içindir.

import type { PreviewRow, Rule } from "../contract";

export interface MockEntry {
  id: number;
  name: string;
  dir: string;
  isDir: boolean;
  size: number;
  modified: number;
  created: number;
  accessed?: number;
  attrs?: { readOnly: boolean; archive: boolean; hidden: boolean; system: boolean };
}

/** Dosyanın etiketleri: `ID3.Artist` → değer. Tarih ve sayı varsa biçimlendirme için ayrıca tutulur. */
export type MockTags = Map<string, { text: string; date?: Date; number?: number }>;

const illegal = /[\\/:*?"<>|]/;
const reserved = /^(con|prn|aux|nul|com[1-9]|lpt[1-9])$/i;

export function checkName(name: string): string | null {
  if (!name.trim()) return "Ad boş.";
  if (name.length > 255) return "Ad 255 karakterden uzun.";
  if (illegal.test(name)) return "Ad şunları içeremez: \\ / : * ? \" < > |";
  if (/[. ]$/.test(name)) return "Ad nokta ya da boşlukla bitemez.";
  if (reserved.test(name.split(".")[0]!.trimEnd())) return "Ayrılmış bir Windows adı.";
  return null;
}

export function titleOf(entry: MockEntry): string {
  const dot = entry.name.lastIndexOf(".");
  return entry.isDir || dot <= 0 ? entry.name : entry.name.slice(0, dot);
}

export function extensionOf(entry: MockEntry): string {
  const dot = entry.name.lastIndexOf(".");
  return entry.isDir || dot <= 0 ? "" : entry.name.slice(dot + 1);
}

function expandRanges(list: string): string[] {
  const chars = [...list];
  const result: string[] = [];
  for (let i = 0; i < chars.length; i++) {
    const from = chars[i]!;
    if (chars[i + 1] === "-" && chars[i + 2] !== undefined && chars[i + 2]! >= from) {
      for (let c = from.codePointAt(0)!; c <= chars[i + 2]!.codePointAt(0)!; c++) result.push(String.fromCodePoint(c));
      i += 2;
    } else {
      result.push(from);
    }
  }
  return result;
}

function wildcardToRegex(pattern: string, caseSensitive: boolean): RegExp {
  const source = [...pattern]
    .map((c) => (c === "*" ? "(.*)" : c === "?" ? "(.)" : c.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")))
    .join("");
  return new RegExp(`^${source}$`, caseSensitive ? "s" : "si");
}

/** `?` sayacının yazılışı: ondalık, onaltılık (1F) ya da harf (a, b, ... z, ba; a = 0). */
export function formatCounter(number: number, width: number, style: "decimal" | "hexadecimal" | "letters"): string {
  if (style === "hexadecimal") return number.toString(16).toUpperCase().padStart(width, "0");
  if (style === "letters") {
    let n = Math.abs(number);
    let text = "";
    do {
      text = String.fromCharCode(97 + (n % 26)) + text;
      n = Math.floor(n / 26);
    } while (n > 0);
    return (number < 0 ? "-" : "") + text.padStart(width, "a");
  }
  return String(number).padStart(width, "0");
}

const pad2 = (n: number) => String(n).padStart(2, "0");

/** `yyyyMMdd_HHmmss` gibi bir biçimle tarih yazar (C# tarafındaki biçimlerin küçük bir kısmı). */
function formatDate(date: Date, format: string): string {
  return format
    .replace(/yyyy/g, String(date.getFullYear()))
    .replace(/yy/g, pad2(date.getFullYear() % 100))
    .replace(/MM/g, pad2(date.getMonth() + 1))
    .replace(/dd/g, pad2(date.getDate()))
    .replace(/HH/g, pad2(date.getHours()))
    .replace(/mm/g, pad2(date.getMinutes()))
    .replace(/ss/g, pad2(date.getSeconds()));
}

/** Dosya adında olamayacak karakterleri alt çizgi yapar ("AC/DC" → "AC_DC"). */
function cleanForFileName(text: string): string {
  return text.replace(/[\\/:*?"<>|\u0000-\u001f]/g, "_").trim().replace(/\.+$/, "");
}

function tagText(tags: MockTags, missing: Set<string>, name: string, format: string | undefined): string {
  const tag = [...tags].find(([key]) => key.toLowerCase() === name.toLowerCase())?.[1];
  if (!tag) {
    missing.add(name);
    return "";
  }
  let text = tag.text;
  if (format && tag.date) text = formatDate(tag.date, format);
  else if (format && tag.number !== undefined) text = String(tag.number).padStart(format.length, "0");
  else if (tag.date) text = formatDate(tag.date, "yyyy-MM-dd HH.mm.ss");
  return cleanForFileName(text);
}

function expand(
  template: string,
  entry: MockEntry,
  groups: string[],
  number: number,
  rule: { useCreatedDate: boolean; numberStyle?: "decimal" | "hexadecimal" | "letters" },
  tags?: MockTags,
  missing?: Set<string>,
): string {
  const date = new Date(rule.useCreatedDate ? entry.created : entry.modified);
  const two = (n: number) => String(n).padStart(2, "0");
  let out = "";
  let span: "U" | "L" | null = null;
  let once: "u" | "l" | null = null;

  const emit = (text: string) => {
    for (const ch of text) {
      const mode = once ?? span;
      once = null;
      out += mode === "U" || mode === "u" ? ch.toLocaleUpperCase("tr-TR") : mode === "L" || mode === "l" ? ch.toLocaleLowerCase("tr-TR") : ch;
    }
  };

  const folderName = (dir: string) => dir.split(/[\\/]/).filter(Boolean).at(-1) ?? "";
  const parentName = (dir: string) => dir.split(/[\\/]/).filter(Boolean).at(-2) ?? "";

  for (let i = 0; i < template.length; i++) {
    const c = template[i]!;
    if (c === "?") {
      let end = i;
      while (template[end] === "?") end++;
      emit(formatCounter(number, end - i, rule.numberStyle ?? "decimal"));
      i = end - 1;
    } else if (c === "*") {
      let end = i;
      while (template[end] === "*") end++;
      const width = Math.min(end - i, 9);
      let h = 2166136261;
      for (const ch of `${entry.dir}\\${entry.name}|${i}`.toLowerCase()) h = Math.imul(h ^ ch.charCodeAt(0), 16777619);
      emit(String((h >>> 0) % 10 ** width).padStart(width, "0"));
      i = end - 1;
    } else if (c === "<" && tags && missing) {
      const end = template.indexOf(">", i + 1);
      const inner = end < 0 ? "" : template.slice(i + 1, end);
      const [name = "", ...rest] = inner.split(":");
      if (end >= 0 && /^[A-Za-z0-9]+\.[A-Za-z0-9-]+$/.test(name)) {
        emit(tagText(tags, missing, name, rest.length > 0 ? rest.join(":") : undefined));
        i = end;
      } else {
        emit(c);
      }
    } else if (c === "\\" && i + 1 < template.length) {
      const code = template[++i]!;
      if (code >= "1" && code <= "9") emit(groups[Number(code)] ?? "");
      else
        switch (code) {
          case "j": emit(String(Math.floor((Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()) - Date.UTC(date.getFullYear(), 0, 0)) / 86_400_000)).padStart(3, "0")); break;
          case "w": emit(String(date.getDay())); break;
          case "#": {
            const next = template[i + 1];
            const part = next === "d" ? date.getDate() : next === "m" ? date.getMonth() + 1 : next === "H" ? date.getHours() : next === "M" ? date.getMinutes() : next === "S" ? date.getSeconds() : undefined;
            if (part === undefined) emit("#");
            else {
              emit(String(part));
              i++;
            }
            break;
          }
          case "Q": {
            const stop = template.indexOf("\\E", i + 1);
            emit(stop < 0 ? template.slice(i + 1) : template.slice(i + 1, stop));
            i = stop < 0 ? template.length : stop + 1;
            break;
          }
          case "0": emit(entry.name); break;
          case "t": emit(titleOf(entry)); break;
          case "e": emit(extensionOf(entry)); break;
          case "f": emit(folderName(entry.dir)); break;
          case "F": emit(parentName(entry.dir)); break;
          case "Y": emit(String(date.getFullYear())); break;
          case "y": emit(two(date.getFullYear() % 100)); break;
          case "m": emit(two(date.getMonth() + 1)); break;
          case "d": emit(two(date.getDate())); break;
          case "H": emit(two(date.getHours())); break;
          case "M": emit(two(date.getMinutes())); break;
          case "S": emit(two(date.getSeconds())); break;
          case "p": emit(date.getHours() < 12 ? "AM" : "PM"); break;
          case "I": emit(two(date.getHours() % 12 || 12)); break;
          case "a": emit(date.toLocaleDateString("tr-TR", { weekday: "short" })); break;
          case "A": emit(date.toLocaleDateString("tr-TR", { weekday: "long" })); break;
          case "b": emit(date.toLocaleDateString("tr-TR", { month: "short" })); break;
          case "B": emit(date.toLocaleDateString("tr-TR", { month: "long" })); break;
          case ";": emit(entry.size.toLocaleString("tr-TR")); break;
          case ":": emit(String(entry.size)); break;
          case "P": emit(entry.dir.replace(/[\\/:*?"<>|]/g, "_")); break;
          case "u": once = "u"; break;
          case "l": once = "l"; break;
          case "U": span = "U"; break;
          case "L": span = "L"; break;
          case "E": span = null; once = null; break;
          default: emit(code);
        }
    } else {
      emit(c);
    }
  }
  return out;
}

class RuleError extends Error {}

function compile(rule: Rule): (entry: MockEntry, number: number) => string {
  const flags = rule.caseSensitive ? "sg" : "sgi";

  if (rule.method === "translation") {
    const from = expandRanges(rule.search);
    const to = expandRanges(rule.replace);
    const map = new Map<string, string>();
    from.slice(0, Math.min(from.length, to.length)).forEach((ch, i) => map.has(ch) || map.set(ch, to[i]!));
    return (entry) => [...titleOf(entry)].map((ch) => map.get(ch) ?? ch).join("") + entry.name.slice(titleOf(entry).length);
  }

  if (rule.method === "wildcard") {
    const regex = wildcardToRegex(rule.search, rule.caseSensitive);
    return (entry, number) => {
      const match = regex.exec(entry.name);
      return match ? expand(rule.replace, entry, [...match], number, rule) : entry.name;
    };
  }

  let regex: RegExp;
  try {
    // .NET'te olan `(?<ad>...)` ve `(?:...)` JavaScript'te de çalışır; %%metin%% işaretleri yok sayılır.
    regex = new RegExp(rule.search.replaceAll("%%", ""), flags);
  } catch (error) {
    throw new RuleError(error instanceof Error ? error.message : "Düzenli ifade geçersiz.");
  }
  return (entry, number) => entry.name.replace(regex, (...args) => {
    const groups = args.slice(0, args.findIndex((a) => typeof a === "number")) as string[];
    return expand(rule.replace, entry, groups, number, rule);
  });
}

export interface PlanResult {
  rows: PreviewRow[];
  ready: number;
  problems: number;
  ruleError: string;
}

/**
 * `targets` adlandırılacak satırlardır; `all` listedeki herkestir (hedef olmayanlar "değişmedi" görünür).
 */
/** `typed`: elle yazılan adlar (satır numarası → ad); kuralın sonucunun yerine geçer. */
export function plan(
  all: MockEntry[],
  targets: MockEntry[],
  rule: Rule,
  autoNumber = rule.autoNumberOnConflict,
  typed?: ReadonlyMap<number, string>,
): PlanResult {
  let apply: ReturnType<typeof compile>;
  try {
    apply = compile(rule);
  } catch (error) {
    const message = error instanceof Error ? error.message : "Kural geçersiz.";
    const rows = all.map((e): PreviewRow =>
      targets.includes(e) ? { id: e.id, newName: e.name, status: "error", message, destination: null } : { id: e.id, newName: e.name, status: "unchanged", message: null, destination: null });
    return { rows, ready: 0, problems: targets.length, ruleError: message };
  }

  return planWith(all, targets, rule, (entry, number) => ({ name: typed?.get(entry.id) ?? apply(entry, number) }), autoNumber);
}

export interface Proposal {
  name: string;
  /** Adın ihtiyaç duyduğu ama dosyada olmayan etiketler. Doluysa dosya atlanır. */
  missing?: string[];
}

/** Ad üretme yöntemi ne olursa olsun aynı numaralandırma, doğrulama ve çakışma kuralları uygulanır. */
export function planWith(
  all: MockEntry[],
  targets: MockEntry[],
  numbering: { numberStart: number; numberStep: number; resetNumberPerFolder: boolean },
  propose: (entry: MockEntry, number: number) => Proposal,
  autoNumber: boolean,
): PlanResult {
  const result = new Map<number, PreviewRow>();
  all.forEach((e) => result.set(e.id, { id: e.id, newName: e.name, status: "unchanged", message: null, destination: null }));

  const counters = new Map<string, number>();
  let overall = 0;
  const proposals = targets.map((entry) => {
    let index: number;
    if (numbering.resetNumberPerFolder) {
      index = counters.get(entry.dir.toLowerCase()) ?? 0;
      counters.set(entry.dir.toLowerCase(), index + 1);
    } else index = overall++;
    return { entry, ...propose(entry, numbering.numberStart + index * numbering.numberStep) };
  });

  // Çakışma: aynı klasörde aynı ad (büyük/küçük harf fark etmez). Adı değişmeyenler yerinde kalır.
  const taken = new Map<string, Set<string>>();
  const folder = (dir: string) => {
    const key = dir.toLowerCase();
    if (!taken.has(key)) taken.set(key, new Set(all.filter((e) => e.dir.toLowerCase() === key).map((e) => e.name.toLowerCase())));
    return taken.get(key)!;
  };
  for (const p of proposals) if (p.name !== p.entry.name && !(p.missing && p.missing.length > 0)) folder(p.entry.dir).delete(p.entry.name.toLowerCase());

  let ready = 0;
  let problems = 0;
  for (const { entry, name, missing } of proposals) {
    const set = folder(entry.dir);
    if (missing && missing.length > 0) {
      result.set(entry.id, { id: entry.id, newName: entry.name, status: "missingTag", message: `Etiket yok: ${missing.join(", ")}`, destination: null });
      set.add(entry.name.toLowerCase());
      continue;
    }
    if (name === entry.name) {
      set.add(name.toLowerCase());
      continue;
    }
    const invalid = checkName(name);
    if (invalid) {
      result.set(entry.id, { id: entry.id, newName: name, status: "invalid", message: invalid, destination: null });
      problems++;
      continue;
    }
    let final = name;
    if (set.has(final.toLowerCase()) && autoNumber) {
      const dot = final.lastIndexOf(".");
      const title = entry.isDir || dot <= 0 ? final : final.slice(0, dot);
      const ext = entry.isDir || dot <= 0 ? "" : final.slice(dot);
      for (let n = 1; set.has(final.toLowerCase()); n++) final = `${title}(${String(n).padStart(2, "0")})${ext}`;
    }
    if (set.has(final.toLowerCase())) {
      result.set(entry.id, { id: entry.id, newName: name, status: "conflict", message: "Bu ad zaten kullanılıyor.", destination: null });
      problems++;
    } else {
      set.add(final.toLowerCase());
      result.set(entry.id, { id: entry.id, newName: final, status: "ready", message: null, destination: null });
      ready++;
    }
  }

  return { rows: all.map((e) => result.get(e.id)!), ready, problems, ruleError: "" };
}

/** Etiketten adlandırma: şablondaki `<ID3.Artist>` gibi parçaları dosyanın etiketleriyle doldurur. */
export function planTags(
  all: MockEntry[],
  targets: MockEntry[],
  options: { template: string; missing: "skip" | "useEmpty"; numberStart: number; numberStep: number; resetNumberPerFolder: boolean; useCreatedDate: boolean },
  tagsOf: (entry: MockEntry) => MockTags,
  autoNumber: boolean,
): PlanResult {
  return planWith(
    all,
    targets,
    options,
    (entry, number) => {
      const missing = new Set<string>();
      const name = expand(options.template, entry, [], number, options, tagsOf(entry), missing);
      return { name, missing: options.missing === "skip" ? [...missing].sort() : [] };
    },
    autoNumber,
  );
}
