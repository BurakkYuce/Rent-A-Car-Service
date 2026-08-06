# FAZ-60 — Trafik Cezası Derinliği: Filtre/Kolon + Çok-satır + Kısmi Ödeme

| | |
|---|---|
| **Desen** | D3 (filtre/kolon) + D2 (çok-satır + bilgi alanları) + D2/PARA-bitişik (kısmi ödeme) |
| **Efor** | 4 gün (0,5 gün filtre/kolon + 1,5 gün çok-satır + 0,5 gün bilgi alanları + 1,5 gün
  kısmi ödeme/Opus) |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `ceza_listesi.aspx`, `cezalar.aspx` (dosya/resim kanıt yükleme HARİÇ
  — bkz. Notlar) |
| **Risk** | orta — kısmi ödeme "Kalan" hesap-şeması yanlış kurulursa para kaybına/çift-sayıma yol
  açabilir (Opus kararı) |

**Zorunlu:** adversarial inceleme (kısmi ödeme kısmı için) — Critical/High/Medium bulgu kalmadan
commit yok.

## Amaç
Kullanıcı ceza listesinde canlı paritesine yakın filtre/kolonlarla arama yapabilir; tek bir ceza
kaydında birden çok ceza maddesi (canlının 3 ayrı `Ceza_Tutari1-3`/`Ceza_Sebebi1-3` satırı) girebilir;
ceza saati/yeri/cep tel/makbuz no/işlem şube/ödenme tarihi bilgilerini kaydedebilir; ve kısmi ödenmiş
cezaların "Kalan" tutarını takip edebilir hale gelir.

**Aynı faza neden birleştirildi:** `ceza_listesi.aspx` ve `cezalar.aspx` AYNI dosya kümesine
dokunuyor (`Penalty.cs`, `PenaltyList.razor`, `PenaltyService.cs`, aynı migration) — iki ayrı PR
açmak aynı tabloya art arda iki migration + aynı formda iki bağımsız düzenleme demek olurdu; FAZ-
TALIMATI kural 3 ("aynı dosyaları paylaşan ekranlar tek faz") gereği birleştirildi.

## Neden (kanıt)
`Penalty.cs` (`src/RentACar.Domain/Entities/Penalty.cs`) şu an tek `Tutar`/`Sebep`/`CezaTuru`/
`TebligTarihi`/`VadeTarihi` alanı taşıyor — canlının 3-satırlı `Ceza_Tutari1-3`/`Ceza_Sebebi1-3`
yapısı yok; `Ceza_Saat`/`Ceza_Yeri`/`Cep_Tel`/`Makbuz_No`/`Islem_Sube`/`Odenme_Tarih` alanları yok
(grep doğrulandı). `PenaltyList.razor`'da filtre paneli yok (Musteri_No/Ad_Soyad, Makbuz_No, Plaka,
Tarih aralığı, `Ceza_Durum`, `Odeme_Durum`, Ofis) ve kolon eksik (Mail Adresi, Sözleşme No, Fatura
Tar./No, İşlem Şube, Rez. Kaynağı, Ödeme Tarihi/Şekli). `CezaDurum` enum'ı (`Yeni/Yansitildi/Odendi/
Iptal`) `Kismi` değeri taşımıyor — kısmi ödeme takibi yok.

## Yapılacaklar
1. **Filtre + temel kolon (D3):** `PenaltyList.razor`'a filtre paneli (Musteri_No/Ad_Soyad,
   Makbuz_No, Plaka, Tarih aralığı, `Ceza_Durum`, `Odeme_Durum`, Ofis) + kolon (Mail Adresi,
   Sözleşme No, Fatura Tar./No, İşlem Şube, Rez. Kaynağı, Ödeme Tarihi/Şekli) eklenir.
2. **Çok-satırlı ceza (D2):** Canlı 3 ayrı `Ceza_Tutari1-3`/`Ceza_Sebebi1-3` satırı taşıyor (tek
   kayıtta birden çok ceza maddesi); bizde tek `Tutar`/`Sebep`. `Penalty`'ye `List<PenaltySatir>`
   (Tutar, Sebep) alt-tablo eklenir — CLAUDE.md §5 "yeni tenant-owned tablo" reçetesine uygun
   (alt-tablo tercih edilir; "+ Satır Ekle" JS ile her satır ayrı `Penalty` POST'u YAPILMAZ, çünkü
   bu iki ayrı ceza kaydı yaratır ve yansıtma/ödeme takibini böler — tek kayıt + alt-satır daha
   sağlam).
3. **Bilgi alanları (D1):** `Ceza_Saat`, `Ceza_Yeri`, `Cep_Tel`, `Makbuz_No`, `Islem_Sube`,
   `Odenme_Tarih` `Penalty`'ye eklenir.
4. **Kısmi ödeme/bakiye takibi (D2/PARA-bitişik):** `Penalty`'ye `OdenenTutar` (decimal) +
   hesaplanan `Kalan` (`Tutar - OdenenTutar`, satırlar varsa `Σ PenaltySatir.Tutar - OdenenTutar`)
   alanı eklenir; `CezaDurum`'a `Kismi` değeri eklenir. Ödeme kaydı YENİ ledger satırı YAZMAZ (ceza
   yansıtma zaten `PenaltyService.YansitAsync` ile var, L74) — yalnız TAKİP alanı.
   **KARAR GEREKLİ — Opus/kullanıcı kararı (bu faz kararı VERMEZ):** çok-satırlı ceza (madde 2) ile
   kısmi ödeme (madde 4) birleştiğinde "Kalan" NASIL hesaplanacak — **(i)** toplam `Σ Satır.Tutar`
   üzerinden TEK bir "Kalan" (basit, ama hangi SATIRIN ödendiği belirsizleşir) — veya **(ii)** her
   satırın kendi `OdenenTutar`/`Kalan`'ı (satır-bazlı takip, daha doğru ama form/servis karmaşıklığı
   artar). Yanlış seçim "Kalan" hesap-şemasının para kaybına/çift-sayıma yol açabileceği plan
   dosyasında AÇIKÇA not edilmiş (05-finans-plan.md) — bu faz karar VERMEZ, yapısal alanları ekler,
   hesaplama formülü Opus incelemesiyle birlikte netleşir.
5. Migration: `Penalty`'ye yeni alanlar + `PenaltySatir` alt-tablosu (CLAUDE.md §5 tam reçete: FK
   `PenaltyId`, `ITenantOwned`, RLS bloğu ELLE eklenir).

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/Penalty.cs` (+ yeni `PenaltySatir` entity)
- (yeni) migration `AddPenaltyDerinlikVeSatir`
- `src/RentACar.Domain/Enums/CezaDurum.cs` (+`Kismi`)
- `src/RentACar.Application/Penalties/PenaltyService.cs`
- `src/RentACar.Web/Components/Pages/Penalties/PenaltyList.razor`

## Migration
Var — `Penalty` tablosuna 6+ nullable alan (`OdenenTutar` dahil) + yeni `PenaltySatir` tablosu
(`PenaltyId` FK, `Tutar`, `Sebep`). **RLS bloğu ELLE EKLENİR** yeni `PenaltySatir` tablosu için
(`ENABLE`+`FORCE ROW LEVEL SECURITY` + `tenant_isolation` policy + `GRANT` `racar_app`'e) — CLAUDE.md
§5 reçetesi.

## Test
- (a) `PenaltyTests.cs`'e ek senaryo: filtre kombinasyonları (bağımsız oracle: beklenen sayı sabit).
- (b) çok-satır: elle 3 satırlı bir ceza oluşturulur (100+200+300) → toplam `Tutar` hesaplanan
  değeri 600 mü (bağımsız oracle: 600 test içinde sabit, koddan türetilmez).
- (c) kısmi ödeme: elle 600 TL cezaya 200 TL ödeme girilir → `Kalan=400` mü (Opus kararına göre
  (i) veya (ii) formülüyle); `PenaltyService.YansitAsync`'in defter etkisi DEĞİŞMEDİ (regresyon —
  yansıtma zaten var, kısmi ödeme yeni ledger satırı YAZMIYOR).
- **Defter dengesi regresyonu:** mevcut yansıtma testleri (Σ Borç Cari = Σ Alacak Gelir) tam suite
  yeşil.

## Exit
- [ ] Filtre/kolon çalışıyor
- [ ] Çok-satırlı ceza kaydedilebiliyor + toplam doğru hesaplanıyor
- [ ] Ceza_Saat/Yeri/Cep_Tel/Makbuz_No/Islem_Sube/Odenme_Tarih formda
- [ ] "Kalan" hesap formülü (i/ii) Opus/kullanıcı onayıyla netleşti VE uygulandı
- [ ] Yeni `PenaltySatir` tablosunda RLS+FORCE aktif (izolasyon testiyle kanıtlı)
- [ ] Adversarial inceleme: Critical/High/Medium bulgu YOK
- [ ] Tam suite yeşil

## Notlar
Dosya/resim kanıt yükleme (`cezalar.aspx`'teki FileUpload maddesi) BU FAZA DAHİL DEĞİL — mevcut
Belge Merkezi altyapısına (CLAUDE.md memory: "belge-paylasim-serisi") bağlanıp bağlanamayacağı ayrı
bir doğrulama gerektiriyor; efor belirsiz, ayrı küçük bir takip fazı olarak değerlendirilmeli.

**"Kalan" hesap formülü (madde 4) Opus/kullanıcı kararıdır** — yanlış seçim CLAUDE.md memory
"yorumdaki-hafifletme-bayatlar" dersindeki türden sessiz bir para hatası üretebilir (bir "şu alan
kullanılmıyor" notu bayatlayıp gerçek bir hesaplama hatasına dönüşebilir); bu yüzden zorunlu
adversarial inceleme şart.
