#!/usr/bin/env node
// Stil denetimi (Yol v2 §2.1, §3, §4, §16; kurallar `docs/tasarim/TASARIM-DILI.md`): tasarım dilinin kod
// kurallarını SCSS'te ve bileşen satır içi stillerinde (`styles: \`…\``) tarar. `npm run lint` bunu `--kati`
// (hata kipi: bulgu varsa çıkış 1) ile koşar (PR-E); bayraksız çağrı uyarı kipidir (çıkış 0, sayım basar).
//
// Kurallar (src/styles/_tokenlar.scss HARİÇ — renkler yalnız orada tanımlanır):
//   renk        hex / rgb() / hsl() literali (renk yalnız var(--rc-*))
//   yazi        font-size değeri var(--rc-yazi-*) üzerinden değil (ham px/rem)
//   medya       @media içinde ham px (kırılım `_kirilim.scss` değişkeniyle)
//   global      features/** içinde `.sayfa/.ust/.kart/.tablo/.num/.bolum/.aciklama/.islemler` ya da
//               herhangi bir `.rc-*` global sınıfının yeniden tanımı (düzen katmanı `_duzen.scss`'te)
//   golge       features/** içinde box-shadow (gölge yalnız katmanlarda, global/shared'da)
//   plaka-tr    shared/plaka/** içinde Türkçe yerel ayarlı büyük harf (Ek A: plakada İ yok)
//   baslik      features/** şablonunda (`.html` ya da satır içi `template:`) gövde `<h1>` — sayfa başlığı
//               `rc-sayfa-bandi`'dir (§5.4, §16). Yalnız BANDSIZ ekranlar (giriş, platform girişi, yazdırma
//               belgesi, vitrin) muaf.
//
// Gerekçeli istisna: bulgunun AYNI ya da BİR ÖNCEKİ satırında `stil-denetimi: izin — <gerekçe>` yorumu varsa bulgu
// sayılmaz, "izinli" olarak raporlanır. YALNIZ `src/app/features/**` DIŞINDA geçerlidir (ör. kök rem tabanı,
// baskıda `pt`); ekran kodu kurala uyar, istisna alamaz. Gerekçesiz işaret yok sayılır.
//
// Kullanım: node scripts/stil-denetimi.mjs [--kati] [--ayrinti]
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative, resolve, sep } from 'node:path';

const KOK = resolve(import.meta.dirname, '..');
const SRC = join(KOK, 'src');
const KATI = process.argv.includes('--kati');
const HARIC = new Set([join(SRC, 'styles', '_tokenlar.scss')]);

/** Feature'da yerel tanımı yasak eski düzen sınıfları (§16; global karşılıkları `_duzen.scss`'te). */
const ESKI_GLOBAL = /\.(sayfa|ust|kart|tablo|num|bolum|aciklama|islemler)(?![\w-])/;
const RC_SINIF = /\.rc-[\w-]+/;

/** Kabuk bandı olmayan ekranlar: başlıkları gövdede `<h1>` (§5.5 giriş, yazdırma belgesi, geliştirici vitrini). */
const BANDSIZ = [
  'src/app/features/giris/',
  'src/app/features/platform/login/',
  'src/app/features/kira-formu/kira-yazdir.ts',
  'src/app/features/vitrin/',
];

function dosyalar(dizin) {
  const sonuc = [];
  for (const ad of readdirSync(dizin)) {
    if (ad === 'node_modules' || ad.startsWith('.')) continue;
    const yol = join(dizin, ad);
    if (statSync(yol).isDirectory()) sonuc.push(...dosyalar(yol));
    else if (/\.(scss|ts|html)$/.test(ad) && !/\.spec\.ts$/.test(ad)) sonuc.push(yol);
  }
  return sonuc;
}

/** Yorumları aynı uzunlukta boşlukla değiştirir (satır numaraları korunur). */
function yorumsuz(metin) {
  return metin
    .replace(/\/\*[\s\S]*?\*\//g, (m) => m.replace(/[^\n]/g, ' '))
    .replace(/(^|[^:])\/\/[^\n]*/g, (m, on) => on + ' '.repeat(m.length - on.length));
}

/** `.ts` dosyasındaki `styles: \`…\`` bloklarını (başlangıç ofsetiyle) döndürür. */
function satirIciStiller(metin) {
  const bloklar = [];
  for (const m of metin.matchAll(/styles:\s*(?:\[\s*)?`([^`]*)`/g)) {
    bloklar.push({ css: m[1], ofset: m.index + m[0].indexOf('`') + 1 });
  }
  return bloklar;
}

/**
 * `.ts` dosyasındaki `template: \`…\`` bloklarını, satır numaraları korunarak (şablon dışı her karakter boşluk,
 * satır sonları aynen) döndürür.
 */
function satirIciSablonlar(metin) {
  const maske = metin.replace(/[^\n]/g, ' ').split('');
  for (const m of metin.matchAll(/template:\s*`([^`]*)`/g)) {
    const bas = m.index + m[0].indexOf('`') + 1;
    for (let i = 0; i < m[1].length; i++) maske[bas + i] = m[1][i];
  }
  return maske.join('');
}

/** Çok satırlı seçiciyi tek satıra indirir (rapor). */
const tek = (metin) => metin.replace(/\s+/g, ' ');

function satirNo(metin, ofset) {
  let n = 1;
  for (let i = 0; i < ofset; i++) if (metin.charCodeAt(i) === 10) n++;
  return n;
}

function cssDenetle(css, { feature }) {
  const bulgular = [];
  const temiz = yorumsuz(css);
  const ekle = (kural, idx, ayrinti) => bulgular.push({ kural, idx, ayrinti });

  for (const m of temiz.matchAll(/#[0-9a-fA-F]{3,8}(?![\w-])|\b(?:rgba?|hsla?)\(/g)) {
    ekle('renk', m.index, m[0]);
  }
  for (const m of temiz.matchAll(/font-size\s*:\s*([^;}\n]+)/g)) {
    const v = m[1].trim();
    if (/\d/.test(v) && !v.includes('var(')) ekle('yazi', m.index, `font-size: ${v}`);
  }
  for (const m of temiz.matchAll(/@media[^{]*/g)) {
    if (/\d+(?:\.\d+)?px/.test(m[0])) ekle('medya', m.index, m[0].trim());
  }
  if (feature) {
    // Seçici metni: `{`'ten önce, son `;`/`{`/`}`'ten sonra. @-kuralları ve iç içe `&` bildirimleri hariç.
    for (const m of temiz.matchAll(/([^{};]+)\{/g)) {
      const secici = m[1].trim();
      if (secici.startsWith('@') || secici === '') continue;
      const eski = ESKI_GLOBAL.exec(secici);
      const rc = RC_SINIF.exec(secici);
      if (eski) ekle('global', m.index + m[0].indexOf(secici), `${eski[0]} (${tek(secici)})`);
      else if (rc) ekle('global', m.index + m[0].indexOf(secici), `${rc[0]} (${tek(secici)})`);
    }
    for (const m of temiz.matchAll(/box-shadow\s*:/g)) ekle('golge', m.index, 'box-shadow');
  }
  return bulgular;
}

const IZIN = /stil-denetimi:\s*izin\s*[—–-]\s*\S/;

/** Bulgu satırında ya da bir öncekinde gerekçeli izin işareti var mı (satır 1 tabanlı). */
function izinli(satirlar, satir) {
  return IZIN.test(satirlar[satir - 1] ?? '') || IZIN.test(satirlar[satir - 2] ?? '');
}

const tumBulgular = [];
const izinliBulgular = [];
for (const dosya of dosyalar(SRC)) {
  if (HARIC.has(dosya)) continue;
  const rel = relative(KOK, dosya).split(sep).join('/');
  const metin = readFileSync(dosya, 'utf8');
  const feature = rel.startsWith('src/app/features/');
  const bulgular = [];

  if (feature && !BANDSIZ.some((onek) => rel.startsWith(onek))) {
    const sablon = dosya.endsWith('.html') ? metin : satirIciSablonlar(metin);
    for (const m of sablon.matchAll(/<h1[\s>]/g)) {
      bulgular.push({ kural: 'baslik', satir: satirNo(sablon, m.index), ayrinti: 'gövde <h1>' });
    }
  }

  if (dosya.endsWith('.html')) {
    // Şablon yalnız başlık kuralına tabi (stil yok).
  } else if (dosya.endsWith('.scss')) {
    for (const b of cssDenetle(metin, { feature })) {
      bulgular.push({ ...b, satir: satirNo(metin, b.idx) });
    }
  } else {
    for (const blok of satirIciStiller(metin)) {
      for (const b of cssDenetle(blok.css, { feature })) {
        bulgular.push({ ...b, satir: satirNo(metin, blok.ofset + b.idx) });
      }
    }
  }
  if (rel.startsWith('src/app/shared/plaka/')) {
    for (const m of yorumsuz(metin).matchAll(/toLocaleUpperCase\(\s*['"`]tr/g)) {
      bulgular.push({ kural: 'plaka-tr', satir: satirNo(metin, m.index), ayrinti: m[0] });
    }
  }
  const satirlar = metin.split('\n');
  for (const b of bulgular) {
    if (!feature && izinli(satirlar, b.satir)) izinliBulgular.push({ dosya: rel, ...b });
    else tumBulgular.push({ dosya: rel, ...b });
  }
}

const KURALLAR = ['renk', 'yazi', 'medya', 'global', 'golge', 'plaka-tr', 'baslik'];
const dosyaBasi = new Map();
for (const b of tumBulgular) {
  const s = dosyaBasi.get(b.dosya) ?? Object.fromEntries(KURALLAR.map((k) => [k, 0]));
  s[b.kural]++;
  dosyaBasi.set(b.dosya, s);
}

const toplam = Object.fromEntries(KURALLAR.map((k) => [k, 0]));
for (const b of tumBulgular) toplam[b.kural]++;

if (process.argv.includes('--ayrinti')) {
  for (const b of tumBulgular) console.log(`${b.dosya}:${b.satir}  [${b.kural}]  ${b.ayrinti}`);
  for (const b of izinliBulgular) {
    console.log(`${b.dosya}:${b.satir}  [${b.kural}, izinli]  ${b.ayrinti}`);
  }
  console.log('');
}

if (tumBulgular.length > 0) {
  const genislik = Math.max(...[...dosyaBasi.keys()].map((d) => d.length));
  console.log(`${'dosya'.padEnd(genislik)}  ${KURALLAR.map((k) => k.padStart(8)).join('')}`);
  for (const [dosya, s] of [...dosyaBasi].sort((a, b) => a[0].localeCompare(b[0], 'en'))) {
    console.log(
      `${dosya.padEnd(genislik)}  ${KURALLAR.map((k) => String(s[k] || '·').padStart(8)).join('')}`,
    );
  }
}
const ozet = KURALLAR.map((k) => `${k} ${toplam[k]}`).join(' · ');
const kip = KATI ? 'hata kipi' : 'uyarı kipi';
console.log(
  `stil denetimi (${kip}): ${tumBulgular.length} bulgu, ${dosyaBasi.size} dosya — ${ozet}` +
    (izinliBulgular.length > 0 ? ` · izinli ${izinliBulgular.length}` : '') +
    (tumBulgular.length > 0 ? ' (ayrıntı: node scripts/stil-denetimi.mjs --ayrinti)' : ''),
);
if (KATI && tumBulgular.length > 0) process.exit(1);
