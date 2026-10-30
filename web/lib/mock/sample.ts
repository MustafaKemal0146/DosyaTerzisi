import type { MockEntry } from "./engine";

const root = "C:\\Users\\Musa\\Belgeler\\Akreditasyon";

const day = 24 * 60 * 60 * 1000;
const base = new Date(2026, 9, 20, 10, 30).getTime();

type Spec = [name: string, kb: number, ageDays: number];

/** Hocanın eski günlüğündeki gibi adlar: Türkçe harfler, " - " ayraçları, kopya numaraları. */
const files: Spec[] = [
  ["Ek I-4.9.1 - Karar Alma Süreçleri ve Organizasyon Şeması.pdf", 812, 3],
  ["Ek I-4.9.2 - Komisyon ve Koordinatörlükler.pdf", 420, 3],
  ["Ek I-4.9.3 - Akademik Genel Kurul Tutanağı.docx", 96, 5],
  ["Ek I-4.9.4 - Bölüm Kurulu Tutanağı.docx", 88, 5],
  ["Ek I-4.9.5 - Danışma Kurulu.pdf", 1204, 8],
  ["Öğrenci Şeması (2).xlsx", 54, 12],
  ["Öğrenci Şeması (3).xlsx", 54, 11],
  ["Çalışma Planı - Güncel.docx", 143, 1],
  ["Çalışma Planı - Eski.docx", 139, 40],
  ["ders programı.xlsx", 77, 14],
  ["Rapor Taslağı.docx", 211, 2],
  ["rapor-taslagi-final.docx", 215, 1],
  ["IMG_20260814_143005.jpg", 3480, 70],
  ["IMG_20260814_143112.jpg", 3512, 70],
  ["IMG_20260815_091544.jpg", 2987, 69],
  ["Shortcut to Dekanlık.lnk", 2, 90],
  ["Copy (2) of Toplantı Notları.txt", 4, 21],
  ["Toplantı Notları.txt", 4, 30],
  ["Şarkı Bitti - Sezen Aksu.mp3", 4210, 100],
  ["Gülümse - Sezen Aksu.mp3", 3987, 100],
  ["İstanbul'da Bir Gün.pdf", 640, 18],
  ["Yıllık Faaliyet Raporu 2025.pdf", 2310, 60],
  ["Yıllık Faaliyet Raporu 2024.pdf", 2144, 400],
  ["özet.txt", 1, 9],
];

const folders = ["KANIT", "Ekler", "Fotoğraflar", "Arşiv 2025"];

export function sampleEntries(): MockEntry[] {
  let id = 0;
  const entries: MockEntry[] = [];
  for (const name of folders)
    entries.push({ id: id++, name, dir: root, isDir: true, size: 0, modified: base - 7 * day, created: base - 30 * day });
  for (const [name, kb, age] of files)
    entries.push({ id: id++, name, dir: root, isDir: false, size: kb * 1024, modified: base - age * day, created: base - (age + 5) * day });

  // Bir alt klasör: "alt klasörler" seçeneğini denemek için.
  const sub = `${root}\\KANIT`;
  const subFiles: Spec[] = [
    ["Ek I-4.9.6 - Yönerge.pdf", 330, 4],
    ["Ek I-4.9.7 - Süreç Haritası.pdf", 275, 4],
    ["Şablon (1).docx", 40, 20],
  ];
  for (const [name, kb, age] of subFiles)
    entries.push({ id: id++, name, dir: sub, isDir: false, size: kb * 1024, modified: base - age * day, created: base - (age + 5) * day });

  return entries;
}

/** Yüzlerce/binlerce dosyalık bir klasör, listenin akıcılığını denemek için (`?big=5000`). */
export function bigEntries(count: number): MockEntry[] {
  const entries: MockEntry[] = [];
  for (let i = 0; i < count; i++) {
    const n = String(i + 1).padStart(5, "0");
    entries.push({
      id: i,
      name: i % 7 === 0 ? `Fotoğraf Çekimi ${n} - Şubat.jpg` : `Belge ${n} (kopya).docx`,
      dir: `${root}\\Büyük`,
      isDir: false,
      size: 20_000 + ((i * 7919) % 5_000_000),
      modified: base - ((i * 31) % 500) * day,
      created: base - ((i * 31) % 500 + 3) * day,
    });
  }
  return entries;
}

export const sampleRoot = root;
