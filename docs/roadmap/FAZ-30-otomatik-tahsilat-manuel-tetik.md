# FAZ-30 — Otomatik Tahsilat (Manuel Tetikleme Ekranı)

| | |
|---|---|
| **Desen** | D5-bitişik (yeni arama+tetikleme ekranı) |
| **Efor** | 1,5 gün (yapısal arama+tetikleme iskeleti); hangi sözleşmelerin "otomatik tahsilat
  edilebilir" sayılacağı ve tahsilat tutarının doğruluğu **PARA — Opus'a devredilir** |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `otomatik_tahsilat.aspx` |
| **Risk** | yüksek (para) — seçili sözleşmeler üzerinde GERÇEK tahsilat tetikliyor (job'un tek-seferlik,
  seçili-kapsamlı çalıştırılması) |
| **Zorunlu** | adversarial inceleme — Critical/High/Medium bulgu kalmadan commit yok. |

## Amaç
Mevcut `/ayarlar`'daki job-seviyesi açık/kapalı anahtarından AYRI, kullanıcının SEÇİLİ sözleşmeler
üzerinde manuel/tek-seferlik tahsilat tetikleyebildiği yeni bir ekran ekleyerek, dönemsel
faturalama+tahsilat akışının gecikmiş/atlanmış kalemlerini elle çalıştırabilir hale gelir.

## Neden (kanıt)
Repoda `otomatik_tahsilat.aspx` karşılığı bir sayfa yok. Mevcut otomatik akış
`src/RentACar.Infrastructure/Persistence/DonemFaturaUretici.cs` üzerinden, `TenantSettings.
DonemselFaturalamaJob`/`DonemselOtomatikTahsilat` (satır 61/64 doğrulandı) bayraklarıyla TÜM
tenant için toplu çalışıyor — kullanıcının belirli sözleşmeleri SEÇİP tek seferlik tetiklediği bir
arayüz yok. `DonemFaturaUretici.RunAsync(db, tenantId, now, ct)` (satır 28) tüm tenant'ı tarıyor;
tahsilat çekirdeği `DonemKesAsync` (satır 78) private ve tekil-sözleşme çağrısına açık değil.

## Yapılacaklar
1. `src/RentACar.Infrastructure/Persistence/DonemFaturaUretici.cs` — `DonemKesAsync` (satır 78)
   metodunu, TEK bir `rentalId` üzerinde çağrılabilecek şekilde **yeniden kullanılabilir** hale
   getir (imza/erişilebilirlik değişikliği — `internal`/`public static` yap, tenant taraması YAPMADAN
   doğrudan verilen sözleşme ID listesiyle çalışacak bir overload ekle). Mevcut `RunAsync`
   (tüm-tenant job) davranışı DEĞİŞMEZ — yalnız çekirdek çağrılabilir hale gelir (kopyalanmaz).
2. Yeni `src/RentACar.Web/Components/Pages/Finance/OtomatikTahsilat.razor` — filtre formu: Sözleşme
   No, Bakiye durumu (Müşteri Bakiyeli/HGS Bakiyeliler/Sadece Otomatik Olanlar —
   `RentalContract`+`AccountLedgerEntry` sorgusu), İşlem Şube, Tarih aralığı → sonuç listesi
   (checkbox'lı satırlar) + "Seçilenler için Tahsilatı Çalıştır" butonu.
3. Yeni endpoint (`OtomatikTahsilatEndpoints.cs` ya da mevcut Finance endpoint dosyasına ekle):
   `POST /finans/otomatik-tahsilat/calistir` — seçili `rentalId` listesini alır, madde 1'deki
   yeniden-kullanılabilir çekirdeği HER sözleşme için TEK TEK çağırır (toplu değil, sözleşme-bazlı —
   birinin hatası diğerlerini durdurmaz, sonuç listesi "kaç başarılı/kaç atlandı" döner).
4. Sonuç ekranında hangi sözleşmelerin kesildiğini/tahsil edildiğini/atlandığını (mevcut `Sonuc`
   record'undaki `Kesilen/Tahsilat/Atlananlar` alanlarına benzer) göster.
5. **Zorunlu adversarial inceleme**: çift-tetikleme (aynı sözleşme iki kez seçilip art arda
   çalıştırılırsa çift-tahsilat riski — mevcut `KilitAsync` (satır 199) idempotency kilidinin bu
   YENİ manuel-tetik yolunda da devrede olduğu doğrulanmalı), yetki (Muhasebe/Admin dışında erişim
   kapalı mı), tutar doğruluğu (seçili sözleşmenin GERÇEK bakiyesiyle tahsil edilen tutar eşleşiyor
   mu), RLS (başka tenant'ın sözleşmesi listede görünmüyor mu). Critical/High/Medium bulgu varsa
   düzeltilmeden commit yok.

## Dokunulacak dosyalar
- `src/RentACar.Infrastructure/Persistence/DonemFaturaUretici.cs` — çekirdek yeniden-kullanım
- (yeni) `src/RentACar.Web/Components/Pages/Finance/OtomatikTahsilat.razor`
- (yeni) endpoint dosyası (Finance altında)

## Migration
Yok — mevcut `RentalContract`/`AccountLedgerEntry`/`TenantSettings` tablolarından okuyor, yeni
tablo/kolon eklemiyor.

## Test
- `OtomatikTahsilatTests` (yeni): 5 sözleşme elle kurulur (3'ü "Müşteri Bakiyeli", 2'si değil);
  Bakiye durumu filtresi yalnız elle bilinen 3'ünü döndürüyor mu (bağımsız oracle — sayı testte
  sabit). Seçili 3 sözleşme için "Çalıştır" tetiklenir; **Σ Borç(base) == Σ Alacak(base)** her
  postlanan kayıt için doğrulanır; sonuç ekranında `Kesilen`/`Tahsilat`/`Atlananlar` sayıları elle
  hesaplanan değerlerle eşleşiyor.
- İdempotency testi: aynı 3 sözleşme İKİNCİ kez seçilip çalıştırılırsa (kullanıcı yanlışlıkla iki
  kez tıklarsa) `KilitAsync` kilidi devreye girip ÇİFT TAHSİLAT OLUŞMADIĞI doğrulanır (deterministik
  probla — rerun'a kaçmadan).
- Yetki testi: Operator rolüyle erişim guard'la engelleniyor.
- RLS testi: başka tenant'ın sözleşmesi filtre sonucunda görünmüyor.

## Exit
- [ ] Filtre + seçim + "Çalıştır" akışı çalışıyor, mevcut job davranışı regresyonsuz
- [ ] Çift-tetikleme çift-tahsilat ÜRETMİYOR (deterministik test kanıtı)
- [ ] Adversarial bulguları (Critical/High/Medium) düzeltildi
- [ ] Tam suite yeşil

## Notlar
Bu ekran mevcut `/ayarlar` job-anahtarından TAMAMEN AYRI — orası "job her gece otomatik çalışsın mı"
sorusuna cevap verir, burası "şu an, şu seçili sözleşmeler için elle çalıştır" der. İkisi karışmamalı.
