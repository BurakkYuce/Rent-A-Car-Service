#!/usr/bin/env node
/**
 * Mobil YATAY TAŞMA koşumu — "mobil uyumlu" iddiasını ölçüye bağlar.
 *
 *   node scripts/mobil-tasma.mjs                     # ERP (varsayılan) 390px
 *   node scripts/mobil-tasma.mjs --site              # halka açık site
 *   node scripts/mobil-tasma.mjs --genislik 320      # başka genişlik
 *   node scripts/mobil-tasma.mjs --json              # makine okunur çıktı
 *   node scripts/mobil-tasma.mjs --spa               # yeni arayüz (/app) sayfalarını da ölç (SPA kurulu olmalı)
 *
 * Neden var: yatay kaydırma mobilde en görünür ve en sinsi kusur — masaüstünde HİÇ belli olmaz.
 * Guard olmadan her faz kendi kendini onaylar. Koşum taşan sayfa bulursa çıkış kodu 1 döner.
 *
 * Ölçüm: `documentElement.scrollWidth > clientWidth`. Suçlu, SAĞ KENARI viewport'u aşan ilk
 * elemandır — genişliği aşan değil: 250px'lik bir eleman da yanlış konumdaysa taşma yaratır
 * (kabuktaki 221px'lik sabit taşma tam olarak böyleydi, genişlik ölçen bir kontrol onu bulamazdı).
 */
import { chromium } from 'playwright';

const args = process.argv.slice(2);
const bayrak = (ad) => args.includes(ad);
const deger = (ad, varsayilan) => {
  const i = args.indexOf(ad);
  return i >= 0 && args[i + 1] ? args[i + 1] : varsayilan;
};

const SITE = bayrak('--site');
const GENISLIK = Number(deger('--genislik', '390'));
const YUKSEKLIK = Number(deger('--yukseklik', '844'));
const KOK = deger('--kok', SITE ? 'http://localhost:5230' : 'http://localhost:5220');
const JSON_CIKTI = bayrak('--json');
const SPA = bayrak('--spa');

/**
 * Yeni arayüz (F4 pilot sayfaları). Yalnız `--spa` ile ve SPA sunucuda kuruluyken (`Spa:Dizin`) anlamlı; pilot
 * OLMAYAN firmada da çizilir (veri yerine "pilot değil" bandı — kabuk/düzen yine ölçülür). Pilot firmada ERP
 * yollarındaki `/`, `/kiralar`, `/kiralar/yeni` zaten buraya yönlenir (F4.6 haritası).
 */
const SPA_YOLLARI = ['/app/panel', '/app/kiralar', '/app/kiralar/yeni'];

/** ERP: her klasörden en yoğun kullanılan temsilciler + taşması bilinen üç sayfa. */
const ERP_YOLLARI = [
  '/', '/vehicles', '/kiralar', '/kiralar/yeni', '/rezervasyonlar',
  '/cariler', '/musaitlik', '/faturalar', '/kasa', '/giderler', '/servisler',
  '/cezalar', '/vade', '/personel', '/subeler', '/lokasyonlar', '/arac-gruplari',
  '/raporlar/fatura-donem', '/raporlar/karlilik', '/raporlar/gelir-gider', '/raporlar/filo',
  '/ayarlar', '/dokumanlar', '/blog-yonetim', '/web-sitesi', '/gelen-talepler', '/bildirimler',
];

/** Halka açık site: tüm rotalar (11 sayfa, hepsi ucuz). */
const SITE_YOLLARI = [
  '/', '/musaitlik', '/blog', '/iletisim', '/sss', '/rezervasyon-talebi', '/talep-alindi',
];

/** `--tum`: temsilci liste yerine scripts/mobil-yollar.txt'teki TÜM rotalar.
 *  Temsilci liste hızlı geri bildirim (geliştirme döngüsü) içindir; `--tum` kapsama içindir. */
let yollar = SITE ? SITE_YOLLARI : ERP_YOLLARI;
if (bayrak('--tum') && !SITE) {
  const { readFileSync } = await import('node:fs');
  const { fileURLToPath } = await import('node:url');
  const { dirname, join } = await import('node:path');
  const dosya = join(dirname(fileURLToPath(import.meta.url)), 'mobil-yollar.txt');
  yollar = readFileSync(dosya, 'utf8').split('\n')
    .map((x) => x.trim())
    .filter((x) => x && !x.startsWith('#'));
}
if (SPA && !SITE) yollar = [...yollar, ...SPA_YOLLARI];

/** ERP giriş gerektirir; seed kimliği (CLAUDE.md §7). Parola ORTAMDAN — repoda sabit parola yok:
 *  RACAR_GIRIS_SIFRE (CI her koşuda rastgele üretip uygulamaya Seed__Parola olarak da verir).
 *  Firma/kullanıcı varsayılanı seed: RACAR_GIRIS_FIRMA=yucerent, RACAR_GIRIS_KULLANICI=umit.
 *
 *  F4.6 TEK GİRİŞ: `/login` artık form çizmez, yeni arayüzün giriş sayfasına (`/app/giris`) yönlenir. Koşum
 *  o sayfanın formunun çağırdığı AYNI uçla girer (`GET /api/ui/v1/oturum/xsrf` → `POST …/oturum/giris`,
 *  aynı `racar.session` çerezi) — SPA bu sunucuda kurulu olmasa da (CI mobil-tasma işi SPA derlemez) çalışır.
 *  Başarısız giriş gürültülü hatadır: yoksa her sayfa girişe düşer ve koşum YALAN yeşil verir. */
async function girisYap(page) {
  const sifre = process.env.RACAR_GIRIS_SIFRE;
  if (!sifre) {
    throw new Error('RACAR_GIRIS_SIFRE yok — ERP giriş parolasını ortamdan ver (seed: Seed:Parola ya da açılış logu).');
  }
  const baglam = page.context();
  const xsrf = await baglam.request.get(`${KOK}/api/ui/v1/oturum/xsrf`);
  if (xsrf.status() !== 204) throw new Error(`XSRF belirteci alınamadı (HTTP ${xsrf.status()}).`);
  const belirtec = (await baglam.cookies(KOK)).find((c) => c.name === 'XSRF-TOKEN');
  if (!belirtec) throw new Error('XSRF-TOKEN çerezi yazılmadı.');
  const yanit = await baglam.request.post(`${KOK}/api/ui/v1/oturum/giris`, {
    headers: { 'X-XSRF-TOKEN': decodeURIComponent(belirtec.value) },
    data: {
      firma: process.env.RACAR_GIRIS_FIRMA || 'yucerent',
      kullanici: process.env.RACAR_GIRIS_KULLANICI || 'umit',
      sifre,
    },
  });
  if (!yanit.ok()) {
    throw new Error(`Giriş başarısız (HTTP ${yanit.status()}) — koşum anlamsız olurdu (her sayfa girişe döner).`);
  }
  const ben = await yanit.json();
  if (!JSON_CIKTI) {
    console.log(`Giriş: ${ben?.kiraci?.kod ?? '?'} / ${ben?.kullanici?.kullaniciAdi ?? '?'} — ${ben?.pilot ? 'PİLOT (/, /kiralar, /kiralar/yeni yeni arayüze yönlenir)' : 'pilot değil (Blazor)'}`);
  }
  return ben;
}

/** Oturum düşmüş ya da giriş sayfasına yönlenmiş mi (eski Blazor girişi ya da tek giriş /app/giris). */
const giriseDustu = (url) => { const p = new URL(url).pathname; return p === '/login' || p.startsWith('/app/giris'); };

/** Sayfadaki taşmayı, suçluyu ve tablo satır sayısını ölçer. */
async function olc(page, W) {
  return page.evaluate((W) => {
    const de = document.documentElement;
    const tasma = de.scrollWidth - de.clientWidth;
    // Veri satırı sayısı: BOŞ bir listenin taşmaması başarı değil, ölçüm yokluğudur.
    // Boş sayfalar sayılır ve özette gösterilir ki yeşil bir koşum "kapsandı" sanılmasın.
    //
    // `tbody tr` YETMEZ: boş liste de tek satır basar ("Kayıt yok." — tüm tabloyu kaplayan
    // colspan'li tek hücre). Bu satır sayılırsa boş sayfa DOLU görünür ve uyarı hiç ateşlemez.
    // Veri satırı = colspan'li hücresi OLMAYAN satır.
    const satir = [...document.querySelectorAll('table tbody tr')]
      .filter((tr) => !tr.querySelector('td[colspan]')).length;
    const tabloVar = document.querySelector('table') !== null;
    if (tasma <= 0) return { tasma: 0, suclular: [], satir, tabloVar };

    const kimlik = (e) => {
      const sinif = typeof e.className === 'string' && e.className.trim()
        ? '.' + e.className.trim().split(/\s+/).slice(0, 2).join('.')
        : '';
      return e.tagName.toLowerCase() + sinif;
    };

    // Sağ kenarı viewport'u aşan elemanlar; en dıştakini bulmak için en sağdakinden başla.
    const suclular = [...document.querySelectorAll('body *')]
      .map((e) => ({ e, sag: Math.round(e.getBoundingClientRect().right) }))
      .filter((x) => x.sag > W + 1)
      .sort((a, b) => b.sag - a.sag)
      .slice(0, 3)
      .map((x) => `${kimlik(x.e)} (sağ kenar ${x.sag}px)`);

    return { tasma, suclular, satir, tabloVar };
  }, W);
}

const ESZAMAN = Number(deger('--eszaman', '4'));

const tarayici = await chromium.launch();
const baglamKur = (oturum) => tarayici.newContext({
  storageState: oturum,
  viewport: { width: GENISLIK, height: YUKSEKLIK },
  // Gerçek telefon davranışı: dokunma + cihaz piksel oranı. Bazı taşmalar yalnız burada görünür.
  hasTouch: true,
  deviceScaleFactor: 2,
  isMobile: true,
});

// Giriş TEK KEZ yapılır ve oturum çerezi işçilere kopyalanır.
// Her işçinin ayrı giriş yapması `/login` HIZ SINIRINA takılıyordu (6 eşzamanlı giriş → bir işçi
// reddediliyor ve koşum patlıyordu). Ayrıca gereksiz: ölçülen şey oturum değil, düzen.
let oturum;
if (!SITE) {
  const ilk = await baglamKur();
  const ilkSayfa = await ilk.newPage();
  await girisYap(ilkSayfa);
  oturum = await ilk.storageState();
  await ilk.close();
}

const sonuclar = [];
try {
  // Her işçi KENDİ bağlamında çalışır: oturum çerezi bağlam düzeyinde tutulduğu için tek bağlamı
  // paylaşan sekmeler aynı anda gezinirken birbirinin gezinmesini iptal ettiriyordu.
  const kuyruk = [...yollar];
  const isci = async () => {
    const baglam = await baglamKur(oturum);
    const page = await baglam.newPage();
    try {
      for (;;) {
        const yol = kuyruk.shift();
        if (yol === undefined) break;
        try {
          const yanit = await page.goto(KOK + yol, { waitUntil: 'domcontentloaded', timeout: 20000 });
          const kod = yanit?.status() ?? 0;
          // 4xx/5xx sayfaları ölçüme girmez: olmayan bir sayfanın taşmaması başarı değildir.
          if (kod >= 400) { sonuclar.push({ yol, kod, atlandi: true }); continue; }
          if (giriseDustu(page.url())) { sonuclar.push({ yol, kod, atlandi: true, not: 'girişe düştü' }); continue; }
          const { tasma, suclular, satir, tabloVar } = await olc(page, GENISLIK);
          sonuclar.push({ yol, kod, tasma, suclular, satir, tabloVar });
        } catch (e) {
          sonuclar.push({ yol, hata: String(e.message || e).split('\n')[0] });
        }
      }
    } finally {
      await baglam.close();
    }
  };
  await Promise.all(Array.from({ length: Math.min(ESZAMAN, yollar.length) }, isci));
  // Çıktı sırası kuyruktan bağımsız olsun (paralel bitiş sırası kararsızdır).
  sonuclar.sort((a, b) => yollar.indexOf(a.yol) - yollar.indexOf(b.yol));
} finally {
  await tarayici.close();
}

const tasanlar = sonuclar.filter((r) => (r.tasma ?? 0) > 0);
const atlananlar = sonuclar.filter((r) => r.atlandi || r.hata);

if (JSON_CIKTI) {
  console.log(JSON.stringify({ genislik: GENISLIK, kok: KOK, sonuclar }, null, 2));
} else {
  const yesil = '\x1b[1;32m', kirmizi = '\x1b[1;31m', sari = '\x1b[1;33m', sifir = '\x1b[0m';
  console.log(`\nMobil taşma koşumu — ${KOK} @ ${GENISLIK}px\n`);
  for (const r of sonuclar) {
    if (r.hata) { console.log(`  ${sari}?${sifir} ${r.yol.padEnd(30)} hata: ${r.hata}`); continue; }
    if (r.atlandi) { console.log(`  ${sari}–${sifir} ${r.yol.padEnd(30)} atlandı (${r.not ?? 'HTTP ' + r.kod})`); continue; }
    if (r.tasma > 0) {
      console.log(`  ${kirmizi}✗${sifir} ${r.yol.padEnd(30)} +${r.tasma}px`);
      for (const s of r.suclular) console.log(`      ${s}`);
    } else {
      console.log(`  ${yesil}✓${sifir} ${r.yol.padEnd(30)} taşma yok`);
    }
  }
  const olculen = sonuclar.length - atlananlar.length;
  // Tablosu olup TEK satır göstermeyen sayfalar: ölçüm bu sayfalar için zayıftır (kart görünümü
  // yalnız veri varken taşabilir). Başarısızlık DEĞİL, kapsam uyarısıdır.
  const bosListe = sonuclar.filter((r) => r.tabloVar && r.satir === 0);
  console.log(`\n${olculen - tasanlar.length} temiz · ${tasanlar.length} taşıyor · ${atlananlar.length} atlandı`);
  if (bosListe.length) {
    console.log(`${sari}!${sifir} ${bosListe.length} sayfada tablo var ama satır YOK — o sayfalarda kart görünümü ölçülmedi.`);
    console.log(`  (${bosListe.slice(0, 6).map((r) => r.yol).join(', ')}${bosListe.length > 6 ? ', …' : ''})`);
  }
  console.log('');
}

process.exit(tasanlar.length > 0 ? 1 : 0);
