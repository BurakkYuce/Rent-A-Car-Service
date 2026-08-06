# FAZ-23 — Şube Derinlik Paketi

| | |
|---|---|
| **Desen** | D2 (büyük derinlik — 20+ yeni alan + 1 child-tablo + 1 veri-taşıma aracı) |
| **Efor** | 3 gün |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `sube_tanimlama.aspx` |
| **Risk** | yüksek — birleştirme aracı FK/metin referanslarını toplu güncelliyor (Vehicle.SubeId,
  Expense, User.AtanmisSube); yanlış hedef seçimi çok kayıt etkiler. Silme YOK (pasife çekme +
  audit iziyle), ama toplu UPDATE'in kendisi geri alınabilir olmalı |

## Amaç
Şube tanımına canlıdaki web görünürlük/komisyon/harita/entegrasyon alanlarını ve "Şube Özel Ücretsiz
Hizmet" child-listesini ekleyip, eski bir şubeyi yeni bir şubeye toplu taşıyan (birleştirme) bir araç
sunarak kullanıcı şube master verisini canlı paritesine yakın yönetebilir, hatalı/yinelenen şubeleri
veri kaybı olmadan birleştirebilir hale gelir.

## Neden (kanıt)
`src/RentACar.Domain/Entities/Branch.cs` şu an `Kod, Ad, Adres, Telefon, Eposta, Il, Ilce, Yetkili,
CalismaSaatleri, KomisyonOran, EvrakNoOnek, Aktif` taşıyor. Plandaki `WebIsim`, `FirmaUnvani`,
haftalık açılış/kapanış, `WebRezOncesiSaat`, `Enlem`/`Boylam`, `HizmetKomisyonOran`,
`RezervasyonRengi`, `AlisSubesiDegilMi`, `WebSira`, `WebOtoparkId`, `BayiCariKod`/`BayiOfisId`,
`KomisyonHesabi`, `OnlineRezId`, `SozlesmeNoFormati`, `NakitHesapId`/`BankaHesapId`,
`EntegrasyonKodu`, `ResimDosyasi` **hiçbiri yok** (grep doğrulandı). Mevcut `KomisyonOran` zaten var
ve plandaki yeni `HizmetKomisyonOran` ONDAN AYRI bir kavram ("hizmetten alınan" vs mevcut genel
komisyon) — isim çakışması riski not edilir. "Şube Özel Ücretsiz Hizmet" child-tablosu ve
eski→yeni şube birleştirme aracı hiç yok.

## Yapılacaklar
1. `src/RentACar.Domain/Entities/Branch.cs` — ekle: `WebIsim` (string?), `FirmaUnvani` (string?),
   `HaftalikCalismaSaatleri` (JSONB, Lokasyon'daki (FAZ-22) `GunSaat` modeliyle AYNI tip —
   FAZ-22 sonra gelmişse tipi paylaş, önce gelmişse aynı şekli burada da tanımla),
   `WebRezOncesiSaat` (int?), `Enlem`/`Boylam` (decimal?), `HizmetKomisyonOran` (decimal?, mevcut
   `KomisyonOran`'dan AYRI kolon — isim çakışmasın diye XML doc'ta ikisinin farkı açık yazılır),
   `RezervasyonRengi` (string?, hex, ör. "#FF00AA"), `AlisSubesiDegilMi` (bool, default false),
   `WebSira` (int?), `WebOtoparkId` (string?), `BayiCariKod` (string?), `BayiOfisId` (string?),
   `KomisyonHesabi` (string?, "Satıştan"/"Maliyetten"), `OnlineRezId` (string?), `SozlesmeNoFormati`
   (string?), `NakitHesapId`/`BankaHesapId` (Guid?, `FinancialAccount` FK — default hesap ataması,
   FK constraint YOK gerekmez, additive Guid? referans), `EntegrasyonKodu` (string?), `ResimDosyasi`
   (string?, dosya yolu — dosya yükleme mekanizması BU FAZA GİRMEZ, yalnız yol metni saklanır).
2. Yeni `src/RentACar.Domain/Entities/SubeUcretsizHizmet.cs` — `ITenantOwned, IAuditable`: `Id`,
   `TenantId`, `SubeId` (Guid, Branch FK), `HizmetAdi` (string), `Aciklama` (string?). 10 satıra
   kadar (uygulama tarafında sayı sınırı UI'da uyarı, DB'de zorlanmaz).
3. `src/RentACar.Infrastructure/Persistence/Configurations/MasterConfigs.cs` — `BranchConfig`'e 20
   yeni kolon ekle; yeni `SubeUcretsizHizmetConfig` sınıfı ekle (`HasOne/WithMany` Branch'e FK,
   `HasIndex(TenantId, SubeId)`).
4. Migration: `dotnet ef migrations add AddSubeDerinlikVeUcretsizHizmet --project
   src/RentACar.Infrastructure --startup-project src/RentACar.Infrastructure`.
5. **`SubeUcretsizHizmet` YENİ TABLO — RLS bloğunu migration'a ELLE ekle** (EF üretmez, CLAUDE.md §5):
   `ENABLE ROW LEVEL SECURITY` + `FORCE ROW LEVEL SECURITY` + `CREATE POLICY tenant_isolation …
   USING/WITH CHECK (NULLIF(current_setting('app.tenant_id',true),'')::uuid)` +
   `GRANT SELECT, INSERT, UPDATE, DELETE ON … TO racar_app` (mali belge değil, tam CRUD).
6. `src/RentACar.Application/Branches/BranchInput.cs` + `BranchService.cs` — 20 yeni alanı
   create/update input'una ekle, map et; `SubeUcretsizHizmet` için CRUD metotları ekle
   (`ListHizmetlerAsync`/`AddHizmetAsync`/`RemoveHizmetAsync`).
7. `src/RentACar.Web/Components/Pages/Branches/BranchList.razor` — 20 yeni alan (haftalık saat
   mini-tablosu dahil) + child-liste UI'ı (ekle/sil, satır başına form).
8. Yeni `src/RentACar.Web/Branches/BranchEndpoints.cs`'e birleştirme ucu ekle: `POST
   /subeler/birlestir` — kaynak şube ID + hedef şube ID alır; `Vehicle.SubeId`, `Expense` (şube
   referansı varsa), `User.AtanmisSube` gibi FK/metin referanslarını TEK transaction'da hedefe
   toplu güncelleyip kaynağı `Aktif=false` yapar (silme YOK). İşlem denetim log'una yazılır (kaç
   kayıt, hangi tablo, kim, ne zaman).
9. `BranchList.razor`'a "Eski Şube→Yeni Şube" formu ekle (iki dropdown + onay checkbox + submit).

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/Branch.cs` — 20 alan
- (yeni) `src/RentACar.Domain/Entities/SubeUcretsizHizmet.cs`
- `src/RentACar.Infrastructure/Persistence/Configurations/MasterConfigs.cs` — `BranchConfig` +
  yeni `SubeUcretsizHizmetConfig`
- (yeni) migration dosyası
- `src/RentACar.Web/Components/Pages/Branches/BranchList.razor`
- `src/RentACar.Web/Branches/BranchEndpoints.cs` — birleştirme ucu
- `src/RentACar.Application/Branches/BranchInput.cs`, `BranchService.cs`

## Migration
Var — `Branches` tablosuna 20 nullable kolon (RLS zaten aktif, dokunmaz) + **yeni**
`SubeUcretsizHizmet` tablosu (RLS bloğu ELLE eklenir, madde 5).

## Test
- `BranchTests`: 20 yeni alan round-trip (bağımsız oracle — elle kurulan şube nesnesi).
- `SubeUcretsizHizmetTests` (yeni): CRUD + **tenant izolasyonu `racar_app` ile** (CLAUDE.md §8 —
  başka tenant'ın hizmet satırı görünmüyor).
- Birleştirme testi (bağımsız oracle): 2 araç + 1 gider kaynak şubeye bağlı elle kurulur; birleştirme
  çalıştırılır; hedef şubede 2 araç + 1 gider, kaynak şube `Aktif=false`, kaynak şubede 0 kayıt
  beklenir (sayı testte sabit yazılır). Negatif senaryo: hedef=kaynak seçilirse guard hatası.
- Yetki testi: birleştirme ucu yalnız Admin/Yonetici rolüyle çalışıyor (Operator 403/guard).

## Exit
- [ ] Şube formunda 20 yeni alan + haftalık saat tablosu var, kaydediliyor
- [ ] Şube Özel Ücretsiz Hizmet child-listesi ekle/sil çalışıyor, tenant izolasyonu doğrulandı
- [ ] Birleştirme aracı 3 referans tablosunu (Vehicle/Expense/User) doğru taşıyor, kaynağı silmeden pasife çekiyor
- [ ] Tam suite yeşil

## Notlar
`HizmetKomisyonOran` ile mevcut `KomisyonOran` AYNI ANLAMA gelmez — forma eklerken iki alanı yan
yana göster ve etiket farkını açık yaz ("Genel Komisyon" vs "Hizmetten Alınan Komisyon"), aksi halde
kullanıcı hangi alanı dolduracağını karıştırır (canlıda ayrı ekran alanları, burada aynı formda).
Birleştirme aracı riskli olduğundan Exit'e ek olarak canlı-benzeri bir "dry-run" (kaç kayıt
etkilenecek, onay öncesi göster) düşünülebilir — bu fazın parçası değil ama Notlar'da bırakılır.
