"use client";

import { Icon } from "./Icon";
import { Switch } from "./Switch";
import type { AppStore, AppState } from "@/lib/store";

export function FolderBar({ store, state }: { store: AppStore; state: AppState }) {
  const { folder, listing, scanning, source, rows, treeOpen } = state;
  const fromPaths = source === "paths";

  return (
    <section className="card folderbar" aria-label="Klasör">
      <div className="folderbar-row">
        <button
          className={`btn icon ${treeOpen ? "primary" : ""}`}
          title="Klasör ağacı"
          aria-label="Klasör ağacı"
          aria-pressed={treeOpen}
          onClick={() => store.toggleTree()}
        >
          <Icon name="folderOpen" size={18} />
        </button>
        <button className="btn icon" title="Geri (Alt+←)" aria-label="Geri" disabled={!state.canBack} onClick={() => void store.goBack()}>
          <Icon name="back" />
        </button>
        <button className="btn icon" title="İleri (Alt+→)" aria-label="İleri" disabled={!state.canForward} onClick={() => void store.goForward()}>
          <Icon name="forward" />
        </button>
        <button className="btn icon" title="Üst klasör (Alt+↑)" aria-label="Üst klasör" disabled={!store.canGoUp()} onClick={() => void store.goUp()}>
          <Icon name="parent" />
        </button>
        <input
          className="input path"
          value={fromPaths ? `Sürüklenen ${rows.length.toLocaleString("tr-TR")} öğe` : folder}
          readOnly={fromPaths}
          placeholder="Bir klasör yolu yazın, klasörü pencereye sürükleyin ya da Gözat'a tıklayın"
          spellCheck={false}
          aria-label="Klasör yolu"
          onFocus={(e) => fromPaths && e.target.blur()}
          onChange={(e) => store.setFolder(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && void store.openFolder(folder)}
        />
        <button className="btn" onClick={() => void store.browse()}>
          <Icon name="folder" /> Gözat…
        </button>
        <button className="btn icon" title="Yenile (F5)" aria-label="Yenile" disabled={scanning || (!folder.trim() && !fromPaths)} onClick={() => void store.refresh()}>
          <Icon name="refresh" />
        </button>
      </div>

      <div className="filters" style={fromPaths ? { opacity: 0.5, pointerEvents: "none" } : undefined} aria-disabled={fromPaths}>
        <div className="group">
          <span>Listele</span>
          <Switch label="Dosyalar" checked={listing.includeFiles} onChange={(v) => store.setListing({ includeFiles: v })} />
          <Switch label="Klasörler" checked={listing.includeFolders} onChange={(v) => store.setListing({ includeFolders: v })} />
        </div>
        <div className="group">
          <span>Filtre</span>
          <input
            className="input mono"
            style={{ width: 170 }}
            value={listing.filter}
            spellCheck={false}
            title="Birden çok kalıp için noktalı virgül kullanın: *.jpg;*.png"
            aria-label="Dosya adı filtresi"
            onChange={(e) => store.setListing({ filter: e.target.value })}
          />
        </div>
        <div className="group">
          <Switch label="Alt klasörler" checked={listing.recursive} onChange={(v) => store.setListing({ recursive: v })} />
          <span>Derinlik</span>
          <input
            className="input narrow"
            inputMode="numeric"
            disabled={!listing.recursive}
            value={listing.depth}
            aria-label="Alt klasör derinliği"
            onChange={(e) => store.setListing({ depth: Number(e.target.value.replace(/\D/g, "")) || 1 })}
          />
        </div>
      </div>
    </section>
  );
}
