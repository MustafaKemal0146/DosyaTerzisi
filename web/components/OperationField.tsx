"use client";

import { Icon } from "./Icon";
import type { Operation } from "@/lib/contract";

const operations: { id: Operation; label: string; hint: string }[] = [
  { id: "rename", label: "Adlandır", hint: "Dosyanın kendisi yeni adı alır. Yeni adda “/” varsa klasöre taşınır, “..” bir üst klasöre çıkar." },
  { id: "copy", label: "Kopyala", hint: "Yeni adla bir kopya oluşturur; özgün dosya yerinde kalır." },
  { id: "shortcut", label: "Kısayol", hint: "Yeni adla dosyaya bir kısayol (.lnk) oluşturur." },
  { id: "hardlink", label: "Sabit bağ", hint: "Aynı içeriği gösteren ikinci bir ad oluşturur (yalnızca dosyalar, aynı sürücüde)." },
];

interface Props {
  operation: Operation;
  targetFolder: string;
  onChange: (patch: { operation?: Operation; targetFolder?: string }) => void;
  onBrowse: () => void;
}

/** Yeni adla ne yapılacağı ve sonucun gideceği klasör. Adlandırma ve Etiketten sekmeleri ortak kullanır. */
export function OperationField({ operation, targetFolder, onChange, onBrowse }: Props) {
  const current = operations.find((o) => o.id === operation)!;
  return (
    <>
      <div className="section-title" style={{ marginTop: 10 }}>İşlem</div>
      <div className="segmented" role="radiogroup" aria-label="İşlem">
        {operations.map((o) => (
          <button key={o.id} role="radio" aria-checked={operation === o.id} className={operation === o.id ? "active" : ""} onClick={() => onChange({ operation: o.id })}>
            {o.label}
          </button>
        ))}
      </div>
      <p className="hint" style={{ margin: "6px 0 8px" }}>{current.hint}</p>
      <div className="field-label"><span>Hedef klasör</span></div>
      <div className="row-inline">
        <input
          className="input path"
          value={targetFolder}
          placeholder="Boş: dosyanın kendi klasörü"
          spellCheck={false}
          aria-label="Hedef klasör"
          onChange={(e) => onChange({ targetFolder: e.target.value })}
        />
        <button className="btn icon" title="Klasör seç" aria-label="Hedef klasörü seç" onClick={onBrowse}>
          <Icon name="folder" />
        </button>
      </div>
    </>
  );
}
