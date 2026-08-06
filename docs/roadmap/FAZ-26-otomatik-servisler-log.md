# FAZ-26 — Otomatik Servisler Günlük Log

| | |
|---|---|
| **Desen** | D2 — yeni küçük tablo + mevcut `Uretici` sınıflarına ince log çağrısı |
| **Efor** | 1 gün |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `otomatik_servisler.aspx` |
| **Risk** | düşük — yalnız log yazımı, mevcut iş mantığına dokunmuyor; `Uretici.RunAsync`
  paterni korunur (yetki yüzeyi büyümez) |

## Amaç
Arka planda çalışan otomatik işlerin (HGS yansıtma, KM güncelleme, taahhüt uzatma, süpürme) her
koşusunu bir log tablosuna yazarak kullanıcı `/raporlar/otomatik-servisler` sayfasından hangi işin
ne zaman çalıştığını, başarılı olup olmadığını görebilir hale gelir.

## Neden (kanıt)
Repoda `JobCalismaLog` adında bir entity **yok** (grep doğrulandı). Mevcut üretici sınıfları
(`src/RentACar.Infrastructure/Persistence/FiloBildirimUretici.cs`, `VadeBildirimUretici.cs`,
`OperasyonOzetUretici.cs`, `DonemFaturaUretici.cs`) çalışıyor ama koşu geçmişi hiçbir yere
yazılmıyor — kullanıcı bu işlerin gerçekten çalışıp çalışmadığını göremiyor (log-uyarılarını say
dersi: geçmişte sessiz-hata birikimi böyle kaçmıştı).

## Yapılacaklar
1. Yeni `src/RentACar.Domain/Entities/JobCalismaLog.cs` — `ITenantOwned, IAuditable`: `Id`,
   `TenantId`, `JobTuru` (enum: `HgsBasarili`/`HgsBasarisiz`/`KmGuncelleme`/`TaahhutUzatma`/
   `Supurme`), `Tarih` (DateTimeOffset, default `UtcNow`), `Detay` (string?), `Basarili` (bool).
2. Yeni enum `src/RentACar.Domain/Enums/JobTuru.cs` (ya da mevcut Enums dosyasına ekle).
3. `src/RentACar.Infrastructure/Persistence/Configurations/` altına ilgili config dosyasına (ör.
   `SystemConfigs.cs`) `internal sealed class JobCalismaLogConfig : IEntityTypeConfiguration<JobCalismaLog>`
   ekle: `HasIndex(TenantId, Tarih)`.
4. Migration: `dotnet ef migrations add AddJobCalismaLog --project src/RentACar.Infrastructure
   --startup-project src/RentACar.Infrastructure`.
5. **Migration'a RLS bloğunu ELLE ekle** (yeni tablo, EF üretmez): `ENABLE`+`FORCE ROW LEVEL
   SECURITY` + `CREATE POLICY tenant_isolation … USING/WITH CHECK (NULLIF(current_setting
   ('app.tenant_id',true),'')::uuid)` + `GRANT SELECT, INSERT, UPDATE, DELETE ON "JobCalismaLoglari"
   TO racar_app`.
6. `src/RentACar.Infrastructure/Persistence/FiloBildirimUretici.cs`,
   `VadeBildirimUretici.cs`, `OperasyonOzetUretici.cs`, `DonemFaturaUretici.cs` — her birinin
   `RunAsync(db, tenant, now)` metodunun sonunda (başarı/hata her ikisi de) `JobCalismaLog` satırı
   ekleyen ince bir çağrı ekle (`try/catch` ile sarılıp `Basarili=false, Detay=ex.Message` yazan
   yol dahil — üreticinin kendi hata yönetimini BOZMADAN, yalnız log ekler). Yetki yüzeyi büyümez —
   `Uretici.RunAsync` imzası değişmez, servis katmanına girmez.
7. Yeni `src/RentACar.Web/Components/Pages/Reports/OtomatikServisler.razor` — Tarih filtreli liste
   (JobTuru/Tarih/Detay/Başarılı kolonları) + `MainLayout.razor` nav girişi (Raporlar altına).
8. Küçük bir `IJobCalismaLogRepository`/repository + `/raporlar/otomatik-servisler` endpoint (ya
   da doğrudan sayfa içinde `IDbContextFactory` ile okuma — mevcut rapor sayfalarının deseni neyse
   ona uyulur).

## Dokunulacak dosyalar
- (yeni) `src/RentACar.Domain/Entities/JobCalismaLog.cs`
- (yeni) enum `JobTuru`
- (yeni) config sınıfı
- (yeni) migration dosyası (RLS bloğu elle)
- `src/RentACar.Infrastructure/Persistence/FiloBildirimUretici.cs`
- `src/RentACar.Infrastructure/Persistence/VadeBildirimUretici.cs`
- `src/RentACar.Infrastructure/Persistence/OperasyonOzetUretici.cs`
- `src/RentACar.Infrastructure/Persistence/DonemFaturaUretici.cs`
- (yeni) `src/RentACar.Web/Components/Pages/Reports/OtomatikServisler.razor` + nav

## Migration
Var — **yeni tablo** `JobCalismaLoglari`. RLS bloğu **ELLE eklenir** (madde 5).

## Test
- Yeni `tests/RentACar.IntegrationTests/JobCalismaLogTests.cs`: her 4 üreticinin `RunAsync`'i
  çağrılır (mevcut üretici testlerindeki fixture'lar kullanılır); koşu sonrası log tablosunda
  **elle beklenen sayıda** (bağımsız oracle — "4 üretici çalıştı, 4 log satırı beklerim") satır
  olduğu doğrulanır. Hata enjekte edilen bir senaryoda `Basarili=false` + `Detay` dolu olduğu
  doğrulanır.
- Tenant izolasyonu `racar_app` ile (başka tenant'ın log satırı görünmüyor).
- Filtre testi: Tarih aralığı dışındaki log görünmüyor.

## Exit
- [ ] 4 üretici koşusu sonrası log tablosunda satır var (başarı + hata yolu ikisi de test edildi)
- [ ] `/raporlar/otomatik-servisler` Tarih filtreli listeliyor
- [ ] Üretici sınıflarının mevcut davranışı (bildirim üretimi) REGRESYONSUZ (mevcut testler yeşil)
- [ ] Tam suite yeşil

## Notlar
Log yazımı üreticinin ANA işini bloklamayacak şekilde eklenir — log yazımı hata verse bile
üreticinin kendi işi (bildirim/fatura üretimi) devam etmeli (log-yazma hatası yutulur, sessizce
loglanır, üreticinin kendi sonucunu etkilemez).
