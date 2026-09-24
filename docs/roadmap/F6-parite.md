# F6.3 — Parite ve e2e doğrulaması (Araçlar fazı)

> Kalıp adım 3 ("Doğrulama: parite + e2e"). Kaynak: `F6.md` envanterindeki 14 Blazor sayfası
> (`Components/Pages/…`) ile SPA bileşenleri (`src/RentACar.Frontend/src/app/features/vehicles` — #285,
> `features/vehicle-finance` — #291), 2026-09-24 `main`. Yöntem: razor dosyası okundu; her alan / sütun / süzgeç /
> eylem / izin SPA şablonu, sütun tanımı, form modeli ve rota tablosuyla karşılaştırıldı. Eksik bulunan eklendi,
> bilinçli farklar gerekçeli.

## Özet

| Sayfa (Blazor → SPA)                                     | Alan                                               | Sütun                                       | Süzgeç                                                     | Eylem                                                                                                                                  | İzin                                                                      | Sonuç                                    |
| -------------------------------------------------------- | -------------------------------------------------- | ------------------------------------------- | ---------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------- | ---------------------------------------- |
| `/vehicles` → `araclar`                                  | yeni araç formu → `araclar/yeni` (tam kart)        | 49/49 (sabit plaka, sağa yaslı para)        | 9/9 (Grup\|SIPP, "sahibi girilmemiş", tarih türü + aralık) | Excel/CSV/PDF, Liste ↔ Modele göre grupla (200 kırpma notu), Detay, Karne (finans rolleri), Sil (OperationsDelete, onaylı), özet şerit | OperationsWrite ∨ ViewReports                                             | tam                                      |
| `/vehicles/{id}` → `araclar/:id`                         | 85/85 (5 bölüm) + tanımsız grup korunur + son 3 KM | —                                           | —                                                          | Kaydet (`surum`, 409 birleştirme), foto yükle/yukarı/aşağı/sil (onaylı, 20 / 2 MB), kapak rozeti                                       | okuma OperationsWrite ∨ ViewReports, yazma OperationsWrite                | tam                                      |
| `/araclar/{id}` → `araclar/:id/detay`                    | künye 4 alan                                       | kira 5 · servis 5 · ceza 5 · KM 3 · hasar 4 | —                                                          | Manuel KM gir, Ön muhasebe/analiz (finans rolleri)                                                                                     | aynı                                                                      | tam                                      |
| `/vehicles/detayli` → `araclar/detayli`                  | —                                                  | 49/49                                       | 3/3                                                        | Temizle                                                                                                                                | ViewReports                                                               | tam                                      |
| `/arac-durum` → `arac-durum`                             | —                                                  | 24/24                                       | 15/15                                                      | Kirala (`?varac=`), Servis, **Tahsis**, 60 sn tazeleme, sayaçlar                                                                       | OperationsWrite                                                           | **eklendi:** Tahsis → SPA BAF formu dolu |
| `/arac-sahipleri`, `/segmentler`, `/arac-tipleri` → aynı | 3+durum / 3+durum / 6+durum                        | 5 / 5 / 8                                   | —                                                          | ekle, düzenle (`surum`), sil (onaylı)                                                                                                  | OperationsWrite                                                           | tam (bkz. bilinçli fark: öneri listesi)  |
| `/arac-kredi` → `arac-kredi` + `arac-kredi/:id`          | 9/9                                                | 12/12                                       | 6/6                                                        | 5 özet kart, toplu taksit iptali (OperationsDelete, onaylı), dışa aktarma süzgeçle, Taksit Öde, İptal                                  | okuma OperationsWrite ∨ FinanceWrite ∨ ViewReports                        | tam                                      |
| `/musteri-taksit` → `musteri-taksit`                     | tek taksit 9/9 + plan 7/7                          | 12/12                                       | 6/6                                                        | 5 özet kart, Ödendi / Geri Al, Düzenle, Sil (onaylı), Plan Üret                                                                        | FinanceWrite ∨ ViewReports (yazma FinanceWrite)                           | tam                                      |
| `/arac-siparis` → `arac-siparis` + `/yeni` + `/:id`      | 26/26 (döviz/kur görünür)                          | 20/20                                       | 7/7                                                        | Onayla, Teslim Al, İptal (onaylı), Düzenle, dışa aktarma süzgeçle                                                                      | okuma OperationsWrite ∨ FinanceWrite ∨ ViewReports, yazma OperationsWrite | tam                                      |
| `/baf` → `baf`                                           | 11/11 + teslim formu                               | tam                                         | 8/8                                                        | Tahsis Et, Teslim Al, İptal (OperationsDelete, onaylı), dışa aktarma                                                                   | OperationsWrite                                                           | tam                                      |
| `/hasar` → `hasar`                                       | 4/4                                                | 7/7                                         | + durum                                                    | Onaya gönder, Onayla, Reddet, Kapat                                                                                                    | OperationsWrite                                                           | tam                                      |
| `/filo-plan` → `filo-plan`                               | 5/5                                                | 10/10                                       | —                                                          | ekle, +1/−1, düzenle (`surum`), sil (onaylı)                                                                                           | ViewReports ∨ OperationsWrite                                             | tam                                      |

## Bulunan eksik (bu PR'da eklendi)

1. **Durum panosu "Tahsis" (eylem paritesi).** Blazor `FleetStatus` satırında personel seçip `/baf/create`'e
   post eden satır içi tahsis formu vardı (araç, çıkış KM'si, şube gizli alanlardan). SPA'da bağlantı eski
   arayüzün `/baf` sayfasına tam sayfa gidiyordu (F6.2b öncesi yer tutucu) — araç bilgisi taşınmıyordu. Artık
   SPA BAF ekranının "Yeni Tahsis" formu **araç + çıkış KM + şube dolu** açılır (router `state`,
   `features/vehicles/allocation-prefill.ts`); personel orada seçilir, kayıt kullanıcıda. İpucu metni
   güncellendi. Çit: `allocation-prefill.spec.ts` (Vitest), `vehicle-finance.spec.ts` sahte e2e, gerçek e2e.

## Bilinçli farklar (gerekçeli)

| Fark                                                                                       | Gerekçe                                                                                                                                                           |
| ------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Taksit Öde kredi kaydında (`arac-kredi/:id`), listede değil                                | Para yazan işlem: sonraki taksit planı, hesap seçimi, sonucu bilinmeyen gönderimde aynı anahtarla tekrar (#291). Liste satırı "No" bağlantısı kayda gider.        |
| Taksit Öde izni FinanceWrite (Blazor düğmeyi Admin/Yönetici/Muhasebe rolüyle gösteriyordu) | Uç zaten FinanceWrite istiyor; SPA sunucunun `yetkiler.taksitOde` bayrağını izler. Rol listesiyle aynı küme.                                                      |
| Kredi oluşturma OperationsWrite (Blazor formu FinanceWrite ile kapılıydı)                  | Uç `RequirePermission(OperationsWrite)`; şubeye bağlı kullanıcı kendi şubesinin aracını seçmek zorunda (gerçek e2e'de doğrulandı: araçsız → `errors[vehicleId]`). |
| Müşteri taksit okuma FinanceWrite ∨ ViewReports (Blazor sayfası yalnız FinanceWrite)       | Uçlar okuma için ViewReports'u da kabul ediyor; yazma düğmeleri FinanceWrite ile.                                                                                 |
| Sipariş formu ayrı sayfa (`/yeni`, `/:id`), Blazor liste altında tek form                  | 26 alanlı form; kayıt `surum`, `cakisma` birleştirmesi ve geçiş düğmeleri kayıt sayfasında.                                                                       |
| Sipariş "Teslim Al" Bekliyor'da da görünür (Blazor yalnız Onaylandı'da)                    | Servisin tek geçiş tablosu Bekliyor → TeslimAlındı'ya izin veriyor; SPA sunucunun `yetkiler`ini izler (gerçek e2e kilitliyor).                                    |
| Boş kur = otomatik (TRY 1, dövizde firma kuru/TCMB)                                        | Blazor kur kutusu `1` önerisiyle gelirdi; dövizde yanlış kur yazdırıyordu. `KurCozucu` sunucuda.                                                                  |
| BAF ve filo plan tek sayfa panel (satır içi Teslim Al / düzenle panelleri)                 | Blazor `<details>` satır içi formlarının erişilebilir karşılığı.                                                                                                  |
| Hasar sayfası OperationsWrite (Blazor `[Authorize]`)                                       | Blazor POST uçları zaten OperationsWrite istiyordu; Muhasebe sayfayı açıp hiçbir işlem yapamıyordu.                                                               |
| Araç listesi/kartı OperationsWrite ∨ ViewReports (Blazor `[Authorize]`)                    | Uç sözleşmesi; Muhasebe salt okunur kart görür ("yalnız görüntüleme" bandı).                                                                                      |
| Tanım ekranlarında (tip: marka/grup) öneri listesi yok                                     | Ortak `rc-tanim-crud` bileşeni öneri taşımıyor; Blazor ComboBox'ı da seç-veya-yaz (serbest metin) idi — değer kümesi değişmez. Ortak bileşene ekleme ayrı iş.     |
| Durum panosu "Servis" eski arayüze tam sayfa gider                                         | Servis ekranı F9'da taşınır; Blazor satır içi `/servisler/create` formu (F9'a kalan uç) orada karşılanır.                                                         |
| Durum panosunda Tahsis satır içi değil, BAF formuna gider                                  | Personel seçimi sunucu typeahead'i; satır içi seçim 24 sütunlu tablo motorunda erişilebilir değil.                                                                |

## e2e

**Sahte API (CI'da koşar):** `vehicles.spec.ts`, `vehicle-finance.spec.ts` (+1: durum panosu Tahsis → BAF formu
dolu, personelsiz kayıt gitmez).

**Gerçek backend (yerelde koşar; CI'da atlanır):** `f6-araclar-gercek.spec.ts` (4), `f6-arac-finans-gercek.spec.ts`
(5), `gercek.ts` yardımcıları. Kimlik ortamdan (`RACAR_E2E_*`); sabit parola yok.

| Senaryo (gerçek backend, yerel `racar`, firma `yucerent`, 2026-09-24)                                                                                                                                                                                                | Sonuç |
| -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----- |
| Araç SPA formundan (Merkez) → marka + KM düzenle (sürüm ilerler) → bayat `surum` PUT 409 `cakisma`, kayıt değişmez → 2 foto yükle ("2/20") → 2.'yi yukarı taşı, sunucu sırası değişir → durum panosunda tek satır → Tahsis: BAF formu araç + 12.500 KM + Merkez dolu | ✔     |
| Şube kapsamı: "ADV Şube B" operatörü Merkez aracının kartı/fotoğrafları 403, listede/panoda yok, KM girişi 403, Merkez'e araç açamaz, silemez; SPA kartında Kaydet yok                                                                                               | ✔     |
| Muhasebe kartı okur, PUT 403, pano 403, detaylı liste 200; SPA "yalnız görüntüleme" bandı; `/arac-durum`, `/arac-sahipleri`, `/araclar/yeni` → uyarı bandı + ana sayfa. Operatör `/araclar/detayli` açamaz                                                           | ✔     |
| Kredi (SPA): 12.000 ₺ / faiz 0 / 12 taksit → aylık 1.000,00 (elle) → Taksit Öde → `odenenTaksit` 1; Kasa defterinde bu kredi için TEK satır, alacak 1.000,00, borç 0 → aynı sıra ikinci kez ödenmez → İptal (onaylı) → ödeme reddi, Taksit Öde düğmesi kalkar        | ✔     |
| Müşteri taksit planı 1.000 / 3 → 333,33 + 333,33 + 333,34 (kalan-yöntemi, elle) → Ödendi → Geri Al → silme (deftere yazmayan takip kaydı)                                                                                                                            | ✔     |
| Sipariş (SPA): toplam 2 × 750.000 = 1.500.000 (elle) → Onayla → Teslim Al; teslim alınmış sipariş iptal/onay reddi; ikinci sipariş İptal (onaylı) → onay reddi                                                                                                       | ✔     |
| Operatör: kredi formu görünür, araçsız kredi `errors[vehicleId]`, filo geneli kredi kapsam dışı (listede yok, 403/404), taksit öde / iptal 403; müşteri taksit 403 + uyarı bandı                                                                                     | ✔     |
| Muhasebe: kredi açamaz (403), kredi kaydında Taksit Öde var / İptal yok (`yetkiler`), müşteri taksit okur; BAF / hasar uçları 403, `/baf`, `/hasar`, `/arac-siparis/yeni` uyarı bandı                                                                                | ✔     |

Toplam: gerçek backend 9/9 ✔; sahte API `vehicles.spec.ts` + `vehicle-finance.spec.ts` 51/51 ✔ (port 4431).
Backend hatası bulunmadı; backend koduna dokunulmadı. Para koduna dokunulmadı (Tahsis ön doldurma yalnız form
değeri; kayıt ve doğrulama sunucuda aynen).

**Koşum (gerçek):** Web `ASPNETCORE_URLS=http://localhost:<port> Spa__Dizin=<dist>/browser dotnet run
--no-launch-profile`; kiracının pilot bayrağı geçici açılır; üç kullanıcı (Admin, "ADV Şube B" Operatör,
Muhasebe) rastgele parolayla eklenir; `RACAR_E2E_KOK=… RACAR_E2E_SIFRE=… RACAR_E2E_ADMIN=… RACAR_E2E_OPERATOR=…
RACAR_E2E_MUHASEBE=… npx playwright test <geçici config: testMatch f6-*-gercek, webServer yok, workers 1>`.
Bitince pilot kapatılır, kullanıcılar silinir, Web durdurulur. Kalan veriler: iptal edilmiş E2E kredileri (ödenen
taksidin gideri + defter satırı değişmez mali kayıt olarak kalır), teslim alınmış/iptal siparişler (deftersiz).
