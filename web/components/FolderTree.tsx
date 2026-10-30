"use client";

import { useCallback, useEffect, useState } from "react";
import { Icon } from "./Icon";
import type { FolderNode } from "@/lib/contract";
import type { AppState, AppStore } from "@/lib/store";

type Loaded = FolderNode[] | "loading" | { error: string };

/** Klasörleri ağaç olarak gösterir. Alt klasörler, açıldıkça (bir kez) uygulamadan istenir. */
export function FolderTree({ store, state }: { store: AppStore; state: AppState }) {
  const [children, setChildren] = useState<Record<string, Loaded>>({});
  const [open, setOpen] = useState<ReadonlySet<string>>(new Set());

  const load = useCallback(
    async (path: string) => {
      setChildren((c) => ({ ...c, [path]: "loading" }));
      try {
        const nodes = await store.listFolders(path);
        setChildren((c) => ({ ...c, [path]: nodes }));
      } catch (error) {
        setChildren((c) => ({ ...c, [path]: { error: error instanceof Error ? error.message : String(error) } }));
      }
    },
    [store],
  );

  useEffect(() => {
    void load("");
  }, [load]);

  const toggle = (node: FolderNode) => {
    const next = new Set(open);
    if (next.delete(node.path)) {
      setOpen(next);
      return;
    }
    next.add(node.path);
    setOpen(next);
    if (!children[node.path]) void load(node.path);
  };

  const render = (nodes: FolderNode[], depth: number) =>
    nodes.map((node) => {
      const expanded = open.has(node.path);
      const sub = children[node.path];
      return (
        <div key={node.path}>
          <div className={`tree-row ${state.folder.toLowerCase() === node.path.toLowerCase() ? "current" : ""}`} style={{ paddingLeft: depth * 14 }}>
            <button className={`tree-toggle ${expanded ? "open" : ""}`} style={{ visibility: node.hasChildren ? "visible" : "hidden" }} aria-label={expanded ? "Daralt" : "Genişlet"} aria-expanded={expanded} onClick={() => toggle(node)}>
              <Icon name="arrow" size={12} />
            </button>
            <button className="tree-label" title={node.path} onClick={() => void store.openFolder(node.path)}>
              <Icon name={expanded ? "folderOpen" : "folder"} size={16} className="file-icon folder" />
              <span>{node.name}</span>
            </button>
          </div>
          {expanded && sub === "loading" && <div className="tree-note" style={{ paddingLeft: depth * 14 + 30 }}>Yükleniyor…</div>}
          {expanded && sub && typeof sub === "object" && "error" in sub && <div className="tree-note" style={{ paddingLeft: depth * 14 + 30, color: "var(--danger)" }}>{sub.error}</div>}
          {expanded && Array.isArray(sub) && render(sub, depth + 1)}
        </div>
      );
    });

  const roots = children[""];
  return (
    <section className="card treecard" aria-label="Klasörler">
      <header>Klasörler</header>
      <div className="tree" role="tree">
        {roots === "loading" || roots === undefined ? <div className="tree-note">Yükleniyor…</div> : null}
        {roots && typeof roots === "object" && "error" in roots && <div className="tree-note">{roots.error}</div>}
        {Array.isArray(roots) && render(roots, 0)}
      </div>
    </section>
  );
}
