export type DiffKind = "same" | "added" | "removed";

export interface DiffPart {
  text: string;
  kind: DiffKind;
}

/**
 * Eski ve yeni adı karşılaştırır: iki adda da olan harfler "same", yalnızca yenide olanlar "added",
 * yalnızca eskide olanlar "removed" olur. En uzun ortak alt diziye göre, harf harf.
 */
export function diffNames(before: string, after: string): DiffPart[] {
  const a = [...before];
  const b = [...after];
  const n = a.length;
  const m = b.length;

  // lcs[i][j]: a[i..] ile b[j..] arasındaki ortak dizinin uzunluğu
  const lcs: Uint16Array[] = Array.from({ length: n + 1 }, () => new Uint16Array(m + 1));
  for (let i = n - 1; i >= 0; i--) {
    for (let j = m - 1; j >= 0; j--) {
      lcs[i]![j] = a[i] === b[j] ? lcs[i + 1]![j + 1]! + 1 : Math.max(lcs[i + 1]![j]!, lcs[i]![j + 1]!);
    }
  }

  const parts: DiffPart[] = [];
  const push = (text: string, kind: DiffKind) => {
    const last = parts[parts.length - 1];
    if (last && last.kind === kind) last.text += text;
    else parts.push({ text, kind });
  };

  let i = 0;
  let j = 0;
  while (i < n && j < m) {
    if (a[i] === b[j]) {
      push(a[i]!, "same");
      i++;
      j++;
    } else if (lcs[i + 1]![j]! >= lcs[i]![j + 1]!) {
      push(a[i]!, "removed");
      i++;
    } else {
      push(b[j]!, "added");
      j++;
    }
  }
  while (i < n) push(a[i++]!, "removed");
  while (j < m) push(b[j++]!, "added");

  return parts;
}
