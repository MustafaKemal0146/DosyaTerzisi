// Köprünün gerçek hâli: her iş bir JSON mesajına çevrilip uygulamaya gönderilir, cevap gelince çözülür.
// Mesaj taşıma işi (WebView2) `Transport` ile soyutlanır; böylece bu katman tarayıcıda da denenebilir.

import type { Bridge } from "./contract";
import { validateResult, ContractError } from "./wire";
import { methodNames, wireVersion, type MethodName } from "./wire.generated";

export interface Transport {
  /** Uygulamaya bir mesaj (JSON metni) gönderir; `attachments` varsa yanında nesneler de gider (sürüklenen dosyalar). */
  post(message: string, attachments?: ArrayLike<unknown>): void;
  /** Uygulamadan gelen mesajları dinler; dönen işlev dinlemeyi bırakır. */
  subscribe(handler: (message: string) => void): () => void;
}

/** Uygulamanın verdiği hata cevabı. `code`: badRequest, unknownMethod, failed, internal. */
export class BridgeError extends Error {
  constructor(
    readonly code: string,
    message: string,
  ) {
    super(message);
    this.name = "BridgeError";
  }
}

export interface RpcOptions {
  /** Gelen cevapları sözleşmeye göre denetle. Hatalıysa çağrı `ContractError` ile biter. Varsayılan: açık. */
  validate?: boolean;
}

interface Pending {
  method: MethodName;
  resolve: (value: unknown) => void;
  reject: (error: Error) => void;
}

export function createRpcBridge(transport: Transport, options: RpcOptions = {}): Bridge & { dispose(): void } {
  const validate = options.validate ?? true;
  const pending = new Map<number, Pending>();
  let nextId = 0;

  const stop = transport.subscribe((raw) => {
    const message = parse(raw);
    if (!message) return; // Bize ait olmayan ya da bozuk mesaj: yok say.

    const waiting = pending.get(message.id);
    if (!waiting) return;
    pending.delete(message.id);

    if (message.error) {
      waiting.reject(new BridgeError(message.error.code, message.error.message));
    } else if (validate) {
      const problems = validateResult(waiting.method, message.result);
      if (problems.length > 0) waiting.reject(new ContractError(waiting.method, "result", problems));
      else waiting.resolve(message.result);
    } else {
      waiting.resolve(message.result);
    }
  });

  const bridge: Record<string, unknown> = {
    kind: "webview",
    dispose() {
      stop();
      for (const waiting of pending.values()) waiting.reject(new BridgeError("closed", "Köprü kapatıldı."));
      pending.clear();
    },
  };

  for (const method of methodNames) {
    bridge[method] = (params: unknown, attachments?: ArrayLike<unknown>) =>
      new Promise((resolve, reject) => {
        const id = ++nextId;
        pending.set(id, { method, resolve, reject });
        try {
          transport.post(JSON.stringify({ v: wireVersion, id, method, params }), attachments);
        } catch (error) {
          pending.delete(id);
          reject(error instanceof Error ? error : new Error(String(error)));
        }
      });
  }

  return bridge as Bridge & { dispose(): void };
}

interface Response {
  id: number;
  result?: unknown;
  error?: { code: string; message: string };
}

function parse(raw: string): Response | null {
  let value: unknown;
  try {
    value = JSON.parse(raw);
  } catch {
    return null;
  }
  if (typeof value !== "object" || value === null) return null;

  const message = value as Record<string, unknown>;
  if (message.v !== wireVersion || typeof message.id !== "number") return null;

  if ("error" in message) {
    const error = message.error as Record<string, unknown> | null;
    if (typeof error?.code !== "string" || typeof error.message !== "string") return null;
    return { id: message.id, error: { code: error.code, message: error.message } };
  }
  return "result" in message ? { id: message.id, result: message.result } : null;
}
