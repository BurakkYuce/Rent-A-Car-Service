#!/usr/bin/env node
// src/i18n/tr.json → src/app/core/i18n/ceviri-anahtarlari.ts (tipli anahtar birliği).
// Revlo scripts/i18n-generate-types.mjs'ten uyarlandı; kaynak dil tr.
// Kullanım: node scripts/i18n-tipleri.mjs            (yazar)
//           node scripts/i18n-tipleri.mjs --kontrol  (bayatsa çıkış 1; npm run lint koşar)
import { readFileSync, writeFileSync } from 'node:fs';
import { relative, resolve } from 'node:path';
import { format, resolveConfig } from 'prettier';

const KOK = resolve(import.meta.dirname, '..');
const KAYNAK = resolve(KOK, 'src/i18n/tr.json');
const HEDEF = resolve(KOK, 'src/app/core/i18n/ceviri-anahtarlari.ts');

function duzlestir(nesne, onek = '') {
  const anahtarlar = [];
  for (const [ad, deger] of Object.entries(nesne)) {
    const yol = onek ? `${onek}.${ad}` : ad;
    if (deger !== null && typeof deger === 'object' && !Array.isArray(deger)) {
      anahtarlar.push(...duzlestir(deger, yol));
    } else if (typeof deger === 'string') {
      anahtarlar.push(yol);
    } else {
      throw new Error(`tr.json: '${yol}' metin değil`);
    }
  }
  return anahtarlar;
}

const anahtarlar = duzlestir(JSON.parse(readFileSync(KAYNAK, 'utf8'))).sort();
const govde = `/** OTOMATİK ÜRETİLDİ: scripts/i18n-tipleri.mjs (kaynak src/i18n/tr.json). Elle düzenlemeyin. */
export type CeviriAnahtari =
${anahtarlar.map((a) => `  | '${a}'`).join('\n')};
`;
const icerik = await format(govde, { ...(await resolveConfig(HEDEF)), filepath: HEDEF });

if (process.argv.includes('--kontrol')) {
  let mevcut = '';
  try {
    mevcut = readFileSync(HEDEF, 'utf8');
  } catch {
    // dosya yok → bayat
  }
  if (mevcut !== icerik) {
    console.error(`${relative(KOK, HEDEF)} bayat. 'npm run i18n:tipler' çalıştırın.`);
    process.exit(1);
  }
  console.log(`i18n tipleri güncel (${anahtarlar.length} anahtar).`);
} else {
  writeFileSync(HEDEF, icerik);
  console.log(`${anahtarlar.length} anahtar → ${relative(KOK, HEDEF)}`);
}
