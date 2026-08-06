# FAZ-74 — Maliyet Hesaplama Derinliği (Kalem-Bazlı Girdi + Kayıtlı Teklif)

| | |
|---|---|
| **Desen** | D2 (`maliyet_hesaplama.aspx`) + D7 (`maliyet_hesaplama_ara.aspx`) — **PARA — Opus**
  (Rotatif kredi formülü, aşağıda) |
| **Efor** | 4 gün (`maliyet_hesaplama.aspx` 2g + `maliyet_hesaplama_ara.aspx` 2g) |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `maliyet_hesaplama.aspx`, `maliyet_hesaplama_ara.aspx` |
| **Risk** | orta — Rotatif kredi hesap yöntemi ayrı bir finansal-model kararı gerektiriyor (Eşit
  Taksitli'nin klonu değil); kalem-toplama yapısı bu karara bağlı değil |

## Amaç
`/maliyet-hesapla` bugünkü tek `AylikGider` alanlı, durumsuz (persist edilmeyen) hesap makinesi
yerine kalem-bazlı gider girişi (Kasko/Trafik/MTV/Bakım/Lastik/Takip/vb.) alır, `KrediHesaplamaSekli`
(Eşit Taksitli/Rotatif) seçilebilir, sonuç **kaydedilebilir** (`MaliyetTeklifi`, boşluksuz no,
arama/liste sayfası).

## Neden (kanıt)
- `src/RentACar.Application/Pricing/MaliyetHesapModels.cs` — bugünkü TAM alan listesi doğrulandı:
  `MaliyetHesapInput(AlisBedeli, ResidualYuzde, SureAy, FaizOran, KkdfOran, BsmvOran, DamgaOran,
  AylikGider, KarMarji, KdvOran)` — kalem bazlı Kasko/Trafik/MTV/Bakım/Lastik/Takip/Tescil/Muayene/
  Yedek-Araç/Yönetim/Enflasyon/Banka-Masraf alanları YOK, hepsi `AylikGider` TEK alanına sıkışmış.
- `MaliyetHesapService.Hesapla` (satır 23-35, doğrulandı): `toplamGider = R(AylikGider × SureAy)`
  (satır 28) — bu tek satır kalem-toplamına dönüştürülecek nokta; `KrediHesaplamaSekli` hiç yok
  (`finansmanFaiz = R(AlisBedeli × FaizOran × SureAy/12)` — DÜZ/basit faiz, Eşit-Taksitli'ye özgü).
- `MaliyetHesaplama.razor` (`/maliyet-hesapla`, doğrulandı): form `method="get"`, tek buton
  "Hesapla" — **"Kaydet" butonu YOK**, sonuç `_sonuc` sadece render ediliyor, DB'ye yazılmıyor.
- `MaliyetTeklifi` entity'si repoda YOK (yeni, D7 — bugün durumsuz hesap makinesi kalıcı kayda
  dönüşecek).

## Yapılacaklar
### A) maliyet_hesaplama.aspx — kalem-bazlı girdi (2 gün)
1. `src/RentACar.Application/Pricing/MaliyetHesapModels.cs` — `MaliyetHesapInput`'a kalem alanları
   eklenir: `KaskoYillik`, `TrafikSigortasiYillik`, `MtvYillik`, `BakimYillik`, `BakimBirim`,
   `LastikYillik`, `LastikKisYillik`, `AracTakipYillik`, `TescilPlakaYillik`, `MuayeneEmisyonYillik`,
   `YedekAracYillik`, `YonetimGideriAylik`, `EnflasyonYillik`, `BankaDosyaDigerMasraf` (hepsi
   `decimal`, default `0m`) + `KrediHesaplamaSekli` enum (`EsitTaksitli`/`Rotatif`) + `CariId
   (Guid?)`, `HazirlayanId (Guid?)`, `AracSayisi (int, default 1)`.
2. `MaliyetHesapService.Hesapla` — `toplamGider` artık `AylikGider` yerine (geriye uyum:
   `AylikGider` "Diğer" kalemine MAP edilir, silinmez) yukarıdaki kalemlerin YILLIK toplamının
   12'ye bölünmüş ortalaması + `AylikGider`(Diğer) ile hesaplanır: `toplamGiderAylik =
   (KaskoYillik+TrafikSigortasiYillik+...+EnflasyonYillik)/12 + YonetimGideriAylik + AylikGider`,
   `toplamGider = R(toplamGiderAylik × SureAy)`.
3. `KrediHesaplamaSekli.EsitTaksitli` seçiliyse mevcut formül (satır 25-26, basit faiz) DEĞİŞMEDEN
   çalışır. `KrediHesaplamaSekli.Rotatif` seçiliyse **PARA — Opus kararı**: bakiye-azalan/rotatif
   faiz modelinin tam formülü (hangi dönemde bakiye üzerinden faiz, ödeme planı) bu fazda
   YAZILMAZ — geçici olarak `Rotatif` seçilirse `ValidationException` ile "Rotatif hesap yöntemi
   yakında" reddi döner (temiz red, sessiz yanlış hesap YOK).
4. `MaliyetHesaplama.razor` — kalem alanları için form input grid'i (14 alan) + `KrediHesaplamaSekli`
   dropdown + `CariId`/`HazirlayanId` ComboBox (datalist seç-veya-yaz) + `AracSayisi` sayısal input.

### B) maliyet_hesaplama_ara.aspx — kayıtlı teklif (2 gün)
5. Yeni `src/RentACar.Domain/Entities/MaliyetTeklifi.cs`: `Id, TenantId, KayitNo (string,
   MT-000001), Baslik, Plaka (string?), Tarih, [madde 1'deki TÜM girdi alanları SNAPSHOT olarak],
   [MaliyetHesapSonuc'un TÜM alanları SNAPSHOT olarak]` + `ITenantOwned, IAuditable`. **Snapshot
   ZORUNLU** — girdi tanımları sonradan değişse geçmiş teklif kaymaz (CLAUDE.md §4 KdvOranSnapshot
   deseniyle aynı prensip).
6. `SequenceAllocator.NextAsync(db, tenant, "MTNo")` ile boşluksuz `MT-000001` üretimi — insert ile
   AYNI transaction.
7. Yeni `src/RentACar.Application/Pricing/MaliyetTeklifiInput.cs`, `IMaliyetTeklifiRepository.cs`,
   `MaliyetTeklifiService.cs` (CRUD + arama: başlık/tarih/plaka/fiyat aralığı).
8. `MaliyetHesaplama.razor`'a "Kaydet" aksiyonu — mevcut `_sonuc` + tüm girdi alanları
   `MaliyetTeklifiService.CreateAsync`'e gönderilir (POST, mevcut `method="get"` hesaplama formunun
   YANINDA ayrı bir form — CLAUDE.md §razor-post-ucu-cakismasi tuzağı: aynı `@page` yoluna
   `MapPost` eklenmez, ayrı alt-yol `/maliyet-hesapla/kaydet` kullanılır).
9. Yeni `src/RentACar.Web/Components/Pages/Pricing/MaliyetTeklifiList.razor` (`/maliyet-teklifleri`)
   — arama/liste sayfası.

## Dokunulacak dosyalar
- `src/RentACar.Application/Pricing/MaliyetHesapModels.cs`
- `src/RentACar.Application/Pricing/MaliyetHesapService.cs`
- `src/RentACar.Web/Components/Pages/Pricing/MaliyetHesaplama.razor`
- (yeni) `src/RentACar.Domain/Entities/MaliyetTeklifi.cs`
- (yeni) `src/RentACar.Application/Pricing/MaliyetTeklifiInput.cs`, `IMaliyetTeklifiRepository.cs`,
  `MaliyetTeklifiService.cs`
- (yeni) `src/RentACar.Infrastructure/Persistence/Repositories/MaliyetTeklifiRepository.cs` +
  `Configurations/` config sınıfı
- (yeni) `src/RentACar.Web/Components/Pages/Pricing/MaliyetTeklifiList.razor`
- `src/RentACar.Web/Pricing/` altına yeni endpoint dosyası (Kaydet aksiyonu için)

## Migration
Yeni tablo `MaliyetTeklifi` (tenant-owned) — **RLS bloğu ELLE eklenir** (CLAUDE.md §5):
`ENABLE`+`FORCE ROW LEVEL SECURITY` + `tenant_isolation` policy + `GRANT` `racar_app`'e. Mali
belge SAYILMAZ (defter'e postlamaz, salt teklif/hesap kaydı) — immutability trigger GEREKMEZ (tam
CRUD grant).

## Test
- `MaliyetHesapServiceTests`: elle kurulan kalem değerleri (`KaskoYillik=12000, TrafikSigortasiYillik
  =6000, ..., SureAy=36`) ile beklenen `toplamGiderAylik`/`toplamGider` TESTTE elle hesaplanıp
  sabit yazılır (serviskodundan türetilmez) — geriye-uyum: `AylikGider` tek başına set edilip diğer
  kalemler 0 bırakıldığında SONUÇ bugünkü formülle AYNI çıkar (regresyon).
- `KrediHesaplamaSekli.Rotatif` seçilince `ValidationException` fırlatıldığı doğrulanır (temiz red,
  sessiz yanlış hesap yapılmadığının kanıtı).
- `MaliyetTeklifiTests`: `KayitNo` boşluksuz sıra (`MT-000001`, `MT-000002`, ... — eşzamanlı 2 create
  çağrısı da sıra atlamıyor/çakışmıyor testi) + snapshot testi (girdi tanımı sonradan "değişse"
  [örn. `KdvOran` varsayılanı değişse] geçmiş `MaliyetTeklifi.TeklifKdvli` DEĞİŞMEDEN kalır) +
  tenant izolasyon (`racar_app`) + arama filtreleri (başlık/tarih/plaka/fiyat).

## Exit
- [ ] Kalem-bazlı girdi formu çalışıyor, `AylikGider` geriye-uyumlu "Diğer" kalemine map'leniyor
- [ ] `Rotatif` seçimi temiz red veriyor (Opus kararına kadar)
- [ ] `MaliyetTeklifi` boşluksuz no ile kaydediliyor, snapshot doğrulanmış
- [ ] Arama/liste sayfası çalışıyor
- [ ] Tam suite yeşil

## Notlar
Rotatif kredi hesap yönteminin faiz/amortisman formülü **AYRI bir finansal-model kararı** gerektirir
— bu faz o karara kadar güvenli-red ile ilerler; kalem-toplama yapısı bu karara BAĞLI DEĞİL ve
yine de eklenir (Opus onayı geldiğinde `Rotatif` dalı ayrı küçük bir takip-PR'ı ile doldurulur).
