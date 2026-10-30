// README için ekran görüntülerini üretir: gerçek uygulamayı ekran dışında açar, örnek dosyalarla doldurur,
// açık ve koyu temada görüntü alır. Kullanıcının ekranına hiçbir şey çıkmaz.
//
//   node tools/screenshots/capture.mjs [exe yolu] [çıkış klasörü]
//
// Örnek dosyalar geçici bir klasörde oluşturulur ve sanal bir sürücü harfiyle (T:) gösterilir; böylece görüntülerde
// kişisel bir yol görünmez.

import { execFileSync, spawn } from "node:child_process";
import { mkdirSync, mkdtempSync, rmSync, utimesSync, writeFileSync } from "node:fs";
import { randomBytes } from "node:crypto";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";

const root = resolve(import.meta.dirname, "..", "..");
const exe = resolve(process.argv[2] ?? join(root, "release", "DosyaTerzisi", "DosyaTerzisi.exe"));
const out = resolve(process.argv[3] ?? join(root, "docs", "images"));
const port = 9344;
const drive = "T:";

mkdirSync(out, { recursive: true });
const work = mkdtempSync(join(tmpdir(), "terzi-shots-"));
const dataDir = join(work, "veri");
const demo = join(work, "Demo");

// ---- Örnek dosyalar -------------------------------------------------------------------------------------------------

const when = (day, h, m) => new Date(2026, 6, day, h, m, 0);
function file(dir, name, size, date) {
  mkdirSync(dir, { recursive: true });
  const path = join(dir, name);
  writeFileSync(path, randomBytes(size));
  utimesSync(path, date, date);
}

const photos = join(demo, "Tatil Fotoğrafları");
const photoNames = [2041, 2042, 2043, 2044, 2047, 2048, 2051, 2052, 2053, 2055, 2058, 2059];
photoNames.forEach((n, i) => file(photos, `IMG_${n}.jpg`, 1_400_000 + ((n * 7919) % 2_000_000), when(12 + Math.floor(i / 4), 9 + (i % 4) * 3, 10 + i)));
file(photos, "Notlar.txt", 1_100, when(14, 20, 5));
file(photos, "Rota - Eski.docx", 48_000, when(11, 8, 30));
mkdirSync(join(photos, "Videolar"), { recursive: true });

/** İçinde gerçek ID3 etiketi olan küçük bir MP3. */
function writeMp3(path, artist, title, track, album) {
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
  const frames = Buffer.concat([tag("TPE1", artist), tag("TIT2", title), tag("TRCK", String(track)), tag("TALB", album)]);
  const size = frames.length;
  const header = Buffer.from([0x49, 0x44, 0x33, 3, 0, 0, (size >> 21) & 0x7f, (size >> 14) & 0x7f, (size >> 7) & 0x7f, size & 0x7f]);
  writeFileSync(path, Buffer.concat([header, frames, ...Array(240).fill(frame)]));
}

const music = join(demo, "Müzik");
mkdirSync(music, { recursive: true });
[
  ["Sezen Aksu", "Gidiyorum", 1, "Deli Kızın Türküsü"],
  ["Sezen Aksu", "Sen Ağlama", 2, "Deli Kızın Türküsü"],
  ["Barış Manço", "Gülpembe", 3, "Hal Hal"],
  ["Barış Manço", "Dönence", 4, "Hal Hal"],
  ["Tarkan", "Şımarık", 5, "Ölürüm Sana"],
  ["Tarkan", "Hepsi Senin Mi?", 6, "Ölürüm Sana"],
].forEach(([artist, title, track, album], i) => {
  const path = join(music, `Track ${String(i + 1).padStart(2, "0")}.mp3`);
  writeMp3(path, artist, title, track, album);
  utimesSync(path, when(3, 10, i), when(3, 10, i));
});

// ---- Uygulamayı sür -------------------------------------------------------------------------------------------------

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
let app = null;
let page = null;

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

async function waitFor(probe, description, timeout = 20_000) {
  const end = Date.now() + timeout;
  while (Date.now() < end) {
    const value = await evaluate(probe);
    if (value) return value;
    await sleep(150);
  }
  throw new Error(`Zaman aşımı: ${description}`);
}

async function type(selector, text) {
  await evaluate(`(() => { const el = document.querySelector(${JSON.stringify(selector)}); el.focus(); el.select(); return true; })()`);
  await page.send("Input.insertText", { text });
}

const clickByText = (tag, text) =>
  evaluate(`(() => { const el = [...document.querySelectorAll(${JSON.stringify(tag)})].find(e => e.textContent.includes(${JSON.stringify(text)})); if (!el) return false; el.click(); return true; })()`);

async function shot(name, scheme) {
  await page.send("Emulation.setEmulatedMedia", { features: [{ name: "prefers-color-scheme", value: scheme }] });
  await sleep(450);
  const { data } = await page.send("Page.captureScreenshot", { format: "png" });
  writeFileSync(join(out, `${name}-${scheme === "dark" ? "koyu" : "acik"}.png`), Buffer.from(data, "base64"));
}

async function open(folder) {
  await evaluate(`(() => { const el = document.querySelector('input[aria-label="Klasör yolu"]') ?? document.querySelector('.folderbar input'); el.focus(); el.select(); return true; })()`);
  await page.send("Input.insertText", { text: folder });
  await page.send("Input.dispatchKeyEvent", { type: "keyDown", key: "Enter", code: "Enter", windowsVirtualKeyCode: 13 });
  await page.send("Input.dispatchKeyEvent", { type: "keyUp", key: "Enter", code: "Enter", windowsVirtualKeyCode: 13 });
  await sleep(900);
}

try {
  try { execFileSync("subst", [drive, "/d"], { stdio: "ignore" }); } catch { /* sürücü zaten boş */ }
  execFileSync("subst", [drive, demo]);
  const photosShown = `${drive}\\Tatil Fotoğrafları`;
  const musicShown = `${drive}\\Müzik`;

  app = spawn(exe, [photosShown], {
    env: { ...process.env, TERZI_OFFSCREEN: "1", TERZI_DATA_DIR: dataDir, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: `--remote-debugging-port=${port}` },
    stdio: "ignore",
  });
  const target = await (async () => {
    const end = Date.now() + 40_000;
    while (Date.now() < end) {
      try {
        const list = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
        const t = list.find((x) => x.type === "page" && x.url.startsWith("https://app.dosyaterzisi/"));
        if (t) return t;
      } catch { /* henüz hazır değil */ }
      await sleep(300);
    }
    throw new Error("Uygulama sayfası açılmadı");
  })();
  page = await connect(target.webSocketDebuggerUrl);
  await page.send("Page.enable");
  await page.send("Runtime.enable");
  await page.send("Emulation.setDeviceMetricsOverride", { width: 1360, height: 840, deviceScaleFactor: 1, mobile: false });

  // İlk açılış turu: önce görüntüsünü al, sonra kapat.
  await waitFor(`!!document.querySelector('.tour')`, "tur görünsün");
  await clickByText(".tour button", "İleri");
  await sleep(500);
  await shot("tur", "light");
  await clickByText(".tour button", "Atla");
  await waitFor(`!document.querySelector('.tour')`, "tur kapansın");
  await waitFor(`document.querySelectorAll('.row').length >= 12`, "fotoğraflar listelensin");

  // 1) Adlandır: joker kuralı + önizleme
  await clickByText("button[role=tab]", "Joker");
  await type('input[aria-label="Aranan"]', "IMG_*.jpg");
  await type('input[aria-label="Yeni ad"]', "Antalya 2026 - ??.jpg");
  await waitFor(`document.querySelectorAll('.row').length > 0 && [...document.querySelectorAll('.row')].some(r => r.textContent.includes('Antalya 2026 - 01.jpg'))`, "önizleme gelsin");
  await shot("adlandir", "light");
  await shot("adlandir", "dark");

  // 2) Onay penceresi
  await evaluate(`document.querySelector('.actionbar .btn.primary').click(), true`);
  await waitFor(`!!document.querySelector('[role=dialog]')`, "onay penceresi");
  await sleep(300);
  await shot("onay", "light");
  await evaluate(`(() => { const b = [...document.querySelectorAll('[role=dialog] button')].find(x => /Vazgeç|İptal/.test(x.textContent)); b?.click(); return true; })()`);
  await sleep(300);

  // 3) Etiketten: müzik
  await open(musicShown);
  await clickByText("button[role=tab]", "Etiketten");
  await waitFor(`!!document.querySelector('input[aria-label="Şablon"]')`, "etiket paneli");
  await type('input[aria-label="Şablon"]', "<ID3.Artist> - <ID3.TrackNumber:00> <ID3.Track>.\\e");
  await waitFor(`[...document.querySelectorAll('.row')].some(r => r.textContent.includes('Sezen Aksu - 01 Gidiyorum.mp3'))`, "etiket önizlemesi", 30_000);
  await evaluate(`(() => { const el = document.querySelector('input[aria-label="Şablon"]'); el.setSelectionRange(0, 0); el.scrollLeft = 0; return true; })()`);
  await shot("etiketten", "light");
  await shot("etiketten", "dark");

  // 4) Öznitelikler
  await open(photosShown);
  await clickByText("button[role=tab]", "Öznitelikler");
  await sleep(900);
  await evaluate(`(() => { document.activeElement?.blur(); const on = [...document.querySelectorAll('button')].filter(b => b.textContent.trim() === 'Aç'); on[0]?.click(); on[2]?.click(); return on.length; })()`);
  await sleep(600);
  await shot("oznitelikler", "light");

  console.log(`Hazır: ${out}`);
} finally {
  page?.close();
  if (app && app.exitCode === null) {
    app.kill();
    await new Promise((r) => app.once("exit", r));
  }
  try { execFileSync("subst", [drive, "/d"], { stdio: "ignore" }); } catch { /* yok say */ }
  await sleep(500);
  rmSync(work, { recursive: true, force: true });
}
