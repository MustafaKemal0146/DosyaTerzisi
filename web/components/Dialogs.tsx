"use client";

import { useEffect, useRef, useState } from "react";
import { Icon } from "./Icon";
import { FavoritesDialog, HistoryDialog, SettingsDialog } from "./ManagerDialogs";
import { Modal } from "./Modal";
import type { AppState, AppStore, Dialog } from "@/lib/store";
import type { Operation, RenameRequest } from "@/lib/contract";
import { operationText } from "@/lib/operation";

export function Dialogs({ store, state }: { store: AppStore; state: AppState }) {
  const dialog: Dialog | null = state.dialog;
  if (!dialog) return null;

  switch (dialog.kind) {
    case "favorites":
      return <FavoritesDialog store={store} favorites={state.favorites} />;
    case "history":
      return <HistoryDialog store={store} state={state} />;
    case "settings":
      return <SettingsDialog store={store} state={state} />;
    case "ask":
      return (
        <Modal small onEscape={() => store.answer(false)}>
          <div className="dialog-body">
            <h2>{dialog.title}</h2>
            <p style={{ whiteSpace: "pre-wrap" }}>{dialog.message}</p>
          </div>
          <div className="dialog-footer">
            <button className="btn" onClick={() => store.answer(false)}>İptal</button>
            <button className="btn primary" autoFocus onClick={() => store.answer(true)}>{dialog.confirm}</button>
          </div>
        </Modal>
      );
    case "confirm":
      return <ConfirmRename store={store} request={dialog.request} operation={store.currentOperation()} />;
    case "prompt":
      return <PromptDialog store={store} dialog={dialog} />;
    case "notice":
      return (
        <Modal onEscape={() => store.answer(undefined)}>
          <div className="dialog-body">
            <h2>{dialog.title}</h2>
            <p style={{ whiteSpace: "pre-wrap", userSelect: "text" }}>{dialog.message}</p>
          </div>
          <div className="dialog-footer">
            <button className="btn primary" autoFocus onClick={() => store.answer(undefined)}>Tamam</button>
          </div>
        </Modal>
      );
    case "failures":
      return (
        <Modal onEscape={() => store.answer(undefined)}>
          <div className="dialog-body">
            <h2>{dialog.title}</h2>
            <ul className="failure-list">
              {dialog.lines.slice(0, 12).map((line, i) => <li key={i}>{line}</li>)}
            </ul>
            {dialog.lines.length > 12 && <p>… ve {dialog.lines.length - 12} tane daha.</p>}
          </div>
          <div className="dialog-footer">
            <button className="btn primary" autoFocus onClick={() => store.answer(undefined)}>Tamam</button>
          </div>
        </Modal>
      );
  }
}

function ConfirmRename({ store, request, operation }: { store: AppStore; request: RenameRequest; operation: Operation }) {
  const text = operationText[operation];
  const problems = request.conflicts.length > 0 || request.otherProblems > 0;
  const cancel = () => store.answer("cancel");

  return (
    <Modal onEscape={cancel}>
      <div className="dialog-body">
        <h2>{request.ready > 0 ? `${request.ready.toLocaleString("tr-TR")} öğe ${text.will}` : "Hiçbir öğe olduğu gibi adlandırılamıyor"}</h2>
        {!problems && <p>İşlem bittikten sonra Geri Al ile hepsini eski adlarına döndürebilirsiniz.</p>}
        {request.conflicts.length > 0 && (
          <>
            <p>
              <Icon name="alert" size={15} /> {request.conflicts.length} öğenin yeni adı aynı klasörde zaten kullanılıyor. Hiçbir dosyanın üzerine yazılmaz.
            </p>
            <div className="conflict-table">
              <table>
                <thead>
                  <tr><th>Mevcut ad</th><th>İstenen ad (zaten var)</th><th>Klasör</th></tr>
                </thead>
                <tbody>
                  {request.conflicts.slice(0, 100).map((c, i) => (
                    <tr key={i}>
                      <td title={c.currentName}>{c.currentName}</td>
                      <td title={c.wantedName}>{c.wantedName}</td>
                      <td title={c.folder}>{c.folder}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
        {request.otherProblems > 0 && <p>{request.otherProblems} öğenin yeni adı geçersiz ya da kural hatalı; bunlar atlanır.</p>}
        {problems && <p>İşlem bittikten sonra Geri Al ile hepsini eski adlarına döndürebilirsiniz.</p>}
      </div>
      <div className="dialog-footer">
        <button className="btn" onClick={cancel}>İptal</button>
        {request.conflicts.length > 0 && (
          <button className="btn" title="Çakışan adların sonuna (01), (02) … eklenir" onClick={() => store.answer("autoNumber")}>
            Numara ekleyerek hepsini adlandır
          </button>
        )}
        <button className="btn primary" autoFocus disabled={request.ready === 0} onClick={() => store.answer("proceed")}>
          {problems ? "Sorunluları atla, devam et" : "Devam et"}
        </button>
      </div>
    </Modal>
  );
}

function PromptDialog({ store, dialog }: { store: AppStore; dialog: Extract<Dialog, { kind: "prompt" }> }) {
  const [value, setValue] = useState(dialog.initial);
  const input = useRef<HTMLInputElement>(null);
  useEffect(() => input.current?.select(), []);
  const cancel = () => store.answer(null);

  return (
    <Modal small onEscape={cancel}>
      <div className="dialog-body">
        <h2>{dialog.title}</h2>
        <p>{dialog.label}</p>
        <input
          ref={input}
          className="input"
          autoFocus
          value={value}
          onChange={(e) => setValue(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && value.trim() && store.answer(value)}
        />
      </div>
      <div className="dialog-footer">
        <button className="btn" onClick={cancel}>İptal</button>
        <button className="btn primary" disabled={!value.trim()} onClick={() => store.answer(value)}>Tamam</button>
      </div>
    </Modal>
  );
}
