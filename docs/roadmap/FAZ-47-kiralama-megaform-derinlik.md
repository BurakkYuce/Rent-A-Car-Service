# FAZ-47 — Kiralama Mega-Form: Şube/Teslim/Ödeme/Bakiye/2.Sürücü Derinliği

| | |
|---|---|
| **Desen** | D6 — mega-form devamı |
| **Efor** | 4 gün (yapısal maddeler 1-7; PARA-Opus kararı ve alt-not #1'in "Bedava" fiyat-tipi
  override küçük parçası [0,5g, ayrı D2] bu eforun DIŞINDA) |
| **Bağımlılık** | madde 5 (çok-taraflı bakiye dağıtım MANTIĞI) Opus kararına bağlı; maddeler 1-4,6,7
  bu fazda BAĞIMSIZ tamamlanır; alt-not #1/#2 (YAPILMAZ) onay gerekmez |
| **Kapsanan canlı ekran** | `kiralama.aspx` |
| **Risk** | yüksek — `RentalContract`/`Vehicle` üzerinde model değişikliği + mega-form JS/tab
  altyapısına (`rc-kira-tabs.js`) dokunmadan yeni alan ekleme; CLAUDE.md §6'daki mega-form tuzakları
  (hash yazımı, datetime-local prefill, personel dropdown guard) BU FAZDA da geçerli |

## Amaç
Kullanıcı kira sözleşmesinde çıkış şubesini override edebilir, çıkış anındaki teslim eden personeli
kaydedebilir, ödeme şeklini formda seçebilir, araca park-yeri (Konum) bilgisi girebilir, çok-taraflı
bakiye alanlarının veri modelini görebilir (dağıtım MANTIĞI henüz yok, Opus sonrası), müşteri
adres/pasaport/ehliyet bilgisini salt-okur görebilir ve kayıtlı-olmayan 2. sürücü için serbest-metin
girebilir hale gelir.

## Neden (kanıt)
1. **İşlem Şube:** `src/RentACar.Web/Components/Pages/Bookings/KiraFormPaneller/
   SekmeKiraBilgisi.razor:57` — `<label>İşlem Şube<input value="çıkış ofisinden türetilir" readonly
   title="TODO: şube FK yetki katmanı (flagged iş)" /></label>` (satır doğrulandı). **Önemli:**
   `RentalContract.CikisSubeId` (satır 33) zaten var VE `RentalUpdateInput.CikisOfisi` (satır 11)
   zaten whitelist'te — backend hazır, eksik olan yalnız UI: `readonly` kaldırılıp düzenlenebilir
   select'e çevrilecek.
2. **Teslim Eden (çıkışta):** `RentalContract.TeslimAlanPersonelId` (satır 66) yalnız DÖNÜŞTE var;
   çıkış anını yakalayan ayrı alan yok (grep doğrulandı).
3. **Ödeme Şekli:** `SekmeFiyat.razor`'da (satır 1-30 doğrulandı) `FiyatTuru`/`GunlukUcret`/`Doviz`
   var, ayrı bir `OdemeSekli` string alanı yok.
4. **Konum:** `src/RentACar.Domain/Entities/Vehicle.cs`'de `Grup` (satır 27) var, `Konum` (park yeri)
   yok (grep sıfır isabet).
5. **Çok-taraflı bakiye:** `RentalContract.cs`'de `MstToplam`/`MstBakiye`/`FirmaToplam`/
   `FirmaBakiye`/`RezKaynakToplam`/`RezKaynakBakiye`/`MstFatura`/`FirmaFatura`/`RezKaynakFatura`
   **hiçbiri yok** (grep sıfır isabet). GERÇEK dağıtım mantığı (kim öder, kim fatura alır) Opus
   kararı — bu fazda yalnız veri modeli (kolonlar) eklenir, hesaplama YOK.
6. Müşteri adres/pasaport/ehliyet — `Customer`'da zaten şifreli (`*Enc`) var, formda salt-okunur
   gösterim yok.
7. **2. sürücü serbest-metin:** `SekmeMusteri.razor:23-26` — FK'li 2. sürücü (`IkinciSurucuId`) var
   ve zaten whitelist'te; misafir (kayıtsız) 2. sürücü için serbest-metin katman yok.

## Yapılacaklar
1. `src/RentACar.Web/Components/Pages/Bookings/KiraFormPaneller/SekmeKiraBilgisi.razor:57` —
   `readonly` input'u kaldır, `<select name="cikisSubeOverride">` yap (mevcut `BranchService.
   ListActiveAsync()` ile doldurulur; varsayılan seçili değer türetilmiş çıkış ofisinden gelen şube,
   kullanıcı override edebilir). `RentalUpdateInput.CikisOfisi` ZATEN whitelist'te — yalnız UI
   değişikliği, Application katmanında ek iş YOK. Create formunda da aynı select eklenir
   (`BookingInput.cs`'te `CikisOfisi` zaten varsa dokunma, yoksa ekle — aç ve doğrula).
2. `src/RentACar.Domain/Entities/RentalContract.cs` — `TeslimEdenPersonelId` (Guid?, `Personel` FK)
   ekle (mevcut `TeslimAlanPersonelId` (satır 66) deseniyle aynı tip, ayrı alan).
3. `src/RentACar.Application/Bookings/RentalUpdateInput.cs` — `TeslimEdenPersonelId` ekle (whitelist
   — para/tarih DEĞİL, personel ataması, CLAUDE.md §5 mega-form tuzağı: Personel dropdown
   `ListForSelectAsync` kullan, `ListAsync` OPERATÖRDE PATLAR).
4. `src/RentACar.Domain/Entities/RentalContract.cs` — `OdemeSekli` (string?) ekle.
5. `src/RentACar.Application/Bookings/RentalUpdateInput.cs` — `OdemeSekli` ekle (bilgi alanı, para
   hesaplamasına girmez).
6. `src/RentACar.Web/Components/Pages/Bookings/KiraFormPaneller/SekmeFiyat.razor` — `OdemeSekli`
   input ekle (seç-veya-yaz ComboBox, CLAUDE.md memory "combobox-sec-veya-yaz").
7. `src/RentACar.Domain/Entities/Vehicle.cs` — `Konum` (string?, park yeri) ekle.
8. `src/RentACar.Domain/Entities/RentalContract.cs` — 9 alan ekle: `MstToplam`, `MstBakiye`,
   `FirmaToplam`, `FirmaBakiye`, `RezKaynakToplam`, `RezKaynakBakiye`, `MstFatura`, `FirmaFatura`,
   `RezKaynakFatura` (hepsi `decimal?`). **XML doc'ta AÇIKÇA yaz:** "veri modeli — dağıtım
   MANTIĞI (kim öder, kim fatura alır) henüz YOK, Opus kararı sonrası doldurulacak; şimdilik null/0,
   mevcut `GenelToplam`/`Tahsilat`/`Bakiye` hesaplamasını ETKİLEMEZ".
9. `src/RentACar.Infrastructure/Persistence/Configurations/BookingConfigs.cs` — `RentalContract`
   config sınıfına 11 yeni kolon (`TeslimEdenPersonelId`, `OdemeSekli`, 9 bakiye alanı; `numeric(19,4)`
   para alanlarına) ekle; `VehicleConfigs.cs`'e `Konum` kolonu ekle.
10. Migration: `dotnet ef migrations add AddKiraSubeTeslimOdemeKonumBakiye --project
    src/RentACar.Infrastructure --startup-project src/RentACar.Infrastructure`.
11. `src/RentACar.Web/Components/Pages/Bookings/KiraFormPaneller/SekmeDonus.razor` — `Vehicle.Konum`
    gösterimi/girişi ekle (mevcut araç bilgi panelinin yanına).
12. `src/RentACar.Web/Components/Pages/Bookings/KiraFormPaneller/SekmeMusteri.razor` — müşteri
    adres/pasaport/ehliyet bilgisi için salt-okur bilgi paneli ekle (`CustomerRepository.Decrypt`
    ile bellekte çözülmüş değerler; düzenleme linki `/cariler/{id}`'ye yönlendirir, bu formda
    düzenlenmez).
13. `src/RentACar.Domain/Entities/RentalContract.cs` — `IkinciSurucuSerbestAd`,
    `IkinciSurucuSerbestSoyad`, `IkinciSurucuSerbestTc`, `IkinciSurucuSerbestTel`,
    `IkinciSurucuSerbestEhliyet` (hepsi `string?`) ekle.
14. `src/RentACar.Application/Bookings/RentalUpdateInput.cs` — 5 serbest-metin alanını whitelist'e
    ekle.
15. `src/RentACar.Web/Components/Pages/Bookings/KiraFormPaneller/SekmeMusteri.razor` — 2. sürücü
    bloğuna (satır 23-26 civarı) "Kayıtlı Değil (Misafir)" seçeneği + 5 serbest-metin input ekle
    (FK'li seçim ile serbest-metin birbirini dışlar — ikisi aynı anda dolu olmasın, form-JS veya
    basit koşullu render ile).

## Dokunulacak dosyalar
- `src/RentACar.Web/Components/Pages/Bookings/KiraFormPaneller/{SekmeKiraBilgisi,SekmeFiyat,
  SekmeMusteri,SekmeDonus}.razor`
- `src/RentACar.Domain/Entities/RentalContract.cs` — 16 alan (TeslimEdenPersonelId, OdemeSekli, 9
  bakiye alanı, 5 serbest-metin 2.sürücü alanı)
- `src/RentACar.Domain/Entities/Vehicle.cs` — `Konum`
- `src/RentACar.Infrastructure/Persistence/Configurations/{BookingConfigs.cs,VehicleConfigs.cs}`
- `src/RentACar.Application/Bookings/{RentalUpdateInput.cs,RentalService.cs,BookingInput.cs}`
- (yeni) migration `AddKiraSubeTeslimOdemeKonumBakiye`

## Migration
Var — `RentalContract`'a 16, `Vehicle`'a 1 nullable kolon (mevcut tenant-owned tablolar, RLS zaten
aktif). **RLS bloğu gerekmez** — yeni tablo yok.

## Test
- `RentalUpdateTests` (mevcut varsa genişlet): `CikisOfisi` override — açık kirada
  `UpdateOpenAsync` ile `CikisOfisi` değiştirilir, `CikisSubeId` interceptor'ının (`OfficeBranchInterceptor`)
  yeni değere göre güncellendiği doğrulanır (bağımsız oracle — elle beklenen şube ID'siyle
  karşılaştır). `TeslimEdenPersonelId`/`OdemeSekli` round-trip.
- Personel dropdown guard testi (regresyon — CLAUDE.md mega-form tuzağı): Operatör rolüyle
  `TeslimEdenPersonelId` seçim listesi çağrıldığında `ListForSelectAsync` kullanıldığı, `ListAsync`
  (ManageUsers-gate) ÇAĞRILMADIĞI doğrulanır — operatör 403/exception ALMAMALI.
- Bakiye alanları testi: 9 yeni alan `decimal?` set edilip okunuyor mu (round-trip) + **regresyon**:
  bu alanlar set edilse de `GenelToplam`/`Tahsilat`/`Bakiye` DEĞİŞMİYOR (elle 2 senaryo — biri
  bakiye alanları null, biri dolu — ikisinde de mevcut toplam aynı sabit değer).
- `Vehicle.Konum` round-trip.
- 2. sürücü serbest-metin testi: `IkinciSurucuId=null` + 5 serbest alan dolu senaryosu round-trip;
  ikisi birlikte doluysa (FK + serbest-metin) davranış testte belgelenir (hangisi öncelikli —
  implementasyon kararı, testte sabitlenir).

## Exit
- [ ] İşlem Şube artık düzenlenebilir select, `RentalUpdateInput.CikisOfisi` ile kaydediliyor
- [ ] Teslim Eden (çıkış) + Ödeme Şekli formda çalışıyor
- [ ] `Vehicle.Konum` formda çalışıyor
- [ ] 9 bakiye alanı veri modeli var, mevcut toplam hesaplarını BOZMADI (regresyon yeşil)
- [ ] Müşteri adres/pasaport/ehliyet salt-okur panel gösteriliyor (KVKK decrypt deseni korunuyor)
- [ ] 2. sürücü misafir serbest-metin çalışıyor
- [ ] Tam suite yeşil

## Notlar
**YAPILMAZ (onay gerekmez, mimari karar):** (1) canlının ~30 sabit-kodlanmış ek hizmet/sigorta tipi
(128 alan) — `EkHizmetTanim` generic master + `RentalAddOn` snapshot modeli ZATEN aynı işi yapıyor,
30 sabit kolon eklemek mimari gerileme olur; gerçek eksik olan **fiyat-tipi override** ("Bedava"
seçeneği, şu an disabled) ayrı bir **0,5 günlük D2** fazı olarak ele alınabilir ama BU FAZA GİRMEZ.
(2) B2B Hizmet Alımı "görsel ama disabled" 9 alan — `DisHizmetAlimi` (FAZ 4-B2) AYNI işi TAM
DEFTERLİ yapıyor, placeholder taklidi geriye gidiş olur.

Madde 5 (çok-taraflı bakiye) burada YALNIZ veri modeli — Opus "kim öder/kim fatura alır" kararını
verince gerçek dağıtım mantığı (`RentalService`/fatura kesim akışı) AYRI bir D5 fazı olarak açılır ve
CLAUDE.md §3.5 zorunlu adversarial inceleme (Σ Borç=Σ Alacak, idempotency) gerektirir — bu fazın
kapsamı DEĞİL, sonraki bir fazın ön koşulunu hazırlar. Fiyat başlığı/indirim ayrıntısı
(`Liste_Fiyat`/`Liste_Indirim`, `Saat_Farki`, `Vade_Farki_Ay/Hizmet`, `Damga_Vergisi_Orani`/
`Damga_Yansit`) da aynı şekilde Opus kararına bağlı, bu fazda YOK (mevcut `IskontoTutar`/
`HaftaSonuFark`/`DamgaVergisi` alanlarıyla çift-sayım riski — KURAL A/B disiplini gerekir).
