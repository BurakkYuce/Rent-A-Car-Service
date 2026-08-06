# FAZ-67 — Nakit İşlem: Bağımsız Giriş Ekranı + Arama/Filtre

| | |
|---|---|
| **Desen** | D3 |
| **Efor** | 1,5 gün (1 gün nakit_islem + 0,5 gün nakit_islem_ara) |
| **Bağımlılık** | Kasa_Kodu (spesifik hesap seçimi) alt-parçası için FAZ-50 — bu faz FAZ-50 öncesi
  de yapılabilir, yalnız Kasa_Kodu alanı FAZ-50 sonrası eklenir |
| **Kapsanan canlı ekran** | `nakit_islem.aspx`, `nakit_islem_ara.aspx` |
| **Risk** | düşük — mevcut para-yazan mantık (`CollectAsync`/`PayAsync`) DEĞİŞMİYOR, yalnız UI/giriş
  yüzeyi; para formunu değiştirdiği için regresyon-kontrolü (adversarial-lite) önerilir |

## Amaç
Kullanıcı Tahsilat/Ödeme'yi bir cari'nin ekstre sayfasına GİTMEDEN, bağımsız bir ekrandan (cari
arama kutusu dahil) yapabilir; `/kasa` sayfasındaki mevcut işlem listesini Cari Ara/İşlem Şube/Tarih
aralığı filtresiyle arayabilir hale gelir.

## Neden (kanıt)
Şu an Tahsilat/Ödeme SADECE bir cari'nin ekstre sayfasından yapılabiliyor (`CustomerStatement.razor`
içinde gömülü form) — canlıda bağımsız ekran (cari arama kutusu dahil) var, bizde yok. `/kasa`
(`KasaHub.razor`) erişilebilir + veri gösteriyor ama filtresiz — filtre paneli (Cari Ara, İşlem
Şube, Tarih aralığı) + Cari Kod kolonu yok (grep doğrulandı).

## Yapılacaklar
1. Yeni sayfa `src/RentACar.Web/Components/Pages/Finance/NakitIslem.razor` (rota:
   `/finans/nakit-islem`) — cari arama (No/Ad/Soyad), ardından mevcut `CashService.CollectAsync`/
   `PayAsync` AYNEN çağrılır (mantık DEĞİŞMEZ, yalnız giriş yüzeyi). Tarih (manuel — şu an sunucu
   "şimdi"), `Onerilen_Tutar` (bakiyeden otomatik öneri, UI convenience) eklenir. Kasa_Kodu
   (spesifik hesap) → FAZ-50 sonrası eklenir.
2. Mevcut `src/RentACar.Web/Finance/FinanceEndpoints.cs` (`/tahsilat`/`/odeme` uçları AYNEN
   kullanılır — yeni uç açılmaz).
3. `KasaHub.razor`'a filtre paneli (Cari Ara, İşlem Şube, Tarih aralığı) + Cari Kod kolonu eklenir.
4. `src/RentACar.Web/Reports/ListExportCatalog.cs` (`NakitIslemler`, ~L98) export'una da Cari
   adı/kodu kolonu eklenir (aynı boşluk export'ta da var).

## Dokunulacak dosyalar
- (yeni) `src/RentACar.Web/Components/Pages/Finance/NakitIslem.razor`
- `src/RentACar.Web/Finance/FinanceEndpoints.cs` (mevcut `/tahsilat`/`/odeme` AYNEN kullanılır)
- `src/RentACar.Web/Components/Pages/Finance/KasaHub.razor`
- `src/RentACar.Web/Reports/ListExportCatalog.cs` (`NakitIslemler`)

## Migration
Yok — mevcut `CashTransaction`/`Customer` tabloları okunuyor/yazılıyor, yeni tablo/kolon yok.

## Test
- Adversarial-lite regresyon: `NakitIslem.razor`'dan yapılan bir Tahsilat, `CustomerStatement.razor`
  üzerinden yapılan AYNI parametreli bir Tahsilat ile AYNI defter etkisini üretiyor mu (iki giriş
  yüzeyi, TEK mantık — `CollectAsync` aynen çağrıldığı için beklenen davranış budur; test bunu
  KANITLAR, varsayım olarak bırakmaz).
- `KasaHub.razor` filtre: elle 3 işlem (2 farklı cari, 2 farklı şube) oluşturulur; Cari Ara/İşlem
  Şube/Tarih filtresi beklenen alt-kümeyi döndürüyor mu (bağımsız oracle, sabit sayı).
- `NakitIslemler` export: Cari adı/kodu kolonu doğru veri taşıyor mu (round-trip).

## Exit
- [ ] `/finans/nakit-islem` sayfası çalışıyor, mevcut `CollectAsync`/`PayAsync` AYNEN kullanılıyor
- [ ] İki giriş yüzeyinin (ekstre-gömülü vs bağımsız) defter etkisi AYNI — regresyon testiyle kanıtlı
- [ ] `KasaHub.razor` filtre paneli çalışıyor
- [ ] `NakitIslemler` export Cari kolonu ekli
- [ ] Tam suite yeşil

## Notlar
Bu faz PARA etiketi orijinal taramadan miras — yeni bir formül/tutar KARARI gerekmiyor (mevcut
mantık aynen kullanılıyor), yine de para formunu değiştirdiğinden regresyon-kontrolü (adversarial-
lite, D5'in tam ağırlığında değil) önerilir; bu yüzden tablo başında **Zorunlu: adversarial
inceleme** satırı EKLENMEDİ (D5 değil, D3) — ama Exit kriterine regresyon testi şart koşuldu.
