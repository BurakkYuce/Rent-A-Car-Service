# FAZ-55 — Gelen e-Fatura: KDV Oran Kırılımı + Gider Bağlama + "Sadece Kdv Yansıt"

| | |
|---|---|
| **Desen** | D2 (oran kırılımı/bağlama) + D5 ("Sadece Kdv Yansıt" — PARA, ilk ledger-yazan metod) |
| **Efor** | 3,5 gün (1,5 gün oran kırılımı+bağlama + 2 gün Kdv yansıtma/Opus) |
| **Bağımlılık** | (a) `kdv_raporu.aspx`'in Alış-KDV dahil etme kısmı (FAZ-53) BUNA bağımlı (ters
  yön — FAZ-53'ün Alış kısmı bu fazın (a) maddesi bitmeden yapılamaz) |
| **Kapsanan canlı ekran** | `gelen_e_fatura_listesi.aspx` |
| **Risk** | yüksek — (b) `GelenEFatura` şu an HİÇ deftere postlamıyor; bu fazda AÇILAN ilk
  ledger-yazan yol, zorunlu adversarial inceleme gerektirir |

**Zorunlu:** adversarial inceleme (bu fazın (b) "Sadece Kdv Yansıt" kısmı için) — Critical/High/
Medium bulgu kalmadan commit yok.

## Amaç
Kullanıcı gelen e-Fatura listesinde KDV oranına göre kırılmış tutarları (matrah+KDV, %20/%10/%1/%0)
görebilir, faturayı bir araca/gider kategorisine bağlayabilir, ve "Sadece Kdv Yansıt" aksiyonuyla
faturanın gider tarafına dönüştürmeden SADECE alış-KDV'sini (indirilecek KDV) deftere yansıtabilir
hale gelir.

## Neden (kanıt)
`GelenEFatura` (`src/RentACar.Domain/Entities/GelenEFatura.cs`) yalnız toplam `NetTutar`/`KdvTutar`/
`GenelToplam` tutuyor — oran-bazlı kırılım (`Kdv20`/`Kdv10`/`Kdv1`/`Kdv0` + matrahları) yok, `VehicleId`/
gider-kategori bağlama alanı yok (grep doğrulandı). Entity'nin kendi XML yorumu (L10-11) şunu açıkça
söylüyor: *"Bu kayıt DEFTERE POSTLAMAZ — gelen faturayı gidere/borca dönüştürme (para hareketi)
ileriki bir adımdır (adversarial gerektirir)."*

## Yapılacaklar
1. **(a) KDV oran kırılımı + araç/gider kategori bağlama (D2):** `GelenEFatura` entity'sine oran-
   bazlı alanlar eklenir: `Kdv20`/`Kdv20Matrah`, `Kdv10`/`Kdv10Matrah`, `Kdv1`/`Kdv1Matrah`,
   `Kdv0`/`Kdv0Matrah` (hepsi `decimal?`). **Doğrulama kuralı** (`GelenEFaturaService`'te): toplamda
   `Σ(Kdv20+Kdv10+Kdv1+Kdv0) == NetTutar` matrah toplamı ve `Σ KdvXX == KdvTutar` — tutarsızsa
   `ValidationException`. `VehicleId` (Guid?) + `ExpenseCategoryId`/`Turu` (gider kategorisiyle
   eşleştirme — Periyodik Servis/Hasar-Kaza/Mekanik Arıza/Bakım, `ExpenseCategory` FK) eklenir.
   `GelenEFaturaList.razor`'a bu kolonlar + filtre (EFatura_Firma, Fatura No aralığı, Plaka,
   `Islem_Turu`) eklenir.
2. **(b) "Sadece Kdv Yansıt" (D5, Opus kararı gerekli):** Yeni bir ledger-yazan metod
   `GelenEFaturaService.YansitKdvAsync(id, ...)` — gideri değil SADECE alış-KDV'sini (indirilecek
   KDV) deftere/beyannameye yansıtır.
   **KARAR GEREKLİ — Opus/kullanıcı kararı (bu faz kararı VERMEZ):** dengeli çiftin karşı hesabı ne
   olacak — **(i)** `Borç Kdv(indirilecek) / Alacak Cari` (fatura tutarı henüz ödenmemiş/borç olarak
   kabul edilir — tedarikçiye borçlanma) — veya **(ii)** `Borç Kdv(indirilecek) / Alacak Gider`
   (gider zaten AYRI bir Expense kaydıyla girilecek varsayımıyla, bu aksiyon SADECE KDV'yi ayrıştırır
   ve gideri "net" göstermek için Gider hesabından düşer — ama bu, gider HENÜZ girilmemişse Gider
   hesabını negatife düşürebilir, yanlış okunabilir). Bu ayrım defter doğruluğunu doğrudan etkiler
   (hangi hesap tipi büyüyor/küçülüyor); bu faz kararı VERMEZ, yapısal iskeleti (form + servis imzası
   + idempotency anahtarı) kurar, gövde Opus incelemesiyle birlikte netleşir.
   Idempotency: her `GelenEFatura.Id` başına EN FAZLA bir "Kdv Yansıt" kaydı (kısmi unique index,
   `SourceType="GelenEFaturaKdv"` + `SourceId=GelenEFatura.Id`).

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/GelenEFatura.cs`
- (yeni) migration `AddGelenEFaturaKdvKirilimVeBaglama`
- `src/RentACar.Application/GelenEFaturalar/GelenEFaturaService.cs`
- `src/RentACar.Application/GelenEFaturalar/GelenEFaturaInput.cs`
- `src/RentACar.Web/Components/Pages/GelenEFaturalar/GelenEFaturaList.razor`
- `src/RentACar.Web/GelenEFaturalar/GelenEFaturaEndpoints.cs`

## Migration
Var — `GelenEFaturalar` tablosuna 8 nullable decimal kolon (oran kırılımı) + `VehicleId` (Guid?, FK)
+ `ExpenseCategoryId` (Guid?, FK) eklenir. RLS bloğu **ELLE EKLENMEZ** (mevcut tenant-owned tabloya
additive kolon, RLS zaten aktif — yeni tablo yok).

## Test
- (a) (yeni) `GelenEFaturaKirilimTests.cs`: elle bir `GelenEFatura` (NetTutar=1000, %20 KDV=200)
  oluşturulur, oran kırılımı Kdv20Matrah=1000/Kdv20=200 girilir → doğrulama geçer (bağımsız oracle:
  1000/200 test içinde sabit). Tutarsız kırılım (Kdv20Matrah=500, kalan hiç girilmemiş ama
  NetTutar=1000) → `ValidationException`.
- (b) (yeni) aynı dosyada veya `GelenEFaturaKdvYansitTests.cs`:
  - **Defter dengesi:** `YansitKdvAsync` çağrısı sonrası `Σ Borç(base) == Σ Alacak(base)` (KDV tutarı
    kadar).
  - **İdempotency:** aynı `GelenEFatura.Id` için ikinci `YansitKdvAsync` çağrısı ikinci kez
    YAZMAMALI (kısmi unique index yutar veya `ValidationException` — hangisi Opus kararına göre
    netleşir).
  - **Tutar doğruluğu:** yansıtılan tutar = `GelenEFatura.KdvTutar` (elle sabit, koddan
    türetilmez).

## Exit
- [ ] KDV oran kırılımı + doğrulama kuralı çalışıyor
- [ ] VehicleId/ExpenseCategoryId bağlama çalışıyor
- [ ] "Sadece Kdv Yansıt" karşı-hesap kararı Opus/kullanıcı onayıyla netleşti VE uygulandı
- [ ] Defter dengesi + idempotency testleri yeşil
- [ ] Adversarial inceleme: Critical/High/Medium bulgu YOK
- [ ] Tam suite yeşil

## Notlar
Bu fazın (a) maddesi bitmeden `kdv_raporu.aspx`'in Alış-KDV dahil etme kısmı (FAZ-53, madde 2)
yapılamaz — sıra: bu faz ÖNCE, FAZ-53'ün Alış-kısmı SONRA.
