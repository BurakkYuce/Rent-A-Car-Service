# Yol — RentACar Tasarım Dili Uygulama Planı (v2)

Durum: onaylı (2026-09-25) · Kaynak: "RentACar Tasarım Dili" ve "Yol v2 — Zengin Kabuk" canvas'ları ·
Referans görüntüler: `referans/panel.png`, `referans/kira-listesi.png` (HEDEF GÖRÜNÜM — piksel değil dil referansı) ·
Hedef: Angular SPA (`src/RentACar.Frontend`), Blazor dokunulmaz.

> **Kod gerçeği düzeltmeleri (çelişkide bunlar kazanır)** — §17'ye bakın: tema özniteliği `data-theme="light|dark"`;
> durum tokenları `-metin/-zemin/-kenar` son ekli; kontrast betiği `var(--rc-ham-*)` zincirini çözecek şekilde genişler;
> sayaç ucu `/api/ui/v1/ozet/sayaclar`; tablo başlığı ASFALT (referans görüntüyle kapandı).

---

## 0. Bir bakışta
- **Sorun:** Ekranlar çalışıyor ama kimlik yok: varsayılan mavi, Inter, 13px, beyaz kartlar. `.sayfa/.ust/.kart/.tablo/.num`
  16 feature dosyasında yeniden tanımlı; iki tablo dünyası (rc-tablo motoru + 52 dosyada düz `<table>`). Rakip
  (TürevRent) daha "dolu" görünüyor ve alıcı bunu yetenek olarak okuyor.
- **Yön:** "Yol" dili — Kağıt/Asfalt nötrler, Lacivert birincil (plaka şeridi + karayolu tabelası), Otoyol yeşili /
  İşaret sarısı / Dur kırmızısı yalnızca filo durumu için. İki imza öğe: **plaka çipi** ve **tabela kartı**. Zengin kabuk:
  ikonlu lacivert kenar çubuğu + kayıtlı görünümler, sayfa bandı, plaka ile hızlı arama, yoğun grid.
- **Kaldıraç:** 1141 `var(--rc-*)` kullanımı var, sabit renk yok. Anlamsal token **adları korunur**, değerleri değişir →
  tema tek dosyadan döner. Asıl iş: kabuk + düzen katmanı + ekran göçü.
- **Sıra:** A (temel) → B (primitif + düzen + plaka + stil denetimi uyarı modunda) → C (kabuk + giriş) →
  **P (pilot: kiralar + kira-formu, seri)** → D1–D4 (paralel) → E (kurallar, denetim hata moduna).

## 1. Tasarım dili
### 1.1 İlkeler
1. **Kağıt üstüne yazı.** Sıcak nötrler (mavi-gri değil). Sayfa içinde gölge yok; 1px kenar + yüzey farkı. Gölge yalnız
   katmanlarda (açılır panel, diyalog, toast, yapışkan finans paneli).
2. **Renk = durum.** Yeşil kirada, asfalt boşta, sarı serviste, lacivert rezerve/eylem, kırmızı gecikmiş/hata, petrol bilgi.
   Bunun dışında dekoratif renk yok.
3. **Fiziksel nesneler sabittir.** Plaka çipi ve tabela kartı iki temada da aynı renk. Tema yalnızca zemin/metin/kontrol değiştirir.
4. **Dolu ama disiplinli.** Rakipten alınan: ikonlu derin menü, sayaçlı kayıtlı görünümler, renkli sayfa bandı, dolu durum
   kartları, tek bakışta filtre paneli, yoğun grid, plaka ile hızlı arama. Alınmayan: gradyan, mor/bordo, her butona renk,
   "drag a column header" çubuğu, 11px metin.
5. **Bir sayfada tek dolu birincil buton.** Gerisi çerçeveli veya düz.
6. **Etiketler cümle düzeninde.** BÜYÜK HARF etiket, eyebrow, "→" eki yok.
7. **Sarı-lacivert marka çifti yok.** Sarı yalnız "serviste/uyarı"; lacivertle yan yana marka öğesi olarak kullanılmaz.

### 1.2 Filo durum sözlüğü (tek kaynak)
| Durum | Rozet (zemin/metin) | Tabela kartı | Kullanım |
|---|---|---|---|
| Kirada | `basari-zemin`/`basari-metin` | `#0F6E4A` / beyaz | araç yolda, gelir |
| Boşta | `notr-zemin`/`notr-metin` | `#2D2B25` (asfalt) / beyaz | müsait |
| Serviste | `uyari-zemin`/`uyari-metin` | `#F2B705` / `#1B1A16` | bakım, dikkat |
| Rezerve | `vurgu-zemin`/`vurgu-metin` | `#1F3A8A` / beyaz | söz verilmiş |
| Gecikmiş | `hata-zemin`/`hata-metin` | `#B3261E` / beyaz | eylem gerek |
| Bugün dönüyor/çıkıyor | `uyari-zemin`/`uyari-metin` + satır vurgusu | — | bugünün işi |

## 2. Token spesifikasyonu
### 2.1 Mimari
- `src/styles/_tokenlar.scss`: üç katman. **Ham** (`--rc-ham-*`, tema bağımsız palet) → **anlamsal** (`--rc-zemin`,
  `--rc-vurgu`… mevcut adlar) → **bileşen** (`--rc-plaka-*`, `--rc-tabela-*`, `--rc-kenar-cubugu-*`, `--rc-bant-*`,
  `--rc-tablo-baslik-*`).
- Tema seçimi mevcut mekanizma: `:root` + `prefers-color-scheme` + `:root[data-theme='dark'|'light']`
  (`core/tema/tema-servisi.ts` `TEMA_ZEMINLERI` senkronlanır).
- Feature SCSS'te hex/rgb/hsl **yasak**; yalnız `var(--rc-*)`. Bileşen tokenı yalnız kendi bileşeninde okunur.

### 2.2 Ham palet (`--rc-ham-*`)
```
kagit-50 #F4F2EC · kagit-100 #ECE9E1 · kagit-200 #E2DED3 · kagit-300 #E7E4DC
asfalt-950 #0F0E0C · asfalt-900 #141310 · asfalt-800 #1C1B17 · asfalt-700 #24221D · asfalt-600 #2D2B25
murekkep-900 #1B1A16 · murekkep-700 #49463F · murekkep-600 #5F5B53 · murekkep-400 #8A8578 · murekkep-300 #78746A
lacivert-900 #172B66 · lacivert-800 #1F3A8A · lacivert-700 #2A428F · lacivert-600 #33509F · lacivert-500 #4A6BD0
lacivert-400 #5A72C4 · lacivert-300 #7D91D6 · lacivert-200 #A9BCF5 · lacivert-100 #C5D0F0 · lacivert-50 #E4E9F7
otoyol-700 #0F6E4A · otoyol-300 #6FD39A · otoyol-50 #DCF2E3 · otoyol-900 #0F2E1C
isaret-500 #F2B705 · isaret-800 #7F4D00 · isaret-300 #F5C969 · isaret-50 #FCEFD2 · isaret-900 #33260A
dur-700 #B3261E · dur-300 #F6A29A · dur-50 #FCE4E0 · dur-900 #3A1614
petrol-700 #0E6B67 · petrol-300 #6AD2C8 · petrol-50 #DCEFEC · petrol-900 #113A37
plaka-beyaz #FFFFFF · plaka-siyah #111111 · plaka-kenar #1B1B1B
```

### 2.3 Anlamsal tokenlar (adlar mevcut, değerler yeni)
| Token | Açık (Kağıt) | Koyu (Asfalt) |
|---|---|---|
| `--rc-zemin` | #F4F2EC | #141310 |
| `--rc-yuzey` | #FFFFFF | #1C1B17 |
| `--rc-yuzey-alt` | #ECE9E1 | #24221D |
| `--rc-yuzey-vurgu` | #E2DED3 | #2D2B25 |
| `--rc-kenar` (yumuşak) | #E2DED3 | #2D2B25 |
| `--rc-kenar-kontrol` | #8A8578 | #78746A |
| `--rc-metin` / `-ikincil` / `-soluk` | #1B1A16 / #49463F / #5F5B53 | #ECE9E1 / #B8B3A6 / #9B9688 |
| `--rc-odak` | #1F3A8A | #8FA8F0 |
| `--rc-vurgu` / `-hover` / `-uzeri` | #1F3A8A / #172C6B / #FFF | #4A6BD0 / #3D5CBF / #FFF |
| `--rc-vurgu-metin` / `-zemin` | #1B337A / #E4E9F7 | #A9BCF5 / #1B2A55 |
| `--rc-secim` | #D7DFF5 | #23366A |
| `--rc-satir-vurgu` (bugün) — yeni | #FFFBEB | #2B2416 |
| `--rc-basari-metin` / `-zemin` | #0F6E4A / #DCF2E3 | #6FD39A / #0F2E1C |
| `--rc-uyari-metin` / `-zemin` / `-dolgu` (yeni) | #7F4D00 / #FCEFD2 / #F2B705 | #F5C969 / #33260A / #F2B705 |
| `--rc-hata-metin` / `-zemin` | #B3261E / #FCE4E0 | #F6A29A / #3A1614 |
| `--rc-bilgi-metin` / `-zemin` | #0E6B67 / #DCEFEC | #6AD2C8 / #113A37 |
| `--rc-notr-metin` / `-zemin` | #49463F / #E7E4DC | #C9C4B7 / #2E2C27 |
`-kenar` durum tokenları: açıkta zemin ile metin arası orta ton, koyuda zemin ile metin arası orta ton (dekoratif;
kontrast betiği kenarı denetlemez). `--rc-metin-pasif`, `--rc-iskelet*`, `--rc-perde`, `--rc-golge-*` yeni palete uyarlanır.

### 2.4 Bileşen tokenları
**Kenar çubuğu** `--rc-kenar-cubugu-*`
| | Açık | Koyu |
|---|---|---|
| zemin | #172B66 | #0F0E0C |
| metin / ikincil / grup | #FFFFFF / #B4C1EA / #8A9BD6 | #ECE9E1 / #A39E91 / #857F73 |
| aktif-zemin / aktif-metin | #2A428F / #FFFFFF | #23366A / #ECE9E1 |
| ayrac / kontrol-kenar | #22397A / #5A72C4 | #2D2B25 / #78746A |
| rozet-zemin / rozet-metin | #FFFFFF / #1F3A8A | #4A6BD0 / #FFFFFF |
| rozet-uyari-zemin / metin | #B3261E / #FFFFFF | #B3261E / #FFFFFF |

**Sayfa bandı** `--rc-bant-*`: zemin #1F3A8A (koyu #1B2F6E) · metin #FFFFFF · ikincil #C5D0F0 · buton-kenar #7D91D6 ·
dolu-buton-zemin #FFFFFF · dolu-buton-metin #1F3A8A.

**Tabela kartı** `--rc-tabela-*` (tema bağımsız): kirada #0F6E4A/#FFF · bosta #2D2B25/#FFF · serviste #F2B705/#1B1A16 ·
rezerve #1F3A8A/#FFF · gecikmis #B3261E/#FFF · bar-iz rgba(255,255,255,.28) (koyu metinli kartta rgba(27,26,22,.18)).

**Plaka** `--rc-plaka-*` (tema bağımsız): zemin #FFFFFF · metin #111111 · serit #1F3A8A · serit-metin #FFFFFF · kenar #1B1B1B.

**Tablo başlığı** `--rc-tablo-baslik-zemin/-metin`: #2D2B25 / #ECE9E1 (iki temada aynı — ASFALT, referans görüntüyle kapandı).

**Chip (kayıtlı görünüm)**: kenar `kenar-kontrol`, seçili zemin `vurgu` + metin `vurgu-uzeri`, sayaç seçilide
beyaz/lacivert, hata sayacı `hata-zemin/hata-metin`.

### 2.5 Kontrast
Tema başına anlamsal çiftler + kabuk/bileşen çiftleri iki temada geçer (bağımsız hesap: buton yazısı/vurgu 10.34 açık ·
4.88 koyu; en dar `soluk/yuzey-vurgu` 5.03 / 4.79; grup başlığı/kenar çubuğu 4.92; beyaz/otoyol 6.27; koyu metin/sarı
9.58; beyaz/asfalt 14.15; beyaz/dur 6.54). `scripts/kontrast-denetimi.mjs`: metin 4.5, ikon/kenar/odak 3.0;
`var(--rc-ham-*)` zincirini çözer; hata → `npm run lint` kırmızı.

## 3. Tipografi
- **IBM Plex Sans** 400/500/600 (gövde) + **IBM Plex Sans Condensed** 600 (yalnız plaka çipi ve plaka arama kutusu).
  Self-host woff2, latin + latin-ext; kaynak `@fontsource/ibm-plex-sans` ve `@fontsource/ibm-plex-sans-condensed`
  (devDependency) → `src/styles/fonts/`; Inter dosyaları silinir. CSP `font-src 'self'` değişmez.
- Ölçek: 2xs 11 · xs 12 · sm 13 · md 14 (gövde) · lg 16 · xl 18 · 2xl 22 · 3xl 26 (sayfa başlığı). Sayfa bandı başlığı
  18/600. Tabela sayısı 34/600, `letter-spacing:-.02em`.
- `font-variant-numeric: tabular-nums` global. Para: `₺ 12.900,00`; km `98.212`; tarih `25.09.2026 14:36`.
- Grid: başlık 12/500, hücre 13, iki satırlı hücre alt satırı 11.5 soluk.

## 4. Boyut, yarıçap, derinlik, yoğunluk
- Kontrol 30/34/40; sayfa bandı butonu 32; kenar çubuğu satırı 30 (alt madde 28).
- Tablo satırı: basit tablo 36 · liste gridi (iki satırlı hücre) 46 · panel tabloları 42. Yoğunluk düğmesi yok.
- Yarıçap: kontrol 6 · kart/bölüm 10 · diyalog 12 · çip/rozet tam · plaka 4.
- Gölge yalnız katmanlarda: `--rc-golge-katman: 0 8px 24px rgba(27,26,22,.18)`.
- Kırılım: `$rc-kirilim-mobil: 900px`, `$rc-kirilim-dar: 600px` (SCSS değişkeni; ham px yasak).

## 5. Kabuk — `kabuk/**`
### 5.1 Kenar çubuğu (240px, daraltılınca 56px ikon şeridi)
- Üst: logo kutusu ("RA", beyaz zemin/lacivert) + "RentACar" + kiracı adı.
- Kısayol çifti: `+ Kira` (dolu beyaz) · `+ Rezervasyon` (çerçeveli).
- Menü: ikon (16px stroke SVG, mevcut ikon seti) + ad + sağda sayaç rozeti/chevron. Gruplar: Panel · Araçlar · Kira ·
  Rezervasyon · Cariler & CRM · Web Sitesi · Servis & Sigorta · Fiyat & Tarife · Finans · Raporlar · Tanımlar · Sistem.
  Aynı anda tek grup açık (akordeon), açık grup ve aktif öğe kalıcı (localStorage `rc.menu.acik`).
- **Kayıtlı görünümler** (Kira altında): Tüm sözleşmeler · Kiradaki araçlar `n` · Dönüşü gecikenler `n` (kırmızı rozet) ·
  Bugün çıkanlar `n` · Bugün dönecekler `n` · Faturası kesilmeyenler · Kapalı sözleşmeler → `kiralar?gorunum=…`; sayaç §9.
- Alt: şube seçici + kullanıcı (baş harf avatarı, ad, rol·şube) + çıkış.
- Koyu temada asfalt varyantı; lacivert varsayılan.
### 5.2 Üst çubuk (56px, yüzey)
Menü daralt · global arama 360px (⌘K → mevcut komut paleti) · sağda **plaka arama** (`rc-plaka-arama`) · bildirim +
sayaç · tema üçlüsü. Kullanıcı bloğu kenar çubuğunda; üst çubukta tekrar yok.
### 5.3 Sekme şeridi (36px) — mevcut çoklu-sekme korunur
Aktif sekme yüzey zemin + 2px lacivert alt çizgi; kapat "×" soluk; sağda `n/10`.
### 5.4 Sayfa bandı (`rc-sayfa-bandi`, 52px, lacivert)
Sol: ekran ikonu + başlık (18/600) + bağlam pill'i + ikincil metin. Sağ: çerçeveli beyaz butonlar + tek dolu beyaz birincil.
Gövdede ayrıca `<h1>` yok; band başlığı `<h1>`. Mobilde başlık + birincil; diğerleri "…" menüsüne.
### 5.5 Giriş ekranı: sol lacivert panel (logo, bir cümle), sağ kağıt form. Plaka motifi yok.
### 5.6 Mobil (≤900): kenar çubuğu çekmece; plaka arama ikon → tam genişlik; band 44px; tabela 2×2; grid yatay kaydırma +
ilk sütun (plaka) yapışkan.

## 6. Bileşenler
| Bileşen | Yol | API / davranış |
|---|---|---|
| `rc-plaka` | `shared/plaka/` | `[plaka]`, `boyut="sm|md|lg"` (22/28/40). `plakaNormalize()` saf fonksiyon (Ek A). Geçersiz → şeritsiz "yabancı" varyant, hata fırlatmaz. |
| `rc-plaka-arama` | `shared/plaka/` | TR şeritli input, Condensed 600, Enter → arama; boş/eşleşme yok → toast `bilgi`. |
| `rc-tabela-karti` | `shared/tabela-karti/` | `durum`, `[deger]`, `[oran]`, `etiket`, `altMetin`, `ikon`. Dolu kart, 34px sayı, oran çubuğu. |
| `rc-sayfa-bandi` | `kabuk/sayfa-bandi/` | `baslik`, `ikon`, `[pill]`, `[altMetin]`, `<ng-content select="[eylemler]">`. |
| `rc-gorunum-cipleri` | `shared/gorunum-cipleri/` | `[gorunumler]: {ad, sayac?, tur?: 'hata', aktif}`. |
| `rc-filtre-paneli` | `shared/filtre-paneli/` | 6 sütunlu grid, `Temizle` + `Filtrele` (tek dolu). |
| `rc-tablo` (mevcut) | `shared/tablo/tablo.scss` | Koyu başlık; seçim sütunu; toplu işlem çubuğu; `.rc-hucre-alt`; `.rc-satir-bugun`; altbilgi. |
| `.rc-duz-tablo` | `_duzen.scss` | Düz `<table>` için `rc-tablo` ile aynı görünüm. |
| `rc-rozet` | mevcut | 6 durum; dolu rozet yok. |
| `rc-hatirlatma-listesi` | `features/panel/` | 1 hf / 30 g / geçen sayaç satırları; geçen → hata rengi. |
| `rc-hizli-islemler` | `features/panel/` | İkonlu link listesi. |
| `rc-bos-durum` | mevcut | İkon + başlık + açıklama. |
| toast / bilgi kutusu | `_geri-bildirim.scss` | Toast asfalt zemin + durum ikonu; bilgi kutusu petrol. |
`rc-money-submit` `.form__eylemler` → global `.rc-form-eylemler`.

## 7. Düzen katmanı — `src/styles/_duzen.scss`
`.rc-sayfa` (dolgu 16 24, ritim 12) · `.rc-sayfa-basligi` (bandsız ekranlar, 3xl) · `.rc-bolum` · `.rc-kart` ·
`.rc-arac-cubugu` · `.rc-filtre-izgara` (6 sütun, ≤900 2, ≤600 1) · `.rc-duz-tablo` · `.rc-hucre-alt` · `.rc-num` ·
`.rc-bos` · `.rc-form-eylemler` · `.rc-izgara-2/3/4` · `.rc-yan-sutun` (316px, ≤900 alta).

## 8. Ekran kalıpları
- **Liste:** band → görünüm chip'leri → filtre paneli (varsayılan açık, durum localStorage) → grid kartı (toplu işlem çubuğu,
  koyu başlık, altbilgi). Plaka `rc-plaka sm`; araç iki satır (model / yakıt·vites·yıl); müşteri iki satır (ad / tür).
- **Form:** band (Kaydet dolu, Vazgeç çerçeveli) → `.rc-bolum`'ler → yapışkan finans paneli (gölge var).
- **Panel:** band → 4 tabela kartı → sol: Dönüşler (Tümü/Gecikmiş/Bugün/Yarın) + Çıkışlar; sağ 316px: Hatırlatmalar + Hızlı işlemler.
- **Detay/cari kartı:** band + `.rc-izgara-3` özet kartları + sekmeli bölümler.

## 9. Veri gereksinimleri (tasarımı bloke etmez)
- `GET /api/ui/v1/ozet/sayaclar` → kirada, bosta, serviste, acikRez, gecikenDonus, bugunCikan, bugunDonecek,
  yarinDonecek, faturasiz, gelenTalep, sigorta/kasko/muayene {hafta, ay, gecen}, kmBakim, gorulmeyenRez. Yoksa sayaçsız.
- Plaka arama: mevcut seçim/arama ucu yeterse yeni uç yok.
- Kayıtlı görünüm rotaları: `kiralar?gorunum=kirada|geciken|bugun-cikan|bugun-donecek|faturasiz|kapali` (frontend ön ayarı).

## 10–14. PR planı, doğrulama, kapsam dışı, başarı ölçütleri
Bkz. `~/.claude/plans/kanka-ak-yor-her-ey-idempotent-cake.md` ile aynı: A → B → C → P → D1–D4 → E. Davranış/iş mantığı
değişmez. Kapsam dışı: Blazor, firma vurgu rengi, grafik kütüphanesi, ikon seti değişimi, i18n yeniden yazımı, yoğunluk
düğmesi, yeni rapor içeriği. Başarı: kontrast iki temada geçer · axe 0 ciddi · taşma 0 · feature SCSS'te global sınıf
yeniden tanımı 0 · düz tablolar `.rc-duz-tablo` · plakalar `rc-plaka` · başlıklar `rc-sayfa-bandi` · initial ≤ 392 kB.

## 16. Ajan talimatları
- Renk yalnız `var(--rc-*)`; hex yazma. Durum rengi yalnız §1.2 anlamında.
- Sayfa başlığı = `rc-sayfa-bandi`; gövdede ikinci `<h1>` yok. Sayfada tek dolu birincil buton.
- Plaka her yerde `rc-plaka`; normalize `plakaNormalize()`; `toLocaleUpperCase('tr')` yasak.
- Düz `<table>` → `.rc-duz-tablo`; yerel `.sayfa/.kart/.tablo` tanımlama.
- Gölge yalnız katmanlarda. Gradyan, emoji, BÜYÜK HARF etiket yok. Yeni bileşen önce vitrine, sonra ekrana.

## 17. Kod gerçeği düzeltmeleri
1. Tema: `<html data-theme="light|dark">` + `prefers-color-scheme` (`sistem` modunda öznitelik yok).
2. Durum tokenları `--rc-{basari,uyari,hata,bilgi,notr}-{metin,zemin,kenar}`; hiçbir mevcut ad silinmez.
3. Kontrast betiği yalnız `#hex` / `var(--x,#hex)` çözüyordu → `var(--rc-ham-*)` zinciri + §2.4 çiftleri eklenir;
   `rgba()` içeren bileşen tokenları denetim listesine alınmaz.
4. e2e sabit arka planlar (`rgb(13, 19, 28)`, `rgb(245, 247, 250)`; vitrin/panel/kiralar/duman spec) yeni değerlere.
5. `tokenlar-vitrini.ts` token adlarını elle listeliyor → güncellenir; görsel tabanlar her görünüm PR'ında yenilenir.
6. Bütçe: `anyComponentStyle` uyarı 4 kB (kira-formu 4.7 KB, panel 4.3 KB kaynak) — düzen global'e taşınınca düşmeli.
7. `vehicles.scss` 7 feature'da `@use` → PR-B'de `_duzen.scss`'e taşınır.
8. Kırılımlar (900/600 px, 64rem, 30rem) → SCSS değişkenleri.

## Ek A — `plakaNormalize()`
- Trim; `toUpperCase()` (locale'siz) — `i`→`I`, `ı`→`I`; boşluk/tire kaldır.
- Desen: `^(0[1-9]|[1-7]\d|8[01])([ABCDEFGHIJKLMNOPRSTUVYZ]{1,3})(\d{2,5})$`; 1 harf → 4–5 rakam, 2 → 3–4, 3 → 2–3.
- Çıktı `{ham, kanonik, gosterim, gecerli}`; gösterim `"34 ABC 123"`. Geçersiz → `gecerli:false`, gösterim = ham büyük harf.
- Vakalar: `"34 abc 123"`→`34 ABC 123` · `"34abc123"`→`34 ABC 123` · `"34 ibc 123"`→`34 IBC 123` · `"07bfg582"`→`07 BFG 582` ·
  `"07 CYC 35"` geçerli · `"6 ABC 123"` geçersiz · `"82 ABC 123"` geçersiz · `"34 ÇBC 123"` geçersiz · `"34 A 12345"` geçerli ·
  `"34 ABC 1"` geçersiz · `"WWW 123"` geçersiz · `""` geçersiz, hata yok.

## Ek B — Ekran görüntüsü listesi (her PR)
Giriş → Panel · Kira listesi (Kirada) · Kira formu (finans paneli açık) · Araç listesi · Cari kartı · Rapor ekranı — 1440 ve
390, açık ve koyu; PR-C sonrası ek: kenar çubuğu daraltılmış, komut paleti açık, sekme şeridi 10/10.

## 18. Göç reçetesi (PR-P'den — D-ajanları bunu kopyalar)
Pilot: `features/kiralar/kira-listesi` (liste kalıbı) ve `features/kira-formu` (form kalıbı). Davranış/iş mantığı
DEĞİŞMEZ; yalnız şablon + stil. Hedef: feature SCSS'te stil denetimi bulgusu 0 (`node scripts/stil-denetimi.mjs --ayrinti`).

| Önce (yerel) | Sonra (global / paylaşılan) |
|---|---|
| `<div class="sayfa"><header class="ust"><h1>…</h1>…eylemler…</header>` | `<rc-sayfa-bandi ikon baslik [pill] [altMetin]>` + `<div class="rc-sayfa">` gövde. Bant sayfanın TEK `<h1>`'i |
| başlıktaki `rc-dugme--birincil rc-dugme--kucuk` "Yeni …" | `<a birincil class="rc-dugme rc-dugme--birincil">` (bantta tek dolu) |
| başlık/araç çubuğundaki ikincil bağlantılar, tablo içi Excel/CSV/PDF | `<ng-container eylemler>` içinde `class="rc-dugme"` (≤ 900 px "…" menüsü); dışa aktarma `disaAktarmaAdresi(d, bicim)` ile banda, `rc-tablo`'ya `[disaAktarma]` VERİLMEZ (çift bağlantı olmasın) |
| `<rc-katlanir-filtre>` + `<form [formGroup]>` + `.filtre__eylemler` (Filtrele/Temizle) | `<rc-filtre-paneli [formGroup] depoAnahtari="rc.filtre.<ekran>" [etkinSayisi] (filtrele) (temizle)>` — alanlar doğrudan içerik (6 sütun ızgara), `<form>` ve Filtrele/Temizle düğmeleri PANELİN |
| — | `<rc-gorunum-cipleri [gorunumler]>`: `?gorunum=` ön ayarı mevcut süzgeçlere çevrilir (bkz. `kira-gorunumleri.ts`), yeni uç yok |
| `.ozet` satırı tablonun üstünde | `<p rcTabloAraclari class="ozet" aria-live="polite">` (tablo araç çubuğunun solu) |
| `{{ satir.plaka }}` | `<rc-plaka boyut="sm" [plaka]="satir.plaka" />` (tabloda `rcTabloHucre="plaka"`, formda md) |
| durum rozeti `rc-rozet--bilgi` (kirada) | §1.2: kirada `--basari`, gecikmiş `--hata` ("n gün gecikti"), bugün `--uyari`, kapalı `--notr`; hesap SAF fonksiyonda (`satirGorunumu`), yalnız gösterim |
| — | `rc-tablo [satirSinifi]="fn"` → bugünün işi `rc-satir-bugun` |
| `.kart`, `.kf-kart` kutu stili | `class="rc-bolum"` (yan yana dizilim boşluğu gerekiyorsa yerel yalnız `margin`) |
| `.tablo-kutusu` + `.tablo` / `.kf-tablo` + `.num` | `.rc-tablo-kap > table.rc-duz-tablo` + `.rc-num`; boş satır `td.rc-bos`; seçili satır `[class.rc-satir-secili]` |
| yerel form eylem satırı | `.rc-form-eylemler` |
| feature'da `box-shadow` | global `.rc-bolum--katman` (yalnız katman: yapışkan finans paneli) |
| host sınıfı `rc-<ekran>` (kapsülsüz stil) | `rc-` önekli OLMAYAN host sınıfı (`kira-formu`) — `.rc-*` feature SCSS'te tanımlanamaz |
| `@media (max-width: 900px)` | `@use 'kirilim' as k;` + `k.$rc-kirilim-mobil` / `-dar` |

```html
<rc-sayfa-bandi ikon="key" [baslik]="'x.baslik' | transloco" [pill]="bantPill()" [altMetin]="bantAltMetni()">
  <ng-container eylemler>
    <a class="rc-dugme" [href]="disaAktarmaAdresi(d, 'excel')">Excel</a>
  </ng-container>
  @if (yeniIzni()) {
    <a birincil class="rc-dugme rc-dugme--birincil" routerLink="/x/yeni">Yeni x</a>
  }
</rc-sayfa-bandi>
<div class="rc-sayfa">
  <rc-gorunum-cipleri [gorunumler]="gorunumler()" />
  <rc-filtre-paneli depoAnahtari="rc.filtre.x" [formGroup]="filtreFormu" (filtrele)="filtrele()" (temizle)="temizle()">
    <rc-alan etiket="Ara"><rc-metin-girdisi formControlName="q" tur="search" /></rc-alan>
  </rc-filtre-paneli>
  <rc-tablo … [satirSinifi]="satirSinifi()">
    <p rcTabloAraclari class="ozet" aria-live="polite">{{ ozet() }}</p>
    <ng-template rcTabloHucre="plaka" [rcTabloHucreSutunlar]="sutunlar" let-s>
      <rc-plaka boyut="sm" [plaka]="s.plaka" />
    </ng-template>
  </rc-tablo>
</div>
```
Notlar: `SayfaBandi` kabukta (`kabuk/sayfa-bandi`) — feature'dan göreli içe aktarılır. Band başlığı değişirse e2e
`hazirBekle` (`baslik`) ve `getByRole('heading', { level: 1 })` beklentileri aynı PR'da güncellenir. Filtre paneli
varsayılan AÇIK: e2e'de "Filtreler" düğmesine tıklamak paneli KAPATIR (tıklama satırı silinir). İki satırlı hücre
(`.rc-hucre-alt`) yalnız API alanı varsa: kira listesinde araç modeli/yakıt/vites/yıl ve müşteri türü `KiraListeSatiri`'nda
YOK — pilotta uygulanmadı (API eki ayrı iş).
