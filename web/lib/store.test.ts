import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MockBridge } from "./mock/mockBridge";
import { validated } from "./wire";
import { AppStore } from "./store";

async function settle(ms = 600) {
  await vi.advanceTimersByTimeAsync(ms);
}

/** Pencere açılınca otomatik cevap veren ekran. */
function autoAnswer(store: AppStore, answers: Record<string, unknown>) {
  store.subscribe(() => {
    const dialog = store.getState().dialog;
    if (dialog && dialog.kind in answers) queueMicrotask(() => store.answer(answers[dialog.kind]));
  });
}

let store: AppStore;

beforeEach(async () => {
  vi.useFakeTimers();
  // Her çağrının parametresi ve cevabı sözleşmeye göre denetlenir.
  store = new AppStore(validated(new MockBridge()));
  const started = store.start();
  await settle();
  await started;
});

afterEach(() => vi.useRealTimers());

describe("AppStore", () => {
  it("açılışta klasörü listeler ve önizleme hesaplar", () => {
    const s = store.getState();
    expect(s.ready).toBe(true);
    expect(s.rows.length).toBeGreaterThan(20);
    expect(s.previews).toHaveLength(s.rows.length);
    expect(s.favorites.length).toBeGreaterThan(0);
    expect(s.presets.length).toBeGreaterThan(5);
    expect(s.readyCount).toBeGreaterThan(0);
  });

  it("kuralı değiştirince önizleme güncellenir (gecikmeli, tek seferde)", async () => {
    store.setRule({ method: "wildcard", search: "* - *", replace: "\\1-\\2" });
    store.setRule({ replace: "\\2-\\1" });
    await settle();

    const row = store.getState().rows.findIndex((r) => r.name.startsWith("Çalışma Planı - Güncel"));
    expect(store.getState().previews[row]!.newName).toBe("Güncel.docx-Çalışma Planı");
  });

  it("bozuk düzenli ifade hata gösterir", async () => {
    store.setRule({ method: "regex", search: "(" });
    await settle();

    expect(store.getState().ruleError).not.toBe("");
    expect(store.getState().readyCount).toBe(0);
  });

  it("seçim varsa yalnızca seçilenler adlandırılır", async () => {
    store.setRule({ method: "wildcard", search: "*", replace: "x_\\1" });
    await settle();
    expect(store.getState().readyCount).toBe(store.getState().rows.length);

    store.setSelection(new Set([store.getState().rows[0]!.id, store.getState().rows[1]!.id]));
    await settle();

    expect(store.getState().readyCount).toBe(2);
    expect(store.getState().previews[2]!.status).toBe("unchanged");
  });

  it("yeniden adlandırma: onay penceresi, sonra liste ve geçmiş yenilenir; geri alınca eski hâl döner", async () => {
    autoAnswer(store, { confirm: "proceed" });
    store.setRule({ method: "wildcard", search: "*.*", replace: "yeni_\\1.\\2" });
    await settle();
    const before = store.getState().rows.map((r) => r.name);

    const renaming = store.rename();
    await settle(1000);
    await renaming;

    let s = store.getState();
    expect(s.rows.every((r) => r.name.startsWith("yeni_"))).toBe(true);
    expect(s.history).toHaveLength(1);
    expect(s.history[0]!.count).toBeGreaterThan(0);
    expect(s.toasts.at(-1)!.text).toContain("yeniden adlandırıldı");

    const undoing = store.undo();
    await settle(1000);
    await undoing;

    s = store.getState();
    expect(s.rows.map((r) => r.name)).toEqual(before);
    expect(s.history).toHaveLength(0);
  });

  it("onay penceresinde iptal edilirse hiçbir şey değişmez", async () => {
    autoAnswer(store, { confirm: "cancel" });
    store.setRule({ method: "wildcard", search: "*", replace: "x_\\1" });
    await settle();

    const renaming = store.rename();
    await settle(1000);
    await renaming;

    expect(store.getState().rows.some((r) => r.name.startsWith("x_"))).toBe(false);
    expect(store.getState().history).toHaveLength(0);
  });

  it("çakışma varsa pencere çakışanları listeler; numara ekleme hepsini adlandırır", async () => {
    let seen = 0;
    store.subscribe(() => {
      const d = store.getState().dialog;
      if (d?.kind === "confirm") {
        seen = d.request.conflicts.length;
        queueMicrotask(() => store.answer("autoNumber"));
      }
    });
    store.setRule({ method: "wildcard", search: "*.*", replace: "ayni.\\2" });
    await settle();
    expect(store.getState().problemCount).toBeGreaterThan(0);

    const renaming = store.rename();
    await settle(1000);
    await renaming;

    expect(seen).toBeGreaterThan(0);
    const names = store.getState().rows.map((r) => r.name);
    expect(new Set(names.map((n) => n.toLowerCase())).size).toBe(names.length);
    expect(names).toContain("ayni.pdf");
    expect(names).toContain("ayni(01).pdf");
  });

  it("favori ekleme: ad sorulur, eklenir, taşınır, silinir", async () => {
    autoAnswer(store, { prompt: "Benim kuralım" });
    store.setRule({ method: "regex", search: "\\d+", replace: "#" });

    const adding = store.addFavorite();
    await settle();
    await adding;

    let favorites = store.getState().favorites;
    expect(favorites.at(-1)!.name).toBe("Benim kuralım");
    expect(favorites.at(-1)!.rule).toMatchObject({ method: "regex", search: "\\d+", replace: "#" });

    await store.moveFavorite(favorites.at(-1)!.id, -1);
    favorites = store.getState().favorites;
    expect(favorites.at(-2)!.name).toBe("Benim kuralım");

    await store.removeFavorite(favorites.at(-2)!.id);
    expect(store.getState().favorites.some((f) => f.name === "Benim kuralım")).toBe(false);
  });

  it("favori adı sorusu iptal edilirse eklenmez", async () => {
    const count = store.getState().favorites.length;
    autoAnswer(store, { prompt: null });

    const adding = store.addFavorite();
    await settle();
    await adding;

    expect(store.getState().favorites).toHaveLength(count);
  });

  it("ön ayar uygulanınca kural alanları dolar", async () => {
    const preset = store.getState().presets.find((p) => p.title === "(n) sil")!;
    store.applySavedRule(preset.rule);
    await settle();

    expect(store.getState().rule).toMatchObject({ method: "regex", search: preset.rule.search });
    const row = store.getState().rows.findIndex((r) => r.name === "Öğrenci Şeması (2).xlsx");
    expect(store.getState().previews[row]!.newName).toBe("Öğrenci Şeması .xlsx");
  });

  it("listeleme seçenekleri listeyi yeniler", async () => {
    const all = store.getState().rows.length;

    store.setListing({ filter: "*.pdf" });
    await settle();
    const pdfs = store.getState().rows;
    expect(pdfs.length).toBeGreaterThan(0);
    expect(pdfs.length).toBeLessThan(all);
    expect(pdfs.every((r) => r.name.toLowerCase().endsWith(".pdf"))).toBe(true);

    store.setListing({ filter: "*", recursive: true, depth: 3 });
    await settle();
    expect(store.getState().rows.length).toBeGreaterThan(all);

    store.setListing({ includeFiles: false, includeFolders: true, recursive: false });
    await settle();
    expect(store.getState().rows.every((r) => r.isDir)).toBe(true);
  });

  it("eski bir cevap yenisinin üzerine yazılmaz", async () => {
    store.setRule({ method: "wildcard", search: "*", replace: "a_\\1" });
    const first = store.preview();
    store.setRule({ replace: "b_\\1" });
    await settle();
    await first;

    expect(store.getState().previews[0]!.newName.startsWith("b_")).toBe(true);
  });
});

async function run(action: () => Promise<void>) {
  const done = action();
  await settle();
  await done;
}

describe("klasör gezintisi", () => {
  it("üst klasör yolu bulunur", async () => {
    const { parentFolder, joinPath } = await import("./store");
    expect(parentFolder("C:\\Belgeler\\Resimler")).toBe("C:\\Belgeler");
    expect(parentFolder("C:\\Belgeler")).toBe("C:\\");
    expect(parentFolder("C:\\")).toBeNull();
    expect(parentFolder("")).toBeNull();
    expect(joinPath("C:\\Belgeler", "Alt")).toBe("C:\\Belgeler\\Alt");
    expect(joinPath("C:\\", "Alt")).toBe("C:\\Alt");
  });

  it("geri ve ileri önceki klasörlere döner", async () => {
    const first = store.getState().folder;
    expect(store.getState().canBack).toBe(false);

    await run(() => store.openFolder(first + "\\Alt"));
    expect(store.getState().canBack).toBe(true);

    await run(() => store.goBack());
    expect(store.getState().folder).toBe(first);
    expect(store.getState().canForward).toBe(true);

    await run(() => store.goForward());
    expect(store.getState().folder).toBe(first + "\\Alt");
    expect(store.getState().canForward).toBe(false);
  });

  it("yeni klasör açınca ileri listesi silinir", async () => {
    const first = store.getState().folder;
    await run(() => store.openFolder(first + "\\A"));
    await run(() => store.goBack());
    await run(() => store.openFolder(first + "\\B"));
    expect(store.getState().canForward).toBe(false);
  });
});

describe("liste düzenleme", () => {
  it("seçimi ters çevirir", async () => {
    const all = store.getState().rows.map((r) => r.id);
    store.setSelection(new Set([all[0]!, all[1]!]));
    store.invertSelection();
    const picked = store.getState().selection;
    expect(picked.size).toBe(all.length - 2);
    expect(picked.has(all[0]!)).toBe(false);
    expect(picked.has(all[2]!)).toBe(true);
  });

  it("elle yazılan ad önizlemeye girer, boşaltılınca kalkar", async () => {
    const id = store.getState().rows.findIndex((r) => r.name.endsWith(".docx"));
    store.editName(id, "Benim adım.docx");
    await settle();
    expect(store.getState().previews[id]!.newName).toBe("Benim adım.docx");
    expect(store.getState().overrides[id]).toBe("Benim adım.docx");

    store.editName(id, "");
    await settle();
    expect(store.getState().overrides[id]).toBeUndefined();
  });

  it("seçili satırlar listeden çıkar ve elle adlar yeni numaralara taşınır", async () => {
    const rows = store.getState().rows;
    const keep = rows[5]!;
    store.editName(keep.id, "kalan.txt");
    store.setSelection(new Set([rows[0]!.id, rows[1]!.id]));
    await run(() => store.removeSelected());

    const now = store.getState();
    expect(now.rows.length).toBe(rows.length - 2);
    const moved = now.rows.find((r) => r.name === keep.name && r.dir === keep.dir)!;
    expect(now.overrides[moved.id]).toBe("kalan.txt");
    expect(now.selection.size).toBe(0);
  });
});

describe("Gezgin menüleri", () => {
  it("ayarlar açılınca durum okunur, anahtar değişince kaydedilir", async () => {
    const opened = store.showSettings();
    await settle();
    expect(store.getState().dialog?.kind).toBe("settings");
    expect(store.getState().integration).toEqual({ available: true, folderMenu: false, sendTo: false });

    await run(() => store.setIntegration({ folderMenu: true }));
    expect(store.getState().integration).toEqual({ available: true, folderMenu: true, sendTo: false });

    store.answer(undefined);
    await opened;
  });
});

describe("görünüm tercihleri", () => {
  it("değişir, kaydedilir ve sıfırlanır", () => {
    const stored: Record<string, string> = {};
    vi.stubGlobal("localStorage", { getItem: (k: string) => stored[k] ?? null, setItem: (k: string, v: string) => { stored[k] = v; } });
    vi.stubGlobal("document", { documentElement: { style: { setProperty() {} }, toggleAttribute() {} } });

    store.setPrefs({ font: "verdana", size: "large", showFilters: false });
    expect(store.getState().prefs).toMatchObject({ font: "verdana", size: "large", showFilters: false, showSummary: true });
    expect(JSON.parse(stored["dosyaterzisi.prefs.v1"]!).font).toBe("verdana");

    store.toggleTree();
    store.resetLayout();
    expect(store.getState().prefs).toMatchObject({ font: "default", size: "normal", showFilters: true });
    expect(store.getState().treeOpen).toBe(false);
    vi.unstubAllGlobals();
  });
});

describe("etiket sütunları ve gruplama", () => {
  async function withBridge() {
    const bridge = new MockBridge();
    const spy = vi.spyOn(bridge, "tagColumns");
    const s = new AppStore(validated(bridge));
    const started = s.start();
    await settle();
    await started;
    return { s, spy };
  }

  it("sütun seçilince görünen satırlar 150 ms sonra tek çağrıyla istenir ve önbelleğe yazılır", async () => {
    const { s, spy } = await withBridge();
    s.toggleTagColumn("ID3.Artist");
    s.toggleTagColumn("EXIF.Model");
    expect(s.getState().tagColumns).toEqual(["ID3.Artist", "EXIF.Model"]);

    s.requestTagValues([0, 1, 2]);
    s.requestTagValues([2, 3]);
    await vi.advanceTimersByTimeAsync(100);
    expect(spy).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(100);
    expect(spy).toHaveBeenCalledTimes(1);
    expect(spy.mock.calls[0]![0]).toEqual({ ids: [0, 1, 2, 3], names: ["ID3.Artist", "EXIF.Model"] });

    const cache = s.getState().tagValues;
    expect(Object.keys(cache)).toHaveLength(4);
    expect(cache[0]).toHaveProperty("ID3.Artist");

    // Önbellekte olanlar yeniden istenmez.
    s.requestTagValues([0, 1]);
    await vi.advanceTimersByTimeAsync(300);
    expect(spy).toHaveBeenCalledTimes(1);
  });

  it("liste yenilenince önbellek silinir", async () => {
    const { s } = await withBridge();
    s.toggleTagColumn("ID3.Artist");
    s.requestTagValues([0, 1]);
    await vi.advanceTimersByTimeAsync(300);
    expect(Object.keys(s.getState().tagValues).length).toBeGreaterThan(0);

    const scanning = s.scan();
    await settle();
    await scanning;
    expect(s.getState().tagValues).toEqual({});
  });

  it("etiketle gruplama tüm kimlikleri 200'lük parçalarla ister; sütun kalkınca gruplama kapanır", async () => {
    const { s, spy } = await withBridge();
    s.setGroupMode("tag:ID3.Artist");
    await vi.advanceTimersByTimeAsync(50);

    const total = s.getState().rows.length;
    expect(s.getState().tagColumns).toContain("ID3.Artist");
    expect(spy).toHaveBeenCalledTimes(Math.ceil(total / 200));
    expect(spy.mock.calls.every((c) => c[0].ids.length <= 200)).toBe(true);
    expect(Object.keys(s.getState().tagValues)).toHaveLength(total);

    s.toggleTagColumn("ID3.Artist");
    expect(s.getState().groupMode).toBe("none");
  });

  it("seçimler tercih olarak saklanır ve açılışta okunur", async () => {
    const stored: Record<string, string> = {};
    vi.stubGlobal("localStorage", { getItem: (k: string) => stored[k] ?? null, setItem: (k: string, v: string) => { stored[k] = v; } });
    vi.stubGlobal("document", { documentElement: { style: { setProperty() {} }, toggleAttribute() {} } });

    const { s } = await withBridge();
    s.toggleTagColumn("DOC.Title");
    s.setGroupMode("ext");
    expect(JSON.parse(stored["dosyaterzisi.listview.v1"]!)).toEqual({ tagColumns: ["DOC.Title"], groupMode: "ext" });

    const again = await withBridge();
    expect(again.s.getState().tagColumns).toEqual(["DOC.Title"]);
    expect(again.s.getState().groupMode).toBe("ext");
    vi.unstubAllGlobals();
  });
});
