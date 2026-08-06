# FAZ-76 — Rapor Filtre Derinliği Serisi (5 Rapor Sayfası)

| | |
|---|---|
| **Desen** | D3 (filtre/kolon derinliği; `bos_arac_raporu` D4-mevcut rapor üzerine D3-nitelikli ek) |
| **Efor** | 4 gün (`bos_arac_raporu.aspx` 1g + `bos_km_detay.aspx` 0,5g + `gunraporu.aspx` 0,5g +
  `periyodik_servis_raporu.aspx` 1g + `rezervasyon_kaynak_raporu.aspx` 1g) |
| **Bağımlılık** | yok (önce canlı-tarama doğrulaması ÖNERİLİR — `bos_km_detay` için, aşağıda) |
| **Kapsanan canlı ekran** | `bos_arac_raporu.aspx`, `bos_km_detay.aspx`, `gunraporu.aspx`,
  `periyodik_servis_raporu.aspx`, `rezervasyon_kaynak_raporu.aspx` |
| **Risk** | orta — `periyodik_servis_raporu` bölümü `OrtakSorgular.PeriyodikServisAsync`'i
  değiştiriyor; bu sorgu `FiloBildirimUretici` (bakım-km bildirimi) ile PAYLAŞILAN (regresyon riski) |

## Amaç
5 mevcut rapor sayfası (bugün filtresiz veya zayıf filtreli) şube/tarih/durum filtresi + eksik JOIN
kolonlarıyla genişler — hepsi AYNI desen: mevcut tek rapor sayfasına filtre formu + kolon eklemek.

## Neden (kanıt) — her alt-bölüm için doğrulanmış bugünkü durum
- **bos_arac_raporu** → `/raporlar/arac-durum-takip` (`AracDurumTakip.razor`). Servis:
  `ReportService.GetAracDurumTakipAsync(from?, to?, ct)` → repo `GetAracDurumTakipRowsAsync`.
  Bugünkü filtre: `from`/`to` VAR (varsayılan son 30 gün); **Ofis (şube) filtresi YOK**. Kolonlar:
  Gün/Toplam/Dolu/Bakım/Boş (5 kolon) — Toplam Baf kolonu YOK. **Ek bulgu (bonus-fix fırsatı):**
  `ReportRepository.cs` bu metodun `ServiceRecords` sorgusunda `Durum != ServisDurum.Iptal` filtresi
  EKSİK (diğer benzer metodlarda bu filtre var) — iptal edilen servis kayıtları da "Bakım" günü
  olarak sayılıyor; bu fazda düzeltilmesi önerilir (küçük, aynı satırda).
- **bos_km_detay** → `/raporlar/km-detay` (`KmDetay.razor`). Kolonlar: Sözleşme/Plaka/Çıkış/Dönüş/
  Katedilen/Limit/Fazla KM/Fazla Bedel (8 kolon). Vehicle JOIN **VAR ama SADECE Plaka için**
  (`ReportRepository.cs` `db.Vehicles.Select(Id,Plaka)` bellek-içi join) — Marka/Vites/Yakıt JOIN
  YOK; Rental (BasTar/BitTar/İşlem Türü) JOIN de YOK (DTO'da tarih alanı hiç yok).
  **UYARI (plan dosyasından taşındı):** canlının gerçek amacı (boşta-geçen-sürede km/fraud kontrolü)
  bizim ekranın konusuyla (kira KM aşım faturalama satırı) FARKLI OLABİLİR — bu PR öncesi bir
  canlı-tarama doğrulaması ÖNERİLİR; farklıysa bu madde D7 (yeni dikey) olarak yeniden açılmalı.
- **gunraporu** → `/raporlar/gunluk` (`GunlukFaaliyet.razor`). Servis:
  `GetGunlukFaaliyetAsync(gun, ct)`. 8 kart (YeniRezervasyon/YeniKira/Cikis/Donus/TahsilatAdet/
  TahsilatTutar/FaturaAdet/FaturaTutar) — **hepsi mevcut**. Islem_Sube (şube) filtresi **YOK**
  (repo sorgusunda hiçbir alt-sorguda Sube filtresi yok, doğrulandı).
- **periyodik_servis_raporu** → `/raporlar/periyodik-servis` (`PeriyodikServis.razor`). **SIFIR
  filtre doğrulandı** — sayfada `<form>`/query param yok. `OrtakSorgular.PeriyodikServisAsync(db,
  ct)` (parametresiz) hem `ReportRepository.cs` hem `FiloBildirimUretici.cs` tarafından ÇAĞRILIYOR
  (paylaşılan sorgu, O12a deseni — kod yorumunda "tek doğruluk kaynağı" olarak açıkça yazılı).
- **rezervasyon_kaynak_raporu** → `/raporlar/rezervasyon-kaynak` (`RezervasyonKaynak.razor`).
  `GetRezervasyonKaynakAsync(from?, to?, ct)` → repo `GetRezervasyonKaynakRowsAsync`. Kolonlar:
  Kaynak/Adet/Toplam Gün/Toplam Ciro (4 kolon). Ofis+Gruplar filtresi YOK; `Reservation.cs`'te
  Döviz/Kur alanı **YOK** (grep doğrulandı — tüm tutarlar düz `decimal`, kur bilgisi taşımıyor).
  **Ek bulgu (KRİTİK, bonus-fix fırsatı):** repo sorgusu (`ReportRepository.cs:179-195`) `Durum`
  filtresi UYGULAMIYOR — `ReservationStatus.Iptal` olan rezervasyonlar da Adet/Toplam Gün/Toplam
  Ciro'ya DAHİL ediliyor. Bu bir veri-doğruluğu hatasıdır; bu fazda Tarih_Listesi filtresiyle
  BİRLİKTE varsayılan `Durum != Iptal` filtresi eklenmesi önerilir (kullanıcı isterse "İptalleri
  Dahil Et" ile açabilir).

## Yapılacaklar
1. **bos_arac_raporu**: `ReportService.GetAracDurumTakipAsync` imzasına opsiyonel `string? sube`
   eklenir; repo sorgusu `Vehicle.SubeId` üzerinden filtrelenir. `AracDurumTakipRow` DTO'suna
   `ToplamBaf (int)` alanı eklenir (`Baf.Durum==Acik` sayımı, `Vehicle.SubeId` ile aynı filtreye
   bağlı). `AracDurumTakip.razor`'a Ofis (şube) dropdown + Toplam Baf kolonu eklenir. Bonus-fix:
   `ServiceRecords` sorgusuna `Durum != ServisDurum.Iptal` filtresi eklenir.
2. **bos_km_detay**: (canlı-tarama doğrulaması SONRASI, bu adım koşullu) `KmDetayRow` DTO'suna
   `Marka, Vites, Yakit` (Vehicle JOIN genişletilir) + `BasTar, BitTar` (Rental JOIN) eklenir;
   `KmDetay.razor` kolon listesi genişler.
3. **gunraporu**: `GetGunlukFaaliyetAsync(gun, sube?, ct)` — Rentals/Reservations alt-sorgularına
   `SubeId` filtresi eklenir (`BranchScope.Effective` ile aynı desen — CikisOfisi→SubeId zinciri,
   FAZ-C ile hizalı). `GunlukFaaliyet.razor`'a Islem_Sube dropdown eklenir.
4. **periyodik_servis_raporu**: `OrtakSorgular.PeriyodikServisAsync(db, ct, plaka?, sube?, durum?,
   uyariEsigi?)` — TÜM parametreler OPSİYONEL, varsayılan `null`/mevcut sabit eşik (geriye uyum,
   `FiloBildirimUretici`'nin parametresiz çağrısı DEĞİŞMEDEN çalışır). `PeriyodikServis.razor`'a
   plaka arama + Ofis + Durum (aktif/pasif) + Uyarı eşiği seçim formu eklenir + Marka/Tipi/Model/
   Yakıt Türü/Vites/Şube/İşlem Tarihi/İşlem KM kolonları.
5. **rezervasyon_kaynak_raporu**: `Reservation.cs`'e additive `Doviz (string?)` kolonu.
   `GetRezervasyonKaynakAsync(from?, to?, sube?, grup?, tarihTipi?, dahilIptal=false, ct)` — Ofis+
   Gruplar filtresi + Tarih_Listesi (Kayıt/Çıkış/Dönüş tarihine göre seçim) + varsayılan
   `Durum != Iptal` (madde "Ek bulgu"daki düzeltme, `dahilIptal=true` ile açılabilir) + `Doviz`
   bazında GROUP BY genişletilir (EK bilgi satırı — **Genel Toplam formülü DEĞİŞMEZ**, hâlâ tek baz
   para/₺).
6. `RezervasyonKaynak.razor` — Ofis/Gruplar/Tarih_Listesi/Döviz-kırılım UI'ı + "İptalleri Dahil Et"
   checkbox (varsayılan kapalı).

## Dokunulacak dosyalar
- `src/RentACar.Application/Reporting/ReportDtos.cs`
- `src/RentACar.Application/Reporting/ReportService.cs`
- `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs`
- `src/RentACar.Infrastructure/Persistence/OrtakSorgular.cs` (`PeriyodikServisAsync` imza genişletme)
- `src/RentACar.Domain/Entities/Reservation.cs` (additive `Doviz`)
- `src/RentACar.Web/Components/Pages/Reports/AracDurumTakip.razor`
- `src/RentACar.Web/Components/Pages/Reports/KmDetay.razor`
- `src/RentACar.Web/Components/Pages/Reports/GunlukFaaliyet.razor`
- `src/RentACar.Web/Components/Pages/Reports/PeriyodikServis.razor`
- `src/RentACar.Web/Components/Pages/Reports/RezervasyonKaynak.razor`

## Migration
`Reservation`'a additive `Doviz (string?)` kolonu — tablo zaten RLS'li, RLS bloğu gerektirmez.
Diğer 4 alt-bölüm migration gerektirmez (yalnız sorgu/DTO/UI değişikliği).

## Test
- `ReportServiceTests` (Ofis filtresi): elle kurulan 2 şubeden araçlar → şube filtreli sorgu doğru
  alt-kümeyi (sabit sayılarla) döndürür.
- **Regresyon testi (zorunlu, periyodik servis):** `FiloBildirimUretici.RunAsync` çağrısı bu fazdan
  ÖNCE ve SONRA AYNI bildirim sayısını üretir (elle kurulan sabit senaryo — `OrtakSorgular.
  PeriyodikServisAsync`'in parametresiz çağrısı davranışsal olarak değişmediğinin kanıtı).
- `rezervasyon_kaynak_raporu` testi: elle kurulan 3 rezervasyon (2 `Aktif`, 1 `Iptal`) → varsayılan
  çağrıda Toplam Ciro SADECE 2 aktifin toplamı (sabit tutar, test verisinden — iptal HARİÇ); `dahilIptal
  =true` ile 3'ünün toplamı döner. Bu, "Ek bulgu"daki veri-doğruluğu hatasının kapandığının kanıtı.
- `bos_arac_raporu` bonus-fix testi: `Durum=Iptal` olan bir `ServiceRecord` "Bakım" gün sayısına
  DAHİL EDİLMİYOR (öncesi/sonrası karşılaştırmalı, sabit senaryo).
- Tarih-listesi/tarih-tipi filtresi (Kayıt/Çıkış/Dönüş) her birinin doğru tarih alanını süzdüğü
  ayrı ayrı doğrulanır.

## Exit
- [ ] 5 rapor sayfası da yeni filtre/kolonlarla çalışıyor
- [ ] `FiloBildirimUretici` regresyon testi yeşil (bildirim sayısı değişmedi)
- [ ] `rezervasyon_kaynak_raporu` varsayılan olarak İptal hariç tutuyor (testli)
- [ ] Tam suite yeşil

## Notlar
`bos_km_detay` maddesi CANLI-TARAMA DOĞRULAMASI TAMAMLANMADAN kodlanmaya BAŞLANMAMALI — K3 %29
sınırda, ad benzerliği tuzağı şüphesi var (plan dosyasından taşındı). Doğrulama sonucu ekranın
amacı gerçekten farklıysa (fraud/boşta-km takibi), bu madde FAZ'dan çıkarılıp D7 (yeni dikey) olarak
ayrı planlanmalı — bu durumda fazın toplam eforu 0,5 gün düşer (3,5 gün'e).
