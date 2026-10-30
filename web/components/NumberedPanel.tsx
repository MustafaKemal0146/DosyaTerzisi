"use client";

import { Icon } from "./Icon";
import { NumberStyleField } from "./NumberStyleField";
import type { NumberedPlan, ObjectKind, PlannedStatus } from "@/lib/contract";
import type { AppState, AppStore } from "@/lib/store";

const kinds: { id: ObjectKind; label: string }[] = [
  { id: "emptyFile", label: "Boş dosya" },
  { id: "copyOfFile", label: "Bir dosyanın kopyaları" },
  { id: "emptyFolder", label: "Boş klasör" },
  { id: "copyOfFolder", label: "Bir klasörün kopyaları" },
];

const statusText: Record<PlannedStatus, string> = {
  ready: "Oluşturulacak",
  exists: "Zaten var",
  invalidName: "Geçersiz ad",
};

export function NumberedPanel({ store, state }: { store: AppStore; state: AppState }) {
  const { numbered, folder, numberedPlan } = state;
  const copies = numbered.kind === "copyOfFile" || numbered.kind === "copyOfFolder";
  const target = numbered.folder || folder;

  const number = (key: "amount" | "start" | "step", min: number) => (
    <input
      className="input narrow"
      inputMode="numeric"
      value={numbered[key]}
      aria-label={key === "amount" ? "Adet" : key === "start" ? "Başlangıç" : "Adım"}
      onChange={(e) => store.setNumbered({ [key]: Math.max(min, Number(e.target.value.replace(/\D/g, "")) || min) })}
    />
  );

  return (
    <section className="card rulepanel" aria-label="Numaralı nesneler">
      <div className="field-label">Ne oluşturulsun?</div>
      <select className="input" aria-label="Tür" value={numbered.kind} onChange={(e) => store.setNumbered({ kind: e.target.value as ObjectKind })}>
        {kinds.map((k) => <option key={k.id} value={k.id}>{k.label}</option>)}
      </select>

      <div className="field-label">Nerede?</div>
      <div className="row-between">
        <input className="input" value={target} aria-label="Hedef klasör" spellCheck={false} onChange={(e) => store.setNumbered({ folder: e.target.value })} />
      </div>
      <div className="small-text muted" style={{ marginTop: 4 }}>Varsayılan: yukarıda seçili klasör.</div>

      <div className="field-label">Ad</div>
      <input className="input mono" value={numbered.namePattern} spellCheck={false} aria-label="Ad şablonu" onChange={(e) => store.setNumbered({ namePattern: e.target.value })} />
      <div className="token-row">
        {["??", "???"].map((t) => (
          <button key={t} className="chip" title="Sıra numarası" onClick={() => store.setNumbered({ namePattern: numbered.namePattern + t })}>{t}</button>
        ))}
      </div>
      <p className="hint">? işareti sayının yeridir (?? iki basamak). İşaret yazmazsanız sayı uzantıdan önce eklenir.</p>

      <div className="three-fields">
        <label>Adet {number("amount", 1)}</label>
        <label>Başlangıç {number("start", 0)}</label>
        <label>Adım {number("step", 1)}</label>
      </div>

      <NumberStyleField value={numbered.numberStyle} onChange={(v) => store.setNumbered({ numberStyle: v })} />

      {numbered.kind === "emptyFile" && (
        <>
          <div className="field-label">Boyut</div>
          <input className="input narrow" style={{ width: 120, textAlign: "left" }} value={numbered.size} placeholder="0" aria-label="Boyut" onChange={(e) => store.setNumbered({ size: e.target.value })} />
          <span className="small-text muted" style={{ marginLeft: 8 }}>512, 10K, 1,5M, 2G</span>
        </>
      )}

      {copies && (
        <>
          <div className="field-label">{numbered.kind === "copyOfFile" ? "Kopyalanacak dosya" : "Kopyalanacak klasör"}</div>
          <div className="row-between">
            <input className="input" value={numbered.sourcePath} readOnly placeholder="Seçilmedi" aria-label="Kaynak" />
            <button className="btn" onClick={() => void store.pickSource()}>Seç…</button>
          </div>
        </>
      )}

      {numberedPlan?.problem && (
        <div className="rule-error" role="alert">
          <Icon name="alert" size={16} />
          <span>{numberedPlan.problem}</span>
        </div>
      )}
      <p className="hint">Var olan hiçbir dosya ya da klasörün üzerine yazılmaz.</p>
    </section>
  );
}

export function NumberedList({ plan }: { plan: NumberedPlan | null }) {
  if (!plan || plan.items.length === 0)
    return (
      <div className="empty">
        <div className="big-icon"><Icon name="hash" size={34} /></div>
        <h2>Oluşturulacak öğeler burada görünür</h2>
        <p>Soldan türü, adı ve adedi seçin; adlar yazdıkça listelenir. “Oluştur” düğmesine basana kadar hiçbir şey yapılmaz.</p>
      </div>
    );

  return (
    <div className="scroller">
      <div className="thead" style={{ gridTemplateColumns: "minmax(0, 1fr) 140px", minWidth: 0 }}>
        <div>Ad</div>
        <div>Durum</div>
      </div>
      <div style={{ minWidth: 0 }}>
        {plan.items.slice(0, 2000).map((item, i) => (
          <div key={i} className="planned-row" title={item.message ?? undefined}>
            <div className="cell name"><Icon name="file" size={16} className="file-icon" /><span className="text">{item.name}</span></div>
            <div className="cell">
              <span className={`badge ${item.status === "ready" ? "ready" : "bad"}`}>{statusText[item.status]}</span>
            </div>
          </div>
        ))}
        {plan.items.length > 2000 && <div className="tree-note">… ve {plan.items.length - 2000} tane daha (hepsi oluşturulur).</div>}
      </div>
    </div>
  );
}
