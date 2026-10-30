import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Bridge } from "./contract";
import { MockBridge } from "./mock/mockBridge";
import { BridgeError, createRpcBridge, type Transport } from "./rpc";
import { AppStore } from "./store";
import { ContractError } from "./wire";
import { methodNames, wireVersion, type MethodName } from "./wire.generated";

/**
 * Uygulamanın (C#) yerine geçen sahte konak: istek metnini alır, bir köprüye (MockBridge) yaptırır ve
 * cevabı JSON metni olarak geri verir. Mesajlar gerçekte olduğu gibi metin olarak gidip gelir.
 */
class LoopbackHost implements Transport {
  readonly sent: string[] = [];
  private handlers = new Set<(message: string) => void>();
  /** Cevapları bu kadar geciktir (ms); işe göre değişebilir. */
  delay: (method: string) => number = () => 0;
  /** Sahte cevabı bozmak için. */
  tamper?: (method: string, response: Record<string, unknown>) => Record<string, unknown>;
  fail?: (method: string) => { code: string; message: string } | undefined;

  constructor(private readonly backend: Bridge) {}

  post(message: string) {
    this.sent.push(message);
    const request = JSON.parse(message) as { v: number; id: number; method: MethodName; params: unknown };

    void (async () => {
      let response: Record<string, unknown>;
      const failure = this.fail?.(request.method);
      if (failure) {
        response = { v: wireVersion, id: request.id, error: failure };
      } else {
        const run = this.backend[request.method] as (p: unknown) => Promise<unknown>;
        const result = await run(request.params);
        response = { v: wireVersion, id: request.id, result: result ?? null };
      }
      if (this.tamper) response = this.tamper(request.method, response);

      const wait = this.delay(request.method);
      setTimeout(() => this.emit(JSON.stringify(response)), wait);
    })();
  }

  subscribe(handler: (message: string) => void) {
    this.handlers.add(handler);
    return () => this.handlers.delete(handler);
  }

  emit(message: string) {
    this.handlers.forEach((h) => h(message));
  }
}

const settle = (ms = 700) => vi.advanceTimersByTimeAsync(ms);

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

describe("createRpcBridge", () => {
  it("her iş için bir işlev kurar ve istekleri sözleşme zarfıyla gönderir", async () => {
    const host = new LoopbackHost(new MockBridge());
    const bridge = createRpcBridge(host);
    expect(bridge.kind).toBe("webview");
    for (const method of methodNames) expect(typeof bridge[method]).toBe("function");

    const pending = bridge.pickFolder({ current: "C:\\x" });
    await settle();
    await pending;

    expect(JSON.parse(host.sent[0]!)).toEqual({ v: wireVersion, id: 1, method: "pickFolder", params: { current: "C:\\x" } });
  });

  it("cevabı çağıran işleve döndürür", async () => {
    const bridge = createRpcBridge(new LoopbackHost(new MockBridge()));

    const init = bridge.init({});
    await settle();

    const state = await init;
    expect(state.presets.length).toBeGreaterThan(5);
    expect(state.notices).toEqual([]);
  });

  it("birbirine karışmadan eşzamanlı istekleri cevaplar (cevaplar ters sırada gelse bile)", async () => {
    const host = new LoopbackHost(new MockBridge());
    host.delay = (method) => (method === "history" ? 500 : 10);
    const bridge = createRpcBridge(host);

    const slow = bridge.history({});
    const fast = bridge.addFavorite({ name: "Hızlı", rule: { method: "regex", search: "a", replace: "b", caseSensitive: false } });
    await settle(100);

    expect((await fast).at(-1)!.name).toBe("Hızlı");
    await settle(600);
    expect(await slow).toEqual([]);
  });

  it("uygulamanın hata cevabını BridgeError olarak fırlatır", async () => {
    const host = new LoopbackHost(new MockBridge());
    host.fail = (method) => (method === "scan" ? { code: "failed", message: "Klasör bulunamadı: C:\\yok" } : undefined);
    const bridge = createRpcBridge(host);

    const scan = bridge.scan({ folder: "C:\\yok", options: { includeFiles: true, includeFolders: false, filter: "*", recursive: false, depth: 10 } });
    const outcome = expect(scan).rejects.toMatchObject({ name: "BridgeError", code: "failed", message: "Klasör bulunamadı: C:\\yok" });
    await settle();
    await outcome;
    await expect(Promise.reject(new BridgeError("x", "y"))).rejects.toBeInstanceOf(Error);
  });

  it("sözleşmeye uymayan cevabı ContractError ile reddeder (kapatılabilir)", async () => {
    const host = new LoopbackHost(new MockBridge());
    host.tamper = (method, response) =>
      method === "history" ? { ...response, result: [{ id: 1, description: "x" }] } : response;

    const strict = createRpcBridge(host);
    const bad = strict.history({});
    const rejected = expect(bad).rejects.toBeInstanceOf(ContractError);
    await settle();
    await rejected;

    const lenient = createRpcBridge(host, { validate: false });
    const loose = lenient.history({});
    await settle();
    await expect(loose).resolves.toEqual([{ id: 1, description: "x" }]);
  });

  it("bize ait olmayan ya da bozuk mesajları yok sayar", async () => {
    const host = new LoopbackHost(new MockBridge());
    const bridge = createRpcBridge(host);
    const history = bridge.history({});

    host.emit("bu json değil");
    host.emit(JSON.stringify({ v: 99, id: 1, result: [] }));
    host.emit(JSON.stringify({ v: wireVersion, id: 999, result: [] }));
    host.emit(JSON.stringify({ v: wireVersion, id: 1 }));
    host.emit(JSON.stringify({ v: wireVersion, id: 1, error: { code: 5 } }));
    host.emit("42");
    await settle();

    await expect(history).resolves.toEqual([]);
  });

  it("aynı cevap iki kez gelirse ikincisi sessizce atılır", async () => {
    const host = new LoopbackHost(new MockBridge());
    const bridge = createRpcBridge(host);
    const history = bridge.history({});
    await settle();
    await history;

    expect(() => host.emit(JSON.stringify({ v: wireVersion, id: 1, result: [] }))).not.toThrow();
  });

  it("gönderme başarısız olursa çağrı hata ile biter", async () => {
    const transport: Transport = {
      post: () => {
        throw new Error("WebView hazır değil");
      },
      subscribe: () => () => undefined,
    };

    await expect(createRpcBridge(transport).init({})).rejects.toThrow("WebView hazır değil");
  });

  it("dispose bekleyenleri kapatır ve dinlemeyi bırakır", async () => {
    const host = new LoopbackHost(new MockBridge());
    host.delay = () => 10_000;
    const bridge = createRpcBridge(host);

    const waiting = bridge.history({});
    const closed = expect(waiting).rejects.toMatchObject({ code: "closed" });
    bridge.dispose();
    await closed;

    const late = bridge.history({});
    host.emit(JSON.stringify({ v: wireVersion, id: 2, result: [] }));
    await settle(20_000);
    // Dinleme bırakıldığı için sahte konağın geç cevabı bile işlenmez; çağrı askıda kalır.
    let done = false;
    void late.then(() => (done = true), () => (done = true));
    await settle(10);
    expect(done).toBe(false);
  });
});

describe("ekranın tamamı, köprü üzerinden (mesajlar JSON olarak gidip geliyor)", () => {
  it("klasörü listeler, önizler, adlandırır ve geri alır", async () => {
    const host = new LoopbackHost(new MockBridge());
    host.delay = () => 15;
    const bridge = createRpcBridge(host);
    const store = new AppStore(bridge);

    store.subscribe(() => {
      const dialog = store.getState().dialog;
      if (dialog?.kind === "confirm") queueMicrotask(() => store.answer("proceed"));
    });

    const started = store.start();
    await settle(3000);
    await started;

    let s = store.getState();
    expect(s.rows.length).toBeGreaterThan(20);
    expect(s.previews).toHaveLength(s.rows.length);
    const before = s.rows.map((r) => r.name);

    store.setRule({ method: "wildcard", search: "*.*", replace: "yeni_\\1.\\2" });
    await settle(3000);
    expect(store.getState().readyCount).toBe(s.rows.length);

    const renaming = store.rename();
    await settle(5000);
    await renaming;
    s = store.getState();
    expect(s.rows.every((r) => r.name.startsWith("yeni_"))).toBe(true);
    expect(s.history).toHaveLength(1);

    const undoing = store.undo();
    await settle(5000);
    await undoing;
    expect(store.getState().rows.map((r) => r.name)).toEqual(before);

    // Her istek sözleşmeye uygun bir zarfla gitmiş olmalı.
    for (const raw of host.sent) {
      const request = JSON.parse(raw) as Record<string, unknown>;
      expect(request.v).toBe(wireVersion);
      expect(Object.keys(request).sort()).toEqual(["id", "method", "params", "v"]);
    }
  });
});
