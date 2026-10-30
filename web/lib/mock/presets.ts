import type { BuiltInPreset } from "../contract";

// Hazır ayarlar (OnAyarlar.txt ile aynı). Gerçek uygulamada C# tarafı verir.
export const builtInPresets: BuiltInPreset[] = [
  { id: "p1", title: "Tarih ve saat", rule: { method: "wildcard", search: "*.*", replace: "\\Y\\m\\d\\H\\M\\S.\\2", caseSensitive: false } },
  { id: "p2", title: "Sıra no + uzantı", rule: { method: "wildcard", search: "*.*", replace: "??.\\2", caseSensitive: false } },
  { id: "p3", title: "Sıra no + ad + uzantı", rule: { method: "wildcard", search: "*.*", replace: "??_\\1.\\2", caseSensitive: false } },
  { id: "p4", title: "Ad + sıra no + uzantı", rule: { method: "wildcard", search: "*.*", replace: "\\1_??.\\2", caseSensitive: false } },
  { id: "p5", title: "“-” işaretinin iki yanını değiştir", rule: { method: "wildcard", search: "*-*.*", replace: "\\2-\\1.\\3", caseSensitive: false } },
  { id: "p6", title: "“DSC” yerine “IMG” yaz", rule: { method: "wildcard", search: "*DSC*", replace: "\\1IMG\\2", caseSensitive: false } },
  { id: "p8", title: "(n) sil", rule: { method: "regex", search: "(.*)\\(\\d+\\)(.*)", replace: "\\1\\2", caseSensitive: false } },
  { id: "p9", title: "[n] sil", rule: { method: "regex", search: "(.*)\\[\\d+\\](.*)", replace: "\\1\\2", caseSensitive: false } },
  { id: "p10", title: "Uzantıyı küçük harf yap", rule: { method: "regex", search: "(\\..*)$", replace: "\\L\\1", caseSensitive: false } },
  { id: "p11", title: "“ - Kopya” ibaresini sil", rule: { method: "regex", search: "(?: - Kopya(?: \\(\\d+\\))?)", replace: "", caseSensitive: false } },
  { id: "p12", title: "Her kelimenin ilk harfi büyük", rule: { method: "regex", search: "((?:[^ \\-+~=#$%&_,.(){}\\[\\]=!]|\\.)+)", replace: "\\L\\u\\1", caseSensitive: false } },
  { id: "p13", title: "Büyük/küçük harfi tersine çevir", rule: { method: "translation", search: "a-zA-Z", replace: "A-Za-z", caseSensitive: false } },
  { id: "p14", title: "Büyük harf", rule: { method: "translation", search: "a-z", replace: "A-Z", caseSensitive: false } },
  { id: "p15", title: "Küçük harf", rule: { method: "translation", search: "A-Z", replace: "a-z", caseSensitive: false } },
  { id: "p16", title: "Türkçe harfleri sadeleştir", rule: { method: "translation", search: "çğıöşüÇĞİÖŞÜ", replace: "cgiosuCGIOSU", caseSensitive: false } },
  { id: "p17", title: "Boşlukları alt çizgi yap", rule: { method: "translation", search: " ", replace: "_", caseSensitive: false } },
];
