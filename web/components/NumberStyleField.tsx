"use client";

import type { NumberStyle } from "@/lib/contract";

const styles: { id: NumberStyle; label: string }[] = [
  { id: "decimal", label: "Ondalık (1, 2, 10)" },
  { id: "hexadecimal", label: "Onaltılık (1, 9, A, 1F)" },
  { id: "letters", label: "Harf (a, b, ... z, ba)" },
];

/** ? sayacının nasıl yazılacağı. Adlandır, Etiketten ve Numaralı nesne sekmeleri ortak kullanır. */
export function NumberStyleField({ value, onChange }: { value: NumberStyle; onChange: (value: NumberStyle) => void }) {
  return (
    <div className="num-row">
      <span>Numara türü</span>
      <select className="input" aria-label="Numara türü" value={value} onChange={(e) => onChange(e.target.value as NumberStyle)}>
        {styles.map((s) => <option key={s.id} value={s.id}>{s.label}</option>)}
      </select>
    </div>
  );
}
