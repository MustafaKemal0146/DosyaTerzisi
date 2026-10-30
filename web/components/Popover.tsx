"use client";

import { useEffect, useLayoutEffect, useRef, useState, type CSSProperties, type ReactNode } from "react";
import { createPortal } from "react-dom";

interface Props {
  /** Menüyü açan düğme. */
  trigger: (state: { open: boolean; toggle: () => void }) => ReactNode;
  /** Menü içeriği; `close` menüyü kapatır. */
  children: (close: () => void) => ReactNode;
  align?: "start" | "end";
  direction?: "down" | "up";
}

const GAP = 4;
const MARGIN = 8;

/**
 * Düğmeye bağlı açılır menü. Sayfanın en üstünde (portal) çizilir; böylece kaydırılan panellerin içinde kesilmez.
 * Dışarı tıklayınca, Esc ile ya da sayfa kaydırılınca kapanır.
 */
export function Popover({ trigger, children, align = "start", direction = "down" }: Props) {
  const [open, setOpen] = useState(false);
  const [style, setStyle] = useState<CSSProperties>({ visibility: "hidden" });
  const wrap = useRef<HTMLDivElement>(null);
  const menu = useRef<HTMLDivElement>(null);

  useLayoutEffect(() => {
    if (!open || !wrap.current) return;
    const anchor = wrap.current.getBoundingClientRect();
    const spaceBelow = window.innerHeight - anchor.bottom - GAP - MARGIN;
    const spaceAbove = anchor.top - GAP - MARGIN;
    const wanted = Math.min(menu.current?.scrollHeight ?? 300, 460);
    const up = direction === "up" ? spaceAbove >= Math.min(wanted, 160) || spaceAbove > spaceBelow : spaceBelow < Math.min(wanted, 160) && spaceAbove > spaceBelow;

    const next: CSSProperties = { position: "fixed", maxHeight: Math.max(120, Math.min(460, up ? spaceAbove : spaceBelow)) };
    if (up) next.bottom = window.innerHeight - anchor.top + GAP;
    else next.top = anchor.bottom + GAP;
    if (align === "end") next.right = Math.max(MARGIN, window.innerWidth - anchor.right);
    else next.left = Math.min(anchor.left, window.innerWidth - 300 - MARGIN);
    setStyle(next);
  }, [open, align, direction]);

  useEffect(() => {
    if (!open) return;
    const onPointer = (event: PointerEvent) => {
      const target = event.target as Node;
      if (!wrap.current?.contains(target) && !menu.current?.contains(target)) setOpen(false);
    };
    const onKey = (event: KeyboardEvent) => event.key === "Escape" && setOpen(false);
    const onScroll = (event: Event) => {
      if (!menu.current?.contains(event.target as Node)) setOpen(false);
    };
    const onResize = () => setOpen(false);
    document.addEventListener("pointerdown", onPointer);
    document.addEventListener("keydown", onKey);
    window.addEventListener("scroll", onScroll, true);
    window.addEventListener("resize", onResize);
    return () => {
      document.removeEventListener("pointerdown", onPointer);
      document.removeEventListener("keydown", onKey);
      window.removeEventListener("scroll", onScroll, true);
      window.removeEventListener("resize", onResize);
    };
  }, [open]);

  return (
    <div className="popover-wrap" ref={wrap}>
      {trigger({ open, toggle: () => setOpen((o) => !o) })}
      {open &&
        createPortal(
          <div className="popover" ref={menu} style={style} role="menu">
            {children(() => setOpen(false))}
          </div>,
          document.body,
        )}
    </div>
  );
}
