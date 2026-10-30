"use client";

import type { ReactNode } from "react";

/** Açılıp kapanan bölüm: ayar panelleri uzamasın, az kullanılanlar katlı dursun. */
export function Fold({ title, hint, children, open = false }: { title: string; hint?: string; children: ReactNode; open?: boolean }) {
  return (
    <details className="fold" open={open}>
      <summary>
        {title}
        {hint && <span className="fold-hint"> {hint}</span>}
      </summary>
      <div className="fold-body">{children}</div>
    </details>
  );
}
