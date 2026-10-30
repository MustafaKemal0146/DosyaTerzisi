"use client";

import { useEffect, type ReactNode } from "react";

/** Ortada açılan pencere. Esc ile kapanır (`onEscape` cevabı verir). */
export function Modal({ children, small, wide, onEscape }: { children: ReactNode; small?: boolean; wide?: boolean; onEscape: () => void }) {
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => event.key === "Escape" && onEscape();
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [onEscape]);

  return (
    <div className="overlay" role="presentation">
      <div className={`dialog ${small ? "small" : ""} ${wide ? "wide" : ""}`} role="dialog" aria-modal="true">
        {children}
      </div>
    </div>
  );
}
