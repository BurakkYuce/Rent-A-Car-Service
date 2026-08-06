# FAZ-56 — Bakiye Düzeltme (yeni ekran)

| | |
|---|---|
| **Desen** | D5 (PARA — Opus) |
| **Efor** | 2 gün |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `bakiye_islem.aspx` |
| **Risk** | yüksek — tek-taraflı görünen bir düzeltmenin dengeli karşı bacağı kararı gerektiriyor |

**Zorunlu:** adversarial inceleme — Critical/High/Medium bulgu kalmadan commit yok.

## Amaç
Kullanıcı Kasa/Banka'ya DOKUNMADAN tek bir cari üzerinde bakiye ayarlaması (alacaklandırma/
borçlandırma) yapabilir hale gelir — ör. yuvarlama farkı, iyi niyet indirimi, manuel muhasebe
düzeltmesi gibi durumlar için.

## Neden (kanıt)
Canlı `bakiye_islem.aspx` (`Islem_Turu=1|2` Alacaklandır/Borçlandır) karşılığı bizde YOK — mevcut
`CashService` yalnız Kasa/Banka'ya dokunan Tahsilat/Ödeme/Virman taşıyor; Kasa/Banka'ya dokunmadan
salt cari bakiyesini değiştiren bir yol yok (grep doğrulandı: `LedgerAccountType` enum'ında
"muhasebe düzeltmesi" için ayrı bir hesap türü yok).

## Yapılacaklar
1. **KARAR GEREKLİ — Opus/kullanıcı kararı (bu faz kararı VERMEZ):** cari bakiyesini Kasa/Banka'ya
   dokunmadan değiştiren bu işlemin DENGELİ karşı bacağı ne olacak:
   **(i)** Yeni `LedgerAccountType.MuhasebeDuzeltmesi` (veya benzeri) — özkaynak-benzeri bir hesap
   türü, `DonemSonucu`'na benzer (enum genişlemesi + migration + var olan raporlarda bu türün nasıl
   gösterileceği kararı). **(ii)** Mevcut `Gelir`/`Gider` hesabına yazılır (Alacaklandırma = Borç
   Cari/Alacak Gelir, Borçlandırma = Borç Gider/Alacak Cari — ama bu, P&L raporlarını "gerçek"
   gelir/giderle KARIŞTIRIR, Araç Karnesi/Filo Analiz gibi ATIF-hassas raporları bozabilir — bkz.
   CLAUDE.md §6 "Araç ön muhasebe" atıf düzeltmesi dersi). Bu karar netleşmeden gerçek
   `AdjustAsync` gövdesi (ledger satırları) YAZILMAZ.
2. Yapısal iskelet: `src/RentACar.Application/Finance/BakiyeDuzeltmeService.cs` —
   `AdjustAsync(cariId, tutar, yon, vade, makbuzNo, aciklama, ct)` imzası + guard (cari var mı,
   tutar pozitif mi, dönem kilidi) kurulur; ledger-yazan gövde karar (madde 1) netleşince eklenir.
3. Yeni sayfa `src/RentACar.Web/Components/Pages/Finance/BakiyeDuzeltme.razor` (Musteri arama,
   Tarih, Vade, Tutar, Döviz, Kur, Makbuz No, Açıklama).
4. `src/RentACar.Web/Finance/FinanceEndpoints.cs`'e `/finans/bakiye-duzeltme` ucu eklenir.
5. İdempotency: form token → `IslemAnahtari` (mevcut `CashInput.IslemAnahtari` deseniyle aynı,
   kısmi unique index çift-submit'i yutar).

## Dokunulacak dosyalar
- (yeni) `src/RentACar.Application/Finance/BakiyeDuzeltmeService.cs`
- (yeni) `src/RentACar.Web/Components/Pages/Finance/BakiyeDuzeltme.razor`
- `src/RentACar.Web/Finance/FinanceEndpoints.cs`
- (madde 1 (i) seçilirse) `src/RentACar.Domain/Enums/LedgerAccountType.cs` + migration

## Migration
(i) seçilirse: `LedgerAccountType` enum genişlemesi migration GEREKTİRMEZ (enum, kolon değil) ama
raporlardaki case-switch'lerin (`ReportService`, Araç Karnesi/Filo Analiz atıf mantığı) yeni türü
doğru ele alması gerekir — bu bir KOD değişikliği, migration değil. (ii) seçilirse migration yok.

## Test
- (yeni) `BakiyeDuzeltmeTests.cs`:
  - **Defter dengesi:** `AdjustAsync` sonrası `Σ Borç(base) == Σ Alacak(base)`.
  - **Cari bakiye etkisi:** Alacaklandırma sonrası cari bakiyesi elle hesaplanan kadar DÜŞER,
    Borçlandırma sonrası YÜKSELİR (bağımsız oracle: 3 gün × 100 gibi sabit senaryo).
  - **P&L atıf regresyonu (i seçilirse):** Araç Karnesi/Filo Analiz raporlarının toplam
    gelir/gider'i bu düzeltme kayıtlarından ETKİLENMİYOR (CLAUDE.md §6 atıf dersi — yeni hesap türü
    P&L toplamına sızmıyor).
  - **İdempotency:** aynı token'la ikinci çağrı ikinci kez yazmıyor.

## Exit
- [ ] Karşı hesap kararı (i/ii) Opus/kullanıcı onayıyla netleşti VE uygulandı
- [ ] `/finans/bakiye-duzeltme` sayfası çalışıyor
- [ ] Defter dengesi + idempotency testleri yeşil
- [ ] P&L raporları (i seçilirse) bu kayıtlardan kirlenmiyor — regresyon testiyle kanıtlı
- [ ] Adversarial inceleme: Critical/High/Medium bulgu YOK
- [ ] Tam suite yeşil

## Notlar
**Karşı hesap kararı (madde 1) Opus/kullanıcıya aittir** — bu ekranın en kritik riski budur:
CLAUDE.md §6'daki "Araç ön muhasebe" atıf düzeltmesi dersi tam olarak bu türden bir hatayı (kaynak-
varlık dışı gelir/giderin P&L'e sızması) daha önce düzeltmişti; aynı hatayı burada TEKRARLAMAMAK
için (ii) seçeneği özellikle dikkatli değerlendirilmeli.
