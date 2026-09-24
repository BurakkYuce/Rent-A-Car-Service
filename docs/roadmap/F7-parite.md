# F7.3 — Parite ve e2e doğrulaması (Cariler & CRM fazı)

> Kalıp adım 3 ("Doğrulama: parite + e2e"). Kaynak: `F7.md` envanterindeki 8 Blazor sayfası (`Components/Pages/…`)
> ile SPA bileşenleri (`src/RentACar.Frontend/src/app/features/customers` ve `features/crm` — #295), 2026-09-24
> `main`. Yöntem: razor dosyası okundu; her alan / sütun / süzgeç / eylem / izin SPA şablonu, sütun tanımı, form
> modeli ve rota tablosuyla karşılaştırıldı. Eksik bulunan eklendi, bilinçli farklar gerekçeli. Uçlar #283 (F7.1) ve
> F8.1a cari ekstresinden; bu PR'da backend koduna dokunulmadı (yalnız yönlendirme haritası ve menü kaydı).

## Özet

| Sayfa (Blazor → SPA)                                     | Alan                                                           | Sütun                                   | Süzgeç                                          | Eylem                                                                                        | İzin                                                          | Sonuç                                   |
| -------------------------------------------------------- | -------------------------------------------------------------- | --------------------------------------- | ----------------------------------------------- | -------------------------------------------------------------------------------------------- | ------------------------------------------------------------- | --------------------------------------- |
| `/cariler` → `cariler`                                   | liste içi kısa form → `cariler/yeni` (tam kart)                | 16/16 (TC hariç, bkz. KVKK) + e-posta/ülke gizli sütun | 7/7 (ara, tür, İYS, uyarı, kara liste, durum, araç verilmez) | Excel/CSV/PDF (ViewReports), Kart, Detay, Ekstre (FinanceWrite ∨ ViewReports), Sil (OperationsDelete, onaylı), rozetler | okuma OW ∨ FW ∨ VR, yeni OW                                   | tam (bkz. bilinçli farklar)             |
| `/cariler/{id}` → `cariler/:id` (+ `cariler/yeni`)       | tüm alanlar, 7 sekme; gizli numaralar yalnız yazılır           | —                                       | —                                               | Kaydet (`surum`, 409 birleştirme), anonimleştirmeyi kaldır (ManageUsers)                      | okuma OW ∨ FW ∨ VR, yazma OW                                  | tam                                     |
| `/cariler/{id}/detay` → `cariler/:id/detay`              | özet (bakiye yalnız FW ∨ VR)                                   | kiralar 6/6 · son hareketler 5/5        | ekstre sekmesi tarih aralığı                    | Yeni kira (OW), Kart, **Tam ekstre** (SPA ekstre rotası)                                     | okuma OW ∨ FW ∨ VR; ekstre FW ∨ VR (#295 M1)                  | **eklendi:** Tam ekstre router bağlantısı |
| `/anketler` → `anketler`                                 | 8/8 + soru/cevap tablosu                                       | 9/10 ("Cevap" sayısı yok)               | 6/6                                             | ekle, düzenle (`surum`), sil (onaylı)                                                        | OW                                                            | tam (bkz. bilinçli farklar)             |
| `/sikayetler` → `sikayetler`                             | 13/13                                                          | 14/14                                   | 6/6                                             | ekle, düzenle (`surum`), sil (onaylı)                                                        | OW                                                            | tam                                     |
| `/assistans` → `assistans`                               | 11/11 (+ KVKK temizle kutuları)                                | 10/10                                   | 5/5 (durum → kapandı/yedek lastik/hareket bayrakları) | ekle, düzenle (`surum`), sil (onaylı)                                                  | OW                                                            | tam                                     |
| `/hukuk` → `hukuk`                                       | 16/16 (2. avukat dahil)                                        | 12/12                                   | 8/8                                             | Excel/CSV/PDF süzgeçle (ViewReports), ekle, düzenle (`surum`), sil (onaylı)                  | OW                                                            | tam (bkz. bilinçli farklar)             |
| `/crm` → `crm`                                           | —                                                              | segment 12/12 · personel 2/2            | 5/5                                             | Personel çalışma raporu bağlantısı                                                           | VR (şubeli kullanıcıya sunucu 403)                            | **eklendi:** rapor bağlantısı router'la |

## Bulunan eksik (bu PR'da eklendi)

1. **Kira formundaki "Cari ekstre" ve "Cari kartını aç" bağlantıları (gezinme paritesi).** Kira formu üst bağlantı
   çubuğu (`kira-formu.html`) ve Müşteri sekmesi (`sekmeler/musteri.ts`) `[href]="'/cariler/…'"` ile Blazor cari
   sayfalarına TAM SAYFA gidiyordu (F4.3 yer tutucusu; F7/F8 ekranları o gün yoktu). Artık `routerLink`:
   `/app/cariler/:id` ve `/app/cariler/:id/ekstre`. Ekstre bağlantısı yalnız ekstre rotasının kapısıyla görünür —
   FinanceWrite ∨ ViewReports (#295 KVKK M1; kural tek yerde, `customer-model.ts` `canSeeStatement`). Önceden
   operatör bağlantıyı görüp Blazor ekstresine gidiyordu.
2. **Cari detayı "Tam ekstre" bağlantısı.** Ekstre sekmesindeki bağlantı Blazor `/cariler/{id}/ekstre`'ye tam sayfa
   gidiyordu; SPA ekstre ekranı F8.2a'da geldiği için artık `routerLink` (`/app/cariler/:id/ekstre`).
3. **CRM analizi "Personel Çalışma" bağlantısı.** Blazor rapor sayfasına tam sayfa gidiyordu; SPA rapor ekranı
   F10.2'de geldiği için artık `routerLink` (`/app/raporlar/personel-calisma`).

Çit: `e2e/kesis-f7.spec.ts` (bağlantı hedefleri, tıklamada tam sayfa yüklemesi olmadığı — pencere işareti korunur —,
izinsiz kullanıcıda ekstre bağlantısının yokluğu).

## KVKK kararları (#295, değişmedi — kesiş bunları taşır)

- **Gizli numaralar:** TC hiçbir ekranda ve hiçbir `/api/ui` yanıtında düz görünmez. TC, ehliyet no, pasaport no ve
  bireysel caride vergi no yalnız yazılır; form bu alanları boş açar, ipucunda "kayıtlı (gizli)" ya da sunucunun
  maskesi görünür. Boş bırakılan alan `null` gider (kayıtlı değer korunur), "Temizle" kutusu `""` gönderir.
- **Anonimleştirilmiş alanlar:** anonim grubun alanları kilitli ve `null` gider, sunucu değeri korur. Listede ve
  detayda "Anonim müşteri" etiketi. Anonimleştirmeyi kaldırma ManageUsers ister; yoksa 403 `yetki_yok` (uyarı bandı +
  KVKK sekmesinde mesaj).
- **Tarayıcı deposu:** PII yazılmaz, form taslağı yok. TC benzeri (11 hane rakam) arama URL'ye, geçmişe ve sekme
  deposuna yazılmaz, yalnız bellekte tutulur (#295 M2).
- **Ekstre ve bakiye (#295 M1):** cari ekstresi bakiye ve tüm şubelerin hareketlerini gösterir; rota, detaydaki
  ekstre sekmesi ve listedeki/kira formundaki bağlantı YALNIZ FinanceWrite ∨ ViewReports ile.

## Bilinçli farklar (gerekçeli)

| Fark | Gerekçe |
| ---- | ------- |
| Listede "TC / Vergi No" sütunu yalnız vergi no | KVKK: TC hiçbir yanıtta yok. TC ile arama yalnız TAM 11 hane (blind-index). |
| "Yeni Cari" liste içi kısa form yerine tam kartı açar | Aynı alanlar; kart `surum`, sekme ve gizli alan kurallarını tek yerde taşır. |
| Listede "Ekstre" satır bağlantısı detayın ekstre sekmesine gider | Blazor F8 ekstre sayfasına giderdi; tam ekstre (yazdır / dışa aktar) sekmedeki bağlantıyla SPA ekstre ekranında. |
| Anket listesinde "Cevap" sayısı sütunu yok | Uç bu veriyi taşımıyor; cevaplar kayıt açılınca soru/cevap tablosunda. |
| Hukuk özetinde tutar/tahsilat/kalan TOPLAMLARI yok | Uç özet dönmüyor, SPA para toplamaz (satır sütunları var). |
| Yeni anketin varsayılan durumu "Yapıldı" | API varsayılanı; Blazor'da ilk seçenek "Yapılmadı" idi. |
| Liste ekranlarında satır içi düzenleme paneli, `?duzenle=` sorgusu yok | Blazor `?duzenle=<id>` sayfayı düzenleme formuyla açıyordu; SPA'da satırın Düzenle düğmesi aynı paneli açar. Bu parametreyle Blazor sayfasına giden başka ekran yok (tarama). |
| Yönlendirmede Blazor süzgeç adları kısmen tanınmaz | Sorgu AYNEN taşınır. Aynı adlı olanlar süzer (`q`, `tip`, `uyari`; şikayet/hukuk `cariId`, `ara`, `durum`…; CRM `minKira`, `kaynak`, `ofis`). SPA adı farklı olanlar (cari `iys`/`kara`/`pasifF`/`avF`, anket `cariF`/`tur`/`durumF`/`min`/`max`, assistans `plakaF`, hukuk/CRM `bas`/`bit`, `page`) liste tanımında bozuk parametre sayılır → varsayılan liste. Uygulama içinde bu parametrelerle F7 sayfasına bağlantı yok; yalnız yer imleri etkilenir. |
| CRM analiz ViewReports + şube kapsamsız | Firma geneli rapor; Blazor sayfası da ViewReports. Şubeli kullanıcıya sunucu 403. |
| `/cariler/{id}/ekstre` bu kesişte yönlenmez | F8 envanterinin sayfası; F8 kesişinde haritaya girer. SPA içindeki bağlantılar şimdiden SPA rotasına gidiyor. |

## e2e

**Sahte API (CI'da koşar):** `customers.spec.ts`, `crm.spec.ts` (#295; üç zorunlu senaryo, KVKK, axe iki tema,
320/390/768/1440 taşma) + `kesis-f7.spec.ts` (bu PR): 8 eski adres → SPA (sorgu ve fragment korunur, doğru başlık);
F7 ekranlarındaki hiçbir bağlantı Blazor cari/CRM sayfasına düşmez (ekstre dahil); kira formundaki bağlantılar
router'la gider, izinsiz kullanıcıda ekstre bağlantısı yok.

Koşum (yerel, port 4591): `kesis-f7` + `customers` + `crm` + `kira-formu` + `kesis-f6` + `kesis-f5` + `kesis` —
98/98 ✔.

**Gerçek backend:** bu PR yeni ekran ya da uç getirmiyor; cari/CRM uçlarının gerçek PostgreSQL üzerindeki izin,
kapsam ve KVKK testleri `UiCustomerApiTests` (#283) ve yönlendirmenin gerçek boru hattı `IlkKesisHostTests`
(bu PR: pilot firmada 302, pilot olmayan firmada 200, POST yönlenmez, döngü yok).
