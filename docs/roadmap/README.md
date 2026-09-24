# Angular geçiş roadmap'i

> **Başlangıç:** 2026-09-21 · **Toplam:** 60 PR · **Kaynak plan:** iki adversarial turda doğrulandı ve kullanıcı onayı aldı.
> Faz sırası **kilitli** (aşağıda "Sıra kuralı"). Her fazın ayrıntısı ve mekanik sayfa/uç envanteri kendi dosyasında.

## Fazlar

| Faz | Kapsam | PR | Para | Durum |
|---|---|---|---|---|
| G0 | Revlo Angular reposuna graphify grafiği (yerel, kod-only) | — | — | ✔ 2026-09-21 |
| [F0](F0.md) | Hazırlık: belgeler + Blazor son kritik düzeltmeler | 2 | — | ✔ 2026-09-21 |
| [F1](F1.md) | Backend API temeli: hata/yetki, `/api/ui` + pilot kapısı, liste, idempotency, `/app` barındırma, F4–F5 seçim uçları + menü kaydı | 6 | F1.4 | ✔ 2026-09-21 |
| [F2](F2.md) | Frontend iskeleti + kalite kapıları + tip üretimi + CI artifact dağıtımı (F2.1 F1'le paralel) | 2 | — | F2.1 ✔ · F2.2 kod ✔ #249, sunucu Exit'i bekliyor |
| [F3](F3.md) | Çekirdek: tasarım sistemi, kabuk, oturum, veri katmanı, tablo, form seti, vitrin | 7 | — | ✔ 2026-09-22 |
| [F4](F4.md) | Pilot: Panel + Kira (liste, form, yazdır), tek SPA girişi, ilk kesiş | 6 | ✔ | kod ✔ 2026-09-23 · pilot + söküm (F4.6b) bekliyor |
| [F5](F5.md) | Rezervasyon, teklif, müsaitlik, takvim, rez şartları, filo kiralama | 5 | — | sürüyor (#271 #272 #273 main'de, #274 açık, F5.4 kesiş) |
| [F6](F6.md) | Araçlar | 6 | ✔ | kod ✔ (#278 #279 #285 #291 #294 + F6.4 kesiş) · Blazor sayfa silme pilot sonrası |
| [F7](F7.md) | Cariler & CRM | 3 | — | kod ✔ (#283 #295 + F7.3 parite + kesiş) · Blazor sayfa silme pilot sonrası |
| [F8](F8.md) | Finans + cari ekstre + fatura yazdır | 6 | ✔ | bekliyor |
| [F9](F9.md) | Servis & Sigorta + Vade + Fiyat & Tarife | 5 | ✔ | kod ✔ (#292 #301 + F9.3 parite + kesiş) · Blazor sayfa silme pilot sonrası |
| [F10](F10.md) | Raporlar | 4 | — | kod ✔ (#287 #296 #302 + F10.3b parite + kesiş) · Blazor sayfa silme pilot sonrası |
| [F11](F11.md) | Tanımlar + Sistem + Web Sitesi + kabuk sayfaları | 6 | — | bekliyor |
| [F12](F12.md) | Platform konsolu | 2 | — | bekliyor |
| [F13](F13.md) | Blazor söküm ve kapanış | 2 | — | bekliyor |

Her faz bir öncekinin **Exit**'i sağlanınca başlar (tek istisna F2.1). Faz içindeki PR'lar tek tek merge edilir.
`/api/ui/v1`'de yalnız eklemeli değişiklik yapılır. Sıra ya da kapsam değişikliği: [DEGISIKLIKLER.md](DEGISIKLIKLER.md).

**Envanter:** 148 sayfa dosyası / 149 `@page` rotası (`KiraForm` iki rotalı) fazlara açıkça atandı;
[`scripts/roadmap-envanteri.py`](../../scripts/roadmap-envanteri.py) her `F*.md`'nin "Envanter" bloğunu üretir
(taban 2026-09-21; sayfalar silindikçe yeniden üretilmez). Bir servis, onu ilk kullanan fazda `/api/ui/v1`
ucuna kavuşur; bir Blazor POST ucu, onu kullanan son fazın kesişinde silinir. Kabuğun (`MainLayout`) veri
ihtiyacı F3'e atanır (F3.2'de gerekir, Blazor kabuğu F13'e kadar yaşar).

## Bağlam

Canlı kullanımda Blazor static SSR üç sorun üretti ve kök nedenleri mimaride:
- **Veri kaybı.** 148 sayfanın hiçbiri Blazor form mekanizmasını kullanmıyor. 334 POST ucu hata olunca `Redirect("?hata=…")` yapıyor ve yazılanları siliyor (`CustomerEndpoints.cs:26`; kira formunda 172 alan).
- **Tasarım tavanı.** Tarayıcıda durum tutulmuyor.
- **Kalite borcu.** UI denetiminde (2026-09-21) belgelendi.

Karar: Angular 21. Kullanıcının kendi Revlo projesinden (`~/Desktop/revlo-market-ai/revlo-angular`) yalnız sağlam parçalar seçilerek kopyalanır. Backend (Domain/Application/Infrastructure, defter, RLS, 2577 test) yerinde kalır; üstüne bir JSON katmanı eklenir.

**Kilitli kararlar (kullanıcı, 2026-09-21):**
- Yeni, yoğun bir ERP teması: Revlo token yapısı üstüne kurulur, koyu tema dahil.
- Blazor dondurulur ve modül modül kapatılır.
- Arayüz Türkçe, i18n altyapısıyla.

**Konum kararı: frontend bu repoda, `src/RentACar.Frontend/` altında (monorepo).** Kullanıcı kararı bana bıraktı; ilk tercihi "ayrı repo" idi. Monorepo'yu şu gerekçelerle seçiyorum:

| Konu | Ayrı repoda | Bu repoda |
|---|---|---|
| API sözleşmesi | Anlık görüntü senkron betiği, backend commit sabitleme, iki CI'da kayma kontrolü, "önce backend PR'ı" kuralı | Uç ve ekran aynı PR'da; OpenAPI anlık görüntüsü aynı CI'da tiplere dönüşür, kayma derleme hatası olur |
| Sürüm eşleşmesi | SPA ve API ayrı sürümlenir; uyumluluk yönetimi gerekir | SPA artifact'ı aynı commit SHA'sından üretilir ve aynı release'e girer. Sunucu tarafında eşleşme garanti. Açık sekmeler için chunk koruması + "yeni sürüm" bildirimi |
| e2e testi | Frontend CI'ının private backend'i checkout etmesi ve başka commit'ten ayağa kaldırması | Aynı commit, aynı CI |
| Kesiş | Menü kaydı, yönlendirme haritası ve Angular rotası iki repoda koordineli | Tek PR'da atomik |
| Blazor testlerinin devri | Testler repo değiştirir | Yan yana |
| Araçlar | CLAUDE.md, hafıza, graphify, hook'lar ikinci kez kurulur | Ortak |

Bedeli: tek CI'da iki araç zinciri (.NET + Node) ve `src/RentACar.Frontend` içinde ikinci bir `package.json` (kökteki Playwright paketi F13'e kadar yaşar).

**Sıra kuralı (kullanıcı isteği).** Faz sırası kilitli; iş sürerken öne ya da arkaya alınmaz. Bunun için:
- Her fazın **sayfa listesi** ve **gereken uç listesi** F0.1'de `docs/roadmap/F*.md` dosyalarına yazılır. Liste, her sayfanın `@inject` satırlarından ve form hedeflerinden **mekanik olarak üretilir**.
- Her modül fazı kendi seçim ve arama uçlarını kendi başında ekler. F1.6 yalnız F4–F5'in ihtiyacını karşılar. Böylece hiçbir faz kendinden sonraki bir fazı beklemez ve uçlar kullanılmadan aylar önce dondurulmaz.
- Çekirdekte eksik çıkarsa, o fazın içine "çekirdek eki" PR'ı olarak eklenir.
- Sıra ancak kullanıcının açık kararıyla ve `docs/roadmap/DEGISIKLIKLER.md` kaydıyla değişir.

## Non-Goals

| Yüzey | Korunacak sözleşme |
|---|---|
| Domain / Application / Infrastructure iş mantığı | Değişmez. Yalnız eklemeli: `YetkiYokException`, `MukerrerIslemException`, `ValidationException.Alan`, liste filtrelerine sayfa/sıralama, `TenantSettings.YeniArayuzPilot` (F1.2), `TabloDuzenleri` (F3.5, RLS'li). **Açık güvenlik istisnası:** `SearchService`'e `BranchScope` eklenir (F1.6). Bugün kapsam yok, Blazor `/ara` da düzelir |
| Defter, RLS, tenant izolasyonu, para kuralları, `IslemAnahtari` (uuid) deseni | Dokunulmaz. Her uçta çift gönderim **bugünkü sonucu** verir (bazıları red, bazıları sessiz idempotent başarı; F1.4 envanteri). Sunucunun deterministik anahtarları (`TahsilatAnahtar`, `RowKey`) header'dan önceliklidir |
| `RentACar.Api` (harici JWT API) | Mevcut 409 eşlemeleri (`conflict`/`duplicate`) değişmez. Yalnız eklenir: `YetkiYok` → 403, `Mukerrer` → 409 |
| `RentACar.PublicSite` | Blazor'da kalır |
| Revlo reposu | Kodu değişmez. G0'da yalnız git dışı `graphify-out/`, yerel `.graphifyignore` ve graphify hook'ları eklendi (doğrulandı: `git status` aynı) |
| İş özelliği | Yeni iş özelliği yok; parite + denetimdeki UX düzeltmeleri. **Açık istisnalar:** sekmeli çalışma alanı, sütun düzeni kaydı, Ctrl+K paleti |
| Global şube seçici | Yok |
| İngilizce çeviri | Yok. Anahtarlar hazır, yalnız `tr.json` |
| Deneysel Angular çalışma zamanı API'leri | Signal Forms, `resource()`, `@angular/aria` kullanılmaz. **Araç istisnası:** `@angular/build:unit-test` `[EXPERIMENTAL]`, sürümü sabitlenir |
| Staging | Kurulmaz. Karanlık yayın: `/app` kabuğu anonim yüklenir ama tüm veri `/api/ui` üzerinden gelir ve oturum + pilot kapısı ister |
| Sunucuda Node / npm | Yok. SPA CI'da derlenir; sunucu commit SHA'sına ait checksum'lı artifact'ı indirir |
| Çevrimdışı PWA | Yok |

## PRE-CODE VERIFY

| İddia | Sonuç | Etkisi |
|---|---|---|
| 148 `@page`, 91 uç dosyası, 334 `MapPost`, 0 `EditForm` | ✔ | Faz boyutları, veri kaybının kök nedeni |
| Kira formu 24 servis inject ediyor; `FormVarsayilanCozucu` dahil | ✔ `KiraForm.razor:6-28,270-271` | F4.1 |
| Kira formunun sorgu sözleşmesini (`varac`, `vfrom`, `vto`, `vgrup`, `musteriId`) Blazor'da kalan sayfalar kullanıyor; uç yönlendirmeleri `#sekme=` taşıyor | ✔ `KiraForm.razor:230-234`, `MusaitlikArama.razor:324-330`, `FleetStatus.razor:165`, `BookingEndpoints.cs:336-379` | SPA aynı parametreleri ve `#sekme=`'yi destekler (F4.3); yönlendirme sorgu + fragment korur |
| Sayfa önekini paylaşan GET uçları (PDF, hesap, müsaitlik, makbuz) | ✔ `PdfEndpoints.cs:34-67`, `BookingEndpoints.cs:177,214,355` | Harita yalnız silinen `@page` şablonlarından |
| Cookie `racar.session`; challenge `/login`'e 302 | ✔ `Program.cs:167-194` | `/app` anonim olmalı, yoksa `/login` → `/app/giris` → `/login` döngüsü |
| Yönlendiren/HTML dönen 6 adım: `TenantActive`, `PlatformIsolation` (muafiyet `:32-40`), rate-limit, `DogrulamaHatasi`, `UseExceptionHandler`, `UseStatusCodePagesWithReExecute` | ✔ | F1.2 |
| Antiforgery yapılandırılmamış; Development'ta kapalı; JSON doğrulanmıyor; claim'lerde `sub` yok | ✔ `AuthExtensions.cs:70-71`, `AuthEndpoints.cs:27-36` | CSRF her ortamda; giriş/çıkıştan sonra token yenilenir |
| `IslemAnahtari` **`Guid?` (uuid)**; manuel fatura bunu **birincil anahtar** yapıyor ve varsa mevcut id'yi dönüyor | ✔ `CashTransaction.cs:48`, `CashInput.cs:26`, `InvoiceService.cs:426-433` | Header sunucuda UUIDv5(tenant\|user\|header) olarak türetilir; istemci değeri asla doğrudan PK olmaz |
| Çift gönderimde davranış tek tip değil. Red: `CashRepository.cs:159-166,273-278`. Sessiz başarı: depozito `CashRepository.cs:349-362`, `ExpenseRepository.cs:124-128` (null), `LedgerPoster.cs:46-51` (no-op), fatura (mevcut id). Karışık catch'ler: `CashRepository:159` (anahtar + ters kayıt), `RegulationRepository:97-103`, `InvoiceRepository:292-300` | ✔ (ilk dördü okundu) | F1.4 envanteri uç başına "200 mü 409 mu" der; sınıflandırma `PostgresException.ConstraintName` ile |
| `TahsilatAnahtar` deterministik, ekran yüklenirken üretiliyor (kira + bakiye + işlem sayısı) | ✔ `TahsilatAnahtar.cs:19-24`, `Home.razor:116`, `RentalList.razor:138` | Panel/liste DTO'su anahtarı taşır, SPA geri gönderir; header bunu ezemez |
| `RentACar.Api` çakışmaya zaten 409 dönüyor (`conflict`, `duplicate`) | ✔ `ExceptionHandlingMiddleware.cs:12,24-30` | `/api/ui`'da `kod: cakisma` (409, yenileme yok) ≠ `mukerrer` |
| Yetki reddi düz `ValidationException` ile: `PermissionGuard.cs:17,29`, `BranchScope.cs:42`, `ScreenPermissionService.cs:141` | ✔ | Üçü de `YetkiYokException` |
| `SearchService` "Yetki gerektirmez"; `SearchRepository` şube kapsamı yok. `ListSecimAsync` yetkisiz ve sınırsız, tüm müşteri adlarını dönüyor (KVKK: ad kişisel veridir) | ✔ `SearchService.cs:5`, `CustomerService.cs:37-38` | F1.6: arama → `BranchScope`; seçim uçları typeahead (`q`, `limit ≤ 20`) + `IzinMetadata` |
| `InvoiceService` List/Search/Get/ListByRental korumasız | ✔ `InvoiceService.cs:33-57` | Alt kayıt uçları üst kaydın kapsamından geçer |
| `yayinla.sh` root ister ve repo checkout'ından derler; tek VPS'te Postgres de var; kurulum yalnız runtime kuruyor ve `if ! command -v dotnet` ile korunuyor | ✔ `yayinla.sh:33,63`, `kurulum.sh:50-54` | Sunucuda npm yok (CI artifact); SDK kontrolü `dotnet --list-sdks` ile |
| systemd: `User=racar`, `WorkingDirectory=/opt/racar/current/web` | ✔ `racar-web.service:13,16` | `Spa:Dizin=../app/browser` (release'e göreli; swap ile eski/yeni karışmaz) |
| Menü rol kapılı + modül bayraklı (Web Sitesi), gruplu ve hızlı bağlantılı | ✔ `MainLayout.razor:35-41,48,98,172,258,372` | `MenuOgesi` alanları genişletildi; Blazor menüsü F4.6'da izin temelli olur (kasıtlı davranış değişikliği) |
| Guard testleri MainLayout ve kira uçlarına bağlı: `MenuKapsamaTests:34,43`, `UcIzinKapsamaTests:59,98-125`, `KabukTests:22`, `RenkKodlariTests:138`, `BasariMesajiDogruDaldaTests:32`, `BilgiTekKaynakTests` | ✔ (inceleme + `rg`) | F4.6 devir listesi |
| `dotnet build/test` frontend'i almıyor (slnx proje listesi açık); GitGuardian yapılandırması yok; systemd `ProtectSystem` SPA'yı okumaya izin veriyor | ✔ (inceleme) | Monorepo güvenli |
| Graphify `node_modules`, `dist` ve `.angular`'ı varsayılan atlıyor; `*.json` kod, `*.html` doküman sayılıyor | ✔ `detect.py` | F2.1'de kök `.graphifyignore` güncellenir |
| Angular 21.2.14; zoneless `@publicApi 20.2`; kararlı CDK girişleri | ✔ | Kullanılır |
| Revlo zone.js ile çalışıyor; testleri Karma/Jasmine; `unit-test` builder deneysel | ✔ | Uyarlama maliyeti F3'e dahil |
| Revlo çekirdek kapanışı 265 dosya / 58,5 bin satır; auth ↔ tabs ↔ locale döngüsü | ✔ + G0 grafiği aynı bağları gösteriyor | Yapraktan köke taşıma |
| `docs/roadmap/KARARLAR.md` 12 dosyadan atıf alıyor; açık işler var | ✔ | `docs/KARARLAR.md`'ye taşınır |

## Hedef yapı

```
Rent-A-Car-Service/
  src/RentACar.Web/
    Api/                              /api/ui/v1
      UiApiExtensions.cs              grup, ProblemDetails, CSRF, no-store, izin ve pilot kapısı
      Oturum/OturumApi.cs             giriş, çıkış, ben, xsrf
      Menu/MenuKaydi.cs + MenuApi.cs  tek menü kaydı (iki UI)
      Secim/, Arama/                  typeahead ve arama (F1.6 + modül fazları)
      <Modul>/<Modul>Api.cs
    Spa/SpaBarindirma.cs              /app anonim statik + fallback (Spa:Dizin)
    Components/                       F13'te silinir
  src/RentACar.Frontend/              Angular çalışma alanı (package.json, AGENTS.md, e2e/)
  src/RentACar.Application/Common/
    ValidationException.cs (+Alan), YetkiYokException.cs, MukerrerIslemException.cs, ListeIstegi.cs, Sayfa.cs
  docs/KARARLAR.md · docs/roadmap/ (README + F0..F13.md + DEGISIKLIKLER.md) · docs/api/ui-v1.json
```

**İmzalar**

- **Backend kayıtları ve istisnalar:**
  - `sealed class YetkiYokException(string mesaj) : ValidationException(mesaj)`
  - `sealed class MukerrerIslemException(string mesaj) : ValidationException(mesaj)`
  - `ValidationException(string mesaj, string? alan = null)`
  - `record ListeIstegi(int Sayfa = 1, int Boyut = 50, string? Sirala = null)`, en fazla 200
  - `record Sayfa<T>(IReadOnlyList<T> Kayitlar, int Toplam, int SayfaNo, int Boyut)`
  - `record MenuOgesi(string Rota, string Etiket, string Grup, int Sira, string Sahip, Permission? Izin, string? Modul, string? RozetKodu, bool HizliBaglanti)`
- **ProblemDetails:** `{ type, title, status, detail, kod, errors?: { [alan]: string[] } }`. `kod` değerleri:
  - `dogrulama` (400)
  - `yetki_yok` (403)
  - `pilot_degil` (403)
  - `cakisma` (409; müsaitlik, TC/plaka gibi iş benzersizliği; form korunur)
  - `mukerrer` (409; kayıt yeniden yüklenir)
  - `oturum_yok` (401)
  - `kiraci_kapali` (401)
  - `cok_istek` (429)
  - `xsrf_gecersiz` (400; F1.2 eki — SPA token'ı yeniler ve isteği bir kez tekrarlar; `dogrulama`/`yetki_yok` ile karışmasın diye ayrı)
- **`TypedResults` zorunlu.**
- **Idempotency (öncelik sırasıyla):**
  1. Sunucunun deterministik anahtarı varsa (`TahsilatAnahtar`: DTO'dan gelir, SPA geri gönderir; `RowKey`: sunucu hesaplar) o kullanılır.
  2. Yoksa `Idempotency-Key` header'ı → `UUIDv5(tenantId | userId | header)` → `IslemAnahtari`.
  3. İstemci anahtarı her 2xx'ten sonra yenilenir; sabit panelde ikinci meşru tahsilat engellenmez.
  - Çift gönderim o uçta bugün ne yapıyorsa aynısı olur: `200` (sessiz idempotent) ya da `409 mukerrer`.
- **Frontend `TemelStore<T>`:**
  - Dört durum.
  - `switchMap` ile önceki isteği iptal eder.
  - Hata asla boş liste olarak gösterilmez.

## ⚠ Tuzaklar

| Tuzak | Somut hata | Önlem |
|---|---|---|
| `IslemAnahtari` uuid ve fatura PK'sı | `{userId}:{key}` metni kolona sığmaz; istemci değeri PK olursa çakışma ya da tahmin edilebilir id çıkar | UUIDv5(tenant\|user\|header), yalnız sunucuda |
| Rastgele istemci anahtarı | Deterministik `TahsilatAnahtar` ezilir; iki sekme ya da iki kullanıcı çift tahsilat yapar | Deterministik anahtar önceliği; DTO anahtarı taşır |
| Form başına tek anahtar | Sabit panelde ikinci meşru tahsilat "mükerrer" sayılır | Anahtar her 2xx'ten sonra yenilenir |
| Karışık catch blokları | "Dal dal" sınıflandırma iş benzersizliğini mükerrer sayar | `ConstraintName` ile sınıflandırma |
| Her 409 = yenile | Müsaitlik çakışması kira formunu siler | `cakisma` ≠ `mukerrer` |
| `/app` oturum isterse | `/login` → `/app/giris` → `/login` döngüsü | Statik kabuk anonim; veri `/api/ui`'da korunur |
| Pilot kapısı sonradan eklenirse | F4 öncesinde üretimdeki para uçları tüm kiracılara açık kalır | Kolon ve sunucu filtresi F1.2'de |
| Sunucuda `npm ci` | Root olarak rastgele lifecycle betiği sırların yanında çalışır; derleme OOM'u Postgres'i öldürür | CI artifact; sunucuda Node yok |
| Mutlak `Spa:Dizin` | Swap ile restart arasında eski süreç yeni SPA'yı servis eder; geri almada yanlış dosyalar | Release'e göreli `../app/browser` |
| Chunk taşıma | Her release öncekinin taşıdıklarını da taşır, sınırsız büyür | Yalnız önceki release'in kendi `chunks.txt`'i; `cp -n`, `chown`'dan önce |
| Korumasız seçim ve arama | Tüm müşteri adları sınırsız döner (KVKK); başka şubenin kayıtları aranır | Typeahead + `IzinMetadata` + `BranchScope` |
| Antiforgery kimliğe bağlı; dev'de kapalı; mutlak URL'de header yok | CSRF hataları üretimde ya da sessizce | Token yenileme, her ortamda filtre, göreli URL lint'i |
| 401/403'te gezinme | 172 alanlı form kaybolur | Yerinde diyalog + tekrar; bant |
| Önek paylaşan GET'ler | PDF SPA'ya yönlenir | Şablon tabanlı harita + negatif test |
| Kira sorgu sözleşmesi | Müsaitlik ve araç durumundan gelen `?varac=` bağlantıları boş form açar | SPA aynı parametreleri ve `#sekme=`'yi destekler |
| Guard testleri MainLayout'a bağlı | F4.6'da ilgisiz modül testleri kırılır | Devir listesi F4.6'da |
| Revlo zone.js | Zoneless altında bileşen güncellenmez | Uyarlama + zoneless testler |
| Türkçe küçük harf, KVKK localStorage, font CSP, `inlineCritical` | Arama kaçar; ad tarayıcıda kalır; stil ya da font yüklenmez | tr normalize; yalnız rota + id; self-host; `inlineCritical:false` |

## Riskler

| Risk | Önlem |
|---|---|
| İki arayüz aylarca yan yana | Tek menü kaydı, tek giriş, modül bazlı kesiş |
| Pilotta çekirdek eksik | "Çekirdek eki" PR'ı; sıra değişmez |
| Para ekranında davranış farkı | Servisler aynı; F1.4 envanteri + parite + e2e + adversarial (F4, F6, F8, F9) |
| Menü görünürlüğü rolden izne geçer | Kasıtlı; F4.6 PR'ında önce/sonra listesi kullanıcıya gösterilir |
| Tablo motoru büyük | "Olduğu gibi + testler" |
| `unit-test` builder deneysel | Sürüm sabit |
| Revlo grafiği bayatlar | G0 hook'ları tazeler; F3 öncesinde `graphify update` |

## Success Metrics

1. Blazor `@page` 148 → 0.
2. "Form korunur", "oturum düşünce form kaybolmaz", "`cakisma` formu silmez" e2e testleri taşınan her formda yeşil.
3. OpenAPI ile tipler aynı PR'da senkron; yapısal izin ve modül testi her `/api/ui` ucunu kapsıyor.
4. axe ciddi/kritik 0; 320–1440 px gövde taşması 0.
5. F1.4 envanterindeki her para ucunun çift gönderim sonucu testle kilitli; adversarial Critical/High/Medium 0; silinen her testin karşılığı listeli.
6. Frontend'de `any`, katman ihlali ve spec derleme hatası 0; istemci hata raporunda açık P1 0.

## Doğrulama

- **Backend:** `RACAR_TEST_PG_ADMIN="Host=localhost;Port=5432;Username=burak;Database=postgres" dotnet test RentACar.slnx -c Debug`; "Failed: 0" açıkça; `gh pr checks --watch`.
- **Frontend** (`src/RentACar.Frontend`): `npm run lint && npm run typecheck && npm test && npm run build && npm run e2e`.
- **Canlı duman testi** (her kesişte, pilot kiracıyla):
  1. Eski URL `/app`'e gidiyor; PDF ve export yönlenmiyor; `?varac=` bağlantısı dolu form açıyor.
  2. Doğrulama hatasında alanlar dolu kalıyor.
  3. Başka sekmede çıkış yap, formu gönder: diyalog açılıyor, form korunuyor.
  4. Müsaitlik çakışması: form korunuyor.
  5. İzinsiz ve pilot olmayan kiracı: uyarı bandı.
  6. Koyu tema + 390 px.
  7. Aynı tahsilatı iki sekmeden gönder: tek kayıt.
  8. Sabit panelde arka arkaya iki meşru tahsilat: iki kayıt.
  9. Yayından sonra açık sekme çalışmaya devam ediyor ya da "yenile" bildirimi çıkıyor.
