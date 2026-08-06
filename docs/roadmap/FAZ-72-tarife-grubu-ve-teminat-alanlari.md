# FAZ-72 — Tarife Grubu Master + Tarife Teminat/Görünürlük Alanları

| | |
|---|---|
| **Desen** | D2 |
| **Efor** | 2 gün (`fiyat_grup_tanimlama.aspx` master 1,5g + `tarifeler.aspx` bölüm b/c/d 0,5g) |
| **Bağımlılık** | yok (broker-auth kullanım senaryosu **bloke** — bkz. `_toplama-06-07-fiyat-rapor.md`) |
| **Kapsanan canlı ekran** | `fiyat_grup_tanimlama.aspx`, `tarifeler.aspx` (bölüm b/c/d — teminat
  bayrakları + Gösterme + Tarife Grubu seçici; KM kademesi FAZ-71'de) |
| **Risk** | düşük (additive master + bool bayraklar; kimlik alanları at-rest şifreli tutulmalı) |

## Amaç
Kullanıcı yeni bir "Tarife Grubu" (Ad + Oran + broker giriş kimliği) tanımlayabilir; mevcut
`/tarifeler` (RateCard) satırlarında teminat dahil/zorunlu bayrakları + gizle bayrağı + (opsiyonel)
bu tarife grubuna referans görebilir/girebilir.

## Neden (kanıt)
- Yeni `TarifeGrubu` entity'si repoda YOK (grep doğrulandı — hiçbir dosyada `TarifeGrubu` geçmiyor).
- `src/RentACar.Domain/Entities/RateCard.cs` bugün: `Kod/Ad/Grup/MinGun/MaxGun/GunlukUcret/Doviz/
  GecerliBas/GecerliBit/Aktif` (satır 14-34) — `SCDW_Dahil`/`MiniHasarDahil`/`HirsizlikDahil`/
  `SCDW_Zorunlu`/`Gosterme`/tarife-grubu referansı YOK.
- `RateCardInput.cs` tam alan listesi doğrulandı (`Kod, Ad, Grup, MinGun, MaxGun, GunlukUcret,
  Doviz, GecerliBas, GecerliBit, Aktif`) — yukarıdaki alanların hiçbiri yok.
- **Not (FAZ-71'den farklı olarak burada RateCard hedefi DOĞRU):** bu alanlar ("teminat dahil mi",
  "gizle", "tarife grubu referansı") salt-görüntü/bilgi amaçlıdır, `RentalQuoteEngine`'in aktif
  fiyatlama yoluna (RateMatrix) girmesi GEREKMEZ — `/tarifeler` (RateCard, route doğrulandı:
  `RateCardList.razor` satır 1 `@page "/tarifeler"`) canlı `tarifeler.aspx`'in doğrudan karşılığı
  olan BAĞIMSIZ bir CRUD ekranıdır; RateCard'ın fiyat motorunda DEPRECATED-fallback olması bu
  ekranın kendi varlık nedenini (master veri girişi) geçersiz kılmaz.

## Yapılacaklar
1. Yeni `src/RentACar.Domain/Entities/TarifeGrubu.cs`: `Id, TenantId, Kod, Ad, Oran (decimal),
   KullaniciAdi (string?), SifreHash (string?)` + `ITenantOwned, IAuditable`. Şifre CLAUDE.md §4 PII
   deseni gibi at-rest korumalı olmalı — düz metin YAZILMAZ; `ISecretProtector` ile hash'lenir (tam
   PII-blind-index gerekmez, sadece tek-yönlü hash + doğrulama yeterli, `IPasswordHasher`/benzeri
   basit bir hash servisi kullanılabilir — mevcutsa Personel/User şifre-hash deseniyle aynı yol).
2. Yeni `src/RentACar.Application/TarifeGrubu/` altında `TarifeGrubuInput.cs` (Ad, Oran,
   KullaniciAdi, Sifre — girişte düz, serviste hash'lenir) + `ITarifeGrubuRepository.cs` +
   `TarifeGrubuService.cs` (CRUD + kod-benzersizlik + `OperationsWrite` guard).
3. Yeni `src/RentACar.Web/Components/Pages/TarifeGrubu/TarifeGrubuList.razor` (`/tarife-grubu`) +
   `TarifeGrubuEndpoints.cs` + `Program.cs` `MapTarifeGrubuEndpoints()` + `_Imports.razor` +
   `MainLayout.razor` nav girdisi.
4. `src/RentACar.Domain/Entities/RateCard.cs` — additive alanlar: `SCDW_Dahil bool`,
   `MiniHasarDahil bool`, `HirsizlikDahil bool`, `SCDW_Zorunlu bool` (hepsi default `false`),
   `Gosterme bool` (default `false` — gizle bayrağı, `false`=görünür), `TarifeGrubuId Guid?`
   (opsiyonel FK, `TarifeGrubu.Id`'ye — additive, zorunlu değil).
5. `src/RentACar.Application/Pricing/RateCardInput.cs` + `RateCardService.cs` — 6 yeni alan
   `Normalize()`/`Apply()`'a eklenir; `TarifeGrubuId` verilirse `ITarifeGrubuRepository.FindAsync`
   ile var olduğu doğrulanır (yoksa `ValidationException`).
6. `RateCardList.razor` — 4 bool checkbox (SCDW/Mini/Hırsızlık/Zorunlu) + `Gosterme` checkbox +
   `TarifeGrubu` seçici (datalist seç-veya-yaz — CLAUDE.md ComboBox konvansiyonu, FK alanı çünkü bu
   AYRI kural, mevcut deseni bozmaz) form alanları eklenir.

## Dokunulacak dosyalar
- (yeni) `src/RentACar.Domain/Entities/TarifeGrubu.cs`
- (yeni) `src/RentACar.Application/TarifeGrubu/TarifeGrubuInput.cs`,
  `ITarifeGrubuRepository.cs`, `TarifeGrubuService.cs`
- (yeni) `src/RentACar.Infrastructure/Persistence/Repositories/TarifeGrubuRepository.cs` +
  `Configurations/` altına config sınıfı
- (yeni) `src/RentACar.Web/Components/Pages/TarifeGrubu/TarifeGrubuList.razor`,
  `src/RentACar.Web/TarifeGrubu/TarifeGrubuEndpoints.cs`
- `src/RentACar.Domain/Entities/RateCard.cs` — additive 6 alan
- `src/RentACar.Application/Pricing/RateCardInput.cs`, `RateCardService.cs`
- `src/RentACar.Web/Components/Pages/Pricing/RateCardList.razor`
- `src/RentACar.Web/Pricing/RateCardEndpoints.cs`
- `src/RentACar.Web/Program.cs`, `_Imports.razor`, `MainLayout.razor`

## Migration
1. Yeni tablo `TarifeGrubu` (tenant-owned) — **RLS bloğu ELLE eklenir** (CLAUDE.md §5): `ENABLE`+
   `FORCE ROW LEVEL SECURITY` + `CREATE POLICY tenant_isolation ... USING/WITH CHECK
   (NULLIF(current_setting('app.tenant_id',true),'')::uuid)` + `GRANT` `racar_app`'e.
2. `RateCard`'a 6 additive kolon (bool×5 + `TarifeGrubuId uuid NULL`) — tablo zaten RLS'li, yeni
   kolon RLS bloğu gerektirmez; `TarifeGrubuId` için FK constraint eklenir (opsiyonel, `ON DELETE
   SET NULL`).

## Test
- `TarifeGrubuTests`: CRUD + kod-benzersizlik + şifre at-rest (düz metin DB'de YOK — `SifreHash`
  kolonunda ham şifre değerinin GEÇMEDİĞİ doğrulanır) + **tenant izolasyonu** (`racar_app` ile,
  başka tenant'ın `TarifeGrubu` satırı görünmüyor/silinemiyor) + yetki (`OperationsWrite` olmayan
  kullanıcı red alır).
- `RateCardTests`: 6 yeni alanın CRUD'da doğru kaydedildiği; `TarifeGrubuId` geçersiz bir Guid ile
  gönderilirse `ValidationException`; `TarifeGrubu` silinirse (varsa) `RateCard.TarifeGrubuId`
  `NULL`'a düştüğü (`ON DELETE SET NULL`) doğrulanır.

## Exit
- [ ] `/tarife-grubu` CRUD çalışıyor, şifre at-rest hash'li (düz metin yok)
- [ ] `RateCard` 6 yeni alan CRUD'da çalışıyor
- [ ] Tenant izolasyon testi yeşil
- [ ] Tam suite yeşil

## Notlar
Bu kaydın gerçek canlı kullanım amacı (broker'ın bu kimlikle XML/feed üzerinden bize giriş/doğrulama
yapması) **BAŞKA ve daha büyük bir kapsam** (yeni dikey: broker feed auth) — **BLOKE**, hangi
broker/XML-feed kimliği kullanılacağı kullanıcıya sorulmadan açılmaz (bkz. toplama dosyası BLOKE
tablosu). Bu faz YALNIZ master kaydı (Ad+Oran+kimlik alanları, CRUD) planlar; auth-gate akışı bu
fazda YOKTUR.
