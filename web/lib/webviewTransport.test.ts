import { describe, expect, it, vi } from "vitest";
import { createRpcBridge } from "./rpc";
import { createWebViewTransport, findWebView, type WebViewApi } from "./webviewTransport";
import { wireVersion } from "./wire.generated";

/** WebView2'nin `window.chrome.webview` nesnesi gibi davranan sahte. */
function fakeWebView() {
  const listeners = new Set<(event: { data: unknown }) => void>();
  const posted: unknown[] = [];
  const api: WebViewApi = {
    postMessage: (m) => void posted.push(m),
    addEventListener: (_type, l) => void listeners.add(l),
    removeEventListener: (_type, l) => void listeners.delete(l),
  };
  return { api, posted, listeners, receive: (data: unknown) => listeners.forEach((l) => l({ data })) };
}

describe("webview taşıyıcısı", () => {
  it("WebView2 yoksa (tarayıcı) null verir, varsa nesneyi verir", () => {
    expect(findWebView({})).toBeNull();
    expect(findWebView({ chrome: {} })).toBeNull();
    expect(findWebView({ chrome: { webview: { postMessage: 1 } } })).toBeNull();

    const { api } = fakeWebView();
    expect(findWebView({ chrome: { webview: api } })).toBe(api);
  });

  it("mesajı metin olarak gönderir", () => {
    const web = fakeWebView();
    createWebViewTransport(web.api).post('{"v":1}');

    expect(web.posted).toEqual(['{"v":1}']);
  });

  it("gelen metin mesajlarını iletir, metin olmayanları atar", () => {
    const web = fakeWebView();
    const handler = vi.fn();
    createWebViewTransport(web.api).subscribe(handler);

    web.receive('{"v":1,"id":1,"result":null}');
    web.receive({ v: 1 });
    web.receive(42);

    expect(handler).toHaveBeenCalledTimes(1);
    expect(handler).toHaveBeenCalledWith('{"v":1,"id":1,"result":null}');
  });

  it("aboneliği bırakınca dinleyici kalkar", () => {
    const web = fakeWebView();
    const stop = createWebViewTransport(web.api).subscribe(() => undefined);
    expect(web.listeners.size).toBe(1);

    stop();

    expect(web.listeners.size).toBe(0);
  });

  it("köprü, WebView2 nesnesi üzerinden istek gönderip cevap alır", async () => {
    const web = fakeWebView();
    const bridge = createRpcBridge(createWebViewTransport(web.api));

    const history = bridge.history({});
    expect(JSON.parse(web.posted[0] as string)).toEqual({ v: wireVersion, id: 1, method: "history", params: {} });

    web.receive(JSON.stringify({ v: wireVersion, id: 1, result: [{ id: "a", description: "d", count: 2, time: 5 }] }));

    await expect(history).resolves.toEqual([{ id: "a", description: "d", count: 2, time: 5 }]);
    bridge.dispose();
    expect(web.listeners.size).toBe(0);
  });
});
