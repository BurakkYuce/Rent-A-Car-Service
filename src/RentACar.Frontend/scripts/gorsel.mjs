#!/usr/bin/env node
// Görsel regresyon (F3.7) Docker'da: CI'ın `e2e` işiyle AYNI imaj (mcr.microsoft.com/playwright:
// v<@playwright/test sürümü>-noble) ve AYNI mimari (linux/amd64 — Apple Silicon'da öykünme) →
// tabanlar CI'la piksel piksel aynı üretilir. macOS'ta doğrudan koşum anlamsız (font/kenar yumuşatma farkı).
//
//   npm run e2e:gorsel             derle + karşılaştır
//   npm run e2e:gorsel:guncelle    derle + tabanları yeniden yaz (e2e/gorsel-tabanlari/) → gözle bak, commit'le
//
// Derleme ana makinede (çıktı platformdan bağımsız), test konteynerde: node_modules salt JS olarak
// paylaşılır (@playwright/test, axe; yerel ikili gerekmez), tarayıcı imajın kendisinden.
import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const KOK = resolve(import.meta.dirname, '..');
const guncelle = process.argv.includes('--guncelle');
const derlemeYok = process.argv.includes('--derleme-yok');

const surum = JSON.parse(
  readFileSync(resolve(KOK, 'node_modules/@playwright/test/package.json'), 'utf8'),
).version;
const imaj = `mcr.microsoft.com/playwright:v${surum}-noble`;

function kos(komut, argumanlar) {
  const sonuc = spawnSync(komut, argumanlar, { cwd: KOK, stdio: 'inherit' });
  if (sonuc.error) {
    console.error(`${komut} çalıştırılamadı: ${sonuc.error.message}`);
    process.exit(1);
  }
  if (sonuc.status !== 0) process.exit(sonuc.status ?? 1);
}

if (!derlemeYok) kos('npx', ['ng', 'build']);
console.log(
  `Görsel regresyon: ${imaj} (linux/amd64)${guncelle ? ' — tabanlar güncelleniyor' : ''}`,
);
kos('docker', [
  'run',
  '--rm',
  '--platform',
  'linux/amd64',
  '--ipc=host',
  '-e',
  'CI=',
  '-v',
  `${KOK}:/w`,
  '-w',
  '/w',
  imaj,
  'npx',
  'playwright',
  'test',
  '--project=gorsel',
  ...(guncelle ? ['--update-snapshots'] : []),
]);
