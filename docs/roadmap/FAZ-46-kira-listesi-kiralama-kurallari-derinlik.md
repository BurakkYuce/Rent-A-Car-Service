# FAZ-46 — Kira Listesi & Kiralama Kuralları Derinliği

| | |
|---|---|
| **Desen** | D3 (kira listesi kolon+filtre) + D2 (kural derinliği, ×2 ekran) |
| **Efor** | 4,5 gün (= kira_listesi 2,0g + "Kiralama kuralları derinliği PR" grubu 2,5g
  [kiralama_kurallari 1,5g + kiralama_kurallari_basic 0g + kiralama_sartlari 1,0g] — plan eforu
  aynen taşındı, toplama düzeltme yok) |
| **Bağımlılık** | yok (kira_listesi'nde Vade/Faturalanan kolon **kaynağı** ve kiralama_kurallari'nde
  banka-matrisi formülü ayrı Opus kararına bağlı — bu faz o kararlardan ÖNCE de yapılabilir, bkz. Notlar) |
| **Kapsanan canlı ekran** | `kira_listesi.aspx`, `kiralama_kurallari.aspx`,
  `kiralama_kurallari_basic.aspx`, `kiralama_sartlari.aspx` |
| **Risk** | orta — kira listesi tarafı salt-okur kolon ekleme (düşük); kiralama kuralları tarafı
  yeni alanlar servis mantığına henüz bağlanmıyor (yalnız veri modeli — D2'nin "kuralın tüketildiği
  yer" kısmı Opus'un banka-matrisi kararına bağlı, bu fazda YOK) |

## Amaç
Kira listesine canlıdaki Kaynak/Provizyon/Depozito/Komisyon/Vade/Onay Kodu gibi ~20 kolonu ve
Tarih Listesi/Ofis Durum/Sahip Grup/Rez Kaynak/Araç Grubu/Personel filtrelerini; kiralama kuralı
tanımına talep-tarihi aralığı + promosyon/kupon/hesaplama-tipi alanlarını + haftanın-günü bazlı
min-gün kısıtını ekleyerek kullanıcı `/kiralar` ve `/kira-kurallari` ekranlarında canlı paritesine
yakın arama+kural tanımlayabilir hale gelir.

## Neden (kanıt)
- `src/RentACar.Application/Bookings/RentalRow.cs` şu an `Id, SozlesmeNo, MusteriId, MusteriAd,
  Doviz, Plaka, BasTar, BitTar, Gun, Tutar, Bakiye, Durum, Faturali` taşıyor. `RentalContract.cs`'de
  ZATEN VAR olan `Kaynak` (satır 104), `Provizyon`/`Depozito`/`KomisyonOran`/`KomisyonTutar` (satır
  93-96), `OnayKodu` (127), `ProjeAdi` (129), `AssistFirma`/`OzelSoforBilgisi` (135-136),
  `HediyeGun`/`FaturalananGun` (74-77) **`RentalRow`'a yansımıyor** (grep doğrulandı — projeksiyonda
  yok). **Plan-vs-repo düzeltmesi:** planın "Doviz" maddesi zaten `RentalRow`'da mevcut (satır 8),
  tekrar eklenmez. `VadeTar` entity'de de **yok** (grep sıfır isabet) — bu tek gerçek yeni alan,
  RentalContract'a eklenip sonra projeksiyona taşınacak.
- `src/RentACar.Application/Bookings/RentalFilter.cs` şu an `Query, Durum, Faturali, BaslangicMin/Max,
  Ofis, Sube, Kapsam` taşıyor — `Tarih_Listesi` türü seçici (Başlangıç/Bitiş/İşlem/Vade), `Ofis_Durum`
  (Çıkış/Dönüş — `CikisOfisi` (satır 31) vs `DonusOfisi` (satır 36), ikisi de entity'de zaten var),
  `SahipGrup` (`Vehicle.AracSahibi` satır 81'de zaten var), `RezKaynak`, `AracGrubu` (`Vehicle.Grup`
  zaten var), `PersonelTip`/`PersonelListesi` **yok**.
- `src/RentACar.Domain/Entities/RentalRule.cs` — plandaki `TalepBas`/`TalepBit`, `PromosyonTuru`,
  `KuponGecerlilik`, `HesaplamaTipi`, `HizliIslem` **hiçbiri yok** (mevcut `GecerlilikBas`/
  `GecerlilikBit` (satır 55-56) KURAL geçerlilik aralığı, TALEP tarihi aralığından AYRI kavram, ikisi
  kalır). `RentalRuleList.razor`'ın liste `<thead>`'i (satır 47) `Kod/Ad/Kanal/Min-Max/İskonto/
  Kampanya/Durum/İşlem` — Şube (form'da satır 25/72'de zaten var) ve Araç Grubu Kodu (form'da satır
  73'te zaten var) **grid'de yok**.
- `kiralama_kurallari_basic.aspx`: bizde tek birleşik `/kira-kurallari` ekranı zaten bu ekranın
  kapsadığı her şeyi karşılıyor — ek eylem gerekmiyor, yukarıdaki `TalepBas`/`TalepBit` ile otomatik
  kapanıyor.
- `kiralama_sartlari.aspx`: `RentalRule`'da `HaftaGunKisiti` (7 günün her biri için bağımsız min-gün
  kısıtı) **yok**.

## Yapılacaklar
1. `src/RentACar.Domain/Entities/RentalContract.cs` — `VadeTar` (DateTimeOffset?) ekle.
2. `src/RentACar.Infrastructure/Persistence/Configurations/` — `RentalContract` config sınıfına
   `VadeTar` kolonu ekle (dosyayı bul: `BookingConfigs.cs` benzeri bir dosya — aç ve doğrula).
3. Migration: `dotnet ef migrations add AddRentalVadeTarihi --project src/RentACar.Infrastructure
   --startup-project src/RentACar.Infrastructure`.
4. `src/RentACar.Application/Bookings/RentalRow.cs` — `Kaynak`, `Provizyon`, `Depozito`,
   `KomisyonOran`, `KomisyonTutar`, `VadeTar`, `OnayKodu`, `ProjeAdi`, `AssistFirma`,
   `OzelSoforBilgisi`, `HediyeGun`, `FaturalananGun` alanlarını ekle (`Doviz` zaten var, EKLEME).
5. `src/RentACar.Application/Bookings/RentalFilter.cs` — `TarihListesiTuru` (enum
   `Baslangic`/`Bitis`/`Islem`/`Vade`, yeni `src/RentACar.Domain/Enums/TarihListesiTuru.cs`),
   `OfisDurum` (enum `Cikis`/`Donus`), `SahipGrup` (string?), `RezKaynak` (string?), `AracGrubu`
   (string?), `PersonelTip`/`PersonelId` (string?/Guid?) ekle.
6. `src/RentACar.Application/Bookings/RentalService.cs` (veya `IBookingRepository`
   implementasyonundaki `SearchRentalRowsAsync`) — 5'teki filtreleri sorguya bağla: `TarihListesiTuru`
   seçilen alana göre `BaslangicMin/Max` hangi kolona uygulanacağını belirler (Başlangıç→`BasTar`,
   Bitiş→`BitTar`, İşlem→`CreatedAtUtc`, Vade→`VadeTar`); `OfisDurum` `CikisOfisi`/`DonusOfisi`
   arasında seçim yapar; `SahipGrup`/`AracGrubu` Vehicle JOIN; `RezKaynak` Reservation/Kaynak JOIN.
7. `src/RentACar.Web/Components/Pages/Bookings/RentalList.razor` — grid'e 11 yeni kolon (Kaynak,
   Provizyon, Depozito, Komisyon Oran/Tutar, Vade Tar, Onay Kodu, Proje Adı, Assist Firma, Özel Şoför
   Bilgisi, Hediye Gün/Faturalanan Gün) + filtre barına Tarih Listesi türü seçici, Ofis Durum,
   Sahip Grup, Rez Kaynak, Araç Grubu, Personel Tip/Listesi ekle.
8. `src/RentACar.Domain/Entities/RentalRule.cs` — `TalepBas`/`TalepBit` (DateTimeOffset?),
   `PromosyonTuru` (enum `Coklu`/`Tek`, yeni `src/RentACar.Domain/Enums/PromosyonTuru.cs`),
   `KuponGecerlilik` (enum `Hepsi`/`SadeceIlkBedel`), `HesaplamaTipi` (enum `Oran`/`Serbest`),
   `HizliIslem` (bool, default false) ekle. **Ayrıca** `HaftaGunKisiti` (`int[]?` veya `string?`
   virgülle ayrılmış 0-6 gün listesi — basit modelleme, plan child-tablo alternatifini de belirtiyor
   ama 7'den fazla farklı min-gün ihtiyacı yoksa dizi/JSON kolon yeterli; implementasyon anında karar
   verilir, JSON kolon tercih edilirse migration'da `jsonb` tipi kullanılır, RLS'e etkisi yok).
9. `src/RentACar.Infrastructure/Persistence/Configurations/PricingConfigs.cs` (`RentalRule` config
   sınıfını içeren gerçek dosyayı aç ve doğrula) — 6 yeni kolon ekle.
10. Migration: `dotnet ef migrations add AddRentalRuleTalepPromosyonHaftaGun --project
    src/RentACar.Infrastructure --startup-project src/RentACar.Infrastructure`.
11. `src/RentACar.Application/RentalRules/RentalRuleInput.cs` — 6 alanı ekle.
12. `src/RentACar.Web/Components/Pages/RentalRules/RentalRuleList.razor` — forma 6 yeni alan
    (`HaftaGunKisiti` için 7 checkbox — Pzt..Paz) ekle; liste `<thead>`'ine (satır 47) **Şube Adı** +
    **Araç Grubu** kolonlarını ekle (form alanı zaten var, tek satır grid değişikliği).

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/RentalContract.cs` — `VadeTar`
- `src/RentACar.Application/Bookings/{RentalRow.cs,RentalFilter.cs,RentalService.cs}`
- `src/RentACar.Web/Components/Pages/Bookings/RentalList.razor`
- (yeni) migration `AddRentalVadeTarihi`
- (yeni) `src/RentACar.Domain/Enums/{TarihListesiTuru.cs,PromosyonTuru.cs}` (+ gerekirse
  `KuponGecerlilik.cs`/`HesaplamaTipi.cs`)
- `src/RentACar.Domain/Entities/RentalRule.cs` — 6 alan
- `src/RentACar.Infrastructure/Persistence/Configurations/PricingConfigs.cs` — `RentalRule` config
- `src/RentACar.Application/RentalRules/RentalRuleInput.cs`
- `src/RentACar.Web/Components/Pages/RentalRules/RentalRuleList.razor`
- (yeni) migration `AddRentalRuleTalepPromosyonHaftaGun`

## Migration
Var — iki migration: (1) `RentalContract`'a 1 kolon, (2) `RentalRule`'a 6 kolon. **RLS bloğu
gerekmez** — ikisi de mevcut tenant-owned tablolara nullable kolon, yeni tablo yok.

## Test
- `RentalListTests`/`RentalRowTests` (mevcut varsa genişlet): 11 yeni `RentalRow` alanı round-trip —
  1 `RentalContract` elle kurulur (Kaynak="Web", Provizyon=500, ...), `SearchRentalRowsAsync`
  sonucunda aynı değerler beklenir (bağımsız oracle, sabit).
- Filtre testi: 3 kira (2 farklı `CikisOfisi`, 1 farklı `DonusOfisi`) elle kurulur; `OfisDurum=Cikis`
  + `Ofis="A Şubesi"` filtresiyle arama yalnız o kaydı döndürüyor mu (beklenen sayı testte sabit).
  `TarihListesiTuru=Vade` seçilince `BaslangicMin/Max` aralığının `VadeTar` kolonuna uygulandığını
  doğrulayan ayrı senaryo (2 kayıt, biri aralık içi biri dışı `VadeTar`, yalnız 1 dönmeli).
- `RentalRuleTests` (mevcut dosyaya ekleme): 6 yeni alan round-trip. Liste testi — form Şube/Araç
  Grubu doldurulmuş 1 kural elle kurulur, liste sorgusunda `SubeAdi`/`AracGrupKod` kolonları dolu
  dönüyor mu (mevcut alanların grid'e taşınması, hesap değil).

## Exit
- [ ] Kira listesi 11 yeni kolon + 6 yeni filtre (Tarih Listesi/Ofis Durum/Sahip Grup/Rez Kaynak/
      Araç Grubu/Personel) çalışıyor
- [ ] `VadeTar` formda görünüyor, kaydediliyor, listede filtrelenebiliyor
- [ ] Kiralama kuralı formunda 6 yeni alan + haftanın-günü kısıtı çalışıyor
- [ ] Kiralama kuralı listesinde Şube Adı + Araç Grubu kolonu görünüyor
- [ ] Tam suite yeşil

## Notlar
**Faz birleştirme notu:** plan bu 4 ekranı iki ayrı grup olarak listeliyordu (`kira_listesi.aspx`
tek başına 2 gün; "Kiralama kuralları derinliği PR" grubu `kiralama_kurallari`+`basic`+`sartlari`
2,5 gün). Numaralandırma aralığı (FAZ-40..49, 10 slot) içinde D7'nin 4 zorunlu ayrı fazı (FAZ-42..45)
ve `kiralama.aspx`'in tek başına 4 günlük fazı (FAZ-47) sabit olduğundan, kalan 6 küçük/orta grup 6
faz slotuna sıkıştırıldı; bu ikisi dosya bakımından örtüşmese de (RentalRow/RentalList vs
RentalRule/RentalRuleList) aynı "kira/kiralama" üst-alanında olduklarından birlikte tek faz dosyasına
alındı — istenirse implementasyon sırasında **iki ayrı commit** olarak bölünüp aynı dalda sırayla
gönderilebilir (CLAUDE.md §3.4 "küçük, gözden geçirilebilir PR'lar" ilkesi — bu faz iki bağımsız
commit'e ayrılabilir, tek faz dosyası olması iki PR olmasını engellemez).

`VadeTar`/`Faturalanan Gün` kolonlarının **dönemsel faturalama** (`FaturaDonemi`) ile ilişkisi (hangi
kaynaktan — Invoice mi, FaturaDonemi planı mı — okunacağı) ve kiralama kuralındaki 24-banka-kodu
taksit/markup matrisi (`HesaplamaTuru`) bu fazın kapsamı DIŞINDA — Opus kararına bağlı, ayrı bir
takip fazı gerektirir (bu faz dosyasında efor YOK, yalnız not).
