const turkish = "tr-TR";

export function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  const units = ["KB", "MB", "GB", "TB"];
  let value = bytes / 1024;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }
  return `${value.toLocaleString(turkish, { maximumFractionDigits: value < 10 ? 1 : 0 })} ${units[unit]}`;
}

export function formatDate(epochMs: number): string {
  return new Date(epochMs).toLocaleString(turkish, {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

export function formatTime(epochMs: number): string {
  return new Date(epochMs).toLocaleString(turkish, { day: "numeric", month: "long", hour: "2-digit", minute: "2-digit" });
}

export function fileKind(name: string, isDir: boolean): string {
  if (isDir) return "Klasör";
  const dot = name.lastIndexOf(".");
  return dot > 0 ? `${name.slice(dot + 1).toLocaleUpperCase(turkish)} dosyası` : "Dosya";
}

/** `C:\Kullanıcı\Belgeler\Evrak` → `Belgeler › Evrak` gibi kısa bir gösterim. */
export function shortPath(path: string, parts = 2): string {
  const segments = path.split(/[\\/]/).filter(Boolean);
  return segments.slice(-parts).join(" › ");
}
