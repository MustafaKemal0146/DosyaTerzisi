// Arayüz ile Windows uygulaması (C#) arasındaki sözleşme.
// Tipler C# tarafından otomatik üretilir (wire.generated.ts); burada yalnızca onlardan türeyen yardımcılar var.

import type { ListingOptions, MethodName, Methods, Rule } from "./wire.generated";

export * from "./wire.generated";

/**
 * Arayüzün Windows uygulamasından istediği her şey. Her iş, sözleşmedeki (`Methods`) bir isimdir:
 * `bridge.scan({ folder, options })`. Gerçek uygulamada JSON mesajıyla gider (rpc.ts),
 * tarayıcıda sahte bir uygulama (mock/mockBridge.ts) cevap verir.
 * Sözleşmeye yeni bir iş eklenirse, bu tip yüzünden sahte köprü derlenmez: ikisi de eksiksiz olmak zorunda.
 */
export type Bridge = {
  readonly kind: "mock" | "webview";
} & {
  /**
   * `attachments`: mesajla birlikte gönderilen nesneler. Yalnızca sürükle-bırak için: bırakılan dosyaların (`File`) yolları
   * JavaScript'e verilmez, WebView2 bunları uygulamaya bu yoldan iletir ve uygulama `paths` alanını doldurur.
   */
  [M in MethodName]: (params: Methods[M]["params"], attachments?: ArrayLike<unknown>) => Promise<Methods[M]["result"]>;
};

export const defaultRule: Rule = {
  method: "translation",
  search: "ÜüĞğİıŞşÇçÖö ",
  replace: "UuGgIiSsCcOo_",
  caseSensitive: false,
  numberStart: 1,
  numberStep: 1,
  resetNumberPerFolder: false,
  autoNumberOnConflict: false,
  useCreatedDate: false,
  operation: "rename",
  targetFolder: "",
  numberStyle: "decimal",
  resetNumberPerDate: false,
  resetNumberPerTag: "",
  shortNames83: false,
  useScript: false,
};

export const defaultListing: ListingOptions = {
  includeFiles: true,
  includeFolders: false,
  filter: "*",
  recursive: false,
  depth: 10,
};
