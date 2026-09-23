# F5.3 — Parite ve e2e doğrulaması (Rezervasyon fazı)

> Kalıp adım 3 ("Doğrulama: parite + e2e"). Kaynak: `F5.md` envanterindeki 6 Blazor sayfası
> (`Components/Pages/…`) ile SPA bileşenleri (`src/RentACar.Frontend/src/app/features/…`), 2026-09-23 `main`
> (#271 uçlar, #272 rezervasyon + teklif, #273 takvim/müsaitlik/rez şartları/filo kiralama).
> Yöntem: razor dosyası satır satır okundu; her alan / sütun / süzgeç / eylem / izin SPA şablonu, sütun
> tanımı, form modeli ve rota tablosuyla karşılaştırıldı. Eksik bulunan eklendi, bilinçli farklar gerekçeli.

## Özet

| Sayfa (Blazor → SPA) | Alan | Sütun | Süzgeç | Eylem | İzin | Sonuç |
|---|---|---|---|---|---|---|
| `/rezervasyonlar` → `features/rezervasyonlar` | 25/25 (+km/yakıt, açıklama) | 16/16 | 5/5 | Onayla, Kiraya çevir, Düzenle, İptal, Excel/CSV/PDF | OperationsWrite; iptal OperationsDelete (sunucu `yetkiler`) | **eklendi:** Opsiyonlu rengi |
| `/teklifler` → `features/teklifler` | 9/9 | 8/8 (tarih 2 sütun) | + durum | Gönder, Kabul → Rezervasyon (onaylı), Reddet (onaylı) | OperationsWrite | tam |
| `/takvim` → `features/takvim` | — | ızgara (K/R, hafta sonu, kira öncelikli) | ay, plaka/marka, grup, şube | ay gezinmesi süzgeci korur, Temizle | OperationsWrite | **eklendi:** rota izni |
| `/musaitlik` → `features/musaitlik` | 11 girdi | 25/25 | pencere + grup/şube/kaynak/döviz/plaka | Kirala (`?varac&vfrom&vto&vgrup`), broker notu | OperationsWrite | **eklendi:** rota izni |
| `/rez-sartlari` → `features/rez-sartlari` | 8/8 | 8/8 | müşteri, durum, talep aralığı | Karşılandı, Geri al, Düzenle, Sil (onaylı), "N kayıt · N bekleyen" | OperationsWrite | **eklendi:** rota izni |
| `/filo-kiralama` → `features/filo-kiralama` | 21/21 (yeni) + 14/14 (künye) | 12/12 | 6/6 | Tamamla, İptal (OperationsDelete, onaylı), Künye, Excel/CSV/PDF, "N sözleşme" | OperationsWrite | **eklendi:** rota izni |

## Bulunan eksikler (bu PR'da eklendi)

1. **Sayfa izni — planlama rotaları (izin paritesi).** Blazor `/takvim`, `/musaitlik`, `/rez-sartlari`,
   `/filo-kiralama` `[Authorize(Policy = "izin:OperationsWrite")]` taşıyor, uç grupları da OperationsWrite
   istiyor; SPA'daki altı planlama rotasında (`takvim`, `musaitlik`, `rez-sartlari`, `filo-kiralama`,
   `filo-kiralama/yeni`, `filo-kiralama/:id`) `canMatch` guard'ı YOKTU. Muhasebe rolü sayfayı açıp API
   403'üyle boş/hatalı ekran görüyordu. Düzeltme: `sayfalar.ts`'te `izinGuard('OperationsWrite')` (rezervasyon
   ve teklif rotalarıyla aynı). Çit: `src/app/f5-rota-izinleri.spec.ts` — beklenen 12 rota ELLE yazılı
   (razor `@attribute`'larından), rota tablosundan türetilmez. Gerçek backend'de Muhasebe kullanıcısıyla da
   doğrulandı (uyarı bandı + ana sayfa). Veri güvenliği değişmedi (uçlar zaten 403 veriyordu); yalnız UX paritesi.
2. **FAZ-81 "Opsiyonlu" rengi.** Blazor rezervasyon listesi `Rezerv` durumunu kiracının seçtiği renkle
   (`--tr-renk-opsiyonlu`) ayırıyordu; SPA sabit uyarı rozeti gösteriyordu. Düzeltme: `Rezerv` rozetine
   `--rc-kiraci-renk-opsiyonlu` kenar çizgisi (Panel satır deseni; renk seçilmemişse şeffaf, metin kontrastı
   kiracı rengine bağlı değil).

## Bilinçli farklar (gerekçeli)

| Fark | Gerekçe |
|---|---|
| Rezervasyon satırında İptal yok, yalnız detayda (F5.2a) | Geri alınamaz işlem; detayda onay diyaloğu + sunucu `yetkiler.iptal` (OperationsDelete). Blazor satırda `<details>` içinde düzenleme formu da taşıyordu — SPA'da düzenleme detay sayfasında. |
| Kiraya çevir onay soruyor (Blazor sormuyordu) | Kira sözleşmesi açar; kirli formda "kaydedilmemiş değişiklik taşınmaz" uyarısı. |
| Filo satır eylemleri (Tamamla/İptal/Künye) detayda (F5.2b) | Künye formu 14 alan; satır içi `<details>` form SPA tablo motorunda erişilebilir değil. Liste "No" bağlantısı detaya gider. |
| Teklif listesinde "Tarih" tek hücre yerine Başlangıç + Bitiş iki sütun | Sıralanabilir ve kart görünümünde okunur; bilgi kaybı yok. |
| Kabul edilen teklifin durum bağlantısı oluşan REZERVASYONA gider | Blazor genel `/rezervasyonlar` listesine gidiyordu; kaydın kendisine gitmek doğrudan. |
| Takvimde plaka → `/kiralar/yeni?varac=` bağlantısı (Blazor'da yoktu) | F4.3 sorgu sözleşmesi; takvimden kira açma kısayolu. Hücreler bağlantısız (Blazor gibi). |
| Takvim "Temizle" ay dışındaki süzgeçleri siler, ayı korur | Blazor ile aynı; SPA'da düğme. |
| Rezervasyon listesi sayfalı (Blazor tümünü basıyordu) | Tablo motoru; toplam sayfa altında. Dışa aktarma ekran süzgeciyle (Blazor adlarıyla). |
| Rez şartları durum süzgeci API adlarıyla (`bekleyen`/`karsilanan`) | Blazor değerleriyle aynı. |
| Yeni rezervasyon tarihlerinde `min=now` HTML sınırı yok | Sınır sunucuda (`TarihPolitikasi`): geçmiş ve +1 yıl alan hatası olarak döner (gerçek e2e'de görüldü: "Rezervasyon en fazla 1 yıl ileri alınabilir."). |

## e2e

**Sahte API (CI'da koşar):** `rezervasyon.spec.ts`, `planlama.spec.ts`, `rez-sartlari.spec.ts`,
`filo-kiralama.spec.ts` (F5.2a/b) — üç zorunlu senaryo, axe iki tema, 320/390/768/1440 taşma.

**Gerçek backend (bu PR, yerelde koşar; CI'da atlanır):** `f5-rezervasyon-gercek.spec.ts`,
`f5-planlama-gercek.spec.ts`, `f5-kapsam-gercek.spec.ts` + `gercek.ts` yardımcıları. Ortam
`RACAR_E2E_KOK` + `RACAR_E2E_SIFRE` (bkz. `e2e/ortam.ts`); kullanıcılar rastgele parolayla koşum öncesi
üretilir, sonra silinir. Pencere rastgele ileri tarih, araç sunucunun müsaitlik ucundan; oluşan kira ve
rezervasyonlar koşum sonunda iptal edilir.

| Senaryo (gerçek backend, yerel `racar`, firma `yucerent`, 2026-09-23) | Sonuç |
|---|---|
| Rezervasyon oluştur → onayla → takvimde 4 R hücresi → takvim plaka bağlantısı kira formunu `?varac=` ile dolu açar → kiraya çevir → SPA kira formu aynı araç + başlangıç/bitiş günü; rezervasyon `KirayaCevrildi` + `kiraId`, takvimde R kalmaz | ✔ |
| Teklif oluştur → gönder → kabul (onaylı) → "Rezervasyonu aç"; rezervasyon aynı müşteri/araç, 3 gün; kabulden sonra eylem yok | ✔ |
| Müsaitlik (gün-sayısı modu) → Kirala: `?varac&vfrom&vto&vgrup` sunucunun çözdüğü pencere; kira formu araç + günlerle dolu (F4.3 sorgu sözleşmesi) | ✔ |
| Rez şartı oluştur → karşılandı → geri al → sil (onaylı) | ✔ |
| Filo oluştur → taksit planı 3.600,00 ₺ (3 × 1.000 × 1,20) → künye kaydet + yeniden yükle → tamamla | ✔ |
| Şube kapsamı: "ADV Şube B" operatörü Merkez rezervasyonunu/filo sözleşmesini açamaz (403/404), listede görmez, onaylayamaz; takvimde Merkez aracı yok (Araç: 0) | ✔ |
| Sayfa izni: Muhasebe `/takvim`, `/musaitlik`, `/rez-sartlari`, `/filo-kiralama`, `/teklifler` → uyarı bandı + ana sayfa | ✔ |

Toplam: gerçek backend 11/11 ✔; sahte API F5 spec'leri 33/33 ✔ (gerçek spec'ler CI'da 11 atlanır);
Vitest 697/697 ✔. Backend hatası bulunmadı; para/yetki koduna dokunulmadı (izin düzeltmesi yalnız SPA rota
guard'ı — sunucu uçları zaten OperationsWrite istiyordu).

**Koşum (gerçek):** Web `ASPNETCORE_URLS=http://localhost:5351 Spa__Dizin=<dist>/browser dotnet run --no-launch-profile`;
kiracının pilot bayrağı geçici açılır; üç kullanıcı (Admin, "ADV Şube B" Operatör, Muhasebe) rastgele parolayla
eklenir; `RACAR_E2E_KOK=… RACAR_E2E_SIFRE=… npx playwright test <geçici config: testMatch -gercek, webServer yok>`.
Bitince pilot kapatılır, kullanıcılar silinir, Web durdurulur. `/login` hız sınırı (10/dk) yüzünden
yardımcı her kullanıcıyla koşum başına bir kez girer.
