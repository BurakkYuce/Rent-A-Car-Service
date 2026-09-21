#!/usr/bin/env node
// Tema kontrast denetimi: src/styles/_tokenlar.scss içindeki açık ve koyu renk token'larını okur,
// WCAG 2.x kontrast oranlarını hesaplar ve tablo basar. Eşik altı çift varsa çıkış kodu 1.
//   Metin ≥ 4.5 (AA, normal boyut) · Kontrol kenarı ve odak halkası ≥ 3 (WCAG 1.4.11)
// Kullanım: node scripts/kontrast-denetimi.mjs            (npm run lint de koşar)
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const DOSYA = resolve(import.meta.dirname, '../src/styles/_tokenlar.scss');
const kaynak = readFileSync(DOSYA, 'utf8');

/** `@mixin ad { ... }` gövdesini okur ve `--rc-x: değer;` satırlarını sözlüğe çevirir. */
function mixinTokenlari(ad) {
  const bas = kaynak.indexOf(`@mixin ${ad} {`);
  if (bas < 0) throw new Error(`@mixin ${ad} bulunamadı`);
  const son = kaynak.indexOf('\n}', bas);
  const govde = kaynak.slice(bas, son);
  const tokenlar = new Map();
  for (const eslesme of govde.matchAll(/(--rc-[\w-]+):\s*([^;]+);/g)) {
    tokenlar.set(eslesme[1], eslesme[2].trim());
  }
  return tokenlar;
}

/** `#abc`, `#aabbcc`, `var(--x)` ya da `var(--kiraci, #hex)` → `#aabbcc`. */
function coz(tokenlar, deger, derinlik = 0) {
  if (derinlik > 8) throw new Error(`Döngüsel token: ${deger}`);
  const v = deger.trim();
  const hex = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(v);
  if (hex) {
    const h = hex[1];
    return h.length === 3 ? `#${h[0]}${h[0]}${h[1]}${h[1]}${h[2]}${h[2]}` : `#${h}`;
  }
  const degisken = /^var\((--[\w-]+)(?:,\s*(.+))?\)$/.exec(v);
  if (degisken) {
    const [, ad, yedek] = degisken;
    if (tokenlar.has(ad)) return coz(tokenlar, tokenlar.get(ad), derinlik + 1);
    if (yedek) return coz(tokenlar, yedek, derinlik + 1);
  }
  throw new Error(`Çözülemeyen renk: ${deger}`);
}

function goreliParlaklik(hex) {
  const kanal = (i) => {
    const c = parseInt(hex.slice(i, i + 2), 16) / 255;
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * kanal(1) + 0.7152 * kanal(3) + 0.0722 * kanal(5);
}

function kontrast(a, b) {
  const [la, lb] = [goreliParlaklik(a), goreliParlaklik(b)];
  return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05);
}

const METIN = 4.5;
const ARAYUZ = 3;
const YUZEYLER = ['--rc-zemin', '--rc-yuzey', '--rc-yuzey-alt', '--rc-yuzey-vurgu'];

/** [ön plan, arka plan, eşik, açıklama] */
const CIFTLER = [
  ...['--rc-metin', '--rc-metin-ikincil', '--rc-metin-soluk', '--rc-vurgu-metin'].flatMap((on) =>
    YUZEYLER.map((arka) => [on, arka, METIN, 'metin']),
  ),
  ['--rc-vurgu-uzeri', '--rc-vurgu', METIN, 'birincil düğme'],
  ['--rc-vurgu-uzeri', '--rc-vurgu-hover', METIN, 'birincil düğme (üzerinde)'],
  ['--rc-vurgu-metin', '--rc-vurgu-zemin', METIN, 'seçili öğe'],
  ['--rc-metin', '--rc-secim', METIN, 'metin seçimi'],
  ...['basari', 'uyari', 'hata', 'bilgi', 'notr'].flatMap((d) => [
    [`--rc-${d}-metin`, `--rc-${d}-zemin`, METIN, 'durum rozeti'],
    [`--rc-${d}-metin`, '--rc-yuzey', METIN, 'durum metni'],
  ]),
  ...['--rc-zemin', '--rc-yuzey', '--rc-yuzey-alt'].map((arka) => [
    '--rc-kenar-kontrol',
    arka,
    ARAYUZ,
    'kontrol kenarı',
  ]),
  // Halka `outline-offset` ile dışarı çizilir; arkasında düğme değil yüzey vardır.
  ...YUZEYLER.map((arka) => ['--rc-odak', arka, ARAYUZ, 'odak halkası']),
];

let hata = 0;
for (const [tema, mixin] of [
  ['açık', 'acik-renkler'],
  ['koyu', 'koyu-renkler'],
]) {
  const tokenlar = mixinTokenlari(mixin);
  console.log(`\n${tema} tema`);
  let enDusuk = Infinity;
  for (const [on, arka, esik, aciklama] of CIFTLER) {
    const oran = kontrast(coz(tokenlar, `var(${on})`), coz(tokenlar, `var(${arka})`));
    enDusuk = Math.min(enDusuk, oran / esik);
    const tamam = oran >= esik;
    if (!tamam) hata++;
    console.log(
      `  ${tamam ? 'tamam' : 'EŞİK ALTI'}  ${oran.toFixed(2).padStart(5)} ≥ ${esik}  ` +
        `${on} / ${arka}  (${aciklama})`,
    );
  }
  console.log(`  en dar pay: eşiğin ${enDusuk.toFixed(2)} katı`);
}

// TemaServisi kiracı vurgusunu bu zeminlere karşı okunur yapar; kopya SCSS'ten kayarsa hata.
const SERVIS = resolve(import.meta.dirname, '../src/app/core/tema/tema-servisi.ts');
const servisKaynak = readFileSync(SERVIS, 'utf8');
const ZEMIN_SIRASI = [
  '--rc-zemin',
  '--rc-yuzey',
  '--rc-yuzey-alt',
  '--rc-yuzey-vurgu',
  '--rc-vurgu-zemin',
];
for (const [tema, mixin] of [
  ['acik', 'acik-renkler'],
  ['koyu', 'koyu-renkler'],
]) {
  const tokenlar = mixinTokenlari(mixin);
  const beklenen = ZEMIN_SIRASI.map((ad) => coz(tokenlar, `var(${ad})`));
  const satir = new RegExp(`${tema}: \\[([^\\]]+)\\]`).exec(servisKaynak);
  const servisteki = satir ? [...satir[1].matchAll(/'(#[0-9a-f]{6})'/gi)].map((m) => m[1]) : [];
  if (servisteki.join() !== beklenen.join()) {
    hata++;
    console.error(
      `TEMA_ZEMINLERI.${tema} SCSS ile eşleşmiyor: ${servisteki.join()} ≠ ${beklenen.join()}`,
    );
  }
}

if (hata > 0) {
  console.error(`\nKontrast denetimi: ${hata} sorun (eşik altı çift ya da SCSS/TS kayması).`);
  process.exit(1);
}
console.log('\nKontrast denetimi: tüm çiftler eşikte ya da üstünde.');
