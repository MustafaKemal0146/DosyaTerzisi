"use client";

import { useRef } from "react";
import { Fold } from "./Fold";
import { Icon } from "./Icon";
import { NumberStyleField } from "./NumberStyleField";
import { ScriptField } from "./ScriptField";
import { ShortNameField } from "./ShortNameField";
import { RestartField } from "./RestartField";
import { OperationField } from "./OperationField";
import { Switch } from "./Switch";
import type { AppState, AppStore } from "@/lib/store";

const groups: { title: string; tokens: { text: string; hint: string }[] }[] = [
  {
    title: "Müzik",
    tokens: [
      { text: "<ID3.Artist>", hint: "Sanatçı" },
      { text: "<ID3.Track>", hint: "Şarkı adı" },
      { text: "<ID3.Album>", hint: "Albüm" },
      { text: "<ID3.TrackNumber:00>", hint: "Parça no (2 basamak)" },
      { text: "<ID3.Year>", hint: "Yıl" },
      { text: "<ID3.Genre>", hint: "Tür" },
    ],
  },
  {
    title: "Resim",
    tokens: [
      { text: "<EXIF.OriginalTime:yyyyMMdd_HHmmss>", hint: "Çekim tarihi ve saati" },
      { text: "<EXIF.Model>", hint: "Fotoğraf makinesi" },
      { text: "<EXIF.Maker>", hint: "Marka" },
      { text: "<IPTC.City>", hint: "Şehir" },
      { text: "<IPTC.ObjectName>", hint: "Başlık" },
    ],
  },
  {
    title: "Belge (Word, Excel, PDF...)",
    tokens: [
      { text: "<DOC.Title>", hint: "Belge başlığı" },
      { text: "<DOC.Author>", hint: "Yazar" },
      { text: "<DOC.Subject>", hint: "Konu" },
      { text: "<DOC.Keywords>", hint: "Anahtar sözcükler" },
      { text: "<DOC.Created:yyyy-MM-dd>", hint: "Oluşturulma tarihi" },
      { text: "<DOC.Company>", hint: "Şirket (Office)" },
      { text: "<DOC.Pages>", hint: "Sayfa sayısı" },
    ],
  },
  {
    title: "Windows özellikleri: aşağıdaki listedeki PROP.* etiketlerine tıklayın",
    tokens: [],
  },
  {
    title: "Web sayfası",
    tokens: [
      { text: "<HTML.Title>", hint: "Sayfa başlığı" },
      { text: "<HTML.Description>", hint: "Açıklama" },
      { text: "<HTML.Author>", hint: "Yazar" },
    ],
  },
  {
    title: "Genel",
    tokens: [
      { text: "\\e", hint: "Uzantı" },
      { text: "\\t", hint: "Dosya adı (uzantısız)" },
      { text: "??", hint: "Sıra numarası (2 basamak)" },
      { text: "\\Y\\m\\d", hint: "Tarih" },
    ],
  },
];

const tokenLabel = (text: string) => text.replace(/^<(.*)>$/, "$1");

export function TagPanel({ store, state }: { store: AppStore; state: AppState }) {
  const { tag, fileTags, rows, selection } = state;
  const input = useRef<HTMLInputElement>(null);

  const insert = (text: string) => {
    const el = input.current;
    const start = el?.selectionStart ?? tag.template.length;
    const end = el?.selectionEnd ?? start;
    store.setTag({ template: tag.template.slice(0, start) + text + tag.template.slice(end) });
    requestAnimationFrame(() => {
      el?.focus();
      el?.setSelectionRange(start + text.length, start + text.length);
    });
  };

  const sample = rows.find((r) => selection.has(r.id)) ?? rows[0];

  return (
    <section className="card rulepanel" aria-label="Etiketten adlandırma">
      <div className="field-label">Yeni ad şablonu</div>
      <input
        ref={input}
        className="input mono"
        value={tag.template}
        spellCheck={false}
        aria-label="Şablon"
        onChange={(e) => store.setTag({ template: e.target.value })}
      />
      <p className="hint">
        Dosyanın içindeki bilgilerle (müzikte sanatçı, şarkı; fotoğrafta çekim tarihi gibi) yeni ad kurar. Aşağıdan bir etikete tıklayınca şablona eklenir.
      </p>

      {groups.map((group) => (
        <div key={group.title} style={{ marginTop: 8 }}>
          <div className="small-text muted" style={{ marginBottom: 4 }}>{group.title}</div>
          <div className="token-row" style={{ marginTop: 0 }}>
            {group.tokens.map((t) => (
              <button key={t.text} className="chip" title={t.hint} onClick={() => insert(t.text)}>
                {tokenLabel(t.text)}
              </button>
            ))}
          </div>
        </div>
      ))}

      <div className="tag-card">
        <div className="row-between">
          <strong>{sample ? <span title={sample.name}>Bu dosyanın etiketleri</span> : "Etiketler"}</strong>
          <Icon name="tag" size={15} />
        </div>
        {sample && <div className="small-text muted" style={{ marginTop: 2, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{sample.name}</div>}
        {!sample && <p className="hint">Etiketlerini görmek için bir klasör seçin.</p>}
        {sample && fileTags?.error && <div className="rule-error"><Icon name="alert" size={16} /><span>{fileTags.error}</span></div>}
        {sample && fileTags && !fileTags.error && fileTags.entries.length === 0 && (
          <p className="hint">Bu dosyada etiket bulunamadı (yalnızca müzik ve resim dosyalarında olur).</p>
        )}
        {fileTags && fileTags.entries.length > 0 && (
          <div className="tag-list">
            {fileTags.entries.map((e) => (
              <button key={e.name} className="tag-chip" title="Şablona ekle" onClick={() => insert(`<${e.name}>`)}>
                <span className="name">{e.name}</span>
                <span className="value">{e.value}</span>
              </button>
            ))}
          </div>
        )}
      </div>

      <div className="divider" />

      <div className="section-title">Etiketi olmayan dosya</div>
      <div className="segmented" role="tablist" aria-label="Etiket eksikse">
        <button role="tab" aria-selected={tag.missing === "skip"} className={tag.missing === "skip" ? "active" : ""} onClick={() => store.setTag({ missing: "skip" })}>
          Dokunma
        </button>
        <button role="tab" aria-selected={tag.missing === "useEmpty"} className={tag.missing === "useEmpty" ? "active" : ""} onClick={() => store.setTag({ missing: "useEmpty" })}>
          Boş bırak
        </button>
      </div>

      <OperationField operation={tag.operation} targetFolder={tag.targetFolder} onChange={(p) => store.setTag(p)} onBrowse={() => void store.browseTarget()} />


      <Fold title="Numaralandırma" hint="(? işareti)">
      <div className="num-row">
        <span>Başlangıç</span>
        <input className="input narrow" inputMode="numeric" value={tag.numberStart} aria-label="Başlangıç numarası"
          onChange={(e) => store.setTag({ numberStart: Number(e.target.value.replace(/[^\d-]/g, "")) || 0 })} />
        <span style={{ marginLeft: 10 }}>Adım</span>
        <input className="input narrow" inputMode="numeric" value={tag.numberStep} aria-label="Numara adımı"
          onChange={(e) => store.setTag({ numberStep: Number(e.target.value.replace(/\D/g, "")) || 1 })} />
      </div>
      <NumberStyleField value={tag.numberStyle} onChange={(v) => store.setTag({ numberStyle: v })} />
      <Switch spread label="Her klasörde yeniden başla" checked={tag.resetNumberPerFolder} onChange={(v) => store.setTag({ resetNumberPerFolder: v })} />
      <RestartField byDate={tag.resetNumberPerDate} byTag={tag.resetNumberPerTag} onChange={(p) => store.setTag(p)} />
      </Fold>

      <Fold title="Diğer seçenekler">
      <Switch spread label="Çakışan adlara otomatik numara ekle" checked={tag.autoNumberOnConflict} onChange={(v) => store.setTag({ autoNumberOnConflict: v })} />
      <Switch spread label="Tarih için oluşturulma tarihini kullan" checked={tag.useCreatedDate} onChange={(v) => store.setTag({ useCreatedDate: v })} />
      <ShortNameField value={tag.shortNames83} onChange={(v) => store.setTag({ shortNames83: v })} />
      <ScriptField store={store} value={tag.useScript} onChange={(v) => store.setTag({ useScript: v })} />

      </Fold>
    </section>
  );
}
