// Gerçek uygulamayı (DosyaTerzisi.exe) açar, gerçek bir klasörde baştan sona dener.
//
// Nasıl çalışır: WebView2'nin uzaktan hata ayıklama kapısı açılır (yalnızca bu denemede), sayfa Chrome DevTools
// Protocol ile sürülür: tıklar, yazar, ekranı okur; sonra diskte gerçekten ne olduğuna bakar.
//
//   node tools/e2e/shell-smoke.mjs [exe yolu] [ekran görüntüsü klasörü]
//
// Kendi veri klasörünü (geçmiş, favoriler, WebView2 profili) ve kendi dosyalarını kullanır; sizinkilere dokunmaz.

import { execFileSync, spawn } from "node:child_process";
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { basename, dirname, join, resolve } from "node:path";

const root = resolve(import.meta.dirname, "..", "..");
const exe = resolve(process.argv[2] ?? join(root, "src", "DosyaTerzisi.Shell", "bin", "Debug", "net8.0-windows", "win-x64", "DosyaTerzisi.exe"));
const shots = resolve(process.argv[3] ?? join(tmpdir(), "dosyaterzisi-e2e-shots"));
const port = 9333;

mkdirSync(shots, { recursive: true });
const work = mkdtempSync(join(tmpdir(), "dosyaterzisi-e2e-"));
const dataDir = join(work, "veri");
const folder = join(work, "Belgeler");
mkdirSync(join(folder, "Alt"), { recursive: true });
for (const name of ["Ek I-4.9.1 - Karar Alma.pdf", "Ek I-4.9.2 - Komisyon.pdf", "Öğrenci Şeması (2).xlsx", "rapor.txt"])
  writeFileSync(join(folder, name), "içerik: " + name);
writeFileSync(join(folder, "Alt", "iç - not.txt"), "alt klasör");

/** İçinde gerçek ID3 etiketi olan küçük bir MP3: ID3v2.3 başlığı (UTF-16 metin) + 30 MPEG karesi. */
function writeMp3(path, artist, title, track) {
  const frame = Buffer.alloc(417);
  frame.set([0xff, 0xfb, 0x90, 0x64]);
  const text = (s) => Buffer.concat([Buffer.from([1, 0xff, 0xfe]), Buffer.from(s, "utf16le")]);
  const tag = (id, s) => {
    const body = text(s);
    const head = Buffer.alloc(10);
    head.write(id, 0, "ascii");
    head.writeUInt32BE(body.length, 4);
    return Buffer.concat([head, body]);
  };
  const frames = Buffer.concat([tag("TPE1", artist), tag("TIT2", title), tag("TRCK", String(track))]);
  const size = frames.length;
  const header = Buffer.from([0x49, 0x44, 0x33, 3, 0, 0, (size >> 21) & 0x7f, (size >> 14) & 0x7f, (size >> 7) & 0x7f, size & 0x7f]);
  writeFileSync(path, Buffer.concat([header, frames, ...Array(30).fill(frame)]));
}

const music = join(folder, "Müzik");
mkdirSync(music, { recursive: true });
writeMp3(join(music, "01.mp3"), "Sezen Aksu", "Şarkı Bitti", 4);
writeMp3(join(music, "02.mp3"), "AC/DC", "Back In Black", 6);

const lab = join(work, "Deneme");
const labCopies = join(work, "Kopyalar");
mkdirSync(lab, { recursive: true });
mkdirSync(labCopies, { recursive: true });
for (const n of ["a", "b", "c"]) writeFileSync(join(lab, `${n}.txt`), `içerik ${n}`);

let app = null;
let page = null;
const results = [];

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const tree = () => readdirSync(folder).sort();

function check(name, ok, detail = "") {
  results.push({ name, ok });
  console.log(`${ok ? "✓" : "✗"} ${name}${ok ? "" : detail ? `  →  ${detail}` : ""}`);
  if (!ok) throw new Error(`Başarısız: ${name} ${detail}`);
}

// ---- Uygulamayı aç --------------------------------------------------------------------------------------------------

async function launch(args = [folder]) {
  app = spawn(exe, args, {
    env: { ...process.env, TERZI_OFFSCREEN: "1", TERZI_DATA_DIR: dataDir, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: `--remote-debugging-port=${port}` },
    stdio: "ignore",
  });
  app.on("error", (e) => {
    throw e;
  });

  const target = await waitFor(async () => {
    try {
      const list = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
      return list.find((t) => t.type === "page" && t.url.startsWith("https://app.dosyaterzisi/"));
    } catch {
      return null;
    }
  }, "uygulama sayfası açılsın", 40_000);

  page = await connect(target.webSocketDebuggerUrl);
  await page.send("Page.enable");
  await page.send("Runtime.enable");
}

async function quit() {
  page?.close();
  page = null;
  if (app && app.exitCode === null) {
    app.kill();
    await new Promise((r) => app.once("exit", r));
  }
  app = null;
  await sleep(500);
}

// ---- Chrome DevTools Protocol ---------------------------------------------------------------------------------------

async function connect(url) {
  const ws = new WebSocket(url);
  await new Promise((res, rej) => {
    ws.onopen = res;
    ws.onerror = () => rej(new Error("DevTools bağlantısı kurulamadı"));
  });
  let nextId = 0;
  const pending = new Map();
  ws.onmessage = (event) => {
    const message = JSON.parse(event.data);
    const waiting = pending.get(message.id);
    if (!waiting) return;
    pending.delete(message.id);
    if (message.error) waiting.reject(new Error(message.error.message));
    else waiting.resolve(message.result);
  };
  return {
    send: (method, params = {}) =>
      new Promise((resolve, reject) => {
        const id = ++nextId;
        pending.set(id, { resolve, reject });
        ws.send(JSON.stringify({ id, method, params }));
      }),
    close: () => ws.close(),
  };
}

async function evaluate(expression) {
  const result = await page.send("Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true });
  if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description ?? result.exceptionDetails.text);
  return result.result.value;
}

async function waitFor(probe, description, timeout = 15_000) {
  const end = Date.now() + timeout;
  let last;
  while (Date.now() < end) {
    last = await (typeof probe === "string" ? evaluate(probe) : probe());
    if (last) return last;
    await sleep(120);
  }
  throw new Error(`Zaman aşımı: ${description}`);
}

async function screenshot(name) {
  const { data } = await page.send("Page.captureScreenshot", { format: "png" });
  writeFileSync(join(shots, `${name}.png`), Buffer.from(data, "base64"));
}

/** Kutuya gerçek bir klavye girişi gibi yazar (React'in input olayı tetiklenir). */
async function type(selector, text) {
  await evaluate(`(() => { const el = document.querySelector(${JSON.stringify(selector)}); el.focus(); el.select(); return true; })()`);
  await page.send("Input.insertText", { text });
}

const click = (selector) => evaluate(`document.querySelector(${JSON.stringify(selector)}).click(), true`);

const clickByText = (tag, text) =>
  evaluate(`(() => { const el = [...document.querySelectorAll(${JSON.stringify(tag)})].find(e => e.textContent.includes(${JSON.stringify(text)})); if (!el) return false; el.click(); return true; })()`);

const rowNames = () => evaluate(`[...document.querySelectorAll('.row .cell.name .text')].map(e => e.textContent)`);
const summary = () => evaluate(`document.querySelector('.summary')?.textContent ?? ''`);

// ---- Deneme ---------------------------------------------------------------------------------------------------------

try {
  await launch();
  check("uygulama WebView2 içinde açıldı", true);

  const bridgeKind = await evaluate(`typeof window.chrome?.webview?.postMessage === 'function'`);
  check("sayfa WebView2 köprüsünü görüyor (sahte köprü değil)", bridgeKind === true);

  // İlk açılış: tanıtım turu kendiliğinden görünür; "Atla" ile kapanır ve bir daha çıkmaz.
  await waitFor(`!!document.querySelector('.tour')`, "tanıtım turu ilk açılışta görünsün");
  check("ilk açılışta tanıtım turu görünüyor", true);
  await clickByText(".tour button", "İleri");
  check("turda ileri gidilebiliyor", (await evaluate(`document.querySelector('.tour-card h2').textContent`)).includes("1."));
  await clickByText(".tour button", "Atla");
  await waitFor(`!document.querySelector('.tour')`, "tur kapansın");
  check("tur kapatıldı", true);

  await waitFor(`document.querySelectorAll('.row').length === 4`, "4 dosya listelensin");
  const names = await rowNames();
  check("komut satırındaki klasör listelendi", names.length === 4 && names.includes("Öğrenci Şeması (2).xlsx"), names.join(" | "));
  await screenshot("1-listelendi");

  const translated = await evaluate(`[...document.querySelectorAll('.row')].map(r => r.textContent).find(t => t.includes('Öğrenci Şeması (2).xlsx'))`);
  check("varsayılan Türkçe→ASCII önizlemesi gerçek motordan geldi", translated.includes("Ogrenci_Semasi_(2).xlsx"), translated);

  // Joker karakter kuralı
  await clickByText("button[role=tab]", "Joker");
  await type('input[aria-label="Aranan"]', "* - *");
  await type('input[aria-label="Yeni ad"]', "\\1-\\2");
  await waitFor(`document.querySelector('.summary')?.textContent.includes('2 değişecek')`, "2 dosya değişecek görünsün");
  await screenshot("2-onizleme");
  check("kuralı yazınca canlı önizleme güncellendi", (await summary()).includes("2 değişecek"));

  // Adlandır
  await click(".actionbar .btn.primary");
  await waitFor(`!!document.querySelector('.dialog')`, "onay penceresi açılsın");
  await screenshot("3-onay");
  await clickByText(".dialog button", "Devam et");
  await waitFor(`document.querySelector('.toast .msg')?.textContent.includes('yeniden adlandırıldı')`, "bildirim çıksın");
  check(
    "dosyalar diskte gerçekten yeniden adlandırıldı",
    JSON.stringify(tree()) === JSON.stringify(["Alt", "Müzik", "Ek I-4.9.1-Karar Alma.pdf", "Ek I-4.9.2-Komisyon.pdf", "rapor.txt", "Öğrenci Şeması (2).xlsx"].sort()),
    tree().join(" | "),
  );
  check("içerik korundu", readFileSync(join(folder, "Ek I-4.9.1-Karar Alma.pdf"), "utf-8") === "içerik: Ek I-4.9.1 - Karar Alma.pdf");
  await screenshot("4-adlandirildi");

  const history = JSON.parse(readFileSync(join(dataDir, "history.json"), "utf-8"));
  check("geçmiş dosyası diske yazıldı", history.length === 1 && history[0].State === "Completed" && history[0].Moves.length === 2, JSON.stringify(history).slice(0, 200));

  // Uygulamayı kapatıp aç: geri alma hâlâ var mı?
  await quit();
  await launch();
  await waitFor(`document.querySelectorAll('.row').length === 4`, "yeniden açılınca liste gelsin");
  check("tur ikinci açılışta tekrar çıkmıyor (görüldü bilgisi saklandı)", (await evaluate(`!!document.querySelector('.tour')`)) === false);
  const undoEnabled = await evaluate(`[...document.querySelectorAll('.actionbar button')].find(b => b.textContent.includes('Geri Al')).disabled === false`);
  check("program kapatılıp açılınca Geri Al hâlâ kullanılabilir", undoEnabled);

  await clickByText(".actionbar button", "Geri Al");
  await waitFor(`document.querySelector('.toast .msg')?.textContent.includes('geri alındı')`, "geri alma bildirimi");
  check(
    "geri alma dosyaları eski adlarına döndürdü",
    JSON.stringify(tree()) === JSON.stringify(["Alt", "Müzik", "Ek I-4.9.1 - Karar Alma.pdf", "Ek I-4.9.2 - Komisyon.pdf", "rapor.txt", "Öğrenci Şeması (2).xlsx"].sort()),
    tree().join(" | "),
  );

  // Çakışma penceresi (program yeniden açıldığı için kural varsayılana dönmüştü)
  await clickByText("button[role=tab]", "Joker");
  await type('input[aria-label="Aranan"]', "*.pdf");
  await type('input[aria-label="Yeni ad"]', "ayni.pdf");
  await waitFor(`document.querySelector('.summary')?.textContent.includes('1 sorunlu')`, "çakışma görünsün");
  await click(".actionbar .btn.primary");
  await waitFor(`!!document.querySelector('.conflict-table')`, "çakışma penceresi açılsın");
  await screenshot("5-cakisma");
  const conflictRows = await evaluate(`document.querySelectorAll('.conflict-table tbody tr').length`);
  check("çakışma penceresi çakışan dosyayı listeledi", conflictRows === 1, String(conflictRows));
  await clickByText(".dialog button", "Numara ekleyerek");
  await waitFor(`document.querySelector('.toast .msg')?.textContent.includes('yeniden adlandırıldı')`, "numaralı adlandırma bitsin");
  const pdfs = tree().filter((n) => n.endsWith(".pdf"));
  check("numara ekleyerek ikisi de adlandırıldı, hiçbiri ezilmedi", JSON.stringify(pdfs) === JSON.stringify(["ayni(01).pdf", "ayni.pdf"].sort()), pdfs.join(" | "));
  const contents = pdfs.map((n) => readFileSync(join(folder, n), "utf-8")).sort();
  check("iki dosyanın içeriği de duruyor", contents.length === 2 && contents[0] !== contents[1]);

  // Favori ekleme
  await click(".rulepanel .btn.small");
  await waitFor(`!!document.querySelector('.popover')`, "ön ayarlar menüsü açılsın");
  await clickByText(".popover button", "Geçerli kuralı favorilere ekle");
  await waitFor(`!!document.querySelector('.dialog input')`, "favori adı penceresi");
  await type(".dialog input", "Denemeki kural");
  await clickByText(".dialog button", "Tamam");
  await waitFor(`document.querySelector('.toast .msg')?.textContent.includes('favorilere eklendi')`, "favori bildirimi");
  const favorites = JSON.parse(readFileSync(join(dataDir, "favorites.json"), "utf-8"));
  check("favori diske yazıldı", favorites.Favorites.some((f) => f.Name === "Denemeki kural" && f.Search === "*.pdf"), JSON.stringify(favorites).slice(0, 200));

  // ---- 8e: yeni sekmeler ve ekranlar ---------------------------------------------------------------------------------

  // Klasör değiştir (yol kutusuna yaz, Yenile): içinde gerçek etiketli iki MP3 var.
  await type('input[aria-label="Klasör yolu"]', music);
  await click('button[aria-label="Yenile"]');
  await waitFor(`document.querySelectorAll('.row').length === 2`, "müzik klasörü listelensin");

  // Etiketten adlandırma
  await clickByText("button[role=tab]", "Etiketten");
  await waitFor(`!!document.querySelector('input[aria-label="Şablon"]')`, "etiket sekmesi açılsın");
  await waitFor(`[...document.querySelectorAll('.row')].some(r => r.textContent.includes('Sezen Aksu - Şarkı Bitti.mp3'))`, "etiketlerden yeni ad gelsin");
  await screenshot("6-etiketten");
  const tagChips = await evaluate(`[...document.querySelectorAll('.tag-chip .name')].map(e => e.textContent)`);
  check("seçili dosyanın gerçek ID3 etiketleri gösterildi", tagChips.includes("ID3.Artist") && tagChips.includes("ID3.Track"), tagChips.join(","));
  const acdc = await evaluate(`[...document.querySelectorAll('.row')].map(r => r.textContent).find(t => t.includes('Back In Black'))`);
  check('"AC/DC" dosya adında "AC_DC" oldu', acdc.includes("AC_DC - Back In Black.mp3"), acdc);

  await click(".actionbar .btn.primary");
  await waitFor(`!!document.querySelector('.dialog')`, "etiket onayı");
  await clickByText(".dialog button", "Devam et");
  await waitFor(`document.querySelector('.toast .msg')?.textContent.includes('yeniden adlandırıldı')`, "etiket adlandırması bitsin");
  check(
    "etiketlerden adlandırma diskte yapıldı",
    JSON.stringify(readdirSync(music).sort()) === JSON.stringify(["AC_DC - Back In Black.mp3", "Sezen Aksu - Şarkı Bitti.mp3"].sort()),
    readdirSync(music).join(" | "),
  );
  await clickByText(".actionbar button", "Geri Al");
  await waitFor(`document.querySelector('.toast .msg')?.textContent.includes('geri alındı')`, "etiket geri alması");
  check("etiketten adlandırma geri alındı", JSON.stringify(readdirSync(music).sort()) === JSON.stringify(["01.mp3", "02.mp3"]), readdirSync(music).join(" | "));

  // Öznitelikler
  await clickByText("button[role=tab]", "Öznitelikler");
  await waitFor(`!!document.querySelector('.attr-line')`, "öznitelik sekmesi açılsın");
  await evaluate(`[...document.querySelectorAll('.attr-line')].find(l => l.textContent.includes('Gizli')).querySelector('[role=radio]:nth-child(2)').click(), true`);
  await waitFor(`document.querySelector('.summary')?.textContent.includes('2 değişecek')`, "öznitelik önizlemesi");
  await screenshot("7-oznitelikler");
  await click(".actionbar .btn.primary");
  await waitFor(`!!document.querySelector('.dialog')`, "öznitelik onayı");
  await clickByText(".dialog button", "Değiştir");
  await waitFor(`document.querySelector('.toast .msg')?.textContent.includes('2 öğe değiştirildi')`, "öznitelik değişikliği");
  const hidden = (file) => execFileSync("attrib", [join(music, file)], { encoding: "latin1" }).includes(" H ");
  check("dosyalar gerçekten gizli yapıldı (Windows attrib ile doğrulandı)", hidden("01.mp3") && hidden("02.mp3"));
  await clickByText(".actionbar button", "Geri Al");
  await waitFor(`document.querySelector('.toast .msg')?.textContent.includes('eski hâline döndü')`, "öznitelik geri alması");
  check("öznitelik değişikliği geri alındı", !hidden("01.mp3") && !hidden("02.mp3"));

  // Numaralı nesneler
  await clickByText("button[role=tab]", "Numaralı");
  await waitFor(`!!document.querySelector('input[aria-label="Ad şablonu"]')`, "numaralı sekme açılsın");
  await type('input[aria-label="Ad şablonu"]', "Hafta ??.txt");
  await type('input[aria-label="Boyut"]', "2K");
  await waitFor(`document.querySelectorAll('.planned-row').length === 3`, "üç ad listelensin");
  await screenshot("8-numarali");
  await click(".actionbar .btn.primary");
  await waitFor(`document.querySelector('.toast .msg')?.textContent.includes('3 öğe oluşturuldu')`, "oluşturma bildirimi");
  const made = readdirSync(music).filter((n) => n.startsWith("Hafta"));
  check("numaralı dosyalar oluşturuldu", JSON.stringify(made) === JSON.stringify(["Hafta 01.txt", "Hafta 02.txt", "Hafta 03.txt"]), made.join(" | "));
  check("dosya boyutu 2 KB", statSync(join(music, "Hafta 02.txt")).size === 2048);

  // Klasör ağacı
  await clickByText("button[role=tab]", "Adlandır");
  await click('button[aria-label="Klasör ağacı"]');
  await waitFor(`document.querySelectorAll('.tree-row').length >= 2`, "ağaç kökleri yüklensin");
  await screenshot("9-agac");
  const treeNames = await evaluate(`[...document.querySelectorAll('.tree-row .tree-label span')].map(e => e.textContent)`);
  check("klasör ağacı gerçek sürücüleri ve klasörleri listeledi", treeNames.some((n) => /\([A-Z]:\)/.test(n)), treeNames.join(" | "));

  // Favori düzenleme penceresi
  await click(".rulepanel .btn.small");
  await waitFor(`!!document.querySelector('.popover')`, "ön ayarlar menüsü");
  await clickByText(".popover button", "Favorileri düzenle");
  await waitFor(`!!document.querySelector('.fav-layout')`, "favori düzenleme penceresi");
  await type('input[aria-label="Favori adı"]', "Yeniden adlandırılmış favori");
  await clickByText(".dialog button", "Kaydet");
  await waitFor(`[...document.querySelectorAll('.fav-item span:first-child')].some(e => e.textContent === 'Yeniden adlandırılmış favori')`, "favori adı değişsin");
  const saved = JSON.parse(readFileSync(join(dataDir, "favorites.json"), "utf-8"));
  check("favori adı düzenlenip diske yazıldı", saved.Favorites.some((f) => f.Name === "Yeniden adlandırılmış favori"));
  await clickByText(".dialog button", "Kapat");
  await waitFor(`!document.querySelector('.dialog')`, "pencere kapansın");

  // Geçmiş penceresi
  await click('button[aria-label="Geçmiş"]');
  await waitFor(`document.querySelectorAll('.history-row').length >= 1`, "geçmiş penceresi");
  const historyRows = await evaluate(`document.querySelectorAll('.history-row').length`);
  check("geçmiş penceresi geri alınmamış tek işlemi (çakışmalı numaralı adlandırma) listeledi", historyRows === 1, String(historyRows));
  await screenshot("10-gecmis");
  await clickByText(".dialog button", "Kapat");

  // Klasör gezintisi: üst klasör, geri, ileri, klasör satırına çift tık
  await clickByText("button[role=tab]", "Adlandır");
  await waitFor(`document.querySelectorAll('.row').length >= 1`, "liste görünsün");
  // Klasör satırları görünsün (varsayılan: yalnızca dosyalar listelenir).
  await evaluate(`[...document.querySelectorAll('label.switch')].find(l => l.textContent.trim() === 'Klasörler').querySelector('input').click()`, true);
  const startFolder = await evaluate(`document.querySelector('input[aria-label="Klasör yolu"]').value`);
  await evaluate(`document.querySelector('button[aria-label="Üst klasör"]').click()`, true);
  await waitFor(`document.querySelector('input[aria-label="Klasör yolu"]').value !== ${JSON.stringify(startFolder)}`, "üst klasöre geçilsin");
  const parentPath = await evaluate(`document.querySelector('input[aria-label="Klasör yolu"]').value`);
  check("üst klasör düğmesi bir üst klasöre geçti", dirname(startFolder) === parentPath, `${startFolder} -> ${parentPath}`);
  await waitFor(`[...document.querySelectorAll('.row')].some(r => r.textContent.includes(${JSON.stringify(basename(startFolder))}))`, "alt klasör satırı görünsün");
  await evaluate(`document.querySelector('button[aria-label="Geri"]').click()`, true);
  await waitFor(`document.querySelector('input[aria-label="Klasör yolu"]').value === ${JSON.stringify(startFolder)}`, "geri gidilsin");
  check("geri düğmesi önceki klasöre döndü", true);
  await evaluate(`document.querySelector('button[aria-label="İleri"]').click()`, true);
  await waitFor(`document.querySelector('input[aria-label="Klasör yolu"]').value === ${JSON.stringify(parentPath)}`, "ileri gidilsin");
  check("ileri düğmesi yeniden üst klasöre gitti", true);
  await evaluate(`[...document.querySelectorAll('.row')].find(r => r.textContent.includes(${JSON.stringify(basename(startFolder))})).dispatchEvent(new MouseEvent('dblclick', { bubbles: true }))`, true);
  await waitFor(`document.querySelector('input[aria-label="Klasör yolu"]').value === ${JSON.stringify(startFolder)}`, "çift tıkla klasöre girilsin");
  check("klasör satırına çift tıklayınca içine girildi", true);
  await waitFor(`document.querySelectorAll('.row').length >= 1`, "liste gelsin");

  // ---- Liste düzenleme, kopyalama, taşıma, ayarlar (ayrı bir deneme klasöründe) ------------------------------------
  const key = (k, code, vk, modifiers = 0) =>
    page.send("Input.dispatchKeyEvent", { type: "keyDown", key: k, code, windowsVirtualKeyCode: vk, modifiers });

  await type('input[aria-label="Klasör yolu"]', lab);
  await click('button[aria-label="Yenile"]');
  await waitFor(`document.querySelectorAll('.row').length === 3`, "deneme klasörü listelensin");
  await clickByText("button[role=tab]", "Joker");
  await type('input[aria-label="Aranan"]', "*.*");
  await type('input[aria-label="Yeni ad"]', "\\1.\\2");

  // F2 ile bir satırın yeni adını elle yaz
  await evaluate(`[...document.querySelectorAll('.row')].find(r => r.textContent.includes('b.txt')).click(), document.querySelector('.scroller').focus(), true`);
  await key("F2", "F2", 113);
  await waitFor(`!!document.querySelector('input[aria-label="Yeni ad"].name-editor')`, "ad kutusu açılsın");
  await page.send("Input.insertText", { text: "özel ad" });
  await key("Enter", "Enter", 13);
  await waitFor(`document.querySelector('.summary')?.textContent.includes('1 değişecek')`, "elle yazılan ad önizlemeye girsin");
  check("F2 ile elle yazılan ad önizlemede görünüyor", (await rowNames()).includes("b.txt") && (await evaluate(`document.querySelector('.row .new-name.typed')?.textContent`)).includes("özel ad.txt"));
  await click(".actionbar .btn.primary");
  await waitFor(`!!document.querySelector('.dialog')`, "onay");
  await clickByText(".dialog button", "Devam et");
  await waitFor(`document.querySelector('.toast .msg')?.textContent.includes('yeniden adlandırıldı')`, "elle yazılan ad uygulansın");
  check("elle yazılan ad diskte uygulandı", JSON.stringify(readdirSync(lab).sort()) === JSON.stringify(["a.txt", "c.txt", "özel ad.txt"].sort()), readdirSync(lab).join(" | "));
  await clickByText(".actionbar button", "Geri Al");
  await waitFor(`document.querySelector('.toast .msg')?.textContent.includes('geri alındı')`, "geri alma");
  check("elle yazılan adlandırma geri alındı", JSON.stringify(readdirSync(lab).sort()) === JSON.stringify(["a.txt", "b.txt", "c.txt"]), readdirSync(lab).join(" | "));
  await waitFor(`document.querySelectorAll('.row').length === 3`, "liste yenilensin");

  // Ctrl+I seçimi ters çevirir, Delete satırı listeden çıkarır (dosyaya dokunmaz)
  await evaluate(`document.querySelector('.scroller').focus(), true`);
  await key("i", "KeyI", 73, 2);
  await waitFor(`document.querySelector('.summary')?.textContent.includes('3 seçili')`, "seçim tersine dönsün");
  check("Ctrl+I seçimi ters çevirdi", true);
  await evaluate(`document.querySelector('.scroller').focus(), true`);
  await evaluate(`[...document.querySelectorAll('.row')].find(r => r.textContent.includes('c.txt')).click(), true`);
  await key("Delete", "Delete", 46);
  await waitFor(`document.querySelectorAll('.row').length === 2`, "satır listeden çıksın");
  check("Delete satırı listeden çıkardı, dosya yerinde", existsSync(join(lab, "c.txt")) && (await rowNames()).length === 2);
  await click('button[aria-label="Yenile"]');
  await waitFor(`document.querySelectorAll('.row').length === 3`, "yenileyince satır geri gelsin");

  // Kopyala: özgün kalır, kopya hedef klasörde; geri alınca yalnızca kopya silinir
  await clickByText("button[role=radio]", "Kopyala");
  await type('input[aria-label="Hedef klasör"]', labCopies);
  await type('input[aria-label="Yeni ad"]', "\\1_kopya.\\2");
  await waitFor(`document.querySelector('.summary')?.textContent.includes('3 değişecek')`, "kopya önizlemesi");
  await screenshot("12-kopya");
  await click(".actionbar .btn.primary");
  await waitFor(`!!document.querySelector('.dialog')`, "kopya onayı");
  check("onay penceresi 'kopyalanacak' diyor", (await evaluate(`document.querySelector('.dialog h2').textContent`)).includes("kopyalanacak"));
  await clickByText(".dialog button", "Devam et");
  await waitFor(`document.querySelector('.toast .msg')?.textContent.includes('kopyalandı')`, "kopyalama bitsin");
  check(
    "kopyalar hedef klasörde, özgünler yerinde",
    JSON.stringify(readdirSync(labCopies).sort()) === JSON.stringify(["a_kopya.txt", "b_kopya.txt", "c_kopya.txt"]) && readdirSync(lab).includes("a.txt"),
    readdirSync(labCopies).join(" | "),
  );
  check("kopyanın içeriği özgünüyle aynı", readFileSync(join(labCopies, "b_kopya.txt"), "utf-8") === "içerik b");
  await clickByText(".actionbar button", "Geri Al");
  await waitFor(`document.querySelector('.toast .msg')?.textContent.includes('geri alındı')`, "kopya geri alma");
  check("geri alma yalnızca kopyaları sildi", readdirSync(labCopies).length === 0 && readdirSync(lab).filter((n) => n.endsWith(".txt")).length === 3);

  // Klasöre taşı: yeni ad "Alt/..." ile
  await clickByText("button[role=radio]", "Adlandır");
  await type('input[aria-label="Hedef klasör"]', "");
  await type('input[aria-label="Yeni ad"]', "Taşınan/\\1.\\2");
  await waitFor(`document.querySelector('.summary')?.textContent.includes('3 değişecek')`, "taşıma önizlemesi");
  await click(".actionbar .btn.primary");
  await waitFor(`!!document.querySelector('.dialog')`, "taşıma onayı");
  await clickByText(".dialog button", "Devam et");
  await waitFor(`document.querySelector('.toast .msg')?.textContent.includes('yeniden adlandırıldı')`, "taşıma bitsin");
  check("'Taşınan/' yazınca dosyalar yeni klasöre taşındı", JSON.stringify(readdirSync(join(lab, "Taşınan")).sort()) === JSON.stringify(["a.txt", "b.txt", "c.txt"]), readdirSync(lab).join(" | "));
  await clickByText(".actionbar button", "Geri Al");
  await waitFor(`document.querySelector('.toast .msg')?.textContent.includes('geri alındı')`, "taşıma geri alma");
  check("geri alınca dosyalar yerine döndü ve taşıma için oluşan klasör silindi", !existsSync(join(lab, "Taşınan")) && existsSync(join(lab, "b.txt")));

  // ---- Betik (gerçek cscript), 8.3 kısa ad ve % yazımı --------------------------------------------------------------
  writeFileSync(join(dataDir, "Script.vbs"), 'Function Buyuk(s)\r\n  Buyuk = UCase(s)\r\nEnd Function\r\n', "utf-8");
  await type('input[aria-label="Klasör yolu"]', lab);
  await click('button[aria-label="Yenile"]');
  await waitFor(`document.querySelectorAll('.row').length === 3`, "deneme klasörü yeniden listelensin");
  await type('input[aria-label="Aranan"]', "*.*");
  await type('input[aria-label="Yeni ad"]', 'Buyuk("\\1") & "." & "\\2"');
  await evaluate(`[...document.querySelectorAll('label.checkbox-row, label.switch')].find(l => l.textContent.includes('Betik (yeni ad')).querySelector('input').click()`, true);
  await waitFor(`[...document.querySelectorAll('.row')].some(r => r.textContent.includes('A.txt'))`, "betik sonucu önizlemede görünsün");
  check("betik ifadesi (kullanıcı işleviyle) önizlemede çalıştı", (await rowNames()).length === 3);
  await click(".actionbar .btn.primary");
  await waitFor(`!!document.querySelector('.dialog')`, "betik onayı");
  await clickByText(".dialog button", "Devam et");
  await waitFor(`document.querySelector('.toast .msg')?.textContent.includes('yeniden adlandırıldı')`, "betikli adlandırma bitsin");
  check("betikle büyük harfli adlar diskte oluştu", JSON.stringify(readdirSync(lab).sort()) === JSON.stringify(["A.txt", "B.txt", "C.txt"]), readdirSync(lab).join(" | "));
  await clickByText(".actionbar button", "Geri Al");
  await waitFor(`document.querySelector('.toast .msg')?.textContent.includes('geri alındı')`, "betik geri alma");
  check("betikli adlandırma geri alındı", JSON.stringify(readdirSync(lab).sort()) === JSON.stringify(["a.txt", "b.txt", "c.txt"]), readdirSync(lab).join(" | "));
  await evaluate(`[...document.querySelectorAll('label.checkbox-row, label.switch')].find(l => l.textContent.includes('Betik (yeni ad')).querySelector('input').click()`, true);

  // % ile tarih yazımı ve 8.3
  await type('input[aria-label="Yeni ad"]', "Çok uzun bir ad %Y.\\2");
  await evaluate(`[...document.querySelectorAll('label.checkbox-row, label.switch')].find(l => l.textContent.includes('8.3 kısa ad')).querySelector('input').click()`, true);
  await waitFor(`[...document.querySelectorAll('.row')].some(r => /Ç.*~1\.txt/.test(r.textContent))`, "8.3 adı önizlemede görünsün");
  check("8.3 biçimi ve % yazımı önizlemede çalıştı", true);
  await evaluate(`[...document.querySelectorAll('label.checkbox-row, label.switch')].find(l => l.textContent.includes('8.3 kısa ad')).querySelector('input').click()`, true);

  // Ayarlar penceresi: deneme ortamında Gezgin menüleri kapalıdır (gerçek kayıt defterine dokunulmaz)
  await click('button[aria-label="Ayarlar"]');
  await waitFor(`!!document.querySelector('.dialog')`, "ayarlar açılsın");
  check("ayarlar penceresi deneme ortamında menüleri kapalı gösteriyor", (await evaluate(`document.querySelector('.dialog').textContent`)).includes("ayarlanamıyor"));
  await clickByText(".dialog button", "Kapat");

  // Sürükle-bırak: gerçek dosya yollarıyla (DevTools sürükleme olayı)
  const dropped = [join(folder, "rapor.txt"), join(folder, "Öğrenci Şeması (2).xlsx")];
  const dragData = { items: [], files: dropped, dragOperationsMask: 1 };
  await page.send("Input.dispatchDragEvent", { type: "dragEnter", x: 700, y: 400, data: dragData });
  await page.send("Input.dispatchDragEvent", { type: "dragOver", x: 700, y: 400, data: dragData });
  await page.send("Input.dispatchDragEvent", { type: "drop", x: 700, y: 400, data: dragData });
  const droppedOk = await waitFor(`document.querySelector('input[aria-label="Klasör yolu"]')?.value.startsWith('Sürüklenen')`, "bırakılanlar listelensin", 8000).catch(() => false);
  if (droppedOk) {
    const droppedRows = await rowNames();
    check("sürüklenen dosyalar listenin kendisi oldu (yolları WebView2'den geldi)", droppedRows.length === 2 && droppedRows.includes("rapor.txt"), droppedRows.join(" | "));
    await screenshot("11-surukle");
  } else {
    console.log("– sürükle-bırak: DevTools sürüklemesi WebView2'ye dosya yollarını iletmedi (gerçek fare sürüklemesiyle elle denenmeli)");
  }

  // Gezgin'in "Gönder" menüsü gibi: komut satırında birden çok dosya
  await quit();
  await launch([join(lab, "a.txt"), join(lab, "c.txt")]);
  const sent = await waitFor(`document.querySelector('input[aria-label="Klasör yolu"]')?.value.startsWith('Sürüklenen')`, "komut satırındaki dosyalar listelensin", 15000).catch(() => false);
  check("komut satırından verilen dosyalar listenin kendisi oldu", !!sent && JSON.stringify((await rowNames()).sort()) === JSON.stringify(["a.txt", "c.txt"]), String(await rowNames()));

  // Güvenlik: dışarı çıkış yok
  await evaluate(`setTimeout(() => { location.href = 'https://example.com/'; }, 0), true`);
  await sleep(1500);
  const still = await evaluate(`location.origin`);
  check("başka bir siteye gidilemiyor", still === "https://app.dosyaterzisi", still);
  await evaluate(`window.open('https://example.com/'), true`);
  await sleep(1000);
  const targets = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).filter((t) => t.type === "page");
  check("yeni pencere açılamıyor", targets.length === 1, String(targets.length));
  const fetched = await evaluate(`fetch('https://app.dosyaterzisi/../../etc/passwd').then(r => r.status).catch(() => 'hata')`);
  check("üst klasördeki dosyalar sunulmuyor", fetched === 404 || fetched === "hata", String(fetched));

  console.log(`\nTamam: ${results.length} denetim geçti. Ekran görüntüleri: ${shots}`);
} catch (error) {
  console.error(`\nHATA: ${error.message}`);
  if (page) await screenshot("hata").catch(() => undefined);
  process.exitCode = 1;
} finally {
  await quit();
  // WebView2'nin yardımcı süreçleri profil klasörünü biraz daha tutabilir; temizlik başarısız olursa bırakılır.
  try {
    rmSync(work, { recursive: true, force: true, maxRetries: 8, retryDelay: 500 });
  } catch {
    console.log(`(geçici klasör silinemedi, elle silebilirsiniz: ${work})`);
  }
}
