# Yol Haritası — eksikler nasıl, hangi sırayla kapanır

Bu belge `plan/` altındaki ekran-ekran planların **sıralanmış** hâlidir. Her eksik ekran bir desene
(D1–D9, bkz. [`10-ekleme-desenleri.md`](10-ekleme-desenleri.md)) bağlandı; burada hangi sırayla
yapılacağı ve neden o sırayla yapılacağı var.

## Toplam tablo

| Desen | Kalem | Efor (gün) | Ne demek |
|---|---:|---:|---|
| **D3** liste/arama — veri VAR, görünmüyor | 44 | 38,5 | Yeni tablo yok, migration yok |
| **D2** kural taşıyan master | 33 | 41,8 | Alan/kural eklenir, tüketen yola bağlanır |
| **D4** rapor (agrega) | 15 | 22,5 | P&L yalnız defterden |
| **D5** para hareketi yazan | 13 | 14,5 | Her biri **zorunlu adversarial inceleme** |
| **D1** basit sözlük master | 9 | 8,0 | Toplu PR'lara gruplandı |
| **D7** yeni dikey | 7 | 22,5 | CRM/anket/şikayet — ad benzeri, iş farklı |
| **D6** mega form | 1 | 4,0 | |
| **D8** entegrasyon bağımlı | 13 | **bloke** | Kimlik/credential gerekir, kod yazılmaz |
| **YAPILMAZ** | 12 | — | Gerekçeli bilinçli karar (aşağıda) |
| Doğrulama bekliyor | 1 | — | `arac_satis_bedeli` — canlı işlevi anlaşılamadı |
| **TOPLAM** | **149** | **~152** | ≈ 30 iş-haftası (tek geliştirici) |

---

## Dalga 0 — Bedava kazançlar (saatler, gün değil)

**"Wire-in eksikliği":** alan hem entity'de hem input modelinde **var**, ama form onu hiç sormuyor.
Migration yok, servis değişikliği yok — yalnız form alanı. Ölçülenler:

- `AracKredi.VehicleId` — input'ta var, create formunda yok
- `VehicleSale.HedefFiyat / SatisKm / SatisKanali / Devir` — dördü de entity+input'ta, form sormuyor
- `Baf.DonusTarihi / DonusYakit` — entity'de var, "Teslim Al" formu yalnız `donusKm` istiyor

> Bu kalıbın tekrarladığı biliniyordu (bkz. hafıza: *bir alanı bağlarken onu yazan TÜM formları
> gre'ple*). Dalga 0, bu borcu tek seferde kapatır.

**Neden ilk:** en yüksek fayda/maliyet oranı ve sıfır risk.

## Dalga 1 — D3: veri var, görünmüyor (44 kalem, ~38,5 gün)

Yeni tablo/migration gerektirmeyen liste-kolonu, filtre ve export işleri. Örnek doğrulananlar:
`Vehicle.AlimBedeli/AlimTarihi/AlimYapilanFirma` entity'de var, `VehicleList.razor`'da **sıfır** kez
geçiyor; `Reservation.Kaynak` formda var ama grid kolonu değil.

**Neden ikinci:** kullanıcının "eksik" dediği şeyin büyük kısmı burada ve en ucuz sınıf bu. Risk
düşük (okuma yolu), test yükü hafif.

## Dalga 2 — D1 + D2: master derinliği (42 kalem, ~50 gün)

Sözlük ekranları toplu PR'lara gruplandı (27 tanım ekranı → 14 PR). Kural taşıyan master'larda
(tarife km kademesi, ek hizmet açıklama/max-gün, şube derinliği) alan eklenir **ve** o kuralı okuyan
yol test edilir — yoksa alan girilir ama hiçbir şeyi değiştirmez.

**İçinde:** para kalibrasyonunda bulunan **tarife km kademesi** açığı (canlıda her gün kademesinin
kendi km limiti + aşım ücreti var, bizde araç grubunda tek ve global).

## Dalga 3 — D4: raporlar (15 kalem, ~22,5 gün)

Filtre derinliği + eksik raporlar. **Değişmez kural:** P&L yalnız defterden okunur, kaynak-varlık
tutarı asla toplanmaz (çift-sayım yasağı).

## Dalga 4 — D5: para yazan işler (13 kalem, ~14,5 gün)

Her biri çift-taraflı defter + idempotency + ters kayıt + **zorunlu adversarial inceleme** ile gelir;
Critical/High/Medium bulgu kalmadan commit edilmez.

**En büyük kalem: çok-hesaplı Kasa/Banka.** İlk teşhis "model değişikliği gerekiyor" idi; kod
okunduğunda **daha ucuz** çıktı — defter şeması hesap-bazlı referansı **zaten destekliyor** ve
`FinancialAccount`'ta `Iban`/`Banka`/`Sube` **zaten var**. Eksik olan bağlama:

- `CashService` Kasa/Banka bacaklarında `AccountRef = null` yazıyor (satır 188/190/288), oysa Cari
  bacaklarında gerçek id'yi yazıyor → hangi banka hesabı olduğu deftere hiç geçmiyor.
- `TransferAsync`'teki `kaynak == hedef` kontrolü **enum düzeyinde** (satır 177) → iki farklı banka
  hesabı "aynı hesap" sayılıyor, **banka-banka virman imkânsız**.
- Virman `CashTransaction` yazmıyor (yalnız `SourceType="Virman"` defter çifti) → **geçmiş virman
  hiçbir ekranda listelenemiyor**. Bunun ucuz çözümü var: defterden okuyan bir liste (D4, ~1 gün),
  şema değişikliği gerekmez.

Yine de bu **para yolu**: `AccountRef` doldurmaya başlamak geçmiş kayıtlarla tutarlılık (backfill mi,
null-toleranslı okuma mı) kararı gerektirir ve adversarial inceleme şarttır.

> **Plan çakışması — düzeltilmesi gereken:** `02-tanim-master` planı `hesap_para_islem` ve
> `genel_kasa` kalemlerini aynı dosyalara ("`CashService`, `KasaHub`, `KasaBankaDefteri`") **1'er gün**
> diye yazmış; kök nedeni görmeden. Bu iki kalem yukarıdaki temel işin **içinde** çözülür, ayrı
> sayılmamalı — aksi halde efor hem şişer hem iki PR aynı dosyayı çeker.

**Ayrıca:** ÖTV (canlıda KDV'si sabit %20, 3 ondalık) — bizde domain'de var, formda yok.

## Dalga 5 — D7: yeni dikeyler (7 kalem, ~22,5 gün)

CRM, sözleşme-bağlı anket, dönüş-bağlı şikayet, hukuk derinliği. Bunlar **ad benzeri ama iş farklı**:
canlının anketi sözleşmeye bağlı 8 soruluk bir akış, bizim `/anketler` jenerik bir geri bildirim
kaydı. En sonda çünkü çekirdek operasyonu bloke etmiyorlar.

---

## Bloke — D8 (13 kalem, kimlik gerekiyor)

e-Fatura/GİB · KABİS · e-Devlet ceza sorgu · XML/OTA broker feed · SMS sağlayıcı · banka/POS.
Port'lar ve stub'lar hazır; **kod yazılmaz, kullanıcıya sorulur**. İş planına dâhil değiller.

## YAPILMAZ — 12 kalem, gerekçeli

Taklit etmek **mimari gerileme** olacağı için bilinçli reddedilenler:

| Ekran | Gerekçe |
|---|---|
| `kullanicilar.aspx` kullanıcı-bazlı ~100 menü checkbox'ı | Bizde rol + `/yetki` ekran-override modeli var; per-kullanıcı checkbox matrisi geri adım |
| `web_site_yonetimi.aspx` çok-dilli tam CMS | Kapsamımız araç vitrini + statik sayfa + blog; çoklu dil zaten kapsam dışı |
| `genel_rapor.aspx` serbest pivot rapor-builder | Kullanıcının kendi alanlarını seçtiği jenerik araç; modüler `/raporlar/*` tasarımına aykırı |
| `default.aspx` monolitik BI paneli | Bizde modüler rapor sayfaları |
| `rezervasyon.aspx`'in ~30 sabit ek-hizmet/sigorta kolonu | Bizde jenerik `EkHizmetTanim` master'ı; derinlik "Kiraya Çevir" sonrası kira mega-formunda. Ön-kopya iki-nüsha senkron riski üretir |
| `arac_kayit.aspx` sigorta/muayene/servis sekmesi | Veri birincil kaynağı `/regulasyon`; ikinci kopya senkron riski |
| `para_tanimlama.aspx` statik Kur alanı | TCMB kur sistemi tek kaynak; ikinci Kur alanı çift-kaynak riski |
| `tabletyonetim`, `turevuzak`, `mobil_teslimat` | Donanım/3. parti ve ayrı mobil oturum modeli; bizde tek duyarlı web platformu |
| `cikis.aspx`, `globalsearch.aspx` | Karşılığı **zaten var** (`/auth/logout`, `/ara`) |

---

## Sıralama mantığı (özet)

1. **Önce risksiz ve ucuz olan** (Dalga 0–1): kullanıcının gördüğü eksiklerin çoğu, migration'sız.
2. **Sonra additive derinlik** (Dalga 2–3): tablo/alan eklenir ama para yolu değişmez.
3. **Para en sonda ve yavaş** (Dalga 4): her kalem adversarial incelemeyle; model değişikliği
   gerektiren tek kalem (IBAN hesap defteri) kullanıcı kararına bağlı.
4. **Yeni dikeyler en sonda** (Dalga 5): çekirdek operasyonu bloke etmiyorlar.
5. **Bloke olanlar hiç sıraya girmez** — kimlik gelene kadar.

## Bu haritanın sınırları

- Eforlar **desen katalogundaki taban maliyetlerden** türetildi; sapan kalemlerde ajanlar sebebini
  yazdı. Gerçek maliyet ilk birkaç PR'dan sonra kalibre edilmeli.
- `arac_satis_bedeli.aspx` planlanamadı: canlı ekranın işlevi ("RentTo") anlaşılamadı ve kod
  tabanında karşılığı yok. **Önce kullanıcıya sorulmalı.**
- Ekran-ekran ayrıntı (hangi alan, hangi dosya, hangi bağımlılık) `plan/` altındaki 6 dosyada.
