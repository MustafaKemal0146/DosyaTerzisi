"use client";

import { memo, useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent, type MouseEvent } from "react";
import { createPortal } from "react-dom";
import { Icon } from "./Icon";
import { buildLayout, knownTags, nextRowIndex, rowIdsBetween, tagMode, visibleRange, type TagLookup } from "@/lib/grouping";
import { diffNames } from "@/lib/diff";
import type { AttrFlags, AttributeRow, FileRow, PreviewRow, RowStatus } from "@/lib/contract";
import { fileKind, formatDate, formatSize } from "@/lib/format";

const ROW_HEIGHT = 36;
const OVERSCAN = 10;

const statusLabel: Record<RowStatus, string> = {
  unchanged: "",
  ready: "Hazır",
  conflict: "Çakışma",
  invalid: "Geçersiz ad",
  error: "Kural hatalı",
  missingTag: "Etiket yok",
};

const statusTone: Partial<Record<RowStatus, string>> = {
  ready: "ready",
  conflict: "bad",
  invalid: "bad",
  error: "bad",
  missingTag: "warn",
};

/** "names": adlandırma ve etiket sekmeleri (yeni ad görünür). "attributes": öznitelik ve tarihler (önce → sonra). */
export type TableVariant = "names" | "attributes";

interface Props {
  rows: FileRow[];
  variant?: TableVariant;
  previews: PreviewRow[];
  attrRows?: AttributeRow[];
  selection: ReadonlySet<number>;
  onSelection: (selection: ReadonlySet<number>) => void;
  /** Alt klasörler listeleniyorsa klasör sütunu gösterilir. */
  showFolder: boolean;
  /** Klasör satırına çift tıklanınca / Enter'a basılınca. */
  onOpen?: (id: number) => void;
  /** Adlandır sekmesinde "Yeni ad" hücresi elle düzenlenebilir (F2 ya da seçili satırın hücresine tıklama). */
  editable?: boolean;
  overrides?: Readonly<Record<number, string>>;
  onEditName?: (id: number, name: string) => void;
  /** Del: seçili satırları listeden çıkar. Ctrl+I: seçimi ters çevir. */
  onRemove?: () => void;
  onInvert?: () => void;
  /** Gruplama: "none" | "folder" | "date" | "ext" | "tag:<ad>". */
  groupMode?: string;
  /** Ek sütun olarak gösterilen etiketler ve değer önbelleği (satır numarası → etiket → değer). */
  tagColumns?: string[];
  tagValues?: Readonly<Record<number, Readonly<Record<string, string>>>>;
  /** Ekranda görünen satırların etiketleri gerekiyor. */
  onNeedTags?: (ids: number[]) => void;
  /** Başlık menüsü: etiket sütunu aç/kapat ve gruplama seçimi. */
  onToggleTagColumn?: (name: string) => void;
  onGroupMode?: (mode: string) => void;
}

export function FileTable({
  rows,
  variant = "names",
  previews,
  attrRows = [],
  selection,
  onSelection,
  showFolder,
  onOpen,
  editable,
  overrides = {},
  onEditName,
  onRemove,
  onInvert,
  groupMode = "none",
  tagColumns = [],
  tagValues = {},
  onNeedTags,
  onToggleTagColumn,
  onGroupMode,
}: Props) {
  const scroller = useRef<HTMLDivElement>(null);
  /** Son tıklanan / gezilen satırın öğe konumu (başlıklar dahil dizide). */
  const anchor = useRef(0);
  const [scrollTop, setScrollTop] = useState(0);
  /** Düzenlenen satırın kimliği. */
  const [editing, setEditing] = useState<number | null>(null);
  const [height, setHeight] = useState(600);
  const [menu, setMenu] = useState<{ x: number; y: number } | null>(null);

  const tagLookup = useCallback<TagLookup>((id, tag) => tagValues[id]?.[tag], [tagValues]);
  const layout = useMemo(() => buildLayout(rows, groupMode, ROW_HEIGHT, tagLookup), [rows, groupMode, tagLookup]);
  const { items, offsets } = layout;

  const columns =
    (variant === "attributes"
      ? "minmax(180px, 1.5fr) 132px minmax(190px, 1.3fr) minmax(190px, 1.3fr) 96px"
      : showFolder
        ? "minmax(180px, 1.7fr) minmax(200px, 1.9fr) 104px minmax(120px, 1.1fr) 76px 128px"
        : "minmax(200px, 1.8fr) minmax(220px, 2fr) 104px 76px 128px") + tagColumns.map(() => " minmax(110px, 1fr)").join("");

  useEffect(() => {
    const el = scroller.current;
    if (!el) return;
    const observer = new ResizeObserver(() => setHeight(el.clientHeight));
    observer.observe(el);
    setHeight(el.clientHeight);
    return () => observer.disconnect();
  }, []);

  // Liste yenilenince en üste dön.
  useEffect(() => {
    if (scroller.current) scroller.current.scrollTop = 0;
  }, [rows]);

  // Gruplama değişince düzenleme biter ve başa dönülür.
  useEffect(() => {
    setEditing(null);
    anchor.current = 0;
    if (scroller.current) scroller.current.scrollTop = 0;
  }, [groupMode]);

  const [first, last] = visibleRange(offsets, scrollTop, height, OVERSCAN);
  const visible = useMemo(() => items.slice(first, last), [items, first, last]);

  // Görünen satırların (ve tamponun) etiket değerlerini iste; gecikme ve birleştirme store'da.
  useEffect(() => {
    if (tagColumns.length === 0 || !onNeedTags) return;
    const ids: number[] = [];
    for (const item of visible) if (item.kind === "row") ids.push(item.row.id);
    onNeedTags(ids);
  }, [visible, tagColumns, onNeedTags]);

  const rowAt = (index: number): FileRow | undefined => {
    const item = items[index];
    return item?.kind === "row" ? item.row : undefined;
  };

  const select = (index: number, event: { ctrlKey: boolean; metaKey: boolean; shiftKey: boolean }) => {
    const row = rowAt(index);
    if (!row) return;

    if (event.shiftKey) {
      const next = new Set(event.ctrlKey || event.metaKey ? selection : []);
      for (const id of rowIdsBetween(items, anchor.current, index)) next.add(id);
      onSelection(next);
    } else if (event.ctrlKey || event.metaKey) {
      const next = new Set(selection);
      if (!next.delete(row.id)) next.add(row.id);
      anchor.current = index;
      onSelection(next);
    } else {
      anchor.current = index;
      onSelection(new Set([row.id]));
    }
  };

  /** Grup başlığına tıklayınca grubun satırları seçilir (Ctrl: seçime eklenir). */
  const selectGroup = (index: number, event: { ctrlKey: boolean; metaKey: boolean }) => {
    const item = items[index];
    if (item?.kind !== "header") return;
    const next = new Set(event.ctrlKey || event.metaKey ? selection : []);
    for (const id of item.ids) next.add(id);
    const firstRow = nextRowIndex(items, index, 1);
    if (firstRow !== null) anchor.current = firstRow;
    onSelection(next);
  };

  const reveal = (index: number) => {
    const el = scroller.current;
    if (!el) return;
    const top = offsets[index] ?? 0;
    if (top < el.scrollTop) el.scrollTop = top;
    else if (top + ROW_HEIGHT > el.scrollTop + el.clientHeight - 34) el.scrollTop = top + ROW_HEIGHT - el.clientHeight + 34;
  };

  const startEdit = (index: number) => {
    const row = rowAt(index);
    if (!editable || !row) return;
    anchor.current = index;
    reveal(index);
    setEditing(row.id);
  };

  /** Yazılanı kaydeder; `move` ±1 ise bir alt/üst satırın düzenlemesine geçer. */
  const finishEdit = (index: number, value: string | null, move: number) => {
    const row = rowAt(index);
    if (row && value !== null) onEditName?.(row.id, value);
    const target = move === 0 ? null : nextRowIndex(items, index, move > 0 ? 1 : -1);
    const targetRow = target === null ? undefined : rowAt(target);
    if (target !== null && targetRow) {
      onSelection(new Set([targetRow.id]));
      startEdit(target);
    } else {
      setEditing(null);
      scroller.current?.focus();
    }
  };

  const onKeyDown = (event: KeyboardEvent) => {
    if (editing !== null) return;
    if (event.key === "F2" && editable) {
      event.preventDefault();
      startEdit(anchor.current);
    } else if (event.key === "Delete" && onRemove) {
      event.preventDefault();
      onRemove();
    } else if (event.key === "i" && (event.ctrlKey || event.metaKey) && onInvert) {
      event.preventDefault();
      onInvert();
    } else if (event.key === "a" && (event.ctrlKey || event.metaKey)) {
      event.preventDefault();
      onSelection(new Set(rows.map((r) => r.id)));
    } else if (event.key === "Enter" && onOpen) {
      const row = rowAt(anchor.current);
      if (row?.isDir && selection.has(row.id)) onOpen(row.id);
    } else if (event.key === "Escape") {
      onSelection(new Set());
    } else if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      const step = event.key === "ArrowDown" ? 1 : -1;
      // Başlık satırları atlanır; ilk basışta bağlanmış satır yoksa en yakın satır seçilir.
      const next = rowAt(anchor.current) ? nextRowIndex(items, anchor.current, step) : nextRowIndex(items, anchor.current - step, step);
      if (next !== null) {
        select(next, event);
        reveal(next);
      }
    }
  };

  const hasMenu = onToggleTagColumn !== undefined && onGroupMode !== undefined;

  return (
    <div
      className="scroller"
      ref={scroller}
      tabIndex={0}
      role="grid"
      aria-label="Dosya listesi"
      aria-multiselectable="true"
      aria-rowcount={items.length}
      onScroll={(e) => setScrollTop(e.currentTarget.scrollTop)}
      onKeyDown={onKeyDown}
    >
      <div
        className="thead"
        style={{ gridTemplateColumns: columns }}
        role="row"
        title={hasMenu ? "Sütunlar ve gruplama için sağ tıklayın" : undefined}
        onContextMenu={(e) => {
          if (!hasMenu) return;
          e.preventDefault();
          setMenu({ x: e.clientX, y: e.clientY });
        }}
      >
        <div>Mevcut ad</div>
        {variant === "attributes" ? (
          <>
            <div title="SO: salt okunur · AR: arşiv · GZ: gizli · Sİ: sistem">Öznitelikler</div>
            <div>Değiştirilme</div>
            <div>Oluşturulma</div>
            <div>Durum</div>
          </>
        ) : (
          <>
            <div>Yeni ad</div>
            <div>Durum</div>
            {showFolder && <div>Klasör</div>}
            <div style={{ textAlign: "right" }}>Boyut</div>
            <div>Değiştirilme</div>
          </>
        )}
        {tagColumns.map((name) => (
          <div key={name} title={name} className="tag-head">
            {name}
          </div>
        ))}
      </div>

      <div className="rows" style={{ height: offsets[items.length] ?? 0 }}>
        {visible.map((item, offset) => {
          const index = first + offset;
          if (item.kind === "header") {
            const all = item.ids.length > 0 && item.ids.every((id) => selection.has(id));
            return (
              <div
                key={`g:${index}:${item.key}`}
                role="row"
                aria-rowindex={index + 1}
                className={`row group-head ${all ? "selected" : ""}`}
                style={{ top: offsets[index] }}
                title="Gruptaki satırları seçmek için tıklayın"
                onClick={(e) => selectGroup(index, e)}
              >
                <span className="group-name">{item.label}</span>
                <span className="group-count">{item.count}</span>
              </div>
            );
          }
          const row = item.row;
          return (
            <Row
              key={row.id}
              row={row}
              variant={variant}
              preview={previews[row.id]}
              attr={attrRows[row.id]}
              selected={selection.has(row.id)}
              top={offsets[index] ?? 0}
              index={index}
              columns={columns}
              showFolder={showFolder}
              onSelect={select}
              onOpen={onOpen}
              editing={editing === row.id}
              typed={overrides[row.id]}
              onStartEdit={editable ? startEdit : undefined}
              onFinishEdit={finishEdit}
              tagColumns={tagColumns}
              tags={tagColumns.length > 0 ? tagValues[row.id] : undefined}
            />
          );
        })}
      </div>

      {menu && hasMenu && (
        <HeaderMenu
          at={menu}
          tagColumns={tagColumns}
          groupMode={groupMode}
          onToggleTagColumn={onToggleTagColumn!}
          onGroupMode={onGroupMode!}
          onClose={() => setMenu(null)}
        />
      )}
    </div>
  );
}

const groupChoices: { mode: string; label: string }[] = [
  { mode: "none", label: "Yok" },
  { mode: "folder", label: "Klasör" },
  { mode: "date", label: "Tarih (gün)" },
  { mode: "ext", label: "Uzantı" },
];

/** Liste başlığına sağ tıklayınca açılan menü: etiket sütunları ve gruplama. */
function HeaderMenu({
  at,
  tagColumns,
  groupMode,
  onToggleTagColumn,
  onGroupMode,
  onClose,
}: {
  at: { x: number; y: number };
  tagColumns: string[];
  groupMode: string;
  onToggleTagColumn: (name: string) => void;
  onGroupMode: (mode: string) => void;
  onClose: () => void;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const [pos, setPos] = useState<{ left: number; top: number; maxHeight: number }>({ left: at.x, top: at.y, maxHeight: 460 });

  useLayoutEffect(() => {
    const el = ref.current;
    const width = el?.offsetWidth ?? 260;
    const wanted = Math.min(el?.scrollHeight ?? 400, 460);
    const maxHeight = Math.min(460, window.innerHeight - 16);
    const top = Math.max(8, Math.min(at.y, window.innerHeight - wanted - 8));
    const left = Math.max(8, Math.min(at.x, window.innerWidth - width - 8));
    setPos({ left, top, maxHeight });
  }, [at]);

  useEffect(() => {
    const onPointer = (event: PointerEvent) => {
      if (!ref.current?.contains(event.target as Node)) onClose();
    };
    const onKey = (event: globalThis.KeyboardEvent) => event.key === "Escape" && onClose();
    const onScroll = (event: Event) => {
      if (!ref.current?.contains(event.target as Node)) onClose();
    };
    document.addEventListener("pointerdown", onPointer);
    document.addEventListener("keydown", onKey);
    window.addEventListener("scroll", onScroll, true);
    window.addEventListener("resize", onClose);
    return () => {
      document.removeEventListener("pointerdown", onPointer);
      document.removeEventListener("keydown", onKey);
      window.removeEventListener("scroll", onScroll, true);
      window.removeEventListener("resize", onClose);
    };
  }, [onClose]);

  return createPortal(
    <div className="popover header-menu" ref={ref} role="menu" style={{ position: "fixed", ...pos }} onContextMenu={(e) => e.preventDefault()}>
      <div className="menu-heading">Etiket sütunları</div>
      {knownTags.map((name) => (
        <label key={name} className="menu-item menu-check">
          <input type="checkbox" checked={tagColumns.includes(name)} onChange={() => onToggleTagColumn(name)} />
          <span className="grow mono">{name}</span>
        </label>
      ))}
      <div className="menu-sep" />
      <div className="menu-heading">Gruplama</div>
      {groupChoices.map(({ mode, label }) => (
        <label key={mode} className="menu-item menu-check">
          <input type="radio" name="group-mode" checked={groupMode === mode} onChange={() => onGroupMode(mode)} />
          <span className="grow">{label}</span>
        </label>
      ))}
      {tagColumns.map((name) => (
        <label key={name} className="menu-item menu-check">
          <input type="radio" name="group-mode" checked={groupMode === tagMode(name)} onChange={() => onGroupMode(tagMode(name))} />
          <span className="grow">
            Etiket: <span className="mono">{name}</span>
          </span>
        </label>
      ))}
    </div>,
    document.body,
  );
}

interface RowProps {
  row: FileRow;
  variant: TableVariant;
  preview: PreviewRow | undefined;
  attr: AttributeRow | undefined;
  selected: boolean;
  top: number;
  index: number;
  columns: string;
  showFolder: boolean;
  onSelect: (index: number, event: MouseEvent) => void;
  onOpen?: (id: number) => void;
  editing: boolean;
  /** Elle yazılmış ad varsa o. */
  typed: string | undefined;
  onStartEdit?: (index: number) => void;
  onFinishEdit: (index: number, value: string | null, move: number) => void;
  tagColumns: string[];
  tags: Readonly<Record<string, string>> | undefined;
}

const Row = memo(function Row({ row, variant, preview, attr, selected, top, index, columns, showFolder, onSelect, onOpen, editing, typed, onStartEdit, onFinishEdit, tagColumns, tags }: RowProps) {
  const status = preview?.status ?? "unchanged";
  const changed = status !== "unchanged" && preview !== undefined;
  const bad = variant === "attributes" ? attr?.status === "error" : status === "conflict" || status === "invalid" || status === "error";
  const parts = useMemo(() => (changed ? diffNames(row.name, preview!.newName) : null), [changed, row.name, preview]);

  return (
    <div
      role="row"
      aria-selected={selected}
      aria-rowindex={index + 1}
      className={`row ${selected ? "selected" : ""} ${bad ? "problem" : ""}`}
      style={{ top, gridTemplateColumns: columns }}
      title={(variant === "attributes" ? attr?.message : preview?.message) ?? undefined}
      onMouseDown={(e) => {
        // Shift ile seçerken metin seçimi başlamasın.
        if (e.shiftKey) e.preventDefault();
      }}
      onClick={(e) => onSelect(index, e)}
      onDoubleClick={() => row.isDir && onOpen?.(row.id)}
    >
      <div className="cell name" role="gridcell">
        <Icon name={row.isDir ? "folder" : "file"} size={16} className={`file-icon ${row.isDir ? "folder" : ""}`} />
        <span className="text">
          {variant === "names" && parts && !bad
            ? parts.filter((p) => p.kind !== "added").map((p, i) => (p.kind === "removed" ? <span key={i} className="d-removed">{p.text}</span> : p.text))
            : row.name}
        </span>
      </div>

      {variant === "attributes" ? (
        <AttributeCells attr={attr} />
      ) : (
        <>
          <div
            className={`cell new-name ${changed ? "" : "unchanged"} ${bad ? "bad" : ""} ${typed !== undefined ? "typed" : ""}`}
            role="gridcell"
            title={onStartEdit ? "Adı elle değiştirmek için F2'ye basın ya da seçili satırda tıklayın" : undefined}
            onClick={() => selected && !editing && onStartEdit?.(index)}
          >
            {editing ? (
              <NameEditor initial={typed ?? (changed ? preview!.newName : row.name)} onDone={(value, move) => onFinishEdit(index, value, move)} />
            ) : changed && parts ? (
              bad ? preview!.newName : parts.filter((p) => p.kind !== "removed").map((p, i) => (p.kind === "added" ? <span key={i} className="d-added">{p.text}</span> : p.text))
            ) : (
              "—"
            )}
            {preview?.destination && !editing && (
              <span className="dest" title={preview.destination}>
                → {preview.destination}
              </span>
            )}
          </div>

          <div className="cell" role="gridcell">
            {statusLabel[status] && <span className={`badge ${statusTone[status] ?? ""}`}>{statusLabel[status]}</span>}
          </div>

          {showFolder && (
            <div className="cell muted" role="gridcell" title={row.dir}>
              {row.dir}
            </div>
          )}
          <div className="cell muted right" role="gridcell" title={fileKind(row.name, row.isDir)}>
            {row.isDir ? "" : formatSize(row.size)}
          </div>
          <div className="cell muted" role="gridcell">
            {formatDate(row.modified)}
          </div>
        </>
      )}

      {tagColumns.map((name) => (
        <div key={name} className="cell muted tag-cell" role="gridcell" title={tags?.[name] || undefined}>
          {tags?.[name] ?? ""}
        </div>
      ))}
    </div>
  );
});

const pills: { key: keyof AttrFlags; label: string; title: string }[] = [
  { key: "readOnly", label: "SO", title: "Salt okunur" },
  { key: "archive", label: "AR", title: "Arşiv" },
  { key: "hidden", label: "GZ", title: "Gizli" },
  { key: "system", label: "Sİ", title: "Sistem" },
];

function AttributeCells({ attr }: { attr: AttributeRow | undefined }) {
  if (!attr) return <><div className="cell" /><div className="cell" /><div className="cell" /><div className="cell" /></>;

  return (
    <>
      <div className="cell" role="gridcell">
        <span className="attr-pills">
          {pills.map(({ key, label, title }) => {
            const was = attr.before[key];
            const now = attr.after[key];
            const tone = was === now ? (now ? "on" : "") : now ? "gained" : "lost";
            return <span key={key} className={`attr-pill ${tone}`} title={`${title}${was === now ? "" : now ? " (eklenecek)" : " (kaldırılacak)"}`}>{label}</span>;
          })}
        </span>
      </div>
      <div className="cell time-change" role="gridcell"><TimeChange before={attr.modifiedBefore} after={attr.modifiedAfter} /></div>
      <div className="cell time-change" role="gridcell"><TimeChange before={attr.createdBefore} after={attr.createdAfter} /></div>
      <div className="cell" role="gridcell">
        {attr.status === "ready" && <span className="badge ready">Değişecek</span>}
        {attr.status === "error" && <span className="badge bad">Geçersiz</span>}
      </div>
    </>
  );
}

function TimeChange({ before, after }: { before: number; after: number }) {
  if (before === after) return <span className="muted">{formatDate(before)}</span>;
  return (
    <>
      <span className="before">{formatDate(before)}</span>
      <span className="after">{formatDate(after)}</span>
    </>
  );
}

/** "Yeni ad" hücresinde açılan yazı kutusu: Enter onaylar, Esc vazgeçer, ↑/↓ komşu satıra geçer. */
function NameEditor({ initial, onDone }: { initial: string; onDone: (value: string | null, move: number) => void }) {
  const [value, setValue] = useState(initial);
  const ref = useRef<HTMLInputElement>(null);
  const finished = useRef(false);

  useEffect(() => {
    const input = ref.current;
    if (!input) return;
    input.focus();
    // Dosya adının uzantısız kısmı seçili gelir (Explorer gibi).
    const dot = initial.lastIndexOf(".");
    input.setSelectionRange(0, dot > 0 ? dot : initial.length);
  }, [initial]);

  const done = (save: boolean, move = 0) => {
    if (finished.current) return;
    finished.current = true;
    onDone(save ? value : null, move);
  };

  return (
    <input
      ref={ref}
      className="input name-editor mono"
      value={value}
      spellCheck={false}
      aria-label="Yeni ad"
      onChange={(e) => setValue(e.target.value)}
      onClick={(e) => e.stopPropagation()}
      onDoubleClick={(e) => e.stopPropagation()}
      onBlur={() => done(true)}
      onKeyDown={(e) => {
        e.stopPropagation();
        if (e.key === "Enter") done(true);
        else if (e.key === "Escape") done(false);
        else if (e.key === "ArrowDown") {
          e.preventDefault();
          done(true, 1);
        } else if (e.key === "ArrowUp") {
          e.preventDefault();
          done(true, -1);
        }
      }}
    />
  );
}
