"use client";

interface Props {
  label: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
  disabled?: boolean;
  /** Etiket solda, anahtar sağda (ayar satırları için). */
  spread?: boolean;
}

export function Switch({ label, checked, onChange, disabled, spread }: Props) {
  const control = (
    <>
      <input type="checkbox" role="switch" checked={checked} disabled={disabled} onChange={(e) => onChange(e.target.checked)} />
      <span className="track" aria-hidden="true" />
    </>
  );

  return spread ? (
    <label className="checkbox-row">
      <span>{label}</span>
      <span className="switch">{control}</span>
    </label>
  ) : (
    <label className="switch">
      {control}
      <span>{label}</span>
    </label>
  );
}
