"use client";

import { Switch } from "./Switch";

export function ShortNameField({ value, onChange }: { value: boolean; onChange: (value: boolean) => void }) {
  return <Switch spread label="8.3 kısa ad biçimi (en çok 8 + 3 karakter)" checked={value} onChange={onChange} />;
}
