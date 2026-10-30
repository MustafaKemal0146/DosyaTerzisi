"use client";

import { useEffect, useState } from "react";
import { ActionBar } from "@/components/ActionBar";
import { AttributesPanel } from "@/components/AttributesPanel";
import { Dialogs } from "@/components/Dialogs";
import { FileTable } from "@/components/FileTable";
import { FolderBar } from "@/components/FolderBar";
import { FolderTree } from "@/components/FolderTree";
import { Icon, type IconName } from "@/components/Icon";
import { NumberedList, NumberedPanel } from "@/components/NumberedPanel";
import { RulePanel } from "@/components/RulePanel";
import { TagPanel } from "@/components/TagPanel";
import { Tour } from "@/components/Tour";
import { Toasts } from "@/components/Toasts";
import { createBridge } from "@/lib/bridge";
import { AppStore, useStore, type Tab } from "@/lib/store";

const tabs: { id: Tab; label: string; icon: IconName }[] = [
  { id: "rename", label: "Adlandır", icon: "rename" },
  { id: "tags", label: "Etiketten", icon: "tag" },
  { id: "attributes", label: "Öznitelikler", icon: "sliders" },
  { id: "numbered", label: "Numaralı nesne", icon: "hash" },
];

export default function Page() {
  // Köprü yalnızca tarayıcıda kurulur (sunucu tarafında pencere yok).
  const [store, setStore] = useState<AppStore | null>(null);
  useEffect(() => {
    const created = new AppStore(createBridge());
    setStore(created);
    void created.start();
  }, []);

  if (!store) return null;
  return <Main store={store} />;
}

function Main({ store }: { store: AppStore }) {
  const state = useStore(store, (s) => s);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "F5") {
        event.preventDefault();
        void store.refresh();
      } else if (event.altKey && event.key === "ArrowLeft") {
        event.preventDefault();
        void store.goBack();
      } else if (event.altKey && event.key === "ArrowRight") {
        event.preventDefault();
        void store.goForward();
      } else if (event.altKey && event.key === "ArrowUp") {
        event.preventDefault();
        void store.goUp();
      } else if (event.key === "z" && event.ctrlKey && !(event.target instanceof HTMLInputElement)) {
        event.preventDefault();
        void (store.getState().tab === "attributes" ? store.undoAttributes() : store.undo());
      }
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [store]);

  // Sürükle-bırak: dosya ve klasörler pencerenin herhangi bir yerine bırakılabilir.
  useEffect(() => {
    const hasFiles = (e: DragEvent) => e.dataTransfer?.types.includes("Files") ?? false;
    let depth = 0;

    const enter = (e: DragEvent) => {
      if (!hasFiles(e)) return;
      e.preventDefault();
      depth++;
      store.setDropping(true);
    };
    const over = (e: DragEvent) => {
      if (hasFiles(e)) e.preventDefault();
    };
    const leave = (e: DragEvent) => {
      if (!hasFiles(e)) return;
      depth = Math.max(0, depth - 1);
      if (depth === 0) store.setDropping(false);
    };
    const drop = (e: DragEvent) => {
      if (!hasFiles(e)) return;
      e.preventDefault();
      depth = 0;
      store.setDropping(false);
      if (e.dataTransfer?.files) void store.openDropped(e.dataTransfer.files);
    };

    window.addEventListener("dragenter", enter);
    window.addEventListener("dragover", over);
    window.addEventListener("dragleave", leave);
    window.addEventListener("drop", drop);
    return () => {
      window.removeEventListener("dragenter", enter);
      window.removeEventListener("dragover", over);
      window.removeEventListener("dragleave", leave);
      window.removeEventListener("drop", drop);
    };
  }, [store]);

  const { rows, folder, scanning, ready, tab, source } = state;

  return (
    <div className="app">
      <nav className="topbar" aria-label="Bölümler">
        <div className="tabs" role="tablist">
          {tabs.map((t) => (
            <button
              key={t.id}
              role="tab"
              aria-selected={tab === t.id}
              className={`tab ${tab === t.id ? "active" : ""}`}
              onClick={() => store.setTab(t.id)}
            >
              <Icon name={t.icon} size={16} />
              {t.label}
            </button>
          ))}
        </div>
        <div className="topbar-spacer" />
        <button className="btn icon subtle" title="Ayarlar: Gezgin menüleri" aria-label="Ayarlar" onClick={() => void store.showSettings()}>
          <Icon name="settings" size={18} />
        </button>
        <button className="btn icon subtle" title="Tanıtım turu" aria-label="Tanıtım turu" onClick={() => store.openTour()}>
          <Icon name="help" size={18} />
        </button>
      </nav>

      <FolderBar store={store} state={state} />

      <div className={`workspace ${state.treeOpen ? "with-tree" : ""}`}>
        {tab === "rename" && <RulePanel store={store} state={state} />}
        {tab === "tags" && <TagPanel store={store} state={state} />}
        {tab === "attributes" && <AttributesPanel store={store} state={state} />}
        {tab === "numbered" && <NumberedPanel store={store} state={state} />}

        {state.treeOpen && <FolderTree store={store} state={state} />}

        <section className="card listcard" aria-label={tab === "numbered" ? "Oluşturulacaklar" : "Dosyalar"}>
          {scanning && <div className="loading-bar" role="progressbar" aria-label="Taranıyor" />}
          {tab === "numbered" ? (
            <NumberedList plan={state.numberedPlan} />
          ) : rows.length > 0 ? (
            <FileTable
              rows={rows}
              variant={tab === "attributes" ? "attributes" : "names"}
              previews={state.previews}
              attrRows={state.attrRows}
              selection={state.selection}
              onSelection={(s) => store.setSelection(s)}
              showFolder={state.listing.recursive || source === "paths"}
              onOpen={(id) => void store.openRow(id)}
              editable={tab === "rename"}
              overrides={state.overrides}
              onEditName={(id, name) => store.editName(id, name)}
              onRemove={() => void store.removeSelected()}
              onInvert={() => store.invertSelection()}
              groupMode={state.groupMode}
              tagColumns={state.tagColumns}
              tagValues={state.tagValues}
              onNeedTags={(ids) => store.requestTagValues(ids)}
              onToggleTagColumn={(name) => store.toggleTagColumn(name)}
              onGroupMode={(mode) => store.setGroupMode(mode)}
            />
          ) : (
            <EmptyState store={store} hasFolder={folder.trim().length > 0} scanning={scanning} ready={ready} />
          )}
        </section>
      </div>

      <ActionBar store={store} state={state} />
      <Dialogs store={store} state={state} />
      <Tour store={store} open={state.tourOpen} />
      <Toasts store={store} toasts={state.toasts} />
      {state.dropping && (
        <div className="dropzone" aria-hidden="true">
          <Icon name="folderOpen" size={40} />
          Dosyaları ya da klasörü buraya bırakın
        </div>
      )}
    </div>
  );
}

function EmptyState({ store, hasFolder, scanning, ready }: { store: AppStore; hasFolder: boolean; scanning: boolean; ready: boolean }) {
  if (!ready || scanning) {
    return (
      <div className="empty">
        <p>Taranıyor…</p>
      </div>
    );
  }

  return hasFolder ? (
    <div className="empty">
      <div className="big-icon"><Icon name="search" size={34} /></div>
      <h2>Listelenecek öğe bulunamadı</h2>
      <p>Filtreyi, “Dosyalar / Klasörler” seçeneklerini ya da alt klasör ayarını değiştirmeyi deneyin.</p>
    </div>
  ) : (
    <div className="empty">
      <div className="big-icon"><Icon name="folderOpen" size={34} /></div>
      <h2>Bir klasör seçerek başlayın</h2>
      <p>Adlarını değiştirmek istediğiniz dosyaların bulunduğu klasörü seçin ya da dosyaları buraya sürükleyin. Yeni adları yazdıkça önizlemede görürsünüz; hiçbir şey siz onaylamadan değişmez.</p>
      <button className="btn primary" onClick={() => void store.browse()}>
        <Icon name="folder" /> Klasör seç…
      </button>
    </div>
  );
}
