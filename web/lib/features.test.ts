import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MockBridge } from "./mock/mockBridge";
import { AppStore } from "./store";
import { validated } from "./wire";

async function settle(ms = 700) {
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

const names = () => store.getState().rows.map((r) => r.name);

describe("Etiketten adlandırma", () => {
  it("müzik dosyalarını etiketlerinden adlandırır, etiketi olmayanları atlar", async () => {
    store.setTab("tags");
    store.setTag({ template: "<ID3.Artist> - <ID3.Track>.\\e" });
    await settle();

    const s = store.getState();
    const song = s.rows.findIndex((r) => r.name === "Gülümse - Sezen Aksu.mp3");
    expect(s.previews[song]!.newName).toBe("Sezen Aksu - Gülümse.mp3");
    expect(s.previews[song]!.status).toBe("ready");
    expect(s.previews.find((p, i) => s.rows[i]!.name === "ders programı.xlsx")!.status).toBe("missingTag");
    expect(s.readyCount).toBe(2);
  });

  it("seçili dosyanın etiketlerini gösterir", async () => {
    store.setTab("tags");
    await settle();
    const song = store.getState().rows.find((r) => r.name === "Gülümse - Sezen Aksu.mp3")!;

    store.setSelection(new Set([song.id]));
    await settle();

    const tags = store.getState().fileTags!;
    expect(tags.error).toBeNull();
    expect(tags.entries).toContainEqual({ name: "ID3.Artist", value: "Sezen Aksu" });
  });

  it("etiket yoksa boş bırakma seçeneği adı yine kurar", async () => {
    store.setTab("tags");
    store.setTag({ template: "<ID3.Artist>.\\e", missing: "useEmpty" });
    await settle();

    expect(store.getState().previews.every((p) => p.status !== "missingTag")).toBe(true);
  });

  it("adlandırma ve geri alma aynı geçmişi kullanır", async () => {
    autoAnswer(store, { confirm: "proceed" });
    store.setTab("tags");
    store.setTag({ template: "<ID3.Artist> - <ID3.Track>.\\e" });
    await settle();

    const renaming = store.rename();
    await settle(1000);
    await renaming;

    expect(names()).toContain("Sezen Aksu - Gülümse.mp3");
    expect(store.getState().history[0]!.description).toContain("Etiket:");

    const undoing = store.undo();
    await settle(1000);
    await undoing;
    expect(names()).toContain("Gülümse - Sezen Aksu.mp3");
  });
});

describe("Öznitelikler", () => {
  it("önizleme her satır için önce/sonra değerlerini verir", async () => {
    store.setTab("attributes");
    store.setAttr({ hidden: "set" });
    await settle();

    const s = store.getState();
    expect(s.attrRows).toHaveLength(s.rows.length);
    expect(s.attrRows[0]!.before.hidden).toBe(false);
    expect(s.attrRows[0]!.after.hidden).toBe(true);
    expect(s.attrChanged).toBe(s.rows.length);
  });

  it("uygulanır, sonra geri alınır", async () => {
    autoAnswer(store, { ask: true });
    store.setTab("attributes");
    store.setAttr({ hidden: "set" });
    await settle();

    const applying = store.applyAttributes();
    await settle(1000);
    await applying;

    expect(store.getState().attrUndoCount).toBe(1);
    expect(store.getState().attrRows[0]!.before.hidden).toBe(true);
    expect(store.getState().attrChanged).toBe(0);

    const undoing = store.undoAttributes();
    await settle(1000);
    await undoing;

    expect(store.getState().attrUndoCount).toBe(0);
    expect(store.getState().attrRows[0]!.before.hidden).toBe(false);
    expect(store.getState().attrChanged).toBe(store.getState().rows.length);
  });

  it("onay penceresinde vazgeçilirse hiçbir şey değişmez", async () => {
    autoAnswer(store, { ask: false });
    store.setTab("attributes");
    store.setAttr({ readOnly: "set" });
    await settle();

    const applying = store.applyAttributes();
    await settle(1000);
    await applying;

    expect(store.getState().attrUndoCount).toBe(0);
    expect(store.getState().attrRows[0]!.before.readOnly).toBe(false);
  });

  it("tarihleri kaydırır; geçersiz tarih hata satırı olur", async () => {
    store.setTab("attributes");
    store.setAttr({ modified: { action: "shift", value: 0, shiftMinutes: 90, copyFrom: "modified", randomTo: 0, seed: 0, limitMin: 0, limitMax: 0 } });
    await settle();
    const row = store.getState().attrRows[0]!;
    expect(row.modifiedAfter - row.modifiedBefore).toBe(90 * 60_000);

    store.setAttr({ created: { action: "shift", value: 0, shiftMinutes: -60 * 24 * 365 * 1000, copyFrom: "modified", randomTo: 0, seed: 0, limitMin: 0, limitMax: 0 } });
    await settle();
    expect(store.getState().attrProblems).toBe(store.getState().rows.length);
    expect(store.getState().attrRows[0]!.status).toBe("error");
  });

  it("çekim tarihi: resim dosyasının adındaki tarihten alınır", async () => {
    store.setTab("attributes");
    store.setAttr({ modified: { action: "photoTaken", value: 0, shiftMinutes: 0, copyFrom: "modified", randomTo: 0, seed: 0, limitMin: 0, limitMax: 0 } });
    await settle();

    const s = store.getState();
    const photo = s.rows.findIndex((r) => r.name === "IMG_20260814_143005.jpg");
    expect(new Date(s.attrRows[photo]!.modifiedAfter)).toEqual(new Date(2026, 7, 14, 14, 30, 5));
    expect(s.attrRows[photo]!.status).toBe("ready");
  });

  it("klasörlere uygulama kapatılabilir", async () => {
    store.setListing({ includeFolders: true });
    await settle();
    store.setTab("attributes");
    store.setAttr({ hidden: "set", includeFolders: false });
    await settle();

    const s = store.getState();
    const folders = s.rows.filter((r) => r.isDir).length;
    expect(folders).toBeGreaterThan(0);
    expect(s.attrChanged).toBe(s.rows.length - folders);
  });
});

describe("Numaralı nesneler", () => {
  it("adları listeler, sorunları söyler", async () => {
    store.setTab("numbered");
    await settle();
    expect(store.getState().numberedPlan!.items.map((i) => i.name)).toEqual(["Yeni dosya 01.txt", "Yeni dosya 02.txt", "Yeni dosya 03.txt"]);

    store.setNumbered({ amount: 0 });
    await settle();
    expect(store.getState().numberedPlan!.problem).not.toBeNull();

    store.setNumbered({ amount: 2, size: "çok büyük" });
    await settle();
    expect(store.getState().numberedPlan!.problem).toContain("Boyut");
  });

  it("var olan adı işaretler", async () => {
    store.setTab("numbered");
    store.setNumbered({ namePattern: "özet.txt", amount: 1 });
    await settle();

    expect(store.getState().numberedPlan!.items[0]!.status).toBe("exists");
    expect(store.getState().numberedPlan!.ready).toBe(0);
  });

  it("oluşturur ve liste yenilenir", async () => {
    store.setTab("numbered");
    await settle();
    const before = store.getState().rows.length;

    const creating = store.createNumbered();
    await settle(1000);
    await creating;

    expect(store.getState().rows.length).toBe(before + 3);
    expect(names()).toContain("Yeni dosya 03.txt");
    expect(store.getState().toasts.at(-1)!.text).toContain("3 öğe oluşturuldu");
    // Aynı adlar artık var: tekrar oluşturulamaz.
    expect(store.getState().numberedPlan!.ready).toBe(0);
  });

  it("kopya için kaynak seçtirir", async () => {
    store.setTab("numbered");
    store.setNumbered({ kind: "copyOfFile", namePattern: "Ödev ?.docx", amount: 2 });
    await settle();
    expect(store.getState().numberedPlan!.problem).toContain("seçin");

    const picking = store.pickSource();
    await settle();
    await picking;
    await settle();

    expect(store.getState().numbered.sourcePath).toContain("sablon.docx");
    expect(store.getState().numberedPlan!.problem).toBeNull();
  });
});

describe("Sürükle-bırak ve klasör ağacı", () => {
  it("bırakılan dosyalar listenin kendisi olur; adlandırınca yeni adlarıyla yenilenir", async () => {
    autoAnswer(store, { confirm: "proceed" });

    const dropping = store.openDropped([{ name: "a.txt" }]);
    await settle();
    await dropping;

    let s = store.getState();
    expect(s.source).toBe("paths");
    expect(s.rows).toHaveLength(5);
    const before = names();

    store.setRule({ method: "wildcard", search: "*", replace: "x_\\1" });
    await settle();
    const renaming = store.rename();
    await settle(1000);
    await renaming;

    s = store.getState();
    expect(s.source).toBe("paths");
    expect(names().every((n) => n.startsWith("x_"))).toBe(true);
    expect(names()).toHaveLength(before.length);

    const undoing = store.undo();
    await settle(1000);
    await undoing;
    expect(names()).toEqual(before);
  });

  it("bırakılan tek klasör, klasör olarak açılır", async () => {
    // Sahte köprüde tek bir yol verilemez; gerçek akış openFolder ile aynıdır.
    const opening = store.openFolder("C:\\Users\\Musa\\Belgeler\\Akreditasyon\\KANIT");
    await settle();
    await opening;

    expect(store.getState().source).toBe("folder");
    expect(store.getState().folder).toBe("C:\\Users\\Musa\\Belgeler\\Akreditasyon\\KANIT");
    expect(names()).toEqual(["Ek I-4.9.6 - Yönerge.pdf", "Ek I-4.9.7 - Süreç Haritası.pdf", "Şablon (1).docx"]);
  });

  it("ağaç kökleri ve alt klasörleri verir", async () => {
    const listing = store.listFolders("");
    await settle();
    const roots = await listing;
    expect(roots.map((r) => r.name)).toContain("Belgeler");

    const docs = roots.find((r) => r.name === "Belgeler")!;
    expect(docs.hasChildren).toBe(true);
    const sub = store.listFolders(docs.path);
    await settle();
    const children = await sub;
    expect(children.map((c) => c.name)).toContain("Akreditasyon");
    expect(children.find((c) => c.name === "Akreditasyon")!.hasChildren).toBe(true);
  });
});

describe("Favoriler ve geçmiş", () => {
  it("favori yeniden adlandırılır, güncellenir ve düzenleme penceresinden uygulanır", async () => {
    const first = store.getState().favorites[0]!;
    await store.renameFavorite(first, "Yeni ad");
    expect(store.getState().favorites[0]!.name).toBe("Yeni ad");

    await store.updateFavorite({ ...store.getState().favorites[0]!, rule: { method: "regex", search: "\\d+", replace: "#", caseSensitive: true } });
    autoAnswer(store, { favorites: store.getState().favorites[0] });

    const editing = store.editFavorites();
    await settle();
    await editing;

    expect(store.getState().rule).toMatchObject({ method: "regex", search: "\\d+", replace: "#", caseSensitive: true });
  });

  it("düzenleme penceresi kapatılırsa kural değişmez", async () => {
    const rule = store.getState().rule;
    autoAnswer(store, { favorites: undefined });

    const editing = store.editFavorites();
    await settle();
    await editing;

    expect(store.getState().rule).toEqual(rule);
  });

  it("içe ve dışa aktarma tarayıcıda sessizce bir şey yapmaz", async () => {
    await store.importFavorites();
    await store.exportFavorites();

    expect(store.getState().toasts.at(-1)!.text).toBe("Yeni favori bulunamadı.");
  });

  it("geçmiş penceresi açılır ve kapanır", async () => {
    autoAnswer(store, { confirm: "proceed" });
    store.setRule({ method: "wildcard", search: "*", replace: "x_\\1" });
    await settle();
    const renaming = store.rename();
    await settle(1000);
    await renaming;

    const showing = store.showHistory();
    await settle();
    expect(store.getState().dialog?.kind).toBe("history");
    expect(store.getState().history).toHaveLength(1);

    store.answer(undefined);
    await showing;
    expect(store.getState().dialog).toBeNull();
  });

  it("sekme değişince önizleme o sekmenin işine göre yenilenir", async () => {
    store.setTab("attributes");
    await settle();
    expect(store.getState().attrRows).toHaveLength(store.getState().rows.length);

    store.setTab("rename");
    await settle();
    expect(store.getState().previews).toHaveLength(store.getState().rows.length);
    expect(store.getState().readyCount).toBeGreaterThan(0);
  });
});
