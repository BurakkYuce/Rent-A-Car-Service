# Tasarım dili "Yol" — kurallar

Durum: yürürlükte (PR-E, 2026-09-26) · Kapsam: Angular SPA (`src/RentACar.Frontend`); Blazor dondurulmuş, kapsam dışı.

Bu dosya **kısa kural kitabıdır**: yeni ekran yazan ya da mevcut ekranı değiştiren herkes buradan başlar. Gerekçeler,
tam token tablosu, kabuk ayrıntısı ve PR geçmişi `YOL-PLANI-v2.md`'de (§ işaretleri o dosyaya aittir). Hedef görünüm:
`referans/panel.png`, `referans/kira-listesi.png` (piksel değil dil referansı). Kurallar `npm run lint` içinde
**hata kipinde** denetlenir (`scripts/stil-denetimi.mjs --kati`, `scripts/kontrast-denetimi.mjs`).

## 1. İlkeler (§1.1)

1. **Kağıt üstüne yazı.** Sıcak nötr zemin; sayfa içinde gölge yok, 1 px kenar + yüzey farkı. Gölge yalnız katmanlarda
   (açılır panel, diyalog, toast, yapışkan finans paneli → `.rc-bolum--katman`).
2. **Renk = durum.** Durum renkleri yalnız §3 sözlüğündeki anlamda; dekoratif renk, gradyan, mor/bordo yok.
3. **Fiziksel nesneler sabit.** Plaka çipi ve tabela kartı iki temada aynı renk; tema yalnız zemin/metin/kontrolü değiştirir.
4. **Dolu ama disiplinli.** Yoğun grid, sayaçlı görünümler, tek bakışta filtre; 11 px gövde metni yok.
5. **Sayfada tek dolu birincil eylem.** Gerisi çerçeveli (`.rc-dugme`) ya da hayalet (`.rc-dugme--hayalet`).
6. **Cümle düzeni.** BÜYÜK HARF etiket, eyebrow, "→" eki, emoji yok.
7. **Sarı-lacivert marka çifti yok.** Sarı yalnız "serviste / uyarı".

## 2. Token katmanları (§2)

Tek dosya: `src/styles/_tokenlar.scss`. Bileşen ve feature stilinde **yalnız `var(--rc-*)`** — hex/rgb/hsl yazılmaz.

| Katman           | Örnek                                                                                                                                                                                                                                    | Kim okur                                                      |
| ---------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------- |
| Ham `--rc-ham-*` | `--rc-ham-lacivert-800`, `--rc-ham-kagit-50`                                                                                                                                                                                             | YALNIZ `_tokenlar.scss` (anlamsal/bileşen tokenlarını besler) |
| Anlamsal         | `--rc-zemin`, `--rc-yuzey`, `--rc-yuzey-alt`, `--rc-kenar`, `--rc-metin(-ikincil/-soluk)`, `--rc-vurgu(-metin/-zemin)`, `--rc-{basari,uyari,hata,bilgi,notr}-{metin,zemin,kenar}`, `--rc-satir-vurgu`, `--rc-secim`, `--rc-golge-katman` | Her bileşen / feature                                         |
| Bileşen          | `--rc-kenar-cubugu-*`, `--rc-bant-*`, `--rc-tabela-*`, `--rc-plaka-*`, `--rc-tablo-baslik-*`, `--rc-cip-*`                                                                                                                               | Yalnız kendi bileşeni                                         |

- **Ölçek:** yazı `--rc-yazi-2xs…3xl` (gövde `md` 14 px, sayfa başlığı `3xl`), boşluk `--rc-bosluk-N` (N × 4 px), yarıçap
  kontrol 6 · kart 10 · diyalog 12 · tam · plaka 4, kontrol yüksekliği 30/34/40, tablo satırı 36 / liste 46 / panel 42.
- **Font:** IBM Plex Sans (`--rc-font`); Condensed 600 (`--rc-font-plaka`) yalnız plaka. Sayılar global `tabular-nums`.
- **Kırılım:** `@use 'kirilim' as k;` → `k.$rc-kirilim-mobil` (900 px), `-dar` (600 px), `-form` (64 rem),
  `-cok-dar` (30 rem). `@media` içinde ham px yazılmaz.
- **Tema:** `:root` açık; `prefers-color-scheme: dark` + `:root:not([data-theme=light])` koyu; `[data-theme=dark]` her
  durumda koyu. Renk değiştiren PR kontrast tablosunu yeşil tutar (metin ≥ 4.5, kontrol kenarı/odak ≥ 3).

## 3. Durum sözlüğü (§1.2 — tek kaynak)

| Durum                 | Rozet                                       | Tabela kartı                       | Anlam                         |
| --------------------- | ------------------------------------------- | ---------------------------------- | ----------------------------- |
| Kirada                | `.rc-rozet--basari`                         | `durum="kirada"` (otoyol yeşili)   | araç yolda, gelir             |
| Boşta                 | `.rc-rozet--notr`                           | `durum="bosta"` (asfalt)           | müsait                        |
| Serviste              | `.rc-rozet--uyari`                          | `durum="serviste"` (işaret sarısı) | bakım, dikkat                 |
| Rezerve               | `.rc-rozet--vurgu`                          | `durum="rezerve"` (lacivert)       | söz verilmiş                  |
| Gecikmiş              | `.rc-rozet--hata` ("n gün gecikti")         | `durum="gecikmis"` (dur kırmızısı) | eylem gerek                   |
| Bugün dönüyor/çıkıyor | `.rc-rozet--uyari` + satır `rc-satir-bugun` | —                                  | bugünün işi                   |
| Bilgi / kapalı        | `.rc-rozet--bilgi` / `--notr`               | —                                  | bilgilendirme, kapanmış kayıt |

Durum → rozet eşlemesi SAF fonksiyonda hesaplanır (ör. kira listesi `satirGorunumu`); şablon yalnız gösterir. Dolu rozet yok.

## 4. Bileşenler ve düzen

**Paylaşılan bileşenler** (`shared/`, `kabuk/`; yeni bileşen önce vitrine, sonra ekrana):

| Ne                  | Nasıl                                                                                                                                                                                                                                       |
| ------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Sayfa başlığı       | `rc-sayfa-bandi` (`kabuk/sayfa-bandi`, göreli içe aktarım): `baslik`, `ikon`, `[pill]`, `[altMetin]`; `<ng-container eylemler>` çerçeveli ikinciller (≤ 900 px "…" menüsü), `[birincil]` yuvası tek dolu eylem. Bant sayfanın TEK `<h1>`'i. |
| Plaka               | `<rc-plaka [plaka] boyut="sm                                                                                                                                                                                                                | md  | lg">`(tabloda`sm`, formda `md`); normalize `plakaNormalize()`; `toLocaleUpperCase('tr')`yasak. Arama:`rc-plaka-arama`. |
| Durum kartı         | `rc-tabela-karti` (`durum`, `[deger]`, `[oran]`, `etiket`, `altMetin`, `ikon`).                                                                                                                                                             |
| Kayıtlı görünüm     | `rc-gorunum-cipleri [gorunumler]` — bağlantı kipi (`link`/`sorgu`) ya da düğme kipi (`id` + `(secildi)`).                                                                                                                                   |
| Filtre              | `rc-filtre-paneli [formGroup] depoAnahtari="rc.filtre.<ekran>" (filtrele) (temizle)` (6 sütun, varsayılan açık). Özel düğme metni/disabled gerekirse: `<form class="rc-bolum">` + `.rc-filtre-izgara` + sağa yaslı eylemler.                |
| Veri tablosu        | `rc-tablo` (asfalt başlık, sıralama, sayfalama, `[satirSinifi]` → `rc-satir-bugun`); dışa aktarma bantta `disaAktarmaAdresi()` ile, `rc-tablo`'ya `[disaAktarma]` verilmez.                                                                 |
| Boş / geri bildirim | `rc-bos-durum`, `.rc-form-mesaji(--uyari/--hata)`, toast (`rc-toast-alani`), `rc-onay-diyalogu`.                                                                                                                                            |

**Düzen katmanı** (`src/styles/_duzen.scss`, global): `.rc-sayfa` (gövde; dolgu 16/24, ritim 12) ·
`.rc-sayfa-basligi` (yalnız bandsız ekran) · `.rc-bolum` (+ `> h2`, `.rc-bolum__baslik`) · `.rc-kart` ·
`.rc-arac-cubugu` · `.rc-filtre-izgara` · `.rc-izgara-2/3/4` · `.rc-yan-sutun` (316 px, ≤ 900 alta) ·
`.rc-tablo-kap > table.rc-duz-tablo` · `.rc-num` · `.rc-hucre-alt` · `td.rc-bos` · `.rc-satir-secili` ·
`.rc-form-eylemler` · `.rc-toplu-islem`. Form: `.rc-form-izgara`, `rc-alan`, `rc-*-girdisi`. Düğme: `.rc-dugme`
(`--birincil`, `--hayalet`, `--tehlike`, `--kucuk`, `--ikon`), `.rc-dugme-grubu`.

## 5. Yap / yapma

| Yap                                                            | Yapma                                                                                                        |
| -------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------ |
| `var(--rc-*)` renk, `--rc-yazi-*` yazı, `--rc-bosluk-*` boşluk | hex/rgb/hsl, ham `font-size: 13px`                                                                           |
| `@use 'kirilim' as k;` + `k.$rc-kirilim-*`                     | `@media (max-width: 900px)`                                                                                  |
| Global `.rc-sayfa/.rc-bolum/.rc-kart/.rc-duz-tablo/.rc-num`    | feature SCSS'te `.sayfa/.ust/.kart/.tablo/.num/.bolum/.aciklama/.islemler` ya da herhangi bir `.rc-*` tanımı |
| Açıklayıcı metin için yerel `.not` (ikincil renk, `xs`)        | gövdede ikinci `<h1>`                                                                                        |
| Satır içi eylemler için yerel ad (`.satir-eylem`)              | feature'da `box-shadow` (katman gerekiyorsa `.rc-bolum--katman`)                                             |
| Host sınıfı `rc-` öneksiz (`kira-formu`)                       | sayfaya ikinci dolu birincil düğme                                                                           |
| Plaka hep `rc-plaka`                                           | plakayı düz metin ya da `tr` büyük harfle basmak                                                             |

**Denetim** (`npm run stil`, lint'te `--kati`): kurallar `renk`, `yazi`, `medya`, `global`, `golge`, `plaka-tr`, `baslik` (features şablonunda gövde `<h1>`; yalnız bandsız ekranlar — giriş, platform girişi, yazdırma belgesi, vitrin — muaf).
`src/app/features/**` istisna alamaz. Feature DIŞINDA gerçekten gerekli tek tük değer (kök rem tabanı, baskıda `pt`)
aynı ya da bir önceki satıra `// stil-denetimi: izin — <gerekçe>` yazılarak işaretlenir; çıktı bunları "izinli"
olarak sayar. Gerekçesiz işaret geçmez.

## 6. Ekran kalıpları (§8, reçete §18)

- **Liste:** bant (dışa aktarma ikincil, "Yeni x" birincil) → görünüm çipleri → filtre paneli → `rc-tablo` (araç
  yuvasında `<p rcTabloAraclari class="ozet">`, plaka hücresi `rc-plaka sm`, bugünün işi `rc-satir-bugun`).
- **Form:** bant (Kaydet dolu, Vazgeç çerçeveli; para gönderimi `rc-money-submit` gövdedeyse bant birincilsiz) →
  `.rc-bolum`'ler → yapışkan finans paneli (`.rc-bolum--katman`).
- **Panel:** bant → 4 tabela kartı (`.rc-izgara-4`) → `.rc-yan-sutun`: sol dönüş/çıkış tabloları, sağ hatırlatmalar +
  hızlı işlemler.
- **Detay / cari kartı:** bant + `.rc-izgara-3` özet kartları (`.rc-kart`) + sekmeli bölümler.
- **Rapor:** bant (Excel/CSV/PDF ikincil) → açıklama `.not` → görünüm çipleri (düğme kipi) → süzgeç `.rc-bolum` +
  `.rc-filtre-izgara` → özet kutuları (`.rc-kart`) → `.rc-bolum` içinde `.rc-duz-tablo` ya da `rc-tablo`.
- **Tanım / sistem CRUD:** bant ("Yeni" birincil) → `rc-tanim-crud` ya da `.rc-bolum` form + `.rc-duz-tablo`.
- **Bandsız ekranlar:** giriş (§5.5: sol lacivert panel, sağ kağıt form), platform girişi, yazdırma belgeleri —
  başlık gövdede `<h1>`; kabuktan bağımsız.
- **Platform konsolu:** kendi kabuğu korunur; sayfa başlığı yine `rc-sayfa-bandi`, gövde aynı düzen katmanı.

## 7. Nereye bakmalı

- Tam spesifikasyon, token değerleri, kabuk: `YOL-PLANI-v2.md` (§2 token, §5 kabuk, §6 bileşen, §17 kod gerçeği).
- Göç reçetesi (önce/sonra tablosu + örnek şablon): `YOL-PLANI-v2.md` §18.
- Canlı örnekler: `/app/vitrin` (bileşen ve token vitrinleri); göçmüş ekranlar `features/kiralar`, `features/panel`,
  `features/vehicles`, `features/finance`, `features/reports`.
- Frontend genel kuralları (i18n, biçim, test): `src/RentACar.Frontend/AGENTS.md`.
