"use client";

import { useState } from "react";
import { Switch } from "./Switch";
import type { AttributeRule, TimeAction, TimeKind, TimeRule, TriState } from "@/lib/contract";
import type { AppState, AppStore } from "@/lib/store";

const flags: { key: "readOnly" | "archive" | "hidden" | "system"; label: string }[] = [
  { key: "readOnly", label: "Salt okunur" },
  { key: "archive", label: "Arşiv" },
  { key: "hidden", label: "Gizli" },
  { key: "system", label: "Sistem" },
];

const times: { key: "modified" | "created" | "accessed"; label: string }[] = [
  { key: "modified", label: "Değiştirilme tarihi" },
  { key: "created", label: "Oluşturulma tarihi" },
  { key: "accessed", label: "Erişim tarihi" },
];

const actions: { id: TimeAction; label: string }[] = [
  { id: "keep", label: "Dokunma" },
  { id: "setTo", label: "Şuna ayarla" },
  { id: "shift", label: "İleri / geri kaydır" },
  { id: "copyFrom", label: "Başka tarihten al" },
  { id: "photoTaken", label: "Fotoğrafın çekim tarihi" },
  { id: "random", label: "Rastgele (aralıktan)" },
];

const kinds: { id: TimeKind; label: string }[] = [
  { id: "modified", label: "Değiştirilme tarihi" },
  { id: "created", label: "Oluşturulma tarihi" },
  { id: "accessed", label: "Erişim tarihi" },
];

const units = [
  { id: "minute", label: "dakika", minutes: 1 },
  { id: "hour", label: "saat", minutes: 60 },
  { id: "day", label: "gün", minutes: 1440 },
] as const;

/** `datetime-local` kutusu yerel saat ister: epoch ms ↔ "2026-10-27T14:30". */
function toLocalInput(ms: number): string {
  const d = new Date(ms || Date.now());
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

function TriControl({ value, onChange, label }: { value: TriState; onChange: (v: TriState) => void; label: string }) {
  const options: { id: TriState; text: string }[] = [
    { id: "keep", text: "Dokunma" },
    { id: "set", text: "Aç" },
    { id: "clear", text: "Kapat" },
  ];
  return (
    <div className="mini-seg" role="radiogroup" aria-label={label}>
      {options.map((o) => (
        <button key={o.id} role="radio" aria-checked={value === o.id} className={`${value === o.id ? "active" : ""} ${o.id !== "keep" ? o.id : ""}`} onClick={() => onChange(o.id)}>
          {o.text}
        </button>
      ))}
    </div>
  );
}

function TimeEditor({ label, rule, onChange }: { label: string; rule: TimeRule; onChange: (rule: TimeRule) => void }) {
  // Kaydırma miktarı dakika olarak saklanır; ekranda en uygun birimle gösterilir.
  const [unit, setUnit] = useState<(typeof units)[number]["id"]>(
    rule.shiftMinutes !== 0 && rule.shiftMinutes % 1440 === 0 ? "day" : rule.shiftMinutes !== 0 && rule.shiftMinutes % 60 === 0 ? "hour" : "minute",
  );
  const minutesPerUnit = units.find((u) => u.id === unit)!.minutes;

  return (
    <div className="time-box">
      <div className="row-between">
        <strong>{label}</strong>
        <select
          className="input"
          style={{ width: 190 }}
          aria-label={`${label}: ne yapılsın`}
          value={rule.action}
          onChange={(e) => onChange({ ...rule, action: e.target.value as TimeAction, value: rule.value || Date.now() })}
        >
          {actions.map((a) => <option key={a.id} value={a.id}>{a.label}</option>)}
        </select>
      </div>

      {rule.action === "setTo" && (
        <input
          type="datetime-local"
          className="input"
          aria-label={`${label}: yeni tarih`}
          value={toLocalInput(rule.value)}
          onChange={(e) => {
            const ms = new Date(e.target.value).getTime();
            if (Number.isFinite(ms)) onChange({ ...rule, value: ms });
          }}
        />
      )}

      {rule.action === "shift" && (
        <div className="time-inputs">
          <input
            className="input narrow"
            style={{ width: 90 }}
            inputMode="numeric"
            aria-label={`${label}: kaydırma miktarı`}
            value={rule.shiftMinutes / minutesPerUnit}
            onChange={(e) => {
              const n = Number(e.target.value.replace(/[^\d-]/g, ""));
              if (Number.isFinite(n)) onChange({ ...rule, shiftMinutes: n * minutesPerUnit });
            }}
          />
          <select
            className="input"
            aria-label={`${label}: birim`}
            value={unit}
            onChange={(e) => {
              const next = e.target.value as typeof unit;
              const amount = rule.shiftMinutes / minutesPerUnit;
              setUnit(next);
              onChange({ ...rule, shiftMinutes: amount * units.find((u) => u.id === next)!.minutes });
            }}
          >
            {units.map((u) => <option key={u.id} value={u.id}>{u.label}</option>)}
          </select>
        </div>
      )}
      {rule.action === "shift" && <div className="small-text muted">Eksi sayı geriye, artı sayı ileriye kaydırır. Her dosya kendi tarihinden başlar.</div>}

      {rule.action === "copyFrom" && (
        <select className="input" aria-label={`${label}: hangi tarihten`} value={rule.copyFrom} onChange={(e) => onChange({ ...rule, copyFrom: e.target.value as TimeKind })}>
          {kinds.map((k) => <option key={k.id} value={k.id}>{k.label}</option>)}
        </select>
      )}

      {rule.action === "random" && (
        <div className="time-random">
          <label className="small-text muted">En erken</label>
          <input
            type="datetime-local"
            className="input"
            aria-label={`${label}: en erken tarih`}
            value={toLocalInput(rule.value)}
            onChange={(e) => {
              const ms = new Date(e.target.value).getTime();
              if (Number.isFinite(ms)) onChange({ ...rule, value: ms });
            }}
          />
          <label className="small-text muted">En geç</label>
          <input
            type="datetime-local"
            className="input"
            aria-label={`${label}: en geç tarih`}
            value={toLocalInput(rule.randomTo || rule.value)}
            onChange={(e) => {
              const ms = new Date(e.target.value).getTime();
              if (Number.isFinite(ms)) onChange({ ...rule, randomTo: ms });
            }}
          />
          <button className="btn small" onClick={() => onChange({ ...rule, seed: rule.seed + 1 })}>Yeniden karıştır</button>
          <div className="small-text muted">Her dosyaya aralıktan ayrı bir zaman verilir. Listede gördüğünüz, uygulanacak olanın aynısıdır.</div>
        </div>
      )}

      {rule.action !== "keep" && (
        <details className="time-limits">
          <summary>Sınırlar (isteğe bağlı)</summary>
          <label className="small-text muted">Bundan önce olmasın</label>
          <div className="row-inline">
            <input
              type="datetime-local"
              className="input"
              aria-label={`${label}: en erken sınır`}
              value={rule.limitMin ? toLocalInput(rule.limitMin) : ""}
              onChange={(e) => {
                const ms = e.target.value ? new Date(e.target.value).getTime() : 0;
                if (Number.isFinite(ms)) onChange({ ...rule, limitMin: ms });
              }}
            />
          </div>
          <label className="small-text muted">Bundan sonra olmasın</label>
          <div className="row-inline">
            <input
              type="datetime-local"
              className="input"
              aria-label={`${label}: en geç sınır`}
              value={rule.limitMax ? toLocalInput(rule.limitMax) : ""}
              onChange={(e) => {
                const ms = e.target.value ? new Date(e.target.value).getTime() : 0;
                if (Number.isFinite(ms)) onChange({ ...rule, limitMax: ms });
              }}
            />
          </div>
        </details>
      )}

      {rule.action === "photoTaken" && (
        <div className="small-text muted">Fotoğrafın içindeki (EXIF) çekim tarihi kullanılır. Çekim tarihi olmayan dosyalara dokunulmaz.</div>
      )}
    </div>
  );
}

export function AttributesPanel({ store, state }: { store: AppStore; state: AppState }) {
  const { attr } = state;
  const set = (patch: Partial<AttributeRule>) => store.setAttr(patch);

  return (
    <section className="card rulepanel" aria-label="Öznitelikler ve tarihler">
      <div className="field-label">Öznitelikler</div>
      {flags.map((f) => (
        <div key={f.key} className="attr-line">
          <span>{f.label}</span>
          <TriControl label={f.label} value={attr[f.key]} onChange={(v) => set({ [f.key]: v })} />
        </div>
      ))}

      <div className="divider" />
      <div className="section-title">Tarihler</div>
      {times.map((t) => (
        <TimeEditor key={t.key} label={t.label} rule={attr[t.key]} onChange={(rule) => set({ [t.key]: rule })} />
      ))}

      <div className="divider" />
      <Switch spread label="Klasörlere de uygula" checked={attr.includeFolders} onChange={(v) => set({ includeFolders: v })} />
      <p className="hint">
        Salt okunur bir dosyanın tarihi de değiştirilebilir; işlem sırasında bayrak geçici olarak kaldırılır ve geri konur.
      </p>
    </section>
  );
}
