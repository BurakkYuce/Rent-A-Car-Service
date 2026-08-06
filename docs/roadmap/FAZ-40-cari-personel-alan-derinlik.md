# FAZ-40 — Cari & Personel Alan Derinliği

| | |
|---|---|
| **Desen** | D1 (alan ekleme, ×2 entity) + D3 (liste/filtre yüzeyleme) |
| **Efor** | 4 gün |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `musteri_kayit.aspx`, `musteri_genel_liste.aspx`, `musteri_listesi.aspx`, `personel_kayit.aspx`, `personel_listesi.aspx` |
| **Risk** | düşük — para/defter mantığı yok, saf alan ekleme + liste kolonu; tek istisna: `Customer.Sifre` (portal şifresi) **düz metin YAZILMAZ**, hash'lenir (bkz. Notlar) |

## Amaç
Cari ve Personel kayıt formlarına canlıdaki eksik alanlar (Cari'de 39, Personel'de 18) eklenip, her
iki modülün liste ekranlarına arama/filtre + zaten entity'de var ama grid'de gösterilmeyen kolonlar
kazandırılarak kullanıcı `/cariler` ve `/personel` üzerinde canlı paritesine yakın kayıt+arama
yapabilir hale gelir.

## Neden (kanıt)
- `src/RentACar.Domain/Entities/Customer.cs` — plandaki 39 alan (`TcDogrulama`, `Ulke`, `Tel2`,
  `OzelKod`, `EntegrasyonKodu`, `Aciklama`, `FaturaAdresFarkli`, `RiskIzin`, `BayiKomisyon`,
  `FaturaTekSatir`, `DogumGunuTakip`, 6× KVKK-anonimleştirme bayrağı, `DogumYeri`, `PasaportTarihi`,
  `PasaportYeri`, `KurumsalNo`, `Sifre`, `UyariSerbest`, `WebIndirim`, `KaraZamani`, `IslemSubeId`,
  `BakiyeGor`, `TevkifatKodu`, `AracVerilmez`, `YasEhliyetSerbest`, `FaturaKiralayanIsim`,
  `MerkezKurumsal`, `Broker`, `FindexZorunlu`, `IsAdresi`, `IsTelefonu`, `FirmaId`, `KayitliIl`,
  `KayitliIlce`, `MahalleKoy`, `SeriNo`, `CiltNo`, `AileSira`, `SiraNo`) **hiçbiri entity'de yok**.
  Ayrıca var olan `Adres` alanı `CustomerEdit.razor` formunda render edilmiyor (grep doğrulandı —
  form'da `name="adres"` input yok).
- `src/RentACar.Application/Customers/CustomerRow.cs` — `MusteriTemsilcisi`, `DogumTarihi`,
  `PasaportNo`, `VadeGun`, `UyariNedeni`, `Gsm2`, `Adres`, `Il`/`Ilce` projeksiyona **girmiyor**
  (entity'de zaten var — D3, en ucuz sınıf). `CustomerFilter.cs`'de `Pasif` (bool?) **yok** —
  `Customer.Aktif` entity'de var ama filtre modelinde eksik.
- `src/RentACar.Domain/Entities/Personel.cs` şu an `Kod, Ad, Soyad, TcKimlikEnc, IseGiris, IseCikis,
  SurucuBelgeNo, MaasEnc, Sube, SubeId, Aktif` taşıyor — plandaki `Adres`, `EvTelefonu`,
  `IsTelefonu`, `CepTel`, `Referans`, `Aciklama`, `SSinifi`, `SVerilisTarihi`, `SVerilisYeri`,
  `MailAdresi`, `DogumTarihi`, `DogumYeri`, `BabaAdi`, `AnaAdi`, `Il`, `Ilce`, `Mahalle`, `CiltNo`,
  `AileSiraNo`, `SiraNo`, `KanGrubu`, `GorevTanimi`, `RacTabletNo` **hiçbiri yok** (grep doğrulandı).
  `GorevTanimi` (İç Ekip/Yönetici/Operasyon) en büyük iş-değeri taşıyan alan — personelin
  unvan/departman ayrımı bugün hiç yok.
- `src/RentACar.Web/Components/Pages/Personnel/PersonelList.razor` şu an `Personel.ListAsync()`
  filtresiz çağrılıyor (arama kutusu yok); liste kolonlarında Cep Tel/Mail/TC Kimlik(maskeli)/Rac
  Tablet No yok.

## Yapılacaklar
1. `src/RentACar.Domain/Entities/Customer.cs` — yukarıdaki 39 nullable/bool alanı ekle
   (`Sifre` **string? hash** olarak saklanır — düz metin alanı ADI `SifreHash` olsun, entity'de
   `Sifre` property'si `[NotMapped]` write-only setter üzerinden hash'lenip `SifreHash`'e yazılır;
   `ISecretProtector`/mevcut hash altyapısı varsa onu kullan, yoksa `PasswordHasher<Customer>`
   benzeri basit bir hash ekle — düz metin kolonu **asla** yazılmaz).
2. `src/RentACar.Infrastructure/Persistence/Configurations/CustomerConfigs.cs` — 39 kolonun
   tip/uzunluk ayarını ekle (`BayiKomisyon`/`WebIndirim` → `numeric(19,4)`; `FirmaId`/`IslemSubeId`
   → `Guid?`, FK constraint yok, additive referans).
3. Migration: `dotnet ef migrations add AddCustomerFormDerinlik2 --project src/RentACar.Infrastructure
   --startup-project src/RentACar.Infrastructure` (isim "2" — `AddCustomerDerinlik` adı 2026-06-30'da
   zaten kullanıldı, çakışmasın).
4. `src/RentACar.Application/Customers/CustomerInput.cs` — 39 alanı create/update input'una ekle,
   `CustomerService.cs`'te map et.
5. `src/RentACar.Web/Components/Pages/Customers/CustomerEdit.razor` — 39 yeni input + var olan
   `Adres` alanı için `<label>Adres<input name="adres" value="@_c.Adres" /></label>` ekle (tek
   satır, entity'de zaten var, forma hiç bağlanmamış).
6. `src/RentACar.Application/Customers/CustomerRow.cs` — `MusteriTemsilcisi`, `DogumTarihi`,
   `PasaportNo` (maskeli — `PasaportNoEnc`'ten decrypt, KVKK-gate aynı desende `CustomerRepository`
   içindeki mevcut decrypt yardımcılarını kullan), `VadeGun`, `UyariNedeni`, `Gsm2`, `Adres`,
   `Il`/`Ilce` alanlarını ekle.
7. `src/RentACar.Application/Customers/CustomerFilter.cs` — `Pasif` (bool?) ekle.
8. `src/RentACar.Infrastructure/Persistence/Repositories/CustomerRepository.cs` —
   `SearchRowsAsync` JOIN'ini genişlet (yeni `CustomerRow` alanları + `Pasif` filtresi); `Bakiye`
   kolonu için `AccountLedgerEntry` GROUP BY CariId tek toplu sorguyu satır-bazlı JOIN'e taşı
   (N+1'e karşı — `/cariler/{id}/detay`'daki mevcut hesaplama sorgusunun aynısı, tek yerde).
9. `src/RentACar.Web/Components/Pages/Customers/CustomerList.razor` — yeni kolonlar
   (Entegrasyon Kodu, Özel Kod, Tel2/Gsm2, Mail, Pasaport No, Ülke, Toplam Gün/Ortalama Günlük/
   Ortalama Km/Toplam Hizmet [RentalContract GROUP BY MusteriId agregaları], Önem [`Sinif`],
   Bakiye) + "İşlem Tarihi" aralık filtresi + ayrı "Soyad" arama alanı + "Durum (Aktif/Pasif/Hepsi)"
   select.
10. `src/RentACar.Domain/Entities/Personel.cs` — 18 alanı ekle: `Adres`, `EvTelefonu`, `IsTelefonu`,
    `CepTel`, `Referans`, `Aciklama`, `SSinifi`, `SVerilisTarihi`, `SVerilisYeri`, `MailAdresi`,
    `DogumTarihi`, `DogumYeri`, `BabaAdi`, `AnaAdi`, `Il`, `Ilce`, `Mahalle`, `CiltNo`, `AileSiraNo`,
    `SiraNo`, `KanGrubu`, `GorevTanimi` (string?, "İç Ekip"/"Yönetici"/"Operasyon" — ComboBox
    seç-veya-yaz), `RacTabletNo`.
11. `src/RentACar.Infrastructure/Persistence/Configurations/CustomerConfigs.cs` — bu dosyanın
    içindeki `PersonelConfig` sınıfına (satır 101, `IEntityTypeConfiguration<Personel>`) 22 yeni
    kolon ekle (**dikkat:** dosya adı `CustomerConfigs.cs` ama `PersonelConfig`/`HukukDosyaConfig`/
    `AnketConfig`/`SikayetConfig` sınıfları da AYNI dosyada — tarihsel "CRM kümesi" dosyası, yanlış
    dosyaya bakma).
12. Migration: `dotnet ef migrations add AddPersonelDerinlik --project src/RentACar.Infrastructure
    --startup-project src/RentACar.Infrastructure`.
13. `src/RentACar.Application/Personnel/PersonelModels.cs` + `PersonelService.cs` — 22 alanı
    input/detail modeline ekle, map et.
14. `src/RentACar.Web/Components/Pages/Personnel/PersonelList.razor` — 22 yeni form alanı +
    arama kutusu (Ad/Soyad/Kod contains) + "Aktif" checkbox filtresi (`Personel.ListAsync()`'e
    filtre parametresi eklenir) + liste kolonlarına Cep Tel, Mail Adresi, TC Kimlik (maskeli —
    `***-**-1234`, PII gate mevcut Admin-only deseniyle), Rac Tablet No ekle.
15. `src/RentACar.Application/Personnel/IPersonelRepository.cs` +
    `src/RentACar.Infrastructure/Persistence/Repositories/PersonelRepository.cs` — arama/filtre
    metodu (`SearchAsync` benzeri, `CustomerRepository.SearchRowsAsync` deseniyle).

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/Customer.cs` — 39 alan
- `src/RentACar.Infrastructure/Persistence/Configurations/CustomerConfigs.cs` — `CustomerConfig`
  (satır 11)
- `src/RentACar.Application/Customers/{CustomerInput.cs,CustomerRow.cs,CustomerFilter.cs,CustomerService.cs}`
- `src/RentACar.Infrastructure/Persistence/Repositories/CustomerRepository.cs`
- `src/RentACar.Web/Components/Pages/Customers/{CustomerEdit.razor,CustomerList.razor}`
- (yeni) migration `AddCustomerFormDerinlik2`
- `src/RentACar.Domain/Entities/Personel.cs` — 22 alan
- `src/RentACar.Infrastructure/Persistence/Configurations/CustomerConfigs.cs` — `PersonelConfig`
  (satır 101, AYNI dosya)
- `src/RentACar.Application/Personnel/{PersonelModels.cs,PersonelService.cs,IPersonelRepository.cs}`
- `src/RentACar.Infrastructure/Persistence/Repositories/PersonelRepository.cs`
- `src/RentACar.Web/Components/Pages/Personnel/PersonelList.razor`
- (yeni) migration `AddPersonelDerinlik`

## Migration
Var — iki migration, ikisi de mevcut tenant-owned tablolara (`Customers`, `Personeller`) nullable
kolon ekliyor. **RLS bloğu gerekmez** — yeni tablo yok, mevcut tablolar zaten
`ENABLE`+`FORCE ROW LEVEL SECURITY` ile RLS'li.

## Test
- `CustomerDerinlik2Tests` (yeni — `CustomerDerinlikTests.cs` zaten var, 2026-06-30 turundan; isim
  çakışmasın): 39 yeni alan round-trip (bağımsız oracle — elle kurulan Customer nesnesiyle
  karşılaştır); `Sifre` set edilince DB'de **düz metin YOK**, yalnız hash kolonu dolu (assert:
  `SifreHash != girilenDeger` ve `BCrypt.Verify`/eşdeğeriyle doğrulanabilir).
  `CustomerFilter.Pasif=true` → yalnız `Aktif=false` kayıtlar dönüyor (3 aktif + 2 pasif elle
  kurulur, beklenen sayı 2, testte sabit).
- `PersonelTests` (mevcut dosyaya ekleme): 22 yeni alan round-trip; arama testi — 3 personel (2
  farklı ad, 1 pasif) elle oluşturulur, "Aktif" filtresiyle arama yalnız 2 aktif kaydı döndürüyor mu
  (beklenen sayı testte sabit).
- Bakiye JOIN testi: 1 cari + 2 `AccountLedgerEntry` (Borç 500 + Alacak 200) elle kurulur,
  `SearchRowsAsync` sonucunda `Bakiye == 300` beklenir (hesap servis kodundan değil, elle toplanan
  sabitten).

## Exit
- [ ] Cari formunda 39 yeni alan + `Adres` görünüyor, kaydediliyor
- [ ] `Sifre` alanı hash'lenmiş saklanıyor, düz metin kolonu yok
- [ ] Cari liste/arama ekranlarında yeni kolonlar + Durum/Soyad/İşlem Tarihi filtresi çalışıyor
- [ ] Personel formunda 22 yeni alan (`GorevTanimi` dahil) görünüyor, kaydediliyor
- [ ] Personel listesinde arama kutusu + Aktif filtresi + 4 yeni kolon çalışıyor
- [ ] Tam suite yeşil

## Notlar
`Customer.Sifre` portal şifresi — CLAUDE.md §4 PII/KVKK bölümündeki TC/ehliyet/pasaport şifreleme
deseniyle AYNI ciddiyette ele alınmalı ama farklı mekanizma: bu bir **hash** (tek yönlü, geri
çözülemez), `TcKimlikEnc` gibi bir **encrypt** (geri çözülebilir, `ISecretProtector`) değil — ikisini
karıştırma. `CustomerConfigs.cs` dosyası tarihsel olarak "Cari/Personel/Hukuk/Anket/Sikayet" config
sınıflarının hepsini barındırıyor — bu fazda hem `CustomerConfig` hem `PersonelConfig` AYNI dosyada
düzenlenecek, iki ayrı dosya değil.
