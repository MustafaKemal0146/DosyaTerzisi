"use client";

import { useRef } from "react";
import { Fold } from "./Fold";
import { Icon } from "./Icon";
import { Popover } from "./Popover";
import { NumberStyleField } from "./NumberStyleField";
import { ScriptField } from "./ScriptField";
import { ShortNameField } from "./ShortNameField";
import { RestartField } from "./RestartField";
import { OperationField } from "./OperationField";
import { Switch } from "./Switch";
import type { RenameMethod } from "@/lib/contract";
import type { AppState, AppStore } from "@/lib/store";

const methods: { id: RenameMethod; label: string }[] = [
  { id: "wildcard", label: "Joker" },
  { id: "regex", label: "Düzenli ifade" },
  { id: "translation", label: "Çeviri" },
];

const hints: Record<RenameMethod, string> = {
  wildcard: "? bir karakter, * herhangi bir metin yerine geçer. Eşleşenler \\1, \\2 … ile yeni ada yazılır.",
  regex: "Düzenli ifade. Gruplar \\1…\\9 olarak kullanılır; \\u \\U \\l \\L \\E büyük/küçük harfi değiştirir.",
  translation:
    "Aranan listedeki her karakter, yeni listede aynı sıradaki karakterle değişir. a-z gibi aralıklar yazılabilir. Uzantı değişmez.",
};

const quickTokens = [
  { text: "\\1", hint: "1. eşleşme" },
  { text: "\\2", hint: "2. eşleşme" },
  { text: "\\t", hint: "Dosya adı (uzantısız)" },
  { text: "\\e", hint: "Uzantı" },
  { text: "??", hint: "Sıra numarası (2 basamak)" },
  { text: "\\Y\\m\\d", hint: "Tarih" },
];

const variables: { heading: string; items: { label: string; text: string }[] }[] = [
  {
    heading: "Ad parçaları",
    items: [
      { label: "Dosya adı, uzantısız", text: "\\t" },
      { label: "Uzantı", text: "\\e" },
      { label: "Tüm ad", text: "\\0" },
      { label: "Klasör adı", text: "\\f" },
      { label: "Üst klasör adı", text: "\\F" },
      { label: "Dosya boyutu (1.234.567)", text: "\\;" },
      { label: "Dosya boyutu (1234567)", text: "\\:" },
      { label: "Tam klasör yolu", text: "\\P" },
    ],
  },
  {
    heading: "Sıra numarası",
    items: [
      { label: "2 basamak", text: "??" },
      { label: "3 basamak", text: "???" },
      { label: "Rastgele sayı (3 basamak)", text: "***" },
    ],
  },
  {
    heading: "Tarih ve saat",
    items: [
      { label: "Tarih", text: "\\Y\\m\\d" },
      { label: "Saat", text: "\\H\\M\\S" },
      { label: "Saat (12 saatlik, AM/PM)", text: "\\p\\I\\M\\S" },
      { label: "Sıfırsız gün (5)", text: "\\#d" },
      { label: "Yılın kaçıncı günü", text: "\\j" },
      { label: "Gün adı (Çarşamba)", text: "\\A" },
      { label: "Gün adı kısa (Çar)", text: "\\a" },
      { label: "Ay adı (Temmuz)", text: "\\B" },
      { label: "Ay adı kısa (Tem)", text: "\\b" },
    ],
  },
  {
    heading: "Büyük / küçük harf",
    items: [
      { label: "Sonraki harf BÜYÜK", text: "\\u" },
      { label: "Sonraki harf küçük", text: "\\l" },
      { label: "Devamı BÜYÜK", text: "\\U" },
      { label: "Devamı küçük", text: "\\L" },
      { label: "Büyük/küçük harf sonu", text: "\\E" },
      { label: "Düz metin başlangıcı (sona kadar)", text: "\\Q" },
    ],
  },
];

export function RulePanel({ store, state }: { store: AppStore; state: AppState }) {
  const { rule, favorites, presets, ruleError } = state;
  const replaceRef = useRef<HTMLInputElement>(null);
  const showVariables = rule.method !== "translation";

  const insert = (text: string) => {
    const input = replaceRef.current;
    if (!input) {
      store.setRule({ replace: rule.replace + text });
      return;
    }
    const start = input.selectionStart ?? rule.replace.length;
    const end = input.selectionEnd ?? start;
    store.setRule({ replace: rule.replace.slice(0, start) + text + rule.replace.slice(end) });
    requestAnimationFrame(() => {
      input.focus();
      input.setSelectionRange(start + text.length, start + text.length);
    });
  };

  return (
    <section className="card rulepanel" aria-label="Kural">
      <div className="field-label">Yöntem</div>
      <div className="segmented" role="tablist" aria-label="Yöntem">
        {methods.map((m) => (
          <button key={m.id} role="tab" aria-selected={rule.method === m.id} className={rule.method === m.id ? "active" : ""} onClick={() => store.setRule({ method: m.id })}>
            {m.label}
          </button>
        ))}
      </div>

      <div className="field-label">
        <span>Aranan</span>
        <Popover
          trigger={({ toggle }) => (
            <button className="btn small subtle" onClick={toggle}>
              <Icon name="star" size={14} /> Ön ayarlar <Icon name="chevronDown" size={12} />
            </button>
          )}
        >
          {(close) => (
            <>
              <div className="menu-heading">Favorilerim</div>
              {favorites.length === 0 && <div className="menu-item" style={{ color: "var(--text-3)" }}>Henüz favori yok</div>}
              {favorites.map((f, i) => (
                <div key={f.id} className="menu-item" role="menuitem" tabIndex={0}
                  onClick={() => { store.applySavedRule(f.rule); close(); }}
                  onKeyDown={(e) => { if (e.key === "Enter") { store.applySavedRule(f.rule); close(); } }}>
                  <Icon name="star" size={14} />
                  <span className="grow" title={`${f.rule.search}  →  ${f.rule.replace}`}>{f.name}</span>
                  <span className="menu-actions">
                    <button className="btn icon small subtle" title="Yukarı taşı" disabled={i === 0} onClick={(e) => { e.stopPropagation(); void store.moveFavorite(f.id, -1); }}><Icon name="up" size={13} /></button>
                    <button className="btn icon small subtle" title="Aşağı taşı" disabled={i === favorites.length - 1} onClick={(e) => { e.stopPropagation(); void store.moveFavorite(f.id, 1); }}><Icon name="down" size={13} /></button>
                    <button className="btn icon small subtle danger" title="Sil" onClick={(e) => { e.stopPropagation(); void store.removeFavorite(f.id); }}><Icon name="trash" size={13} /></button>
                  </span>
                </div>
              ))}
              <button className="menu-item" onClick={() => { close(); void store.addFavorite(); }}>
                <Icon name="plus" size={14} /> <span className="grow">Geçerli kuralı favorilere ekle…</span>
              </button>
              <button className="menu-item" onClick={() => { close(); void store.editFavorites(); }}>
                <Icon name="pencil" size={14} /> <span className="grow">Favorileri düzenle…</span>
              </button>
              <div className="menu-sep" />
              <div className="menu-heading">Hazır ön ayarlar</div>
              {presets.map((p) => (
                <button key={p.id} className="menu-item" onClick={() => { store.applySavedRule(p.rule); close(); }}>
                  <span className="grow">{p.title}</span>
                  <span className="meta">{p.rule.method === "wildcard" ? "joker" : p.rule.method === "regex" ? "regex" : "çeviri"}</span>
                </button>
              ))}
            </>
          )}
        </Popover>
      </div>
      <input
        className={`input mono ${ruleError ? "error" : ""}`}
        value={rule.search}
        spellCheck={false}
        aria-label="Aranan"
        onChange={(e) => store.setRule({ search: e.target.value })}
      />

      <div className="field-label">
        <span>Yeni ad</span>
        {showVariables && (
          <Popover
            align="end"
            trigger={({ toggle }) => (
              <button className="btn small subtle" onClick={toggle}>
                Değişkenler <Icon name="chevronDown" size={12} />
              </button>
            )}
          >
            {(close) =>
              variables.map((group, i) => (
                <div key={group.heading}>
                  {i > 0 && <div className="menu-sep" />}
                  <div className="menu-heading">{group.heading}</div>
                  {group.items.map((item) => (
                    <button key={item.text} className="menu-item" onClick={() => { insert(item.text); close(); }}>
                      <span className="grow">{item.label}</span>
                      <span className="meta">{item.text}</span>
                    </button>
                  ))}
                </div>
              ))
            }
          </Popover>
        )}
      </div>
      <input
        ref={replaceRef}
        className="input mono"
        value={rule.replace}
        spellCheck={false}
        aria-label="Yeni ad"
        onChange={(e) => store.setRule({ replace: e.target.value })}
      />
      {showVariables && (
        <div className="token-row">
          {quickTokens.map((t) => (
            <button key={t.text} className="chip" title={t.hint} onClick={() => insert(t.text)}>
              {t.text}
            </button>
          ))}
        </div>
      )}

      <p className="hint">{hints[rule.method]}</p>
      {ruleError && (
        <div className="rule-error" role="alert">
          <Icon name="alert" size={16} />
          <span>{ruleError}</span>
        </div>
      )}

      <div className="divider" />

      <Switch spread label="Büyük/küçük harfe duyarlı" checked={rule.caseSensitive} onChange={(v) => store.setRule({ caseSensitive: v })} />

      <OperationField operation={rule.operation} targetFolder={rule.targetFolder} onChange={(p) => store.setRule(p)} onBrowse={() => void store.browseTarget()} />


      <Fold title="Numaralandırma" hint="(? işareti)">
      <div className="num-row">
        <span>Başlangıç</span>
        <input className="input narrow" inputMode="numeric" value={rule.numberStart} aria-label="Başlangıç numarası"
          onChange={(e) => store.setRule({ numberStart: Number(e.target.value.replace(/[^\d-]/g, "")) || 0 })} />
        <span style={{ marginLeft: 10 }}>Adım</span>
        <input className="input narrow" inputMode="numeric" value={rule.numberStep} aria-label="Numara adımı"
          onChange={(e) => store.setRule({ numberStep: Number(e.target.value.replace(/\D/g, "")) || 1 })} />
      </div>
      <NumberStyleField value={rule.numberStyle} onChange={(v) => store.setRule({ numberStyle: v })} />
      <Switch spread label="Her klasörde yeniden başla" checked={rule.resetNumberPerFolder} onChange={(v) => store.setRule({ resetNumberPerFolder: v })} />
      <RestartField byDate={rule.resetNumberPerDate} byTag={rule.resetNumberPerTag} onChange={(p) => store.setRule(p)} />
      </Fold>

      <Fold title="Diğer seçenekler">
      <Switch spread label="Çakışan adlara otomatik numara ekle" checked={rule.autoNumberOnConflict} onChange={(v) => store.setRule({ autoNumberOnConflict: v })} />
      <Switch spread label="Tarih için oluşturulma tarihini kullan" checked={rule.useCreatedDate} onChange={(v) => store.setRule({ useCreatedDate: v })} />
      <ShortNameField value={rule.shortNames83} onChange={(v) => store.setRule({ shortNames83: v })} />
      <ScriptField store={store} value={rule.useScript} onChange={(v) => store.setRule({ useScript: v })} />

      </Fold>
    </section>
  );
}
