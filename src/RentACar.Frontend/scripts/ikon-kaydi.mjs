#!/usr/bin/env node
// src/app/shared/ikon/ikon-listesi.json → src/app/shared/ikon/ikon-kaydi.ts
// Revlo scripts/generate-icon-registry.mjs'ten uyarlandı: Tabler (outline/filled) SVG'leri pakete
// gömülür, yalnız listelenenler (ağaç sallama yerine açık alt küme). SVG sadeleştirilir: yorum,
// sınıf, boyut, xmlns (HTML içinde gereksiz) ve Tabler'ın görünmez çerçeve yolu atılır, çizgi
// kalınlığı sabitlenir.
// Kullanım: node scripts/ikon-kaydi.mjs            (yazar)
//           node scripts/ikon-kaydi.mjs --kontrol  (bayatsa çıkış 1; npm run lint koşar)
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { relative, resolve } from 'node:path';
import { format, resolveConfig } from 'prettier';

const KOK = resolve(import.meta.dirname, '..');
const LISTE = resolve(KOK, 'src/app/shared/ikon/ikon-listesi.json');
const HEDEF = resolve(KOK, 'src/app/shared/ikon/ikon-kaydi.ts');
const TABLER = resolve(KOK, 'node_modules/@tabler/icons/icons');
const CIZGI = '1.75';

function sadelestir(svg) {
  return svg
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/<path stroke="none" d="M0 0h24v24H0z" fill="none"\s*\/>/g, '')
    .replace(/\s(class|width|height|xmlns)="[^"]*"/g, '')
    .replace(/stroke-width="[^"]*"/g, `stroke-width="${CIZGI}"`)
    .replace(/\s+/g, ' ')
    .replace(/>\s+</g, '><')
    .replace(/\s*\/>/g, '/>')
    .replace(/\s+>/g, '>')
    .trim();
}

const liste = JSON.parse(readFileSync(LISTE, 'utf8'));
const ikonlar = new Map();
const eksik = [];
for (const [tur, onek] of [
  ['outline', ''],
  ['filled', 'dolu-'],
]) {
  for (const ad of liste[tur] ?? []) {
    const dosya = resolve(TABLER, tur, `${ad}.svg`);
    if (!existsSync(dosya)) {
      eksik.push(`${tur}/${ad}`);
      continue;
    }
    ikonlar.set(`${onek}${ad}`, sadelestir(readFileSync(dosya, 'utf8')));
  }
}
if (eksik.length > 0) {
  console.error(`Tabler'da bulunamayan ikon: ${eksik.join(', ')}`);
  process.exit(1);
}

const satirlar = [...ikonlar.entries()]
  .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
  .map(([ad, svg]) => `  '${ad}': '${svg.replace(/\\/g, '\\\\').replace(/'/g, "\\'")}',`)
  .join('\n');
const govde = `/** OTOMATİK ÜRETİLDİ: scripts/ikon-kaydi.mjs (liste ikon-listesi.json, kaynak @tabler/icons). Elle düzenlemeyin. */
export const IKONLAR = {
${satirlar}
} as const;

export type IkonAdi = keyof typeof IKONLAR;
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
    console.error(`${relative(KOK, HEDEF)} bayat. 'npm run ikonlar' çalıştırın.`);
    process.exit(1);
  }
  console.log(`İkon kaydı güncel (${ikonlar.size} ikon).`);
} else {
  writeFileSync(HEDEF, icerik);
  console.log(`${ikonlar.size} ikon → ${relative(KOK, HEDEF)}`);
}
