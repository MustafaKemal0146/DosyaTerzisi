"use client";

import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { Icon } from "./Icon";
import type { AppStore } from "@/lib/store";

const SEEN_KEY = "dosyaterzisi.tour.v1";

interface Step {
  /** Vurgulanacak elemanın CSS seçicisi; yoksa kart ortada durur. */
  target?: string;
  title: string;
  body: string;
}

const steps: Step[] = [
  {
    title: "Dosya Terzisi'a hoş geldiniz",
    body: "Çok sayıda dosyanın adını bir kerede, önce önizleyerek değiştirirsiniz. Hiçbir şey siz onaylamadan değişmez ve her işlem geri alınabilir. Kısa bir tur yapalım (yaklaşık yarım dakika).",
  },
  {
    target: ".folderbar-row",
    title: "1. Klasörü seçin",
    body: "Klasör yolunu yazın, “Gözat” ile seçin ya da bir klasörü veya dosyaları doğrudan pencereye sürükleyin. Geri, ileri ve üst klasör düğmeleriyle (Alt+←, Alt+→, Alt+↑) gezinebilir, listede bir klasöre çift tıklayarak içine girebilirsiniz. Soldaki düğme klasör ağacını açar.",
  },
  {
    target: ".filters",
    title: "2. Neyin listeleneceğini belirleyin",
    body: "Dosyaları, klasörleri ya da ikisini birden listeleyin. “*.jpg;*.png” gibi bir filtre yazın, alt klasörleri de derinliğini seçerek dahil edin.",
  },
  {
    target: ".tabs",
    title: "3. Dört çalışma biçimi",
    body: "Adlandır: arama-değiştirme kuralıyla ad değiştirir. Etiketten: MP3 etiketlerinden ve fotoğraf bilgilerinden ad üretir. Öznitelikler: salt okunur, gizli gibi öznitelikleri ve tarihleri değiştirir. Numaralı nesne: numaralı boş dosya ya da klasör oluşturur.",
  },
  {
    target: ".rulepanel",
    title: "4. Kuralı yazın",
    body: "Yöntemi seçin (Joker, Düzenli ifade, Çeviri), “Aranan” ve “Yeni ad” alanlarını doldurun. “Ekle” menüleri değişkenleri tek tıkla yazar. Alttaki kutu kuralın ne yaptığını anlatır. Hazır “Ön ayarlar”ı ve favorilerinizi buradan kullanabilirsiniz. Aşağıdaki “İşlem” bölümüyle adlandırmak yerine kopyalayabilir, kısayol ya da sabit bağ oluşturabilirsiniz; yeni adı “Klasör/ad” diye yazarsanız dosyalar o klasöre taşınır.",
  },
  {
    target: ".listcard",
    title: "5. Önizlemeyi inceleyin",
    body: "“Yeni ad” sütunu, yazdıkça canlı güncellenir: silinecek kısımlar üstü çizili, eklenenler vurgulu görünür. Çakışma ya da geçersiz ad varsa satır kırmızı olur ve işlem yapılmaz. Bir satırda F2’ye basarak yeni adı elle yazabilir, Delete ile satırı listeden çıkarabilirsiniz (dosyaya dokunulmaz). Ctrl+A hepsini seçer, Ctrl+I seçimi ters çevirir; işlem yalnızca seçililere uygulanır.",
  },
  {
    target: ".actionbar",
    title: "6. Uygulayın, gerekirse geri alın",
    body: "“Adlandır” düğmesi önce bir onay penceresi gösterir; siz onaylamadan hiçbir dosya değişmez ve mevcut bir dosyanın üzerine asla yazılmaz. “Geri Al” (Ctrl+Z) son işlemi geri alır; geçmiş, program kapanıp açılsa bile saklanır.",
  },
  {
    title: "Hazırsınız",
    body: "Sağ üstteki dişli düğmesi Windows Gezgini’ne “sağ tık” menüleri ekler; yardım (?) düğmesi bu turu yeniden açar. İyi çalışmalar!",
  },
];

const CARD_WIDTH = 380;

/** Kartı, vurgulanan alanı örtmeyecek yere koyar: önce altı, sonra üstü, sağı, solu; hiçbiri sığmazsa alt ortaya. */
function placeCard(rect: DOMRect | null, height: number, view: { width: number; height: number }): React.CSSProperties {
  const w = view.width;
  const h = view.height;
  const gap = 14;
  const margin = 16;
  const clampLeft = (x: number) => Math.min(Math.max(margin, x), Math.max(margin, w - CARD_WIDTH - margin));
  const clampTop = (y: number) => Math.min(Math.max(margin, y), Math.max(margin, h - height - margin));

  if (!rect) return { width: CARD_WIDTH, left: "50%", top: "50%", transform: "translate(-50%, -50%)" };
  if (rect.bottom + gap + height + margin <= h) return { width: CARD_WIDTH, left: clampLeft(rect.left), top: rect.bottom + gap };
  if (rect.top - gap - height >= margin) return { width: CARD_WIDTH, left: clampLeft(rect.left), top: rect.top - gap - height };
  if (rect.right + gap + CARD_WIDTH + margin <= w) return { width: CARD_WIDTH, left: rect.right + gap, top: clampTop(rect.top) };
  if (rect.left - gap - CARD_WIDTH >= margin) return { width: CARD_WIDTH, left: rect.left - gap - CARD_WIDTH, top: clampTop(rect.top) };
  return { width: CARD_WIDTH, left: clampLeft((w - CARD_WIDTH) / 2), top: clampTop(h - height - margin) };
}

/** İlk açılışta bir kez görünen, yardım düğmesinden yeniden açılabilen tanıtım turu. */
export function Tour({ store, open }: { store: AppStore; open: boolean }) {
  const [index, setIndex] = useState(0);
  const [rect, setRect] = useState<DOMRect | null>(null);
  const [cardHeight, setCardHeight] = useState(240);
  const [view, setView] = useState({ width: 1000, height: 700 });
  const next = useRef<HTMLButtonElement>(null);
  const card = useRef<HTMLDivElement>(null);
  const step = steps[index]!;

  // İlk açılışta, daha önce görülmediyse otomatik başlar. "?tour=0" ile (geliştirme/deneme) kapatılabilir.
  useEffect(() => {
    let seen = false;
    try {
      seen = localStorage.getItem(SEEN_KEY) === "1" || new URLSearchParams(location.search).get("tour") === "0";
    } catch {
      seen = true; // Depolama kapalıysa her açılışta rahatsız etme; yardım düğmesi yine çalışır.
    }
    if (!seen) store.openTour();
  }, [store]);

  useEffect(() => {
    if (open) setIndex(0);
  }, [open]);

  const close = () => {
    try {
      localStorage.setItem(SEEN_KEY, "1");
    } catch {
      // Kaydedilemedi: bir sonraki açılışta tur yine görünür.
    }
    store.closeTour();
  };

  useLayoutEffect(() => {
    if (!open) return;
    const measure = () => {
      const el = step.target ? document.querySelector(step.target) : null;
      // Sayfa büyütülmüşse (Ayarlar → yazı boyutu) ölçüler büyütülmüş koordinatlardadır; tur de aynı kapsayıcıdadır.
      const zoom = Number.parseFloat(getComputedStyle(document.documentElement).getPropertyValue("--ui-zoom")) || 1;
      const r = el?.getBoundingClientRect();
      setRect(r ? new DOMRect(r.left / zoom, r.top / zoom, r.width / zoom, r.height / zoom) : null);
      setView({ width: window.innerWidth / zoom, height: window.innerHeight / zoom });
      if (card.current) setCardHeight(card.current.offsetHeight);
    };
    measure();
    window.addEventListener("resize", measure);
    return () => window.removeEventListener("resize", measure);
  }, [open, step]);

  useLayoutEffect(() => {
    if (open && card.current) setCardHeight(card.current.offsetHeight);
  }, [open, index]);

  useEffect(() => {
    if (!open) return;
    next.current?.focus();
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") close();
      else if (e.key === "ArrowRight") setIndex((i) => Math.min(steps.length - 1, i + 1));
      else if (e.key === "ArrowLeft") setIndex((i) => Math.max(0, i - 1));
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, index]);

  if (!open) return null;

  const last = index === steps.length - 1;
  const pad = 6;
  const cardStyle = placeCard(rect, cardHeight, view);

  return (
    <div className="tour" role="dialog" aria-modal="true" aria-label="Tanıtım turu">
      {rect ? (
        <div
          className="tour-spot"
          style={{ left: rect.left - pad, top: rect.top - pad, width: rect.width + pad * 2, height: rect.height + pad * 2 }}
        />
      ) : (
        <div className="tour-dim" />
      )}
      <div className="tour-card" ref={card} style={cardStyle}>
        <div className="tour-head">
          <h2>{step.title}</h2>
          <button className="btn icon small subtle" aria-label="Turu kapat" title="Turu kapat (Esc)" onClick={close}>
            <Icon name="x" size={14} />
          </button>
        </div>
        <p>{step.body}</p>
        <div className="tour-foot">
          <span className="tour-dots" aria-label={`Adım ${index + 1} / ${steps.length}`}>
            {steps.map((_, i) => (
              <span key={i} className={i === index ? "on" : ""} />
            ))}
          </span>
          <span className="tour-spacer" />
          {!last && (
            <button className="btn subtle" onClick={close}>
              Atla
            </button>
          )}
          {index > 0 && (
            <button className="btn" onClick={() => setIndex(index - 1)}>
              Geri
            </button>
          )}
          <button ref={next} className="btn primary" onClick={() => (last ? close() : setIndex(index + 1))}>
            {last ? "Başla" : "İleri"}
          </button>
        </div>
      </div>
    </div>
  );
}
