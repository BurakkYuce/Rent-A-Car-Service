# FAZ-14 — Regülasyon Kısmi Ödeme Genişletmesi (MTV + Muayene + Servis Tanım Matrisi)

| | |
|---|---|
| **Desen** | D2 — kural taşıyan master (MTV/Muayene kısmi ödeme akışı ledger-posting mantığını değiştirir) |
| **Efor** | 3,5 gün (1,5 + 1 + 1) |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `arac_mtv_islemleri.aspx`, `arac_muayene_islemleri.aspx`, `servis_tanim_tablosu.aspx` |
| **Risk** | yüksek (Bölüm A/B — mevcut `MtvOdeAsync`/`MuayeneOdeAsync` tam-tutar posting'ini kısmi-tutara çeviriyor, idempotency kritik) · düşük (Bölüm C) |
| **Zorunlu** | Bölüm A/B için adversarial inceleme — Critical/High/Medium bulgu kalmadan commit yok |

## Amaç
MTV/Muayene kaydına kısmi ödeme (`Kalan` bakiye) girilebilir, her kısmi ödeme kendi defter
kaydını post eder ve `Kalan=0` olunca `Odendi=true` olur; ayrıca `ServisTanim` filodaki GERÇEK
Marka/Tip/Yakıt/Vites kombinasyonlarına bağlanarak otomatik türetilebilir hale gelir.

## Neden (kanıt)
- `arac_mtv_islemleri.aspx`: K2=%21 (4/19). `Tutar_Doviz/Tutar_Kur`, `Kalan` (kısmi ödeme
  bakiyesi), `Evrak_No`, `Islem_Yapan`, `Aciklama`, `Odeme_Tarihi` (ayrı — bugün "Öde" =
  anlık), `Kasa_Kodu`/`Hesap_No` (spesifik kasa/IBAN — bugün sadece Kasa/Banka ikili select)
  yok. **Kod doğrulaması:** `RegulationService.MtvOdeAsync` bugün `Tutar`'ın TAMAMINI tek
  seferde Debit/Credit olarak postluyor (`RegulationService.cs:78-101`) — kısmi ödeme
  mekanizması hiç yok.
- `arac_muayene_islemleri.aspx`: K2=%28 (5/18). `Islem_KM`, `Evrak_No`, `Islem_Yapan`,
  `Kalan`, `Odeme_Tarihi` ayrı, `Kasa_Kodu`/`Hesap_No`, `Aciklama` yok. Aynı desende MTV —
  `MuayeneOdeAsync` da tam-tutar postluyor (`RegulationService.cs:109-134`).
- `servis_tanim_tablosu.aspx`: K2=%50 (1/2), K3=%33 (2/6). Canlı tablo satırları filodaki
  GERÇEK Marka+Tipi+Yakıt Türü+Vites kombinasyonlarından otomatik türüyor; bizde `ServisTanim`
  serbest-metin `AracTipi`+`Kod` anahtarlı (`ServisTanim.cs`, config `MasterConfigs.cs:415`).

## Yapılacaklar

### Bölüm A — arac_mtv_islemleri
1. `src/RentACar.Domain/Entities/MtvRecord.cs` — ekle: `Kalan` (`decimal`, oluşturulunca
   `=Tutar`), `EvrakNo`, `IslemYapan`, `Aciklama`, `OdemeTarihi` (`DateTimeOffset?`),
   `TutarDoviz`/`TutarKur`, `KasaKodu`/`HesapNo`.
2. `src/RentACar.Infrastructure/Persistence/Configurations/RegulationConfigs.cs` — `MtvRecord`
   config genişlet.
3. `src/RentACar.Application/Regulation/RegulationService.cs` — `MtvOdeAsync`'i **kısmi
   tutar** parametresi (`decimal odemeTutari`) alacak şekilde değiştir: `Kalan -= odemeTutari`;
   yalnız `odemeTutari` kadar Debit/Credit çifti post edilir (Σ Borç(base)=Σ Alacak(base) HER
   KISMİ ADIMDA); `Kalan<=0` olunca `Odendi=true`.
4. **İdempotency:** her kısmi ödeme kendi deterministik anahtarını taşır — MONOTON bileşen
   şart (`idempotency-anahtar-tasarimi` dersi): anahtar `"MtvOdeme:{mtvId}:{odemeSirasi}"`,
   `odemeSirasi` = o kayıt için önceden yapılmış ödeme sayısı (repository'den okunur, satır
   kilidiyle). Aynı sıra ikinci kez gönderilirse kısmi unique index `UniqueViolation`'ı yutar.
5. `src/RentACar.Application/Regulation/IRegulationRepository.cs` /
   `src/RentACar.Infrastructure/Persistence/Repositories/RegulationRepository.cs` —
   `PostMtvOdemeAsync`'i kısmi tutar + ödeme sırası ile çalışacak şekilde genişlet (satır
   kilidi altında `Kalan` güncellemesi + idempotent insert).
6. `src/RentACar.Web/Components/Pages/Regulation/RegulationList.razor` (MTV bölümü) — `Kalan`
   gösterimi + "Öde" formunda kısmi tutar girişi + `EvrakNo`/`IslemYapan`/`Aciklama`/
   `OdemeTarihi`/`KasaKodu`/`HesapNo` alanları.
7. `src/RentACar.Web/Regulation/RegulationEndpoints.cs` — MTV ödeme ucu kısmi tutar
   parametresi alacak şekilde güncellenir.

### Bölüm B — arac_muayene_islemleri (aynı desen, aynı PR — kısmi ödeme altyapısı paylaşılır)
8. `src/RentACar.Domain/Entities/InspectionRecord.cs` — ekle: `IslemKm`, `EvrakNo`,
   `IslemYapan`, `Kalan` (oluşturulunca `=Ucret+Ceza`), `OdemeTarihi`, `KasaKodu`/`HesapNo`,
   `Aciklama`. Bugünkü `Ceza` alanı ödeme formunda giriliyor (ana kayıtta değil) — bu akış
   FARKI korunur (yapısal karar, tutar etkisi yok).
9. `RegulationConfigs.cs` — `InspectionRecord` config genişlet.
10. Migration (Bölüm A+B TEK migration'da): `dotnet ef migrations add
    AddMtvMuayeneKismiOdeme --project src/RentACar.Infrastructure --startup-project
    src/RentACar.Infrastructure`.
11. `RegulationService.cs` — `MuayeneOdeAsync`'i aynı kısmi-tutar + idempotency desenine
    (`"MuayeneOdeme:{inspectionId}:{odemeSirasi}"`) geçir.
12. `IRegulationRepository.cs`/`RegulationRepository.cs` — `PostMuayeneOdemeAsync` genişlet.
13. `RegulationList.razor` (Muayene bölümü) — aynı alan grubu wiring.
14. `RegulationEndpoints.cs` — Muayene ödeme ucu güncellenir.

### Bölüm C — servis_tanim_tablosu
15. `src/RentACar.Domain/Entities/ServisTanim.cs` — `Marka`/`Tip`/`Yakit`/`Vites` (hepsi
    `string?`, nullable) ekle; eski `Kod`/`AracTipi` **KALIR** (additive, geriye-uyum).
16. `src/RentACar.Infrastructure/Persistence/Configurations/MasterConfigs.cs`
    (`ServisTanimConfig`, satır ~415) — 4 yeni kolon.
17. Migration'a bu 4 kolonu da ekle (aynı Bölüm A/B migration'ında ya da ayrı — dosya
    büyüklüğüne göre serbest).
18. `src/RentACar.Application/ServisTanimlari/ServisTanimFiles.cs` — "otomatik türet" akışı:
    `db.Vehicles`'tan DISTINCT `(Marka,Tip,Yakit,Vites)` kombinasyonları çekilip eşleşen
    `ServisTanim` yoksa satır önerilir (yeni bir metod, örn. `OneriAsync`).
19. `src/RentACar.Infrastructure/Persistence/Repositories/ServisTanimRepository.cs` —
    DISTINCT sorgusu implementasyonu.
20. `src/RentACar.Web/Components/Pages/ServisTanimlari/ServisTanimList.razor` — KM alanı
    inline düzenleme + "Öner" aksiyonu.
21. `src/RentACar.Web/ServisTanimlari/ServisTanimEndpoints.cs` — öneri kabul/KM güncelleme
    uçları.

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/MtvRecord.cs`, `InspectionRecord.cs`, `ServisTanim.cs`
- `src/RentACar.Infrastructure/Persistence/Configurations/RegulationConfigs.cs`,
  `MasterConfigs.cs`
- (yeni) migration dosyası (`AddMtvMuayeneKismiOdeme`, gerekirse `ServisTanim` için ayrı)
- `src/RentACar.Application/Regulation/RegulationService.cs`, `IRegulationRepository.cs`
- `src/RentACar.Infrastructure/Persistence/Repositories/RegulationRepository.cs`
- `src/RentACar.Application/ServisTanimlari/ServisTanimFiles.cs`
- `src/RentACar.Infrastructure/Persistence/Repositories/ServisTanimRepository.cs`
- `src/RentACar.Web/Components/Pages/Regulation/RegulationList.razor`,
  `src/RentACar.Web/Regulation/RegulationEndpoints.cs`
- `src/RentACar.Web/Components/Pages/ServisTanimlari/ServisTanimList.razor`,
  `src/RentACar.Web/ServisTanimlari/ServisTanimEndpoints.cs`

## Migration
Var — `MtvRecord`/`InspectionRecord`'a additive kolonlar + `ServisTanim`'e 4 additive kolon.
RLS zaten aktif (mevcut tablolar) → yeni RLS bloğu gerekmez.

## Test
- `RegulationServiceTests` — kısmi ödeme senaryosu: MTV `Tutar=1000` seed edilir; 2 kısmi ödeme
  (400 + 600) yapılır. **Bağımsız oracle:** ilk ödeme sonrası `Kalan=600` (elle `1000-400`
  hesaplanmış sabit), ikinci sonrası `Kalan=0` ve `Odendi=true` (sabit).
- **Defter dengesi:** her kısmi ödeme sonrası `Σ Debit(base) == Σ Credit(base)` — 400'lük ve
  600'lük postlar AYRI AYRI test edilir (toplamları karıştırılmaz).
- **İdempotency:** aynı `odemeSirasi` ile ikinci POST → `ValidationException`/kısmi unique
  index ihlali yutulur, `Kalan` DEĞİŞMEZ (regresyon: çift-ödeme yazılmaz).
- Aynı 3 senaryo Muayene için tekrarlanır.
- `ServisTanimServiceTests` — otomatik türetme: 3 araç seed edilir (2'si aynı
  Marka/Tip/Yakıt/Vites kombinasyonu, 1'i farklı); öneri sorgusu **2 farklı** kombinasyon
  bekler (sabit sayı).

## Exit
- [ ] MTV/Muayene "Öde" formunda kısmi tutar girilebiliyor, `Kalan` doğru düşüyor
- [ ] Her kısmi ödeme kendi dengeli defter çiftini postluyor (test yeşil)
- [ ] İdempotency testi çift-postlamayı önlüyor
- [ ] `ServisTanim` filodan otomatik kombinasyon önerebiliyor, KM inline düzenlenebiliyor
- [ ] **Adversarial inceleme (Bölüm A/B) tamamlandı, Critical/High/Medium bulgu yok**
- [ ] Tam suite yeşil

## Notlar
Bugünkü **Vade** alanı (MTV'nin vade tarihi) zorunlu takip ediliyor — canlı profilinde ayrı bir
vade/due-date alanı YOK (muhtemelen dönem bazlı zımni vade); bu davranış KORUNUR, kaybedilmemeli.
Ceza_Tutari alanının ana kayıtta mı ödemede mi tutulacağı (Muayene) yapısal bir tercih, tutar
etkisi yok — bu fazda ödeme formunda kalır (mevcut davranış). Plan dosyasının kendi notu:
"kısmi ödemede defter dengesinin HER ADIMDA mı yoksa yalnız kapanışta mı yazılacağı" — bu faz
HER ADIMDA yazma yönünü seçti (canlı davranışına daha yakın, kısmi ödeme geçmişi kaybolmuyor);
bu tercih adversarial incelemede özellikle sorgulanmalı.
