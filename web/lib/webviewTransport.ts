import type { Transport } from "./rpc";

/** WebView2'nin sayfaya verdiği `window.chrome.webview` nesnesinin kullandığımız kısmı. */
export interface WebViewApi {
  postMessage(message: unknown): void;
  /** Bırakılan dosyaları (File nesneleri) uygulamaya yollarıyla birlikte iletir. */
  postMessageWithAdditionalObjects?(message: unknown, objects: ArrayLike<unknown>): void;
  addEventListener(type: "message", listener: (event: { data: unknown }) => void): void;
  removeEventListener(type: "message", listener: (event: { data: unknown }) => void): void;
}

/** Sayfa bir WebView2 penceresinin içinde çalışıyorsa onun mesaj nesnesini verir; tarayıcıdaysa null. */
export function findWebView(root: object = globalThis): WebViewApi | null {
  const chrome = (root as { chrome?: { webview?: WebViewApi } }).chrome;
  const webview = chrome?.webview;
  return webview && typeof webview.postMessage === "function" && typeof webview.addEventListener === "function" ? webview : null;
}

/** Mesajlar metin olarak gider ve gelir; uygulama (C#) `PostWebMessageAsString` ile cevap verir. */
export function createWebViewTransport(api: WebViewApi): Transport {
  return {
    post(message, attachments) {
      if (attachments && attachments.length > 0 && api.postMessageWithAdditionalObjects)
        api.postMessageWithAdditionalObjects(message, attachments);
      else api.postMessage(message);
    },
    subscribe(handler) {
      const listener = (event: { data: unknown }) => {
        if (typeof event.data === "string") handler(event.data);
      };
      api.addEventListener("message", listener);
      return () => api.removeEventListener("message", listener);
    },
  };
}
