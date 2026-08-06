# FAZ-45 — Personel Çalışma/Vardiya Raporu (Yeni Dikey)

| | |
|---|---|
| **Desen** | D7 — yeni dikey |
| **Efor** | 3 gün |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `personel_calisma_grafigi.aspx` |
| **Risk** | orta — tamamen yeni entity/tablo, CLAUDE.md §5 tam reçetesi (RLS dahil); para/defter
  dokunmuyor |

## Amaç
Kullanıcı personel için şube+tarih bazlı vardiya (başlangıç/bitiş saati) kaydedebilir ve
`/raporlar/personel-calisma`'da şube+tarih filtreli personel×gün matris görünümüyle vardiya planını
görebilir hale gelir.

## Neden (kanıt)
İsim benzerliği var ("Personel Çalışma") ama iş tamamen farklı: canlı ekran şube+tarih bazlı
personel çalışma/vardiya grafiği; bizdeki `/crm`'deki "Personel Çalışma (BAF)" tablosu
(`CrmAnaliz.razor` satır 33, `<th>Personel</th><th>Tahsis Sayısı</th>`) personelin araç tahsis
sayısını gösteriyor — ilgisiz bir metrik, bu faz ONA dokunmaz. Vardiya/çalışma saati kavramı
(`PersonelVardiya` benzeri bir entity) grep'te **sıfır isabet** — tamamen yeni bir dikey gerekir.

## Yapılacaklar
1. Yeni `src/RentACar.Domain/Entities/PersonelVardiya.cs` — `ITenantOwned, IAuditable,
   IBranchScoped`: `Id`, `TenantId`, `PersonelId` (Guid, `Personel` FK), `Tarih` (DateOnly veya
   `DateTimeOffset`, gün hassasiyeti), `SubeId` (Guid?, `Branch` FK — `IBranchScoped` deseniyle
   metin `Sube` de tutulabilir ama FK öncelikli, CLAUDE.md §6 FAZ-5 C-serisi kararıyla tutarlı),
   `BaslangicSaat` (TimeOnly veya `TimeSpan`), `BitisSaat` (TimeOnly veya `TimeSpan`), `Aciklama`
   (string?).
2. `src/RentACar.Infrastructure/Persistence/Configurations/CustomerConfigs.cs` (ya da yeni ayrı
   `PersonnelConfigs.cs` — yeni bir dikey olduğundan ayrı dosya AÇILABİLİR, tercih: ayrı dosya, tarihsel
   "CRM kümesi" dosyasını büyütmemek için) — yeni `PersonelVardiyaConfig` sınıfı
   (`HasIndex(TenantId, PersonelId, Tarih)` — aynı personelin aynı gün birden çok vardiyası olabilir,
   UNIQUE değil).
3. Migration: `dotnet ef migrations add AddPersonelVardiya --project src/RentACar.Infrastructure
   --startup-project src/RentACar.Infrastructure`.
4. **YENİ TABLO — RLS bloğunu migration'a ELLE ekle** (EF üretmez, CLAUDE.md §5):
   `ENABLE ROW LEVEL SECURITY` + `FORCE ROW LEVEL SECURITY` + `CREATE POLICY tenant_isolation …
   USING/WITH CHECK (NULLIF(current_setting('app.tenant_id',true),'')::uuid)` +
   `GRANT SELECT, INSERT, UPDATE, DELETE ON … TO racar_app`.
5. Yeni `src/RentACar.Application/Personnel/IPersonelVardiyaRepository.cs`
   (`SearchAsync(SubeId?, TarihBas, TarihBit, BranchFilter kapsam, ct)`, `CreateAsync`,
   `UpdateAsync`, `DeleteAsync`) +
   `src/RentACar.Infrastructure/Persistence/Repositories/PersonelVardiyaRepository.cs`
   (`IDbContextFactory<AppDbContext>`, `AsNoTracking`).
6. Yeni `src/RentACar.Application/Personnel/PersonelVardiyaService.cs` — `PermissionGuard.Require(
   OperationsWrite)` guard'lı CRUD; `BranchScope.Effective(user)` ile Operatör kendi şubesini,
   diğerleri tümünü görür (CLAUDE.md §4 Yetki bölümü).
7. Yeni `src/RentACar.Web/Components/Pages/Reports/PersonelCalismaTablosu.razor` —
   `/raporlar/personel-calisma`: Şube + Tarih (Baş./Bit.) filtresi, personel×gün matris görünümü
   (satır=personel, sütun=gün, hücre=BaslangicSaat-BitisSaat aralığı), vardiya ekle/sil formu.
8. `src/RentACar.Web/Components/Layout/MainLayout.razor` — Raporlar nav grubuna
   `<a href="/raporlar/personel-calisma">Personel Çalışma</a>` ekle.
9. DI: `src/RentACar.Application/DependencyInjection.cs`
   (`AddScoped<PersonelVardiyaService>`) +
   `src/RentACar.Infrastructure/DependencyInjection.cs`
   (`AddScoped<IPersonelVardiyaRepository, PersonelVardiyaRepository>`).

## Dokunulacak dosyalar
- (yeni) `src/RentACar.Domain/Entities/PersonelVardiya.cs`
- (yeni) `src/RentACar.Infrastructure/Persistence/Configurations/PersonnelConfigs.cs` (veya mevcut
  bir config dosyasına ekleme — implementasyon anında karar verilir)
- (yeni) migration `AddPersonelVardiya` (RLS bloğu dahil)
- (yeni) `src/RentACar.Application/Personnel/IPersonelVardiyaRepository.cs`
- (yeni) `src/RentACar.Infrastructure/Persistence/Repositories/PersonelVardiyaRepository.cs`
- (yeni) `src/RentACar.Application/Personnel/PersonelVardiyaService.cs`
- (yeni) `src/RentACar.Web/Components/Pages/Reports/PersonelCalismaTablosu.razor`
- `src/RentACar.Web/Components/Layout/MainLayout.razor` — nav
- `src/RentACar.Application/DependencyInjection.cs`, `src/RentACar.Infrastructure/DependencyInjection.cs`

## Migration
Var — **yeni** `PersonelVardiya` tablosu, RLS bloğu ELLE eklenir (madde 4).

## Test
- `PersonelVardiyaTests` (yeni): CRUD round-trip (bağımsız oracle — elle kurulan vardiya nesnesi).
  **Tenant izolasyonu `racar_app` ile** (CLAUDE.md §8) — 2 tenant'ta aynı personel/tarih için vardiya
  elle kurulur, çapraz sorgu 0 satır dönmeli.
- Şube kapsamı testi: Operatör (AssignedBranch=Şube A) 3 vardiyadan (2 Şube A, 1 Şube B) yalnız 2'sini
  görüyor mu (beklenen sayı testte sabit, `BranchScope.Effective` çağrısından değil elle kurulan
  senaryodan).
- Matris testi: 2 personel × 3 gün için 6 vardiya elle kurulur; rapor sorgusu 6 hücreyi doğru
  personel/gün eşleşmesiyle döndürüyor mu (bağımsız oracle).

## Exit
- [ ] `/raporlar/personel-calisma` açılıyor, vardiya ekle/sil çalışıyor
- [ ] Şube + Tarih filtresi + personel×gün matris görünümü çalışıyor
- [ ] Tenant izolasyonu ve şube kapsamı doğrulandı
- [ ] Tam suite yeşil

## Notlar
`/crm`'deki mevcut "Personel Çalışma (BAF)" (araç tahsis sayısı) ile bu YENİ `/raporlar/
personel-calisma` (vardiya saatleri) **AYNI İSİMDE AMA FARKLI İŞLEV** — ikisi de kalır, birbirine
dokunulmaz; kullanıcı arayüzünde iki farklı menü etiketiyle karışıklık önlenmeli (ör. "Personel
Çalışma (Tahsis)" vs "Personel Çalışma (Vardiya)").
