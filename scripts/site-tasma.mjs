#!/usr/bin/env node
/**
 * Halka açık site — YATAY TAŞMA ölçümü, açılış animasyonu SIRASINDA ve SONRASINDA.
 *
 *   node scripts/site-tasma.mjs http://pazar.localhost:5230 http://rota.localhost:5230 http://vitrin.localhost:5230
 *   node scripts/site-tasma.mjs --genislik 320,390,768 http://pazar.localhost:5230
 *
 * Her kök için sayfalar: ana sayfa, /musaitlik (tarihsiz ve yarın + 3 gün), ilk ilan detayı, talep formu, iletişim,
 * SSS, blog listesi + ilk yazı, eşleşmeyen adres (404). Ölçüm (varsayılan 320 ve 390 px):
 *   1. Animasyon sırasında: belgedeki TÜM animasyonlar duraklatılır, zaman 0'dan bitişe 25 ms adımla ilerletilir;
 *      her adımda `scrollWidth − clientWidth` ölçülür (gerçek zamanlı bekleme yok → kararlı, hızlı).
 *   2. Animasyon sonrasında: aynı ölçü + KIRPILMIŞ İÇERİK denetimi — dinlenme hâlinde görünüm alanını aşan öğe
 *      (kaydırma kabı, `overflow: hidden` ya da sahne içindeki öğeler hariç). `main { overflow-x: clip }` gibi bir
 *      kırpma taşmayı belgeden gizler; bu denetim gerçek içeriğin kesilmediğini ayrıca doğrular.
 * Taşma bulunursa çıkış kodu 1. Suçlu öğe, sağ kenarı görünüm alanını aşan öğedir (genişlik değil, konum).
 *
 * CI'da koşmaz (PublicSite + veritabanı + yayındaki ilanlar ister); CI'daki kilit yapısal testlerdir
 * (`PublicSiteThemeContractTests.Overflow`). Tema değişikliğinde yerelde/demoda çalıştır (Themes/README.md).
 * Playwright, ön yüz projesinin bağımlılığından yüklenir (`src/RentACar.Frontend`'de `npm ci` yeterli).
 */
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const require = createRequire(join(dirname(fileURLToPath(import.meta.url)), '..', 'src', 'RentACar.Frontend', 'package.json'));
const { chromium } = require('playwright');

const args = process.argv.slice(2);
const wIdx = args.indexOf('--genislik');
const widths = (wIdx >= 0 ? args.splice(wIdx, 2)[1] : '320,390').split(',').map(Number);
const roots = args.map((r) => r.replace(/\/$/, ''));
if (!roots.length) {
  console.error('Kullanım: node scripts/site-tasma.mjs [--genislik 320,390] <kök-url>...');
  process.exit(2);
}

// Yerel gün (UTC değil): gece yarısından sonra toISOString bir önceki günü verir.
const day = (n) => {
  const d = new Date();
  d.setDate(d.getDate() + n);
  const p = (x) => String(x).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
};

/** Sayfada çalışır: animasyonları adım adım oynatıp en kötü taşmayı, sonra dinlenme hâlini ölçer. */
async function measure() {
  await document.fonts.ready;
  const doc = document.documentElement;
  const overflow = () => doc.scrollWidth - doc.clientWidth;
  const name = (e) => `${e.tagName.toLowerCase()}${e.id ? '#' + e.id : ''}${[...e.classList].map((c) => '.' + c).join('')}`;
  const offenders = () => {
    const w = doc.clientWidth;
    const scrollerOrClipped = (e) => {
      for (let a = e.parentElement; a && a !== document.body; a = a.parentElement) {
        const o = getComputedStyle(a).overflowX;
        if (o === 'auto' || o === 'scroll' || o === 'hidden') return true;
      }
      return false;
    };
    return [...document.querySelectorAll('body *')]
      .filter((e) => {
        const r = e.getBoundingClientRect();
        if (r.width <= 1 || r.height <= 1) return false; // görsel olarak gizli (ekran okuyucu metni)
        return (r.right > w + 0.5 || r.left < -0.5) && !scrollerOrClipped(e);
      })
      .slice(0, 5)
      .map((e) => {
        const r = e.getBoundingClientRect();
        return `${name(e)} [${Math.round(r.left)}..${Math.round(r.right)}]`;
      });
  };

  const anims = document.getAnimations();
  anims.forEach((a) => a.pause());
  const end = Math.max(0, ...anims.map((a) => a.effect?.getComputedTiming().endTime ?? 0));
  let during = { px: 0, t: 0, who: [] };
  for (let t = 0; t <= end + 25; t += 25) {
    anims.forEach((a) => { a.currentTime = t; });
    const o = overflow();
    if (o > during.px) during = { px: o, t, who: offenders() };
  }
  anims.forEach((a) => { try { a.finish(); } catch { a.cancel(); } });
  return { animations: anims.length, during, after: { px: overflow(), who: offenders() } };
}

const browser = await chromium.launch();
let failures = 0;
for (const root of roots) {
  const probe = await browser.newPage();
  await probe.goto(root + '/', { waitUntil: 'domcontentloaded' });
  const first = async (sel) => probe.evaluate((s) => document.querySelector(s)?.getAttribute('href') ?? null, sel);
  const listing = await first('a[href^="/araclar/"]');
  await probe.goto(`${root}/musaitlik?bas=${day(1)}&bit=${day(4)}`, { waitUntil: 'domcontentloaded' });
  const request = await first('a[href^="/rezervasyon-talebi"]');
  await probe.goto(root + '/blog', { waitUntil: 'domcontentloaded' });
  const post = await first('a[href^="/blog/"]');
  await probe.close();

  const paths = ['/', '/musaitlik', `/musaitlik?bas=${day(1)}&bit=${day(4)}`, listing, request, '/iletisim', '/sss',
    '/blog', post, '/olmayan-bir-sayfa'].filter(Boolean);
  for (const width of widths) {
    const ctx = await browser.newContext({ viewport: { width, height: 844 } });
    // Çerez şeridi kapalı başlasın (şerit sabit konumlu; taşmaya etkisi yok ama ölçümü sadeleştirir).
    await ctx.addInitScript(() => { try { localStorage.setItem('rc-cerez-bildirim-v1', 'tamam'); } catch { /* yok */ } });
    for (const path of paths) {
      const page = await ctx.newPage();
      await page.goto(root + path, { waitUntil: 'domcontentloaded' });
      const r = await page.evaluate(measure);
      const bad = r.during.px > 0 || r.after.px > 0 || r.after.who.length > 0;
      if (bad) failures++;
      console.log(`${bad ? 'TAŞMA' : 'tamam'} ${width} ${root}${path} animasyon=${r.animations} sırasında=${r.during.px}px` +
        `${r.during.px ? `@${r.during.t}ms` : ''} sonrasında=${r.after.px}px kırpılan=${r.after.who.length}`);
      for (const w of r.during.px ? r.during.who : []) console.log(`    sırasında: ${w}`);
      for (const w of r.after.who) console.log(`    sonrasında: ${w}`);
      await page.close();
    }
    await ctx.close();
  }
}
await browser.close();
console.log(failures ? `\n${failures} sayfada taşma.` : '\nTaşma yok.');
process.exit(failures ? 1 : 0);
