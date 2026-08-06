# FAZ-16 — Servis Kaydı: Kaza/Fatura/Ödeme Derinliği + Rezervasyon Durumu

| | |
|---|---|
| **Desen** | D5 — para hareketi/defter etkili derinlik |
| **Efor** | 4 gün (3 + 1) |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `arac_servis_islemleri.aspx`, `servis_rezervasyon.aspx` |
| **Risk** | yüksek — fatura/ödeme bloğu para-bilgisi taşıyor (bu fazda deftere postlanmıyor ama alan seti hazırlanıyor); kaza/hasar verisi rücu akışını besliyor |
| **Zorunlu** | adversarial inceleme — Critical/High/Medium bulgu kalmadan commit yok |

## Amaç
Servis kaydına kaza/hasar detayı, fatura bilgisi, ödeme bilgisi ve çıkış/dönüş yakıt seviyesi
eklenebilir; servis kalemleri (`ServiceLine`) birim fiyat/miktar/indirim/KDV ile zenginleşir;
ayrıca bir servis kaydı "Rezerve" durumunda önceden planlanıp sonradan "Servise Al" ile
açılabilir.

## Neden (kanıt)
- `arac_servis_islemleri.aspx`: K2=%11 (6/55), K3=%0 (0/7 — canlı grid Açıklama/Birim Fiyat/
  Toplam Fiyat/İndirim/Tutar/KDV/Genel Toplam bir FİYATLANDIRMA alt-tablosu; bizde bu satır
  kalemleri görünmüyor). **Kod doğrulaması** (`ServiceRecord.cs`): `ServiceLine` bugün yalnız
  `Aciklama`+`Tutar` taşıyor; kaza/fatura/ödeme blokları hiç yok; `CikisKm`/`GirisKm` var ama
  yakıt seviyesi yok. İşçilik kırılımı (Kaporta/Boya/Trim/Elektrik/Mekanik/Şase — 6 ayrı alan)
  canlıda var, bizde tek serbest `Kalem` satırı.
- `servis_rezervasyon.aspx`: aday route yok (`grep`'le "ServisRezervasyon" bulunamadı).
  **Kod doğrulaması** (`ServisDurum.cs`): enum `Acik=0, Serviste=1, Tamamlandi=2, Iptal=3` —
  "rezerve edilmiş ama henüz açılmamış" durumu yok.

## Yapılacaklar

### Bölüm A — arac_servis_islemleri
1. `src/RentACar.Domain/Entities/ServiceRecord.cs` — kaza/hasar bloğu ekle: `BeyanTuru`,
   `KarsiPlaka`, `KarsiTrafikSigortasi`, `KazaTarihi`, `KazaSorumlusu`, `HasarDosyaNo`,
   `DegerKaybi`; fatura bloğu: `FaturaTarihi`, `FaturaNo`, `FaturaTutar`, `FaturaKdv`,
   `FaturaGenelToplam`; ödeme bloğu: `OdemeTarihi`, `Odeme`, `OdemeDoviz`, `OdemeKur`,
   `OdemeTuru`, `KasaKodu`, `HesapNo`; `CikisYakit`/`DonusYakit` (`int?`).
2. `ServiceLine` (aynı dosyada) — `BirimFiyat`, `Miktar`, `Indirim`, `Kdv` alanları ekle
   (mevcut `Aciklama`+`Tutar` KALIR, `Tutar` artık `BirimFiyat*Miktar*(1-Indirim)*(1+Kdv)`
   türevi OLABİLİR ama bu fazda serbest alan olarak KALIR — hesaplamayı zorunlu kılmak
   PARA-Opus).
3. İşçilik 6-kırılımı (Kaporta/Boya/Trim/Elektrik/Mekanik/Şase): **YENİ SABİT ALAN
   EKLENMEZ** — mevcut serbest `ServiceLine` listesiyle karşılanır (6 ayrı satır elle
   girilir); sabit-alan biçimi Opus tercihi netleşene kadar bu fazda AÇILMAZ.
4. `src/RentACar.Application/ServiceRecords/ServiceRecordInput.cs` — yeni alanları ekle.
5. `src/RentACar.Application/ServiceRecords/ServiceRecordService.cs` — validasyon genişlet.
   **PARA — Opus not:** fatura/ödeme bloğu bu fazda GERÇEK gider+defter kaydına
   (`ExpenseType`) **BAĞLANMAZ** — `ServiceRecord` deftersiz kalır (mevcut yorum: "gerçek
   gider Gider dilimine bağlanır, follow-up"), yeni alanlar salt-bilgi.
6. Migration (Bölüm A+B tek migration).
7. `src/RentACar.Web/Components/Pages/ServiceRecords/ServiceRecordList.razor` — yeni alan
   grupları için form bölümleri (kaza/fatura/ödeme).
8. `src/RentACar.Web/ServiceRecords/ServiceRecordEndpoints.cs` — POST handler genişlet.

### Bölüm B — servis_rezervasyon (aynı PR, aynı dosyalar)
9. `src/RentACar.Domain/Enums/ServisDurum.cs` — `Rezerve = 4` ekle (Rezerve→Açık→Serviste→
   Tamamlandı/İptal akışı).
10. `ServiceRecord.cs` — `PlanBasTarihi`/`PlanBitTarihi` (`DateTimeOffset?`, nullable —
    planlanan randevu penceresi, gerçek `GirisTarihi`/`CikisTarihi`'den AYRI).
11. `ServiceRecordService.cs` — "Servise Al" aksiyonu (`Rezerve`→`Acik` geçişi;
    `GirisTarihi`/`GirisKm` o an doldurulur).
12. `ServiceRecordList.razor` — "Rezervasyonlar" filtresi/sekmesi (`Durum=Rezerve`) +
    "Servise Al" butonu.
13. `ServiceRecordEndpoints.cs` — "Servise Al" POST ucu.

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/ServiceRecord.cs` (ServiceLine dahil), `Enums/ServisDurum.cs`
- (yeni) migration dosyası
- `src/RentACar.Application/ServiceRecords/ServiceRecordInput.cs`, `ServiceRecordService.cs`
- `src/RentACar.Web/Components/Pages/ServiceRecords/ServiceRecordList.razor`
- `src/RentACar.Web/ServiceRecords/ServiceRecordEndpoints.cs`

## Migration
Var — mevcut `ServiceRecords`/`ServiceLines` tablolarına additive kolonlar. RLS zaten aktif →
yeni RLS bloğu gerekmez.

## Test
- `ServiceRecordServiceTests` — yeni alanlarla oluşturulan kaydın round-trip testi (oracle:
  elle sabit değerler yazılıp geri okunur).
- Rezerve→Açık geçiş testi: bağımsız oracle — `PlanBasTarihi=X` ile `Durum=Rezerve` seed
  edilir; "Servise Al" çağrılır; beklenen `GirisTarihi≈now` (tolerans içinde) ve
  `Durum=Acik` (sabit) doğrulanır.
- **D5 kapsamı:** bu fazda ledger posting YOK, dolayısıyla defter dengesi testi
  gerekmiyor — ama regresyon testi yeni alanların (fatura/ödeme bloğu) mevcut Gider/defter
  akışını **tetiklemediğini** açıkça doğrular (yanlışlıkla bir posting yolu açılmadığının
  kanıtı).
- İdempotency testi bu fazda gerekmiyor (posting yok); ledger entegrasyonu fazı açıldığında
  zorunlu olacak.

## Exit
- [ ] Kaza/fatura/ödeme blokları + `CikisYakit`/`DonusYakit` formda görünüyor, kaydediliyor
- [ ] `ServiceLine` birim fiyat/miktar/indirim/KDV ile zenginleşti
- [ ] `Rezerve` durumu + "Servise Al" akışı çalışıyor
- [ ] Yeni alanlar hiçbir ledger/Gider postuna yol AÇMADI (regresyon testiyle doğrulanır)
- [ ] **Adversarial inceleme tamamlandı, Critical/High/Medium bulgu yok**
- [ ] Tam suite yeşil

## Notlar
Rücu Yansıtma (mevcut `Yansitildi`/`YansitilanTutar`/`YansitilanCariId`) bu fazda
DEĞİŞTİRİLMEZ — canlının "Yansitma_Cari/Yansitma_Tutar" alanları farklı bir akış gibi
görünüyor ama bu faz kapsamı DIŞINDA. Fatura/ödeme bloğunun GERÇEK gider+defter kaydına
bağlanıp bağlanmayacağı kararı geldiğinde AYRI bir faz açılmalı — o faz zorunlu olarak D5
adversarial + defter dengesi/idempotency testi gerektirecek (bu fazınkinden ayrı, çünkü posting
mantığı o zaman devreye girecek).
