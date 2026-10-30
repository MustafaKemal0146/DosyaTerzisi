/** Görünüm tercihleri: yazı tipi, yazı boyutu, hangi şeritlerin görüneceği. Bu bilgisayarda saklanır. */

export const fonts = {
  default: { label: "Windows varsayılanı", css: '"Segoe UI Variable Text", "Segoe UI", system-ui, -apple-system, "Helvetica Neue", Arial, sans-serif' },
  segoe: { label: "Segoe UI", css: '"Segoe UI", system-ui, sans-serif' },
  calibri: { label: "Calibri", css: 'Calibri, "Segoe UI", sans-serif' },
  arial: { label: "Arial", css: "Arial, Helvetica, sans-serif" },
  verdana: { label: "Verdana (geniş, okunaklı)", css: "Verdana, Geneva, sans-serif" },
  georgia: { label: "Georgia (tırnaklı)", css: 'Georgia, "Times New Roman", serif' },
} as const;

export const sizes = {
  small: { label: "Küçük", zoom: 0.9 },
  normal: { label: "Normal", zoom: 1 },
  large: { label: "Büyük", zoom: 1.15 },
  xlarge: { label: "Çok büyük", zoom: 1.3 },
} as const;

export type FontChoice = keyof typeof fonts;
export type SizeChoice = keyof typeof sizes;

export interface Prefs {
  font: FontChoice;
  size: SizeChoice;
  /** "Listele / Filtre / Alt klasörler" şeridi. */
  showFilters: boolean;
  /** Alttaki öğe sayıları ve durum özeti. */
  showSummary: boolean;
}

export const defaultPrefs: Prefs = { font: "default", size: "normal", showFilters: true, showSummary: true };

const KEY = "dosyaterzisi.prefs.v1";

export function loadPrefs(): Prefs {
  try {
    const raw = localStorage.getItem(KEY);
    if (!raw) return defaultPrefs;
    const saved = JSON.parse(raw) as Partial<Prefs>;
    return {
      font: saved.font && saved.font in fonts ? saved.font : defaultPrefs.font,
      size: saved.size && saved.size in sizes ? saved.size : defaultPrefs.size,
      showFilters: saved.showFilters ?? true,
      showSummary: saved.showSummary ?? true,
    };
  } catch {
    return defaultPrefs; // Depolama kapalı ya da bozuk: varsayılanlarla devam.
  }
}

export function savePrefs(prefs: Prefs) {
  try {
    localStorage.setItem(KEY, JSON.stringify(prefs));
  } catch {
    // Kaydedilemedi: bu oturumda geçerli kalır.
  }
}

/** Tercihleri sayfaya uygular. */
export function applyPrefs(prefs: Prefs, doc: Document | undefined = typeof document === "undefined" ? undefined : document) {
  if (!doc) return; // Sunucuda ve testlerde sayfa yok.
  const root = doc.documentElement;
  root.style.setProperty("--font", fonts[prefs.font].css);
  root.style.setProperty("--ui-zoom", String(sizes[prefs.size].zoom));
  root.toggleAttribute("data-hide-filters", !prefs.showFilters);
  root.toggleAttribute("data-hide-summary", !prefs.showSummary);
}

/** Dosya listesi görünümü: ek etiket sütunları ve gruplama. Bu bilgisayarda saklanır. */
export interface ListView {
  tagColumns: string[];
  /** "none" | "folder" | "date" | "ext" | "tag:<ad>" */
  groupMode: string;
}

export const defaultListView: ListView = { tagColumns: [], groupMode: "none" };

const VIEW_KEY = "dosyaterzisi.listview.v1";

export function loadListView(): ListView {
  try {
    const raw = localStorage.getItem(VIEW_KEY);
    if (!raw) return defaultListView;
    const saved = JSON.parse(raw) as Partial<ListView>;
    const tagColumns = Array.isArray(saved.tagColumns) ? saved.tagColumns.filter((n): n is string => typeof n === "string" && n.length > 0) : [];
    let groupMode = typeof saved.groupMode === "string" ? saved.groupMode : "none";
    const valid = groupMode === "none" || groupMode === "folder" || groupMode === "date" || groupMode === "ext" || (groupMode.startsWith("tag:") && tagColumns.includes(groupMode.slice(4)));
    if (!valid) groupMode = "none";
    return { tagColumns, groupMode };
  } catch {
    return defaultListView;
  }
}

export function saveListView(view: ListView) {
  try {
    localStorage.setItem(VIEW_KEY, JSON.stringify(view));
  } catch {
    // Kaydedilemedi: bu oturumda geçerli kalır.
  }
}
