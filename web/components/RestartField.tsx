"use client";

const tagChoices = ["ID3.Album", "ID3.Artist", "ID3.Year", "ID3.Genre", "EXIF.Model", "EXIF.OriginalTime:yyyyMMdd", "DOC.Author", "DOC.Title"];

interface Props {
  byDate: boolean;
  byTag: string;
  onChange: (patch: { resetNumberPerDate?: boolean; resetNumberPerTag?: string }) => void;
}

/** "Her klasörde yeniden başla"nın yanına: tarihe ve etikete göre de sıfırla. */
export function RestartField({ byDate, byTag, onChange }: Props) {
  return (
    <>
      <label className="checkbox-row">
        <span>Her tarihte (gün) yeniden başla</span>
        <span className="switch">
          <input type="checkbox" role="switch" checked={byDate} onChange={(e) => onChange({ resetNumberPerDate: e.target.checked })} />
          <span className="track" aria-hidden="true" />
        </span>
      </label>
      <div className="num-row">
        <span>Etiket değişince yeniden başla</span>
        <select className="input" aria-label="Etikete göre yeniden başla" value={byTag} onChange={(e) => onChange({ resetNumberPerTag: e.target.value })}>
          <option value="">Hayır</option>
          {tagChoices.map((t) => <option key={t} value={t.split(":")[0]}>{t.split(":")[0]}</option>)}
        </select>
      </div>
    </>
  );
}
