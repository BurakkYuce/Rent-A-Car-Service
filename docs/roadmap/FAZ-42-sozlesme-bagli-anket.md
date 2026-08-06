# FAZ-42 — Sözleşme-Bağlı Çıkış/Dönüş Anketi (Yeni Dikey)

| | |
|---|---|
| **Desen** | D7 — yeni dikey |
| **Efor** | 4 gün |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `anket_listesi.aspx` |
| **Risk** | orta — yeni child-tablo (`AnketCevap`) RLS kurulumu gerektiriyor; para/defter dokunmuyor
  ama hatalı RLS policy tenant-sızıntısına yol açar (CLAUDE.md §4 ikinci savunma katmanı) |

## Amaç
Kullanıcı bir kira sözleşmesine (çıkış veya dönüş anında) bağlı, 8 sorudan oluşan yapılandırılmış bir
anket kaydedebilir ve `/anketler`'de Cari/Anket Türü/Durum/Tarih/Çıkış Ofisi ile arayıp filtreleyebilir
hale gelir — bugünkü sözleşmeden bağımsız genel "Puan/Yorum" geri-bildirim modelinden ayrı, canlı
paritesine yakın yeni bir dikey.

## Neden (kanıt)
`src/RentACar.Domain/Entities/Anket.cs` şu an `Id, TenantId, CariId, Puan, Yorum, Tarih, Kaynak` taşıyor
— sözleşmeden TAMAMEN bağımsız, tekil "puan+yorum" modeli. Canlı ekran ise **sözleşmeye bağlı,
8-soru yapılandırılmış** çıkış/dönüş anketi — farklı bir iş modeli, mevcut `Anket` tablosuna alan
eklemek yeterli değil (8 soru düz kolon olarak eklenirse soru sayısı değişince migration gerekir —
CLAUDE.md §5 desenine uygun normalize child-tablo tercih edilir). `AnketList.razor` (satır 15) grid'i
yalnız Tarih/Puan/Yorum/Kaynak gösteriyor, filtre yok.

## Yapılacaklar
1. `src/RentACar.Domain/Entities/Anket.cs` — additive alanlar ekle: `RentalId` (Guid?, `RentalContract`
   FK), `AnketTuru` (enum `Cikis`/`Donus`, yeni `src/RentACar.Domain/Enums/AnketTuru.cs`), `Durum`
   (enum `Yapildi`/`Yapilmadi`, yeni `src/RentACar.Domain/Enums/AnketDurum.cs`), `CikisOfisi`
   (string?, `RentalContract.CikisOfisi`'nden snapshot).
2. Yeni `src/RentACar.Domain/Entities/AnketCevap.cs` — `ITenantOwned, IAuditable`: `Id`, `TenantId`,
   `AnketId` (Guid, `Anket` FK), `SoruNo` (int), `Soru` (string), `Cevap` (string), `Aciklama`
   (string?). 8 satıra kadar (uygulama tarafında UI uyarısı, DB'de zorlanmaz).
3. `src/RentACar.Infrastructure/Persistence/Configurations/CustomerConfigs.cs` — mevcut `AnketConfig`
   sınıfına (satır 140) 4 yeni kolon ekle; yeni `AnketCevapConfig` sınıfı ekle
   (`HasOne/WithMany` Anket'e FK, `HasIndex(TenantId, AnketId)`).
4. Migration: `dotnet ef migrations add AddAnketSozlesmeBagliVeCevap --project
   src/RentACar.Infrastructure --startup-project src/RentACar.Infrastructure`.
5. **`AnketCevap` YENİ TABLO — RLS bloğunu migration'a ELLE ekle** (EF üretmez, CLAUDE.md §5):
   `ENABLE ROW LEVEL SECURITY` + `FORCE ROW LEVEL SECURITY` + `CREATE POLICY tenant_isolation …
   USING/WITH CHECK (NULLIF(current_setting('app.tenant_id',true),'')::uuid)` +
   `GRANT SELECT, INSERT, UPDATE, DELETE ON … TO racar_app` (mali belge değil, tam CRUD).
6. `src/RentACar.Application/Crm/ICrmRepositories.cs` — **`IAnketRepository` ZATEN VAR** (mevcut
   `ListAsync`/`FindAsync`/`CreateAsync`/`UpdateAsync`/`DeleteAsync`, roadmap C3'ten) — yeni dosya
   AÇMA. Arayüze `ListAsync(AnketFilter filtre, CancellationToken ct = default)` overload'u +
   `CreateWithCevapAsync(Anket anket, List<AnketCevap> cevaplar, CancellationToken ct = default)`
   (Anket + 8 AnketCevap tek transaction) ekle.
7. `src/RentACar.Infrastructure/Persistence/Repositories/CrmRepositories.cs` — mevcut
   `AnketRepository` sınıfına (satır 8) 6'daki iki metodu implemente et (`ListAsync` filtre
   overload'u `db.Anketler.Where(...)`; `CreateWithCevapAsync` `db.Anketler.Add` + `db.AnketCevaplari
   .AddRange` + tek `SaveChangesAsync`).
8. `src/RentACar.Application/Crm/CrmModels.cs` — mevcut `AnketInput`'a 4 yeni alan (`RentalId`,
   `AnketTuru`, `Durum`, `CikisOfisi`) ekle; yeni `AnketFilter` (Cari, AnketTuru, Durum, Tarih
   aralığı, CikisOfisi) + `AnketCevapInput` (SoruNo, Soru, Cevap, Aciklama) sınıflarını ekle.
9. `src/RentACar.Application/Crm/AnketService.cs` — mevcut `CreateAsync`/`UpdateAsync`'e 4 yeni alanı
   `Apply` metodunda map et; yeni `SearchAsync(AnketFilter, ct)` + `CreateWithCevapAsync(AnketInput,
   List<AnketCevapInput>, ct)` metodlarını ekle (ikincisi `PermissionGuard.Require(OperationsWrite)`
   sonrası repository'nin `CreateWithCevapAsync`'ini çağırır).
10. `src/RentACar.Web/Components/Pages/Crm/AnketList.razor` — filtre barı (Cari, Anket Türü, Durum,
    Tarih aralığı, Çıkış Ofisi) + form'a `RentalId` seçici (sözleşme no ile ComboBox) + `AnketTuru`/
    `Durum` select + 8 soru için sabit input grubu (`Soru` metni sabit katalogdan, `Cevap`/`Aciklama`
    kullanıcı girer) + liste kolonlarına Sözleşme No, Anket Türü, Durum, Çıkış Ofisi ekle.

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/Anket.cs` — 4 alan
- (yeni) `src/RentACar.Domain/Entities/AnketCevap.cs`
- (yeni) `src/RentACar.Domain/Enums/{AnketTuru.cs,AnketDurum.cs}`
- `src/RentACar.Infrastructure/Persistence/Configurations/CustomerConfigs.cs` — `AnketConfig` (satır
  140) + yeni `AnketCevapConfig`
- (yeni) migration `AddAnketSozlesmeBagliVeCevap` (RLS bloğu dahil)
- `src/RentACar.Application/Crm/ICrmRepositories.cs` — `IAnketRepository` (mevcut arayüz, yeni
  overload'lar)
- `src/RentACar.Infrastructure/Persistence/Repositories/CrmRepositories.cs` — `AnketRepository`
  (satır 8, mevcut sınıf)
- `src/RentACar.Application/Crm/CrmModels.cs` (`AnketInput` genişlet + yeni `AnketFilter`/
  `AnketCevapInput`)
- `src/RentACar.Application/Crm/AnketService.cs` (mevcut sınıf, yeni metodlar)
- `src/RentACar.Web/Components/Pages/Crm/AnketList.razor`

## Migration
Var — `Anket`'e (mevcut tenant-owned tablo, RLS zaten aktif, dokunmaz) 4 nullable kolon +
**yeni** `AnketCevap` tablosu (RLS bloğu ELLE eklenir, madde 5).

## Test
- `AnketTests` (mevcut varsa genişlet, yoksa yeni): 4 yeni alan round-trip + `AnketCevap` 8 satır
  create→oku (bağımsız oracle — elle kurulan 8 soru/cevap listesiyle karşılaştır, sıra `SoruNo`'ya
  göre).
- `AnketCevapTests` (yeni): CRUD + **tenant izolasyonu `racar_app` ile** (CLAUDE.md §8 — başka
  tenant'ın cevap satırı görünmüyor; 2 tenant'ta aynı `AnketId` benzeri veri elle kurulup çapraz
  sorgu 0 satır dönmeli).
- Filtre testi: 3 anket (2 farklı tür [Cikis/Donus], 2 farklı durum) elle oluşturulur; `AnketTuru=Cikis`
  filtresiyle arama yalnız 2 kaydı döndürüyor mu (beklenen sayı testte sabit).

## Exit
- [ ] `/anketler` formunda sözleşme seçimi + 8 soru girişi çalışıyor, kaydediliyor
- [ ] `AnketCevap` tenant izolasyonu doğrulandı (racar_app ile çapraz-tenant testi geçiyor)
- [ ] Filtre barı (Cari/Tür/Durum/Tarih/Çıkış Ofisi) çalışıyor
- [ ] Tam suite yeşil

## Notlar
Eski "puan+yorum" `Anket` kullanım yolu (varsa) kırılmamalı — yeni alanlar hepsi nullable, mevcut
sözleşmesiz anketler `RentalId=null` ile çalışmaya devam eder (geriye dönük uyumluluk, veri kaybı
yok). 8 sorunun metni sabit mi (kod içinde) yoksa tenant bazlı özelleştirilebilir mi olacağı bu
fazda KARARLAŞTIRILMADI — plan "SoruNo/Soru/Cevap/Aciklama" child-modelini önerdiği için `Soru`
metni her `AnketCevap` satırında saklanabilir (tenant kendi soru setini değiştirebilir); sabit
katalogdan başlatılıp kullanıcı override edebilir şeklinde en esnek yol seçildi.
