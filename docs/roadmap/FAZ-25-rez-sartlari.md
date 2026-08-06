# FAZ-25 — Rez Şartları (Yeni Küçük Dikey)

| | |
|---|---|
| **Desen** | D1 — yeni tenant-owned tablo, CLAUDE.md §5 reçetesi tam uygulanır |
| **Efor** | 1 gün |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `rezsartlar.aspx` |
| **Risk** | düşük — para/model değişikliği yok, saf yeni tablo |

## Amaç
Müşterinin rezervasyon/teklif üzerindeki özel talep ve şartlarını (ör. "çocuk koltuğu", "erken
teslim") kayıt altına alıp karşılanma durumunu takip eden yeni bir ekran ile kullanıcı bu talepleri
müşteri/tarih/durum filtreli bir listede görüp yönetebilir hale gelir.

## Neden (kanıt)
Repoda `RezSart` adında bir entity/servis/sayfa **yok** (grep ile doğrulandı — hiçbir dosya
bulunmadı). Canlı TürevRent'te `rezsartlar.aspx` müşteri özel taleplerini ayrı bir listede tutuyor;
bizde karşılığı yok.

## Yapılacaklar
1. Yeni `src/RentACar.Domain/Entities/RezSart.cs` — `ITenantOwned, IAuditable`: `Id` (Guid,
   ValueGeneratedNever), `TenantId` (Guid), `MusteriId` (Guid, Customer FK — additive Guid, FK
   constraint opsiyonel), `Grup` (string?), `Sart` (string, özel talep metni, zorunlu), `BasTar`/
   `BitTar` (DateTimeOffset?), `TalepTarihi` (DateTimeOffset, default `UtcNow`), `KarsilamaTarihi`
   (DateTimeOffset?, null=henüz karşılanmadı), `TeslimEden` (string?, personel adı — serbest metin,
   PII değil), `ReservationId`/`QuotationId` (Guid?, gevşek bağlama — FK YOK, additive), audit
   timestamp'leri (`CreatedAtUtc`, `UpdatedAtUtc?`).
2. `src/RentACar.Infrastructure/Persistence/Configurations/` altında ilgili alan-dosyasına (ör.
   `CustomerConfigs.cs` — Customer'a bağlı bir kavram olduğundan, ya da yeni bir `RezSartConfigs.cs`)
   `internal sealed class RezSartConfig : IEntityTypeConfiguration<RezSart>` ekle: kolon tipleri,
   `HasIndex(TenantId, MusteriId)`, `HasIndex(TenantId, KarsilamaTarihi)` (durum filtresi için).
3. Migration: `dotnet ef migrations add AddRezSart --project src/RentACar.Infrastructure
   --startup-project src/RentACar.Infrastructure`.
4. **Migration'a RLS bloğunu ELLE ekle** (EF üretmez, CLAUDE.md §5): `ALTER TABLE "RezSartlar"
   ENABLE ROW LEVEL SECURITY;` + `FORCE ROW LEVEL SECURITY;` + `CREATE POLICY tenant_isolation ON
   "RezSartlar" USING/WITH CHECK ("TenantId" = NULLIF(current_setting('app.tenant_id', true),
   '')::uuid);` + `GRANT SELECT, INSERT, UPDATE, DELETE ON "RezSartlar" TO racar_app;` (mali belge
   değil, tam CRUD grant).
5. Yeni `src/RentACar.Application/RezSartlar/IRezSartRepository.cs` + `RezSartRepository.cs`
   (Infrastructure, `IDbContextFactory<AppDbContext>`, `AsNoTracking`).
6. Yeni `src/RentACar.Application/RezSartlar/RezSartService.cs` — doğrulama (Sart boş olamaz,
   MusteriId geçerli müşteriye ait olmalı) + `PermissionGuard.Require(user, Permission.OperationsWrite)`.
7. DI: `src/RentACar.Application/DependencyInjection.cs` (`AddScoped<RezSartService>`) +
   `src/RentACar.Infrastructure/DependencyInjection.cs` (`AddScoped<IRezSartRepository,
   RezSartRepository>`).
8. Yeni `src/RentACar.Web/Components/Pages/RezSartlar/RezSartList.razor` — liste (müşteri/tarih/
   durum-Karşılandı-mı filtreli) + create/edit form + `src/RentACar.Web/RezSartlar/
   RezSartEndpoints.cs` + `Program.cs`'e `MapRezSartEndpoints()` + `_Imports.razor` using +
   `MainLayout.razor` nav girişi.

## Dokunulacak dosyalar
- (yeni) `src/RentACar.Domain/Entities/RezSart.cs`
- (yeni) config sınıfı (Configurations altında)
- (yeni) migration dosyası (RLS bloğu elle)
- (yeni) `src/RentACar.Application/RezSartlar/IRezSartRepository.cs`, `RezSartService.cs`
- (yeni) `src/RentACar.Infrastructure/Persistence/Repositories/RezSartRepository.cs`
- (yeni) `src/RentACar.Web/Components/Pages/RezSartlar/RezSartList.razor`
- (yeni) `src/RentACar.Web/RezSartlar/RezSartEndpoints.cs`
- `src/RentACar.Web/Program.cs`, `_Imports.razor`, `MainLayout.razor` — wiring

## Migration
Var — **yeni tablo** `RezSartlar`. RLS bloğu **ELLE eklenir** (madde 4).

## Test
- Yeni `tests/RentACar.IntegrationTests/RezSartTests.cs`: CRUD + benzersizlik (yok, serbest metin) +
  **tenant izolasyonu `racar_app` ile** (başka tenant'ın rez şartı görünmüyor — CLAUDE.md §8) +
  yetki (Operator OperationsWrite ile ekleyebilir, salt-okur rol ekleyemez).
- Filtre testi (bağımsız oracle): 3 kayıt elle oluşturulur (2 karşılanmış, 1 karşılanmamış); durum
  filtresi "Karşılanmadı" seçilince elle bilinen 1 kayıt dönmeli (sayı testte sabit).

## Exit
- [ ] `RezSart` CRUD çalışıyor, liste müşteri/tarih/durum filtreli
- [ ] Tenant izolasyonu `racar_app` ile doğrulandı
- [ ] Tam suite yeşil

## Notlar
`ReservationId`/`QuotationId` FK DEĞİL, gevşek Guid? bağlama — CLAUDE.md §2 additive prensibiyle
uyumlu, mevcut rezervasyon/teklif akışına dokunmadan bağlanabilir.
