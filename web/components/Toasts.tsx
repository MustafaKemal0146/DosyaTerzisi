"use client";

import { Icon } from "./Icon";
import type { AppStore, Toast } from "@/lib/store";

export function Toasts({ store, toasts }: { store: AppStore; toasts: Toast[] }) {
  return (
    <div className="toasts" role="status" aria-live="polite">
      {toasts.map((toast) => (
        <div key={toast.id} className={`toast ${toast.tone}`}>
          <span className="tone"><Icon name={toast.tone === "error" ? "alert" : toast.tone === "success" ? "check" : "info"} size={18} /></span>
          <span className="msg">{toast.text}</span>
          <button className="btn icon small subtle" aria-label="Kapat" onClick={() => store.dismissToast(toast.id)}>
            <Icon name="x" size={14} />
          </button>
        </div>
      ))}
    </div>
  );
}
