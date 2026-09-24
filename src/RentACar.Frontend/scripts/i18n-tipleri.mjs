#!/usr/bin/env node
// Çeviri sözlüğü: çekirdek `src/i18n/tr.json` (ilk pakete gömülü) + özellik blokları
// `src/i18n/bloklar/<blok>.json` (rota parçasıyla tembel yüklenir; `ceviriBlogu(...)`).
// Üretir:
//   src/app/core/i18n/ceviri-anahtarlari.ts  tipli anahtar birliği (TÜM dosyalar; kaynak dil tr)
//   src/app/core/i18n/ceviri-bloklari.ts     blok → dinamik import kaydı (her blok ayrı parça)
//   src/test-saglayicilari.ts                birim testleri: tüm bloklar önyüklü (angular.json providersFile)
// Üst düzey anahtarın hangi blokta olduğu TEK yerde: aşağıdaki BLOK_HARITASI. Haritadaki bir anahtar
// tr.json'da bulunursa (ör. eski dal merge edildi) yazma kipi onu blok dosyasına TAŞIR (tr.json'daki sürüm
// kazanır, üst düzey anahtar bütün olarak değişir); kontrol kipi hata verir.
// Revlo scripts/i18n-generate-types.mjs'ten uyarlandı.
// Kullanım: node scripts/i18n-tipleri.mjs            (yazar / taşır)
//           node scripts/i18n-tipleri.mjs --kontrol  (bayat ya da taşınmamışsa çıkış 1; npm run lint koşar)
import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { relative, resolve } from 'node:path';
import { format, resolveConfig } from 'prettier';

/**
 * Üst düzey anahtar → özellik bloğu (dosya adı `src/i18n/bloklar/<blok>.json`, `ceviriBlogu('<blok>')`).
 * Haritada OLMAYAN üst düzey anahtar çekirdektir (`tr.json`, ilk paket): yalnız kabuk / core / shared ya da
 * birden çok özelliğin ortak kullandığı metin. Yeni faz: kendi bloğunu buraya ekler, anahtarlarını blok
 * dosyasına yazar, rotasına `ceviriBlogu('<blok>')` koyar (AGENTS.md "i18n").
 */
const BLOK_HARITASI = {
  kiraFormu: 'kira-formu',
  kiraFormuParite: 'kira-formu',
  kiraFinans: 'kira-formu',
  panel: 'panel',
  kiraListesi: 'kiralar',
  // F5.2a rezervasyonlar + teklifler (tek blok: teklif kabulü rezervasyona bağlanır).
  rezervasyon: 'rezervasyon',
  teklif: 'rezervasyon',
  vitrin: 'vitrin',
  // F5.2b planlama ekranları (takvim, müsaitlik, rez şartları, filo kiralama).
  takvimSayfasi: 'planlama',
  musaitlikSayfasi: 'planlama',
  rezSartlari: 'planlama',
  filoKiralama: 'planlama',
  // F6.2a araç ekranları (liste, detaylı liste, kart + foto, detay, durum panosu, tanımlar).
  arac: 'arac',
  // F12.2 platform konsolu (giriş, özet, firmalar, firma detayı, belge merkezi) — firma kabuğundan ayrı.
  platform: 'platform',
};

const KOK = resolve(import.meta.dirname, '..');
const CEKIRDEK = resolve(KOK, 'src/i18n/tr.json');
const BLOK_DIZINI = resolve(KOK, 'src/i18n/bloklar');
const ANAHTAR_HEDEF = resolve(KOK, 'src/app/core/i18n/ceviri-anahtarlari.ts');
const BLOK_HEDEF = resolve(KOK, 'src/app/core/i18n/ceviri-bloklari.ts');
const TEST_HEDEF = resolve(KOK, 'src/test-saglayicilari.ts');
const kontrol = process.argv.includes('--kontrol');

const hatalar = [];
const okuJson = (yol) => JSON.parse(readFileSync(yol, 'utf8'));
const blokDosyasi = (blok) => resolve(BLOK_DIZINI, `${blok}.json`);
const bloklar = [...new Set(Object.values(BLOK_HARITASI))].sort();

function duzlestir(nesne, dosya, onek = '') {
  const anahtarlar = [];
  for (const [ad, deger] of Object.entries(nesne)) {
    const yol = onek ? `${onek}.${ad}` : ad;
    if (deger !== null && typeof deger === 'object' && !Array.isArray(deger)) {
      anahtarlar.push(...duzlestir(deger, dosya, yol));
    } else if (typeof deger === 'string') {
      anahtarlar.push(yol);
    } else {
      throw new Error(`${dosya}: '${yol}' metin değil`);
    }
  }
  return anahtarlar;
}

// --- Oku ve doğrula ---
const cekirdek = okuJson(CEKIRDEK);
const blokIcerik = new Map(
  bloklar.map((b) => [b, existsSync(blokDosyasi(b)) ? okuJson(blokDosyasi(b)) : {}]),
);

if (existsSync(BLOK_DIZINI)) {
  for (const dosya of readdirSync(BLOK_DIZINI)) {
    if (!dosya.endsWith('.json') || !blokIcerik.has(dosya.replace(/\.json$/, ''))) {
      hatalar.push(`src/i18n/bloklar/${dosya}: BLOK_HARITASI'nda böyle bir blok yok.`);
    }
  }
}
for (const [blok, icerik] of blokIcerik) {
  for (const ust of Object.keys(icerik)) {
    if (BLOK_HARITASI[ust] !== blok) {
      hatalar.push(
        `src/i18n/bloklar/${blok}.json: '${ust}' bu bloğa ait değil (BLOK_HARITASI: ${BLOK_HARITASI[ust] ?? 'çekirdek tr.json'}).`,
      );
    }
  }
}

// --- Taşı: haritadaki üst düzey anahtar tr.json'da kalmış olabilir (eski dal merge'ü) ---
const tasinacak = Object.keys(cekirdek).filter((ust) => ust in BLOK_HARITASI);
if (tasinacak.length > 0) {
  if (kontrol) {
    hatalar.push(
      `src/i18n/tr.json özellik bloğu anahtarı içeriyor (${tasinacak.join(', ')}). 'npm run i18n:tipler' taşır.`,
    );
  } else {
    for (const ust of tasinacak) {
      blokIcerik.get(BLOK_HARITASI[ust])[ust] = cekirdek[ust];
      delete cekirdek[ust];
    }
  }
}

if (hatalar.length > 0) {
  console.error(hatalar.join('\n'));
  process.exit(1);
}

// Blok dosyasında üst düzey sıra BLOK_HARITASI sırası (merge geçmişinden bağımsız, deterministik).
const siraliBlok = (blok) => {
  const icerik = blokIcerik.get(blok);
  const sonuc = {};
  for (const [ust, b] of Object.entries(BLOK_HARITASI)) {
    if (b === blok && ust in icerik) sonuc[ust] = icerik[ust];
  }
  return sonuc;
};
const doluBloklar = bloklar.filter((b) => Object.keys(blokIcerik.get(b)).length > 0);

const anahtarlar = [
  ...duzlestir(cekirdek, 'tr.json'),
  ...doluBloklar.flatMap((b) => duzlestir(blokIcerik.get(b), `bloklar/${b}.json`)),
].sort();

// --- Üret ---
const bicimle = async (metin, hedef, parser) =>
  format(metin, {
    ...(await resolveConfig(hedef)),
    filepath: hedef,
    ...(parser ? { parser } : {}),
  });
const tsAdi = (ad) => ad.replace(/-(\w)/g, (_, h) => h.toUpperCase());

const ciktilar = new Map();
ciktilar.set(
  ANAHTAR_HEDEF,
  await bicimle(
    `/** OTOMATİK ÜRETİLDİ: scripts/i18n-tipleri.mjs (kaynak src/i18n/tr.json). Elle düzenlemeyin. */
export type CeviriAnahtari =
${anahtarlar.map((a) => `  | '${a}'`).join('\n')};
`,
    ANAHTAR_HEDEF,
  ),
);
ciktilar.set(
  BLOK_HEDEF,
  await bicimle(
    `/** OTOMATİK ÜRETİLDİ: scripts/i18n-tipleri.mjs (BLOK_HARITASI + src/i18n/bloklar/*.json). Elle düzenlemeyin. */
import type { Translation } from '@jsverse/transloco';

/** Özellik çeviri blokları: her biri ayrı tembel parça; rota \`ceviriBlogu('<blok>')\` ile yükler. */
export const CEVIRI_BLOKLARI = {
${doluBloklar
  .map(
    (b) =>
      `  '${b}': (): Promise<Translation> => import('../../../i18n/bloklar/${b}.json').then((m) => m.default),`,
  )
  .join('\n')}
} as const;

export type CeviriBlogu = keyof typeof CEVIRI_BLOKLARI;
`,
    BLOK_HEDEF,
  ),
);
ciktilar.set(
  TEST_HEDEF,
  await bicimle(
    `/** OTOMATİK ÜRETİLDİ: scripts/i18n-tipleri.mjs. Elle düzenlemeyin. */
import type { Provider } from '@angular/core';
import { ONYUKLU_CEVIRI_BLOKLARI } from './app/core/i18n/onyuklu-ceviri';
${doluBloklar.map((b) => `import ${tsAdi(b)} from './i18n/bloklar/${b}.json';`).join('\n')}

/**
 * Birim testleri (angular.json \`test.providersFile\`): bileşen/servis testleri rotadan geçmediği için tüm
 * özellik çeviri blokları çekirdekle birlikte eşzamanlı yüklenir. Uygulama bu dosyayı İÇE AKTARMAZ; üretimde
 * bloklar rotada \`ceviriBlogu(...)\` ile tembel gelir (yükleme davranışı \`ceviri-blogu.spec.ts\`'te).
 */
const saglayicilar: Provider[] = [
  { provide: ONYUKLU_CEVIRI_BLOKLARI, useValue: [${doluBloklar.map(tsAdi).join(', ')}] },
];
export default saglayicilar;
`,
    TEST_HEDEF,
  ),
);
if (tasinacak.length > 0) {
  ciktilar.set(CEKIRDEK, await bicimle(JSON.stringify(cekirdek, null, 2), CEKIRDEK, 'json'));
}
for (const b of doluBloklar) {
  ciktilar.set(
    blokDosyasi(b),
    await bicimle(JSON.stringify(siraliBlok(b), null, 2), blokDosyasi(b), 'json'),
  );
}

const degisen = [...ciktilar]
  .filter(([hedef, icerik]) => !existsSync(hedef) || readFileSync(hedef, 'utf8') !== icerik)
  .map(([hedef]) => hedef);

if (kontrol) {
  if (degisen.length > 0) {
    const liste = degisen.map((h) => relative(KOK, h)).join(', ');
    console.error(`Bayat: ${liste}. 'npm run i18n:tipler' çalıştırın.`);
    process.exit(1);
  }
  console.log(`i18n tipleri güncel (${anahtarlar.length} anahtar, ${doluBloklar.length} blok).`);
} else {
  mkdirSync(BLOK_DIZINI, { recursive: true });
  for (const hedef of degisen) writeFileSync(hedef, ciktilar.get(hedef));
  console.log(
    `${anahtarlar.length} anahtar, ${doluBloklar.length} blok; yazılan: ${degisen.map((h) => relative(KOK, h)).join(', ') || 'yok'}` +
      (tasinacak.length > 0 ? ` (tr.json → blok taşındı: ${tasinacak.join(', ')})` : ''),
  );
}
