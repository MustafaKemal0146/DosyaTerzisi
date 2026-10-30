// Gelen ve giden mesajların sözleşmeye uyup uymadığını denetler.
// Kurallar C# tarafındaki ContractValidator ile aynıdır: her alan var, tipi doğru, fazladan alan yok.

import type { Bridge } from "./contract";
import { methodNames, methodShapes, shapes, type FieldShape, type MethodName } from "./wire.generated";

/** Boş liste = değer sözleşmeye uyuyor. Hata metinleri C# tarafıyla aynı yolu (`$.rows[0].message`) gösterir. */
export function conforms(value: unknown, shape: FieldShape, path = "$"): string[] {
  if (value === null) return shape.nullable ? [] : [`${path}: boş (null) olamaz`];

  switch (shape.kind) {
    case "string":
      return typeof value === "string" ? [] : [`${path}: metin olmalı`];
    case "boolean":
      return typeof value === "boolean" ? [] : [`${path}: doğru/yanlış olmalı`];
    case "number":
      return typeof value === "number" && Number.isFinite(value) ? [] : [`${path}: sayı olmalı`];
    case "integer":
      return typeof value === "number" && Number.isSafeInteger(value) ? [] : [`${path}: tam sayı olmalı`];
    case "enum": {
      const def = shapes[shape.name];
      const values = def?.kind === "enum" ? def.values : [];
      return typeof value === "string" && values.includes(value) ? [] : [`${path}: şunlardan biri olmalı: ${values.join(", ")}`];
    }
    case "array":
      return Array.isArray(value) ? value.flatMap((item, i) => conforms(item, shape.item, `${path}[${i}]`)) : [`${path}: liste olmalı`];
    case "object": {
      if (typeof value !== "object" || Array.isArray(value)) return [`${path}: nesne olmalı`];
      const def = shapes[shape.name];
      if (def?.kind !== "object") return [`${path}: bilinmeyen tip ${shape.name}`];

      const record = value as Record<string, unknown>;
      const errors = Object.keys(record)
        .filter((key) => !(key in def.fields))
        .map((key) => `${path}.${key}: tanımsız alan`);
      for (const [name, field] of Object.entries(def.fields))
        errors.push(...(name in record ? conforms(record[name], field, `${path}.${name}`) : [`${path}.${name}: eksik`]));
      return errors;
    }
  }
}

export function validateParams(method: MethodName, value: unknown): string[] {
  return conforms(value, methodShapes[method].params);
}

export function validateResult(method: MethodName, value: unknown): string[] {
  return conforms(value, methodShapes[method].result);
}

export class ContractError extends Error {
  constructor(
    readonly method: string,
    readonly part: "params" | "result",
    readonly problems: string[],
  ) {
    super(`${method} ${part === "params" ? "parametreleri" : "cevabı"} sözleşmeye uymuyor: ${problems.slice(0, 5).join("; ")}`);
  }
}

/** Bir köprüyü sarar: her çağrının parametresini ve cevabını sözleşmeye göre denetler. Testlerde kullanılır. */
export function validated(inner: Bridge): Bridge {
  const wrapped: Record<string, unknown> = { kind: inner.kind };
  for (const method of methodNames) {
    wrapped[method] = async (params: unknown, attachments?: ArrayLike<unknown>) => {
      const bad = validateParams(method, params);
      if (bad.length > 0) throw new ContractError(method, "params", bad);

      const result = await (inner[method] as (p: unknown, a?: ArrayLike<unknown>) => Promise<unknown>)(params, attachments);
      const badResult = validateResult(method, result);
      if (badResult.length > 0) throw new ContractError(method, "result", badResult);
      return result;
    };
  }
  return wrapped as Bridge;
}
