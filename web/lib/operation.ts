import type { Operation } from "./contract";

/** İşlemin ekranlarda kullanılan Türkçe karşılıkları. */
export const operationText: Record<Operation, { action: string; will: string; did: string; failed: string; button: string }> = {
  rename: { action: "yeniden adlandır", will: "yeniden adlandırılacak", did: "yeniden adlandırıldı", failed: "Yeniden adlandırılamayanlar", button: "Yeniden Adlandır" },
  copy: { action: "kopyala", will: "kopyalanacak", did: "kopyalandı", failed: "Kopyalanamayanlar", button: "Kopyala" },
  shortcut: { action: "kısayol oluştur", will: "için kısayol oluşturulacak", did: "için kısayol oluşturuldu", failed: "Kısayolu oluşturulamayanlar", button: "Kısayol Oluştur" },
  hardlink: { action: "sabit bağ oluştur", will: "için sabit bağ oluşturulacak", did: "için sabit bağ oluşturuldu", failed: "Bağı oluşturulamayanlar", button: "Sabit Bağ Oluştur" },
};
