# Dosya Terzisi — Kullanım

Dosya ve klasörlerin adlarını toplu olarak, önce önizleyerek değiştirir. Hiçbir şey siz onaylamadan değişmez, mevcut bir dosyanın üzerine hiçbir zaman yazılmaz ve her işlem **Geri Al** ile (program kapatılsa bile) geri alınabilir.

## Başlamak

1. `DosyaTerzisi.exe`'yi açın. İlk açılışta kısa bir tanıtım turu çıkar (sağ üstteki **?** ile tekrar açılır).
2. Bir klasör seçin: yolu yazın, **Gözat** deyin ya da klasörü/dosyaları pencereye sürükleyin. Geri, ileri ve üst klasör düğmeleri (Alt+←, Alt+→, Alt+↑) ile gezinin; listede bir klasöre çift tıklayınca içine girilir.
3. Kuralı yazın; **Yeni ad** sütunu yazdıkça güncellenir.
4. **Yeniden adlandır** → onay penceresi → **Devam et**.

## Sekmeler

- **Adlandır:** Joker (`*`, `?`), düzenli ifade ya da karakter çevirisi ile ad değiştirir. Değişkenler: `\1` `\2` (eşleşen parçalar), `\0` ad, `\t` uzantısız ad, `\e` uzantı, `\f` klasör adı, `\Y\m\d \H\M\S` tarih ve saat, `?` sıra numarası (`??` = 2 basamak), `\u \U \l \L \E` büyük/küçük harf. Tam liste için **Değişkenler** menüsüne bakın.
- **Etiketten:** Dosyanın içindeki bilgilerden ad kurar: `<ID3.Artist> - <ID3.Track>.\e` (müzik), `<EXIF.OriginalTime:yyyyMMdd>` (fotoğraf), `<DOC.Title>` (Word, Excel, PDF...), `<HTML.Title>` (web sayfası). Etiket adlarına tıklayarak şablona ekleyin.
- **Öznitelikler:** Salt okunur, arşiv, gizli, sistem özniteliklerini ve üç tarihi değiştirir (sabit tarih, kaydırma, başka tarihten alma, fotoğrafın çekim tarihi, rastgele).
- **Numaralı nesne:** Numaralı boş dosya/klasör ya da bir dosya/klasörün numaralı kopyalarını oluşturur.

## Sık kullanılanlar

| Ne | Nasıl |
|---|---|
| Bir satırın adını elle yaz | Satırı seçip **F2** |
| Satırı listeden çıkar (dosyaya dokunmaz) | **Delete** |
| Hepsini seç / seçimi ters çevir | **Ctrl+A** / **Ctrl+I** |
| Son işlemi geri al | **Ctrl+Z** ya da **Geri Al** |
| Listeyi yenile | **F5** |
| Kopyala, kısayol, sabit bağ | **İşlem** bölümü |
| Klasöre taşı | Yeni ada `Klasör/ad` yazın; `..` bir üst klasördür |
| Sütunlara etiket ekle, gruplara ayır | Liste başlığına sağ tık |
| Favori kural | **Ön ayarlar** menüsü |

## Gezgin'den açmak

Sağ üstteki **dişli** düğmesi → Ayarlar:

- **Klasöre sağ tık:** "Dosya Terzisi ile yeniden adlandır".
- **Dosyalara sağ tık → Gönder → Dosya Terzisi.**

Yönetici izni gerekmez; yalnızca sizin hesabınız için eklenir, aynı pencereden kaldırılır. Windows 11'de menüler "Daha fazla seçenek göster" altındadır. Program başka bir klasöre taşınırsa menüleri yeniden açın.

## Betik (ileri düzey)

**Diğer seçenekler → Betik** işaretlenirse **Yeni ad** bir VBScript/JScript ifadesi olarak değerlendirilir: `Buyuk("\1") & "." & "\2"`. Kendi işlevlerinizi **Betiği düzenle…** ile açılan `%AppData%\DosyaTerzisi\Script.vbs` (ya da `Script.js`) dosyasında tanımlarsınız.

## Dosyalarınız nerede saklanır

`%AppData%\DosyaTerzisi` altında: `history.json` (geri alma geçmişi), `favorites.json` (favoriler), betik dosyası. Program kapanırken ya da elektrik giderken yarım kalan bir işlem bir sonraki açılışta onarılır.

## Gereksinimler

Windows 10/11 ve Microsoft Edge WebView2 (Windows 11'de hazır gelir; yoksa program indirme sayfasını önerir). Ayrıca .NET kurulumu gerekmez.
