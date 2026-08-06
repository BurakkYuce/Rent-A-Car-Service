# FAZ-15 — Araç Sigorta Zeyil Alt-Sistemi

| | |
|---|---|
| **Desen** | D5 — para hareketi/defter etkili derinlik (yeni tablo + değer alanları; bu fazda ledger'a henüz YAZMIYOR ama D5 sınıfına giriyor) |
| **Efor** | 2,5 gün |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `arac_sigorta_islemleri.aspx` |
| **Risk** | orta — yeni tablo + RLS; defter posting'e henüz bağlı değil (Opus kararı bekliyor) |
| **Zorunlu** | adversarial inceleme — Critical/High/Medium bulgu kalmadan commit yok |

## Amaç
Kullanıcı bir sigorta poliçesine kalıcı **Zeyil** (poliçe eki) geçmişi ekleyip listeleyebilir/
silebilir (9 kolonlu: No/Tarih/Tanzim/Değer/Brüt/Net/Fon-Vergi/Tipi/Neden); poliçeye sigorta
değer tabanı (`AracDegeri`/`ImmDegeri`/`AksesuarDegeri`) ve kalan bakiye (`Kalan`) eklenir.

## Neden (kanıt)
`arac_sigorta_islemleri.aspx`: K2=%20 (7/35), K3=%0 (0/9 — canlı grid Zeyil No/Tarih/Tanzim/
Değer/Bürüt/Net/Fon-Vergi/Tipi/Neden **TAMAMEN zeyil alt-tablosu**; bizde zeyil kaydı yok).
**Zeyil (poliçe eki) yönetimi tamamen yok** — canlıda ayrı Zeyil Listele/Yeni Zeyil/Zeyil
Kayıt Sil aksiyonları + 9 kolonlu geçmiş var; bizde ödeme formunda tek bir "zeyil ek prim"
sayısı (`InsurancePolicy.ZeyilPrim`) var, kalıcı zeyil kaydı/geçmişi yok.
`Arac_Degeri`/`IMM_Degeri`/`Aksesuar_Degeri` (sigorta değer tabanı) ve `Kalan` (bakiye) yok.

## Yapılacaklar
1. Yeni `src/RentACar.Domain/Entities/InsurancePolicyZeyil.cs` — tenant-owned + auditable:
   `PolicyId` (Guid, FK), `ZeyilNo` (string), `Tarih` (DateTimeOffset), `Tanzim`
   (DateTimeOffset?), `Deger` (decimal), `Brut` (decimal), `Net` (decimal), `FonVergi`
   (decimal), `Tipi` (string?), `Neden` (string?).
2. `src/RentACar.Domain/Entities/InsurancePolicy.cs` — 3 yeni additive alan:
   `AracDegeri`/`ImmDegeri`/`AksesuarDegeri` (`decimal?`) + `Kalan` (`decimal`, oluşturulunca
   `=Prim+ZeyilPrim`).
3. `src/RentACar.Infrastructure/Persistence/Configurations/RegulationConfigs.cs` — yeni
   `InsurancePolicyZeyilConfig` (composite tenant-FK `PolicyId`→`InsurancePolicy`) +
   `InsurancePolicy` config genişlet.
4. Migration: `dotnet ef migrations add AddInsurancePolicyZeyil --project
   src/RentACar.Infrastructure --startup-project src/RentACar.Infrastructure`. **YENİ TABLO →
   RLS bloğu ELLE eklenir** (EF üretmez, CLAUDE.md §5 adım 4): `ENABLE`+`FORCE ROW LEVEL
   SECURITY` + `CREATE POLICY tenant_isolation … USING/WITH CHECK
   (NULLIF(current_setting('app.tenant_id',true),'')::uuid)` + `GRANT SELECT, INSERT, UPDATE,
   DELETE ON "InsurancePolicyZeyiller" TO racar_app`.
5. `src/RentACar.Application/Regulation/IRegulationRepository.cs` —
   `ListZeyilAsync(policyId)`/`AddZeyilAsync`/`DeleteZeyilAsync` ekle.
6. `src/RentACar.Infrastructure/Persistence/Repositories/RegulationRepository.cs` —
   implementasyon.
7. `src/RentACar.Application/Regulation/RegulationService.cs` — Zeyil CRUD servis metodları
   (`OperationsWrite` guard).
8. `src/RentACar.Web/Components/Pages/Regulation/RegulationList.razor` (Sigorta bölümü) —
   "Zeyil" alt-liste/form (Listele/Yeni Zeyil/Sil) + `AracDegeri`/`ImmDegeri`/`AksesuarDegeri`/
   `Kalan` alanları poliçe formuna.
9. `src/RentACar.Web/Regulation/RegulationEndpoints.cs` — Zeyil CRUD POST uçları.
10. **Bu fazın kapsamı DIŞINDA:** Zeyil'in kendi defter kaydını postlaması (her zeyil kalıcı
    bir prim artışı mı, deftere ne zaman düşer) — **PARA — Opus** kararı; bu fazda Zeyil
    kaydı yalnız BİLGİ/GEÇMİŞ amaçlıdır, `SigortaOdeAsync`'in mevcut posting mantığına
    BAĞLANMAZ.

## Dokunulacak dosyalar
- (yeni) `src/RentACar.Domain/Entities/InsurancePolicyZeyil.cs`
- `src/RentACar.Domain/Entities/InsurancePolicy.cs` (3 yeni alan)
- `src/RentACar.Infrastructure/Persistence/Configurations/RegulationConfigs.cs` (yeni config)
- (yeni) migration dosyası (`AddInsurancePolicyZeyil`) — **RLS bloğu elle**
- `src/RentACar.Application/Regulation/IRegulationRepository.cs`, `RegulationService.cs`
- `src/RentACar.Infrastructure/Persistence/Repositories/RegulationRepository.cs`
- `src/RentACar.Web/Components/Pages/Regulation/RegulationList.razor`
- `src/RentACar.Web/Regulation/RegulationEndpoints.cs`

## Migration
Var — **yeni tablo** `InsurancePolicyZeyiller` → RLS bloğu ELLE eklenir (yukarıdaki adım 4).
`InsurancePolicy`'ye 4 additive kolon (mevcut tablo, RLS zaten aktif, ek bloğu gerekmez).

## Test
- `RegulationServiceTests` — Zeyil CRUD: bağımsız oracle — 1 poliçeye 3 zeyil elle eklenir,
  listelenince **3** sonuç bekleniyor (sabit); 1'i silinince **2** (sabit).
- Tenant izolasyon: `racar_app` ile 2. tenant'ın zeyili görünmemeli — **yeni tablo** olduğu
  için `ModelGuardTests`'in genel kapsamına ek olarak bu tabloya ÖZEL bir izolasyon testi
  yazılır (RLS FORCE + owner-bypass-yok kontrolü).
- Defter dengesi/idempotency testi **gerekmiyor** (bu faz posting yapmıyor) — ama regresyon
  testi Zeyil eklemenin `SigortaOdeAsync`'in defter çıktısını DEĞİŞTİRMEDİĞİNİ doğrular.

## Exit
- [ ] Zeyil CRUD çalışıyor, 9 kolonlu geçmiş listeleniyor
- [ ] `AracDegeri`/`ImmDegeri`/`AksesuarDegeri`/`Kalan` poliçe formunda görünüyor
- [ ] Yeni tabloda RLS FORCE aktif, `racar_app` ile tenant-izolasyon testi yeşil
- [ ] Zeyil eklemek mevcut sigorta ödeme defter çıktısını değiştirmiyor (regresyon)
- [ ] **Adversarial inceleme tamamlandı, Critical/High/Medium bulgu yok**
- [ ] Tam suite yeşil

## Notlar
Yabancı para poliçede kur zorunlu-doğrulama davranışı (boşsa TCMB/sabit kur otomatik,
bulunamazsa red) bizde ZATEN VAR, canlıda yok — bu faz bu davranışı BOZMAMALI (regresyon
testiyle korunmalı). Zeyil'in prime nasıl ekleneceği (kalıcı geçmiş mi, her zeyil kendi defter
kaydını mı postlar) ve `AracDegeri`/`ImmDegeri`'nin herhangi bir hesaba (hasar/rücu tavanı)
girip girmeyeceği — PARA — Opus, ayrı bir takip fazı gerektirir.
