# FAZ-41 — Hukuk Dosyası & CRM Segment Derinliği

| | |
|---|---|
| **Desen** | D1+D3 (Hukuk alan+liste) + D4 (CRM segment rapor filtresi) |
| **Efor** | 2,25 gün |
| **Bağımlılık** | yok (Tahsilat/Kalan'ın defter-postlama biçimi ayrı bir Opus kararına bağlı — bkz. Notlar; bu faz o karardan ÖNCE de yapılabilir) |
| **Kapsanan canlı ekran** | `hukuk_birimi.aspx`, `hukuk_islem_listesi.aspx`, `musteri_crm.aspx` |
| **Risk** | orta — Hukuk'ta yeni `Tahsilat` alanı bilgi-amaçlı eklenir (deftere YAZMAZ), ama isim
  kullanıcıya "gerçek tahsilat" hissi verebilir; formda/raporda "bilgi amaçlı, deftere işlemez" notu
  ZORUNLU (aksi halde muhasebe bu sayıyı gerçek tahsilat sanıp yanlış mutabakat yapar) |

## Amaç
Hukuk dosyasına canlıdaki avukat iletişim alanları + kısmi tahsilat/kalan bakiye bilgisini,
hukuk işlem listesine arama/filtre+export'u, CRM segment raporuna tarih/kiralama-adedi/kaynak/şube
filtresi ve eksik projeksiyon alanlarını ekleyerek kullanıcı `/hukuk` ve `/crm` ekranlarında canlı
paritesine yakın çalışabilir hale gelir.

## Neden (kanıt)
- `src/RentACar.Domain/Entities/HukukDosya.cs` şu an `DosyaNo, CariId, Tur, Avukat, Tutar, Durum,
  Tarih, Aciklama, Aktif` taşıyor. Plandaki `FaturaNoTemp`, `Avukat2Ad`, `Avukat2Tel`, `Avukat2Mail`,
  `AvukatTel`, `AvukatMail` **hiçbiri yok** (grep doğrulandı — 1. avukatın telefon/mail'i de eksik).
  Canlıda **Tahsilat** (kısmi ödeme) ve türetilmiş **Kalan** var; bizde `Tutar` tek alan (kod
  yorumunda zaten "bilgilendirme amaçlı, deftere postlamaz" — bkz. entity XML doc benzeri not).
- `src/RentACar.Web/Components/Pages/Legal/HukukList.razor`'ın liste bölümünde filtre yok (Ad Soyad/
  Tarih aralığı/Fatura No/Dosya No arama eksik); Müşteri adı `CariId` var ama JOIN edilip
  gösterilmiyor; `/hukuk` için export ucu yok (`ListExportCatalog`'da `HukukDosyalari` tablosu yok).
- `src/RentACar.Application/Reporting/ReportDtos.cs:99` — `MusteriSegmentRow(Guid CariId, string Ad,
  int KiraSayisi, decimal ToplamCiro, DateTimeOffset? SonIslem, string Segment)` — Müşteri Mail/Tel,
  Ortalama Kira Bedeli, Ortalama KM, Doğ.Tar, İlk Kira Zamanı, Hizmet Bedeli **projeksiyonda yok**.
  `GetMusteriSegmentRowsAsync(CancellationToken ct)` parametresiz — Tarih/Kiralama Adedi/Rez.
  Kaynağı/Çıkış Şube filtresi **yok**; `CrmAnaliz.razor`'daki tablo (satır 13) filtre barı içermiyor.

## Yapılacaklar
1. `src/RentACar.Domain/Entities/HukukDosya.cs` — `FaturaNoTemp` (string?), `AvukatTel` (string?),
   `AvukatMail` (string?), `Avukat2Ad` (string?), `Avukat2Tel` (string?), `Avukat2Mail` (string?)
   ekle. **Ayrıca** `Tahsilat` (decimal?, varsayılan 0) ekle — XML doc'ta AÇIKÇA yaz:
   "BİLGİ AMAÇLI, `Tutar` gibi deftere/cari bakiyeye YAZMAZ; gerçek tahsilat cari üzerinden
   `AccountLedgerEntry` ile ayrıca girilir". `Kalan` alan DEĞİL, hesaplanan property:
   `public decimal Kalan => Tutar - (Tahsilat ?? 0);`.
2. `src/RentACar.Infrastructure/Persistence/Configurations/CustomerConfigs.cs` — `HukukDosyaConfig`
   sınıfına (satır 122) 7 yeni kolon ekle (`Kalan` map edilmez, hesaplanan property).
3. Migration: `dotnet ef migrations add AddHukukDosyaDerinlik --project src/RentACar.Infrastructure
   --startup-project src/RentACar.Infrastructure`.
4. `src/RentACar.Application/Legal/HukukDosyaInput.cs` + `HukukDosyaService.cs` — 7 alanı
   create/update input'una ekle, map et.
5. `src/RentACar.Web/Components/Pages/Legal/HukukList.razor` — form alanlarına 6 iletişim alanı +
   `Tahsilat` input (yanında salt-okunur `Kalan = Tutar - Tahsilat` gösterimi, "bilgi amaçlı, deftere
   işlemez" uyarı metniyle) ekle. Liste bölümüne filtre barı: Ad Soyad (`CariId` JOIN), Tarih
   aralığı, Fatura No (`FaturaNoTemp`), Dosya No (contains). Liste kolonuna Müşteri adı ekle (JOIN
   edilip gösterilmiyor, veri zaten var).
6. `src/RentACar.Application/Legal/IHukukDosyaRepository.cs` +
   `src/RentACar.Infrastructure/Persistence/Repositories/HukukDosyaRepository.cs` — arama metodunu
   ekle (Ad Soyad/Tarih/Fatura No/Dosya No filtresi, Customer JOIN).
7. `src/RentACar.Web/Reports/ListExportCatalog.cs` — yeni `HukukDosyalari(IReadOnlyList<HukukDosya>)`
   export tablosu (Dosya No, Müşteri, Tür, Avukat×2 iletişim, Tutar, Tahsilat, Kalan, Durum, Tarih).
8. `src/RentACar.Application/Reporting/ReportDtos.cs:99` — `MusteriSegmentRow`'a `Mail`, `Tel`,
   `OrtalamaKiraBedeli` (`ToplamCiro / KiraSayisi`, `KiraSayisi==0` ise 0), `OrtalamaKm`
   (RentalContract `DonusKm - CikisKm` ortalaması), `DogumTarihi`, `IlkKiraZamani` (MIN BasTar),
   `HizmetBedeli` (RentalAddOn toplamı, kira dışı) alanlarını ekle (record'a yeni pozisyonel alanlar).
9. `src/RentACar.Application/Reporting/IReportRepository.cs` —
   `GetMusteriSegmentRowsAsync`'e parametre ekle: `TarihBas`/`TarihBit` (DateTimeOffset?, kira BasTar
   aralığı), `MinKiraSayisi` (int?), `RezKaynak` (string?), `CikisSubeId` (Guid?).
10. `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs` —
    `GetMusteriSegmentRowsAsync` sorgusunu 8-9'daki alan/filtrelerle genişlet (`CikisSubeId` filtresi
    `BranchScope` altyapısıyla — Operatör zaten kendi şubesine kısıtlı, admin tümünü görür).
11. `src/RentACar.Web/Components/Pages/Crm/CrmAnaliz.razor` — segment tablosuna filtre barı (Tarih
    Listesi Baş./Bit., Kiralama Adedi eşiği, Rez. Kaynağı, Çıkış Şube) + yeni kolonlar (Mail, Tel,
    Ortalama Kira Bedeli, Ortalama KM, Doğ.Tar, İlk Kira Zamanı, Hizmet Bedeli).

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/HukukDosya.cs` — 7 alan + `Kalan` hesaplanan property
- `src/RentACar.Infrastructure/Persistence/Configurations/CustomerConfigs.cs` — `HukukDosyaConfig`
  (satır 122)
- `src/RentACar.Application/Legal/{HukukDosyaInput.cs,HukukDosyaService.cs,IHukukDosyaRepository.cs}`
- `src/RentACar.Infrastructure/Persistence/Repositories/HukukDosyaRepository.cs`
- `src/RentACar.Web/Components/Pages/Legal/HukukList.razor`
- `src/RentACar.Web/Reports/ListExportCatalog.cs`
- (yeni) migration `AddHukukDosyaDerinlik`
- `src/RentACar.Application/Reporting/{ReportDtos.cs,IReportRepository.cs}`
- `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs`
- `src/RentACar.Web/Components/Pages/Crm/CrmAnaliz.razor`

## Migration
Var — `HukukDosya`'ya (mevcut tenant-owned tablo, RLS zaten aktif) 7 nullable kolon. **RLS bloğu
gerekmez** — yeni tablo yok. `musteri_crm.aspx` tarafı için migration **yok** (salt rapor/projeksiyon
değişikliği, entity/tablo değişmiyor).

## Test
- `HukukTests` (mevcut dosyaya ekleme): 7 yeni alan round-trip. `Tahsilat`/`Kalan` testi — bağımsız
  oracle: `Tutar=1000, Tahsilat=300` elle kurulur; assert `Kalan == 700m` (sabit sayı, hesap formülü
  testte tekrar üretilmez — `Tutar-Tahsilat` yalnız entity'de bir kez yazılı). **Ayrıca:** `Tahsilat`
  set edildiğinde `AccountLedgerEntry` tablosunda YENİ satır oluşmadığını doğrulayan regresyon testi
  (deftere yazmıyor garantisi — bu satır para-adversarial içermese de kalıcı regresyon kilididir).
- Hukuk arama testi: 3 dosya (2 farklı cari, 1 farklı fatura no) elle kurulur; Ad Soyad filtresiyle
  arama yalnız o cariye ait kaydı döndürüyor mu (beklenen sayı testte sabit).
- `CustomerCrmTests` (mevcut dosyaya ekleme): `MusteriSegmentRow` yeni alanları — 2 müşteri + 3 kira
  (elle: Cari A 2 kira 100+200=300 ciro, Cari B 1 kira 500 ciro) kurulur, `OrtalamaKiraBedeli`
  A=150, B=500 beklenir (bağımsız oracle, sabit). Filtre testi: `MinKiraSayisi=2` verilince yalnız
  Cari A dönüyor mu.

## Exit
- [ ] Hukuk formunda 6 iletişim alanı + Tahsilat/Kalan görünüyor, kaydediliyor
- [ ] `Tahsilat` deftere YAZMIYOR (regresyon testiyle doğrulandı)
- [ ] Hukuk liste arama/filtre + Müşteri adı kolonu + export çalışıyor
- [ ] CRM segment raporunda 4 filtre + 6 yeni kolon çalışıyor
- [ ] Tam suite yeşil

## Notlar
Canlıda Tahsilat gerçek cari tahsilatına bağlıysa (Opus'un vereceği "(b) seçeneği") bu faz **yeniden
açılır**: `Tahsilat` alanı silinmez ama davranışı değişir (artık `AccountLedgerEntry` postlar) — o an
faz D5'e yükselir ve CLAUDE.md §3.5 zorunlu adversarial inceleme gerekir (Σ Borç=Σ Alacak, idempotency
senaryosu dahil). Bu faz şu an (a) seçeneğini (bilgi amaçlı, deftere yazmaz) uyguluyor — düşük risk,
mevcut "postlamaz" tasarımıyla tutarlı, Opus kararı beklemeden yapılabilir.
