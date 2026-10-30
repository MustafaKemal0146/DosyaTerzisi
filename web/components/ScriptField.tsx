"use client";

import type { AppStore } from "@/lib/store";
import { Switch } from "./Switch";

/** "Betik" anahtarı: açıkken yeni ad alanı bir VBScript/JScript ifadesi olarak değerlendirilir. */
export function ScriptField({ store, value, onChange }: { store: AppStore; value: boolean; onChange: (value: boolean) => void }) {
  return (
    <div>
      <Switch spread label="Betik (yeni ad bir ifade olarak değerlendirilir)" checked={value} onChange={onChange} />
      <button type="button" className="btn small subtle" onClick={() => void store.editScript()}>
        Betiği düzenle…
      </button>
      {value && (
        <p className="hint">
          Metin için çift tırnak kullanın: <code>{'Buyuk("\\1") & "." & "\\2"'}</code>. Tırnaksız <code>{"\\1+\\1"}</code> sayıları toplar.
          Kendi işlevlerinizi “Betiği düzenle” ile açılan dosyada tanımlayın.
        </p>
      )}
    </div>
  );
}
