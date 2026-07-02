# RentACar (TürevRent Klonu) — System Design Değerlendirme Raporu

> Tarih: 2026-07-02 · Durum: main `9d94b47` (roadmap-v4 sonrası) · Rakamlar koddan ampirik sayıldı.
> Amaç: yayın (launch) öncesi dürüst değerlendirme — güçlü yanlar, riskler, öncelikli geliştirme listesi.

## Yönetici özeti

Bu proje, iç mimari kalitesi açısından **sektör ortalamasının belirgin üstünde**: çok-kiracılık iki bağımsız
savunma katmanıyla (EF filter + Postgres RLS), para çift-taraflı defterle ve DB seviyesinde
değiştirilemezlikle, doğruluk 687 gerçek-PostgreSQL entegrasyon testiyle güvence altında. Zayıf taraf kodda
değil, **işletme (ops) katmanında**: gözlemlenebilirlik sıfır, yedekleme/DR tanımsız, KVKK (PII şifreleme) ve
e-Fatura (yasal zorunluluk) açık. Yayına çıkmadan önce kapatılması gereken liste kısa ve net (aşağıda P0) —
bunlar kapanınca sistem 20-tenant ölçeğinde güvenle çıkarılabilir.

**Rakamlarla mevcut durum:** 65 entity · 70 servis · 100 Razor sayfası · 62 endpoint grubu · 87 migration ·
687 entegrasyon testi (~14.400 satır test kodu) · ~33.000 satır uygulama kodu (migration'lar hariç) ·
CI mevcut (`.github/workflows/ci.yml`).

---

## 1. Mimari genel bakış

- **Katmanlama:** `Domain (2.9k LOC) → Application (10.2k) → Infrastructure (7.7k) → Web (12.5k)` + ayrı
  `Api` iskeleti (1k). Bağımlılık yönü gerçekten içe doğru; Domain hiçbir şeye bağımlı değil. "Temiz mimari"
  burada slogan değil, ölçülebilir gerçek.
- **UI modeli:** Blazor **statik SSR** — interaktif circuit yok, formlar minimal-API uçlarına POST eder.
  Sonuç: kullanıcı başına RAM maliyeti yok denecek kadar az, sunucu ucuz, state yönetimi basit. ERP formları
  için doğru seçim.
- **Çok-kiracılık:** her tenant verisi `TenantId` taşır; tenant, bağlantı başına `set_config('app.tenant_id')`
  ile Postgres'e iletilir (`TenantConnectionInterceptor`); RLS policy bunu okur.
- **Para:** `Money(Amount, Currency, Rate)` struct'ı; her işlem dengeli borç/alacak kümesi yazar; düzeltme
  yalnız ters kayıtla.

## 2. Güçlü yanlar

### 2.1 Çok-kiracılık: derinlemesine savunma (en güçlü tasarım kararı)
- **İki bağımsız katman:** EF global query filter + Postgres RLS (`ENABLE` + `FORCE ROW LEVEL SECURITY`).
  Uygulama katmanında bir hata olsa bile (filtre unutulmuş sorgu, raw SQL) veritabanı sızıntıyı fiziksel
  olarak reddeder.
- **İki DB rolü:** `racar_owner` (yalnız migration/DDL) ve `racar_app` (runtime, `NOSUPERUSER NOBYPASSRLS`).
  Uygulama RLS'i bypass **edemez** — bu, "yapmıyoruz" değil "yapamayız" garantisi.
- İzolasyon testleri gerçek runtime rolüyle (`racar_app`) koşuyor; güvence test edilmiş, varsayılmamış.
- Çoğu SaaS'ın tek `WHERE TenantId=` ile yaptığı işin iki kademe üstü.

### 2.2 Para ve muhasebe disiplini
- **Çift-taraflı defter, denge zorunlu:** repo katmanı Σ borç(base) ≠ Σ alacak(base) ise exception fırlatır —
  dengesiz kayıt yazılamaz.
- **Değiştirilemezlik DB'de:** fatura/defter tablolarında `rc_prevent_mutation()` BEFORE UPDATE/DELETE
  trigger; düzeltme yalnız ters kayıtla. Mali iz, uygulama kodundan bağımsız korunur.
- **İdempotency DB'de:** işlem-başına `SourceType` + kısmi unique index → çift ödeme yarış koşulunda bile
  imkânsız; `UniqueViolation` yakalanıp anlamlı hataya çevriliyor.
- **Atomiklik:** bayrak güncelleme + defter kaydı tek transaction; dönem kilidi (`IPeriodLockGuard`) kapalı
  aya kayıt engelliyor; boşluksuz belge numarası insert ile aynı transaction'da.

### 2.3 Test ve doğrulama kültürü
- 687 test **gerçek PostgreSQL'e** karşı (mock yok) — RLS, index, trigger dahil gerçek davranış doğrulanıyor.
- **Bağımsız oracle ilkesi:** beklenen değerler elle kurulmuş senaryodan türetiliyor; kod kendi kendini
  "doğrulayamıyor".
- **Adversarial inceleme süreci** para PR'larında zorunluydu ve gerçek hatalar yakaladı (ör. regülasyon
  giderinin araca atfedilmemesi → kârlılık raporunun şube kırılımında maliyet kaybolması; paylaşımlı
  idempotency token). Süreç kendini kanıtlamış durumda.
- `docs/parite/` (156 ekranlık canlı envanter + modül-modül gap tablosu) planlamanın tek doğruluk kaynağı —
  kanıta dayalı geliştirme.

### 2.4 Güvenlik ve yetki
- Rol matrisi (floor) + ekran-bazlı override (yalnız **sıkılaştırır**, genişletemez) + şube kapsamı + çift
  savunma (servis guard **ve** endpoint attribute).
- Şifreler ASP.NET `PasswordHasher` (PBKDF2), SMTP vb. sırlar `ISecretProtector` ile şifreli, antiforgery
  prod'da zorunlu, audit log ve 2 aşamalı login mevcut.

## 3. Zayıf yanlar / riskler

### 3.1 Üretim işletimi — asıl açık burada
- **Gözlemlenebilirlik sıfır:** health check endpoint'i, yapılandırılmış log (Serilog vb.), metrik/trace yok.
  Üretimde bir tenant "sistem yavaş" dediğinde bakılacak hiçbir şey yok.
- **Yedekleme/DR tanımsız:** tek Postgres; otomatik `pg_dump`, off-site kopya, geri-dönüş tatbikatı yok.
  **Muhasebe verisi tutan bir sistem için launch-blocker.**
- **Tek sunucu = tek arıza noktası.** App stateless olduğu için çözümü kolay ama planlanmamış.
- **Rate limiting yok** — login dahil; brute-force'a açık.
- Migration+seed uygulama açılışında koşuyor (`DbInitializer`): tek-instance varsayımı. Bugün doğru; ikinci
  instance açıldığı gün deploy adımına taşınmalı.

### 3.2 Bilinen ve kabul edilmiş teknik borçlar
- **Şube alanları serbest metin** (FK değil): "Kadıköy" ve "kadıkoy" raporlarda ayrı şube olur. (Flagged,
  bilinçli ertelendi.)
- **PII şifreleme (F2) ertelendi:** TC kimlik/ehliyet verisi düz metin → **KVKK riski**; launch öncesi karar
  şart.
- Para uçlarında **deadlock (40P01) retry yok** — eşzamanlı işlemde kullanıcıya çirkin hata düşer
  (para-güvenli: idempotency çift kaydı zaten engeller, ama UX kötü).
- `AddInsuranceAsync` Currency set etmiyor (çok-döviz sigorta create yolu eksik).
- Hata mesajları query-string ile taşınıyor (`?hata=...`) — çalışıyor ama URL'de bilgi sızıntısı/uzunluk
  kırılganlığı var; flash-cookie deseni daha sağlam.

### 3.3 Ürün/parite boşlukları (bilinçli)
- **e-Fatura stub** — Türkiye'de yasal zorunluluk; entegre edilmeden gerçek faturalama yapılamaz. SMS/HGS/POS
  da stub.
- **Fiyat motoru** kural-bazlı ama canlıyla kuruş-kalibrasyonu yapılmadı; canlının tarife matrisi
  (kanal×şube×tarih×gün-kademesi) tek katmanlı `RateCard`'da.
- Canlının alan derinliği hâlâ uzak (ör. `kiralama.aspx` ~705 alan vs klon ~35) — ancak kimlik gerektirmeyen
  kapsamın %100'ü kapatıldı; kalan her şey credential/karar bekliyor. Tasarım hatası değil, bilinçli kapsam
  sınırı.

### 3.4 Ölçek sınırları (bugün değil, 100+ tenant'ta)
- Cache katmanı yok, raporlar canlı SQL agregasyonu — veri büyüyünce özet tablo/materialized view gerekir.
- Arka plan iş altyapısı yok (vade hatırlatma, e-posta) — veri hazır ama scheduler (Hangfire/Quartz) yok.
- RLS **veriyi** izole eder, **performansı** etmez: gürültücü-komşu tenant'a karşı önlem yok.

## 4. Geliştirme önerileri (öncelik sırasıyla)

### P0 — yayın öncesi şart (hepsi küçük iş)
1. Otomatik yedek: gecelik `pg_dump` + off-site kopya + bir kez restore tatbikatı.
2. Gözlemlenebilirlik minimumu: `/health` endpoint + Serilog (dosya/journald) + hata alarmı.
3. KVKK kararı: F2 PII şifreleme yap **ya da** bilinçli erteleyip sözleşme/aydınlatma metnine yansıt.
4. Login rate limiting (`AddRateLimiter` — birkaç satır).
5. Para uçlarına deadlock retry (40P01 → 3 deneme).

### P1 — ilk aylar
6. e-Fatura entegrasyonu (entegratör üzerinden — yasal önkoşul).
7. Şube FK migrasyonu (flagged borcu kapat).
8. Tarife matrisi (kanal×şube×tarih×gün) — fiyat motorunun asıl gövdesi.
9. Scheduler + bildirim (sigorta/MTV/muayene vade uyarıları — veri zaten panoda).

### P2 — büyürken
10. `RentACar.Api`'yi genişlet (şu an 1k LOC iskelet; mobil/3. parti için).
11. Rapor özet tabloları + cache; pgbouncer; migration'ı deploy adımına taşı (multi-instance hazırlığı).

## 5. Sonuç

Projenin karakteristik özelliği **asimetri**: veri bütünlüğü ve tenant izolasyonu tarafında çoğu üretim
SaaS'ından iyi durumda; işletme/gözlemlenebilirlik tarafında ise henüz hiçbir şey yok. İyi haber, zor tarafın
(doğruluk) bitmiş, kolay tarafın (ops) kalmış olması — P0 listesi birkaç günlük iştir ve sonrasında 20-tenant
senaryosunda tek VPS'te güvenle yayına çıkılabilir.
