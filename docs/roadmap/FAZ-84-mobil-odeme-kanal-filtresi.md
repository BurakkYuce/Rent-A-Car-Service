# FAZ-84 — Mobil/Tablet Tahsilat: Kanal Filtresi (Yapısal)

| | |
|---|---|
| **Desen** | D3 (yapısal kısım) — kanal alanının hangi tabloya yazılacağı **PARA — Opus** onayı ister |
| **Efor** | 0,5 gün (yapısal: form alanı + grid filtresi) — şema kararı sonrası kablolama AYRI efor |
| **Bağımlılık** | **PARA — Opus onayı** (kanal etiketinin hangi tabloya yazılacağı) — onay gelmeden şema DEĞİŞMEZ, bu faz yalnız ONAY SONRASI uygulanacak somut adımları hazırlar |
| **Kapsanan canlı ekran** | `mobil_odeme.aspx` |
| **Risk** | orta — para-tutan tabloya (`CashTransactions`) kolon eklemek; **Zorunlu:** adversarial inceleme — Critical/High/Medium bulgu kalmadan commit yok (CLAUDE.md §3 madde 5) |

## Amaç
`/kasa` genel Kasa/Banka defterine, tahsilatın hangi kanaldan (Masaüstü/Mobil/Tablet) yapıldığını
gösteren bir filtre eklenir — canlının ayrı "mobil/tablet tahsilat" ekranına eşdeğer, ayrı ekran
açmadan.

## Neden (kanıt)
`src/RentACar.Web/Components/Pages/Finance/KasaHub.razor` (`@page "/kasa"`) tüm Kasa/Banka
işlemlerini kapsıyor ama hiçbir "kanal" bilgisi taşımıyor/göstermiyor. Tahsilat/ödeme giriş
formları `src/RentACar.Web/Finance/FinanceEndpoints.cs` satır 90-133 (`/finans/tahsilat`,
`/finans/odeme`) `CashInput` (`src/RentACar.Application/Finance/CashInput.cs`, 22 satır) alıyor —
bu sınıfta `CariId/RentalId/Tutar/Doviz/Kur/Tarih/Aciklama/Hesap/IslemAnahtari` var, **Kanal
alanı yok**.

**Şema araştırması (bu fazı hazırlarken netleştirildi — plandaki "karar verilmeyen kısım"a somut
öneri):** `CashService.cs` satır 57-86 (`PostCashAsync`), her tahsilat/ödeme İKİ farklı tabloya
yazıyor: (1) `CashTransaction` (`src/RentACar.Domain/Entities/CashTransaction.cs`, 38 satır) —
tenant-owned "belge" (No/Tip/CariId/RentalId/Tarih/Amount/KarsiHesap/Aciklama/TersKayitMi/
IslemAnahtari), (2) `AccountLedgerEntry` çift-taraflı defter satırları (`_repository.
PostAsync(tx, entries, ct)`, satır 83). `CashTransactions` tablosu **immutable-trigger'lı**
(`rc_prevent_mutation()`, ilk migration `20260626215743_AddCashAndLedger.cs` +
`20260626223603_AddAuditAndReversalGuards.cs`) — yani CREATE'te yazılıp bir daha DEĞİŞMEZ, tıpkı
defter satırları gibi. **Kanal bilgisi `CashTransaction`'a eklenirse:** (a) `AccountLedgerEntry`
şemasına hiç dokunulmaz → Σ Borç=Σ Alacak dengesi/RLS/immutability'nin hiçbiri risk almaz, çünkü
Kanal saf BİLGİ alanıdır, iki taraflı kayda katkısı yoktur; (b) mevcut immutability zaten Kanal'ın
da CREATE-sonrası değişmeyeceğini garanti eder (tutarlılık). `AccountLedgerEntry`'ye eklemek ise
defter şemasını (çok daha geniş, rapor/karne/P&L'in TEK kaynağı — CLAUDE.md §4 "P&L yalnız
defterden") gereksiz genişletir. **Bu fazın Opus'a taşıdığı öneri: Kanal → `CashTransaction`,
`AccountLedgerEntry`'ye DOKUNULMASIN.**

## Yapılacaklar
**(Adım 1-2 Opus onayından ÖNCE de yapılabilir — saf yapısal, şema kararına bağlı değil.)**
1. `src/RentACar.Web/Components/Pages/Finance/KasaHub.razor` grid'ine "Kanal" kolonu + üstte bir
   filtre `<select>` (Tümü/Masaüstü/Mobil/Tablet) eklenir — **şema alanı gelene kadar bu kolon
   her satırda "Masaüstü" (varsayılan) gösterir**, gerçek veri Opus onayı sonrası akar.
2. `src/RentACar.Web/Finance/FinanceEndpoints.cs` `/finans/tahsilat` ve `/finans/odeme` uçlarına
   (satır 90-133) opsiyonel `[FromForm] string? kanal` parametresi eklenir (form alanı zaten
   HAZIR görünür, ama Opus onayı gelene kadar `CashInput`'a AKTARILMAZ — parametre alınır, log'a
   yazılır veya yok sayılır, ASLA DB'ye gitmez).

**(Adım 3+ SADECE Opus onayı sonrası uygulanır — bu faz bunları hazırlar, uygulamaz.)**
3. Opus onayı → `src/RentACar.Domain/Entities/CashTransaction.cs`'e
   `public string? Kanal { get; set; }` eklenir ("Masaüstü"/"Mobil"/"Tablet", varsayılan
   `"Masaüstü"`).
4. `CashInput.cs`'e `Kanal` eklenir; `CashService.PostCashAsync` (satır 68-78, `tx = new
   CashTransaction { ... }`) `Kanal = input.Kanal ?? "Masaüstü"` satırı eklenir.
5. `FinanceEndpoints.cs`'teki adım 2'de hazırlanan `kanal` parametresi artık `CashInput.Kanal`'a
   BAĞLANIR.
6. `KasaHub.razor` grid'i artık gerçek `tx.Kanal` değerini gösterir, filtre gerçek veriyle çalışır.
7. Migration `AddCashTransactionKanal` — `CashTransactions` tablosuna 1 nullable string kolon.

## Dokunulacak dosyalar
- `src/RentACar.Web/Components/Pages/Finance/KasaHub.razor`
- `src/RentACar.Web/Finance/FinanceEndpoints.cs`
- (Opus onayı sonrası) `src/RentACar.Domain/Entities/CashTransaction.cs`
- (Opus onayı sonrası) `src/RentACar.Application/Finance/CashInput.cs`
- (Opus onayı sonrası) `src/RentACar.Application/Finance/CashService.cs`
- (Opus onayı sonrası, yeni) migration `AddCashTransactionKanal`

## Migration
Opus onayından ÖNCE: yok. Onay sonrası: `CashTransactions` tablosuna 1 nullable `varchar` kolon —
tablo zaten RLS'li (ilk migration'da), RLS bloğu tekrar eklenmez. `AccountLedgerEntry`'ye
DOKUNULMAZ (öneri, madde "Neden" bölümünde gerekçeli).

## Test
**Zorunlu:** adversarial inceleme — Critical/High/Medium bulgu kalmadan commit yok (yalnız Opus
onayı sonrası, şema değiştiren adımlar için).
- Yapısal (Opus onayından önce de çalışır): `KasaHub.razor` filtre dropdown'unun var olduğu, hiçbir
  veriyi filtrelemediği (henüz gerçek Kanal yok) bir smoke test.
- Şema sonrası — **defter dengesi testi (bağımsız oracle):** `Kanal="Mobil"` ile açılan bir
  tahsilat sonrası `Σ Borç(base) == Σ Alacak(base)` hâlâ tutuyor (Kanal eklenmesi denge testini
  BOZMAMALI — bu, Kanal'ın gerçekten ledger'a değil CashTransaction'a yazıldığının kanıtı).
- **İdempotency senaryosu:** aynı `IslemAnahtari` ile farklı `Kanal` değeriyle 2. bir POST atılırsa
  (çift-submit + kanal değişikliği) kayıt YİNE tek kalmalı (mevcut kısmi unique index davranışı
  Kanal eklenmesinden ETKİLENMEMELİ).
- `KasaHub.razor` Kanal filtresi "Mobil" seçiliyken yalnız `Kanal="Mobil"` satırların göründüğü
  (bağımsız oracle: test 3 farklı kanalda elle 3 tahsilat açar, filtre sayısını sabit 1 bekler).

## Exit
- [ ] `KasaHub.razor`'da Kanal kolonu + filtre var (Opus onayından önce de görünür, "Masaüstü"
      sabit)
- [ ] PARA — Opus onayı ALINDI ve şema kararı (`CashTransaction.Kanal`) dokümante edildi
- [ ] Onay sonrası: gerçek Kanal verisi akıyor, defter dengesi + idempotency regresyonu yeşil
- [ ] Adversarial inceleme raporunda Critical/High/Medium bulgu YOK
- [ ] Tam suite yeşil

## Notlar
Bu faz, plandaki "karar verilmeyen kısım"ı çözümsüz bırakmıyor — somut bir öneri (Kanal →
`CashTransaction`, ledger'a dokunma) ve gerekçesini (immutable belge tablosu, defter şemasından
ayrı, P&L kaynağını bozmaz) hazırlıyor. Kullanıcıya (Opus rolünde) sorulacak asıl soru: "Kanal
bilgisinin ileride bir RAPORA (ör. 'kanal bazlı tahsilat hacmi') girmesi planlanıyor mu?" — eğer
evet ise `CashTransaction.Kanal` yeterli (raporlar `CashTransaction` üzerinden de sorgulanabilir,
P&L'in KENDİSİ hâlâ yalnız `AccountLedgerEntry`'den okunur, ihlal yok).
