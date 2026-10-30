import type { Bridge } from "./contract";
import { MockBridge } from "./mock/mockBridge";
import { createRpcBridge } from "./rpc";
import { createWebViewTransport, findWebView } from "./webviewTransport";

/**
 * Hangi köprü kullanılacak?
 * - Windows uygulamasının içinde (WebView2): gerçek köprü; her iş JSON mesajı olarak uygulamaya gider.
 * - Tarayıcıda: sahte köprü, bellekte bir dosya sistemi. `?big=5000` yazılırsa binlerce dosyalı bir klasör gösterir.
 */
export function createBridge(): Bridge {
  const webview = findWebView();
  if (webview) return createRpcBridge(createWebViewTransport(webview));

  const big = typeof window === "undefined" ? 0 : Number(new URLSearchParams(window.location.search).get("big") ?? 0);
  return new MockBridge(Number.isFinite(big) ? Math.min(Math.max(big, 0), 100_000) : 0);
}
