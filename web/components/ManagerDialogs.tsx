"use client";

import { useEffect, useState } from "react";
import { Icon } from "./Icon";
import { Modal } from "./Modal";
import { Switch } from "./Switch";
import type { Favorite, RenameMethod } from "@/lib/contract";
import { formatTime } from "@/lib/format";
import { fonts, sizes, type FontChoice, type SizeChoice } from "@/lib/prefs";
import type { AppState, AppStore } from "@/lib/store";

const methodNames: Record<RenameMethod, string> = { wildcard: "Joker", regex: "Düzenli ifade", translation: "Çeviri" };

/** Kayıtlı kuralları düzenler: ad, yöntem, aranan ve yeni ad; sırayı değiştirir, siler, içe/dışa aktarır. */
export function FavoritesDialog({ store, favorites }: { store: AppStore; favorites: Favorite[] }) {
  const [selectedId, setSelectedId] = useState<string | undefined>(favorites[0]?.id);
  const current = favorites.find((f) => f.id === selectedId) ?? favorites[0];
  const [draft, setDraft] = useState<Favorite | undefined>(current);

  // Liste ya da seçim değişince (kaydetme, silme, taşıma) taslak baştan alınır.
  useEffect(() => {
    setSelectedId(current?.id);
    setDraft(current);
  }, [current]);

  const dirty = !!draft && !!current && JSON.stringify(draft) !== JSON.stringify(current);
  const index = favorites.findIndex((f) => f.id === current?.id);
  const close = () => store.answer(undefined);
  const edit = (patch: Partial<Favorite["rule"]>) => draft && setDraft({ ...draft, rule: { ...draft.rule, ...patch } });

  return (
    <Modal wide onEscape={close}>
      <div className="dialog-body">
        <h2>Favorilerim</h2>
        <p>Sık kullandığınız kuralları adlandırıp saklayın. Sırayı yukarı/aşağı düğmeleriyle değiştirebilirsiniz.</p>

        {favorites.length === 0 ? (
          <p>Henüz favori yok. “Ön ayarlar” menüsünden geçerli kuralı favorilere ekleyebilir ya da bir dosyadan içe aktarabilirsiniz.</p>
        ) : (
          <div className="fav-layout">
            <div className="fav-list" role="listbox" aria-label="Favoriler">
              {favorites.map((f) => (
                <button
                  key={f.id}
                  role="option"
                  aria-selected={f.id === current?.id}
                  className={`fav-item ${f.id === current?.id ? "active" : ""}`}
                  onClick={() => setSelectedId(f.id)}
                >
                  <span>{f.name}</span>
                  <span className="small-text muted">{methodNames[f.rule.method]}</span>
                </button>
              ))}
            </div>

            {draft && (
              <div className="fav-editor">
                <div className="field-label">Ad</div>
                <input className="input" value={draft.name} aria-label="Favori adı" onChange={(e) => setDraft({ ...draft, name: e.target.value })} />
                <div className="field-label">Yöntem</div>
                <select className="input" aria-label="Yöntem" value={draft.rule.method} onChange={(e) => edit({ method: e.target.value as RenameMethod })}>
                  {Object.entries(methodNames).map(([id, label]) => (
                    <option key={id} value={id}>{label}</option>
                  ))}
                </select>
                <div className="field-label">Aranan</div>
                <input className="input mono" value={draft.rule.search} spellCheck={false} aria-label="Aranan" onChange={(e) => edit({ search: e.target.value })} />
                <div className="field-label">Yeni ad</div>
                <input className="input mono" value={draft.rule.replace} spellCheck={false} aria-label="Yeni ad" onChange={(e) => edit({ replace: e.target.value })} />
                <div style={{ marginTop: 8 }}>
                  <Switch spread label="Büyük/küçük harfe duyarlı" checked={draft.rule.caseSensitive} onChange={(v) => edit({ caseSensitive: v })} />
                </div>
                <div className="row-between" style={{ marginTop: 10 }}>
                  <span style={{ display: "flex", gap: 6 }}>
                    <button className="btn icon" title="Yukarı taşı" aria-label="Yukarı taşı" disabled={index <= 0} onClick={() => void store.moveFavorite(draft.id, -1)}>
                      <Icon name="up" />
                    </button>
                    <button className="btn icon" title="Aşağı taşı" aria-label="Aşağı taşı" disabled={index < 0 || index >= favorites.length - 1} onClick={() => void store.moveFavorite(draft.id, 1)}>
                      <Icon name="down" />
                    </button>
                    <button className="btn danger" onClick={() => void store.removeFavorite(draft.id)}>
                      <Icon name="trash" /> Sil
                    </button>
                  </span>
                  <button className="btn primary" disabled={!dirty || !draft.name.trim()} onClick={() => void store.updateFavorite(draft)}>
                    Kaydet
                  </button>
                </div>
              </div>
            )}
          </div>
        )}
      </div>
      <div className="dialog-footer" style={{ justifyContent: "space-between" }}>
        <span style={{ display: "flex", gap: 8 }}>
          <button className="btn" onClick={() => void store.importFavorites()}>İçe aktar…</button>
          <button className="btn" disabled={favorites.length === 0} onClick={() => void store.exportFavorites()}>Dışa aktar…</button>
        </span>
        <span style={{ display: "flex", gap: 8 }}>
          <button className="btn" onClick={close}>Kapat</button>
          <button className="btn primary" disabled={!current || dirty} title={dirty ? "Önce kaydedin" : undefined} onClick={() => store.answer(current)}>
            Uygula ve kapat
          </button>
        </span>
      </div>
    </Modal>
  );
}

/** Yapılan adlandırmaların listesi. En üstteki geri alınır; art arda geri alarak daha eskilere inilir. */
export function HistoryDialog({ store, state }: { store: AppStore; state: AppState }) {
  const { history, busy } = state;
  const close = () => store.answer(undefined);

  return (
    <Modal small onEscape={close}>
      <div className="dialog-body">
        <h2>Geçmiş</h2>
        <p>
          Yaptığınız adlandırmalar, en yenisi üstte. Geri Al her seferinde en üstteki işlemi geri alır. Program kapatılsa bile geçmiş saklanır.
        </p>
        {history.length === 0 ? (
          <p>Henüz bir işlem yok.</p>
        ) : (
          <div className="history-list">
            {history.map((item, i) => (
              <div key={item.id} className={`history-row ${i === 0 ? "next" : ""}`}>
                <div style={{ minWidth: 0 }}>
                  <div className="title" title={item.description}>{item.description}</div>
                  <div className="small-text muted">{formatTime(item.time)}</div>
                </div>
                <div className="small-text muted" style={{ alignSelf: "center" }}>{item.count} öğe</div>
              </div>
            ))}
          </div>
        )}
      </div>
      <div className="dialog-footer">
        <button className="btn" onClick={close}>Kapat</button>
        <button
          className="btn primary"
          disabled={history.length === 0 || busy}
          onClick={() => {
            // Geri alma kendi pencerelerini açabilir; bu pencere önce kapanır.
            close();
            void store.undo();
          }}
        >
          <Icon name="undo" /> En üsttekini geri al
        </button>
      </div>
    </Modal>
  );
}

/** Gezgin menüleri: klasöre sağ tık ve dosyalara Gönder menüsü. */
export function SettingsDialog({ store, state }: { store: AppStore; state: AppState }) {
  const info = state.integration;
  const close = () => store.answer(undefined);

  return (
    <Modal small onEscape={close}>
      <div className="dialog-body">
        <h2>Ayarlar</h2>
        <div className="section-title">Görünüm</div>
        <div className="num-row">
          <span>Yazı tipi</span>
          <select className="input" aria-label="Yazı tipi" value={state.prefs.font} onChange={(e) => store.setPrefs({ font: e.target.value as FontChoice })}>
            {Object.entries(fonts).map(([id, f]) => <option key={id} value={id}>{f.label}</option>)}
          </select>
        </div>
        <div className="num-row">
          <span>Yazı boyutu</span>
          <select className="input" aria-label="Yazı boyutu" value={state.prefs.size} onChange={(e) => store.setPrefs({ size: e.target.value as SizeChoice })}>
            {Object.entries(sizes).map(([id, s]) => <option key={id} value={id}>{s.label}</option>)}
          </select>
        </div>
        <Switch spread label="Listeleme ve filtre şeridini göster" checked={state.prefs.showFilters} onChange={(v) => store.setPrefs({ showFilters: v })} />
        <Switch spread label="Alttaki durum özetini göster" checked={state.prefs.showSummary} onChange={(v) => store.setPrefs({ showSummary: v })} />
        <button className="btn small" style={{ marginTop: 8 }} onClick={() => store.resetLayout()}>Görünümü ilk haline döndür</button>

        <div className="divider" />
        <div className="section-title">Windows Gezgini</div>
        <p>Dosya Terzisi'ı Windows Gezgini'nden de açabilirsiniz. Yönetici izni gerekmez; yalnızca sizin kullanıcı hesabınız için eklenir ve buradan istediğiniz zaman kaldırılır.</p>
        {!info?.available ? (
          <p className="small-text muted">Bu pencerede Gezgin menüleri ayarlanamıyor.</p>
        ) : (
          <>
            <Switch
              spread
              label="Klasöre sağ tık: “Dosya Terzisi ile yeniden adlandır”"
              checked={info.folderMenu}
              onChange={(v) => void store.setIntegration({ folderMenu: v })}
            />
            <Switch
              spread
              label="Dosyalara sağ tık → Gönder → Dosya Terzisi"
              checked={info.sendTo}
              onChange={(v) => void store.setIntegration({ sendTo: v })}
            />
            <p className="small-text muted" style={{ marginTop: 8 }}>
              Windows 11'de menüler “Daha fazla seçenek göster” altında görünür (Shift+F10 ile de açılır). Program başka bir klasöre taşınırsa menüleri yeniden açın.
            </p>
          </>
        )}
      </div>
      <div className="dialog-footer">
        <button className="btn primary" autoFocus onClick={close}>Kapat</button>
      </div>
    </Modal>
  );
}
