"use client";

import { Icon } from "./Icon";
import { operationText } from "@/lib/operation";
import type { AppState, AppStore } from "@/lib/store";

const n = (value: number) => value.toLocaleString("tr-TR");

export function ActionBar({ store, state }: { store: AppStore; state: AppState }) {
  const { tab, rows, selection, readyCount, problemCount, history, busy, scanning, skippedFolders, numberedPlan } = state;
  const latest = history[0];

  return (
    <footer className="card actionbar">
      <div className="summary" aria-live="polite">
        {tab === "numbered" ? (
          <>
            <span className="item">{n(numberedPlan?.items.length ?? 0)} öğe</span>
            <span className="item"><span className="dot ready" />{n(numberedPlan?.ready ?? 0)} oluşturulacak</span>
            {(numberedPlan?.items.length ?? 0) - (numberedPlan?.ready ?? 0) > 0 && (
              <span className="item"><span className="dot bad" />{n((numberedPlan?.items.length ?? 0) - (numberedPlan?.ready ?? 0))} sorunlu</span>
            )}
          </>
        ) : (
          <>
            <span className="item">{n(rows.length)} öğe</span>
            {selection.size > 0 && <span className="item"><span className="dot sel" />{n(selection.size)} seçili</span>}
            <span className="item">
              <span className="dot ready" />
              {n(tab === "attributes" ? state.attrChanged : readyCount)} değişecek
            </span>
            {(tab === "attributes" ? state.attrProblems : problemCount) > 0 && (
              <span className="item"><span className="dot bad" />{n(tab === "attributes" ? state.attrProblems : problemCount)} sorunlu</span>
            )}
            {skippedFolders > 0 && <span className="item">{skippedFolders} öğe okunamadı</span>}
          </>
        )}
      </div>

      {tab === "attributes" ? (
        <>
          <button
            className="btn"
            disabled={busy || state.attrUndoCount === 0}
            title={state.attrUndoCount === 0 ? "Geri alınacak değişiklik yok" : "Son öznitelik/tarih değişikliğini geri al (program açıkken)"}
            onClick={() => void store.undoAttributes()}
          >
            <Icon name="undo" /> Geri Al
          </button>
          <button className="btn primary" style={{ minWidth: 190, height: 36 }} disabled={busy || scanning || state.attrChanged === 0} onClick={() => void store.applyAttributes()}>
            <Icon name="sliders" /> {state.attrChanged > 0 ? `${n(state.attrChanged)} öğeyi değiştir` : "Değiştir"}
          </button>
        </>
      ) : tab === "numbered" ? (
        <button
          className="btn primary"
          style={{ minWidth: 190, height: 36 }}
          disabled={busy || !numberedPlan || !!numberedPlan.problem || numberedPlan.ready === 0}
          onClick={() => void store.createNumbered()}
        >
          <Icon name="hash" /> {numberedPlan && numberedPlan.ready > 0 ? `${n(numberedPlan.ready)} öğe oluştur` : "Oluştur"}
        </button>
      ) : (
        <>
          <div className="split">
            <button
              className="btn"
              disabled={busy || !latest}
              title={latest ? `Geri al: ${latest.description}` : "Geri alınacak işlem yok"}
              onClick={() => void store.undo()}
            >
              <Icon name="undo" /> Geri Al
            </button>
            <button className="btn" aria-label="Geçmiş" title="Geçmiş" disabled={history.length === 0} onClick={() => void store.showHistory()}>
              <Icon name="history" size={15} />
            </button>
          </div>

          <button className="btn primary" style={{ minWidth: 190, height: 36 }} disabled={busy || scanning || (readyCount === 0 && problemCount === 0)} onClick={() => void store.rename()}>
            <Icon name="rename" /> {readyCount > 0 ? `${n(readyCount)} öğeyi ${operationText[store.currentOperation()].action}` : operationText[store.currentOperation()].button}
          </button>
        </>
      )}
    </footer>
  );
}
