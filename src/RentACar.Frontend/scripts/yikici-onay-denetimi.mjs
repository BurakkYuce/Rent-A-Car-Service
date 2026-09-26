#!/usr/bin/env node
/**
 * Yıkıcı işlem onay denetimi (F13.1a; Blazor `YikiciFormOnayTests` çitinin yeni arayüz karşılığı).
 *
 * Kural: `/api/ui`'ye giden her YIKICI çağrı, bulunduğu sınıf üyesinde (metot / özellik / fonksiyon) kullanıcıya
 * onay sorar — `ConfirmService.ask({...})` ya da `MoneySubmission`'ın `confirm:` seçeneği. Geri alınamaz bir silme,
 * iptal, ters kayıt, iade ya da kapatma tek yanlış tıkla gitmesin (Blazor'da ölçüm: 87 yıkıcı hedefin 24'ü onaysızdı).
 *
 * Yıkıcı çağrı:
 *  - `api.delete(…)` / `http.delete(…)` (HTTP DELETE);
 *  - ifade içindeki (tip konumunda olmayan) bir metin/şablon literalinin SON yol parçası yıkıcı fiil taşıyorsa
 *    (`/iptal`, `/ters`, `/iade`, `/sil`, `/kapat`, `/kapatma`, `/irat`; tire ile ayrılmış tam sözcük:
 *    `dis-hizmet-iptal` yıkıcı, `iptal-sebepleri` değil — kaynak adı).
 *
 * Onay çağrıyı saran EN YAKIN sınıf üyesinde (ok fonksiyonları geçilir — callback'teki çağrının onayı onu çağıran
 * metottadır) ya da o üyenin çağırdığı bir `this.x(…)` yardımcısında (tek sıçrama) aranır. Onay başka yerde alınıyorsa
 * (şablonda iki adımlı onay) gerekçesiyle İSTİSNA listesine yazılır; listede olup artık ihlal üretmeyen satır da hata
 * verir (liste bayatlamasın).
 *
 * Kullanım: `node scripts/yikici-onay-denetimi.mjs` (lint zincirinde). `--listele` bulunan tüm çağrıları yazar.
 */
import { readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import ts from 'typescript';

const ROOT = new URL('..', import.meta.url).pathname;
const APP = join(ROOT, 'src/app');
const VERBS = new Set([
  'sil',
  'iptal',
  'ters',
  'iade',
  'kapat',
  'kapatma',
  'irat',
  'delete',
  'cancel',
  'reverse',
]);
const CONFIRM = /\.ask\(\s*\{|\bconfirm\s*:/;

/**
 * Gerekçeli istisnalar: `dosya:üye` → { neden, kanit? }. Onay başka yerde alınıyorsa ya da işlem gerçekten zararsızsa.
 * `kanit` verilirse o dosyada o metin DURMALI (onayı taşıyan yer kalkarsa istisna da düşer). Gerekçe ZORUNLU.
 */
const TANIM_CRUD = {
  neden:
    'Genel tanım CRUD kaynağının `sil` fonksiyonu; çağıran rc-tanim-crud iki adımlı satır içi onay kullanır ' +
    '(confirmDelete → onay satırı → remove).',
  kanit: ['src/app/shared/form/tanim-crud/definition-crud.ts', 'protected confirmDelete('],
};
const EXCEPTIONS = new Map([
  [
    'src/app/core/oturum/dugme-izinleri.ts:DUGME_IZINLERI',
    { neden: 'Düğme izin haritasının belge metni (uç adı); HTTP çağrısı değil.' },
  ],
  ['src/app/shared/form/tanim-crud/definition-source.ts:restDefinitionSource', TANIM_CRUD],
  [
    'src/app/features/vehicles/vehicle-definitions/definition-source.ts:definitionSource',
    TANIM_CRUD,
  ],
  [
    'src/app/shared/tablo/table-layout-store.ts:reset',
    {
      neden:
        'Kullanıcının KENDİ tablo düzenini (sütun/sıralama tercihi) varsayılana döndürür; iş verisi değil, ' +
        'yeniden ayarlanabilir.',
    },
  ],
  [
    'src/app/features/kira-formu/rental-form-state.ts:closePreAuth',
    {
      neden:
        'Manuel provizyon kapama: kullanıcının tutar/iade seçimini elle doldurduğu form gönderimi (tek tık değil); ' +
        'deftere yazmaz (ProvizyonDurum yaşam döngüsü). Blazor çitinde de "kapat" fiili yıkıcı sayılmıyordu.',
    },
  ],
]);

function files(dir) {
  const out = [];
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    const p = join(dir, e.name);
    if (e.isDirectory()) {
      if (e.name === 'uretilen') continue;
      out.push(...files(p));
    } else if (e.name.endsWith('.ts') && !e.name.endsWith('.spec.ts')) out.push(p);
  }
  return out;
}

/** Literalin yol parçaları; son parça (sorgu/fragment atılmış) yıkıcı fiil taşıyor mu. */
export function destructiveLiteral(text) {
  if (!text.includes('/')) return false;
  const path = text.split(/[?#]/)[0].replace(/\/+$/, '');
  const last = path.slice(path.lastIndexOf('/') + 1);
  return last.split(/[-_]/).some((w) => VERBS.has(w.toLowerCase()));
}

function inTypePosition(node) {
  for (let p = node.parent; p; p = p.parent) {
    if (ts.isTypeNode(p)) return true;
    if (ts.isExpression(p) || ts.isStatement(p)) return false;
  }
  return false;
}

function member(node) {
  for (let p = node.parent; p; p = p.parent) {
    if (
      ts.isMethodDeclaration(p) ||
      ts.isPropertyDeclaration(p) ||
      ts.isFunctionDeclaration(p) ||
      ts.isConstructorDeclaration(p) ||
      ts.isGetAccessorDeclaration(p)
    )
      return p;
    if (ts.isVariableStatement(p) && ts.isSourceFile(p.parent)) return p;
  }
  return node.getSourceFile();
}

function memberName(m) {
  if (ts.isSourceFile(m)) return '(dosya)';
  if (ts.isVariableStatement(m))
    return m.declarationList.declarations[0]?.name.getText() ?? '(değişken)';
  return m.name?.getText() ?? '(adsız)';
}

/** Sınıf üyelerinin adı → metni (bir sıçramalık yardımcı çözümü: `this.ask(…)`, `this.transition(…)`). */
function members(sf) {
  const map = new Map();
  const visit = (node) => {
    if ((ts.isMethodDeclaration(node) || ts.isPropertyDeclaration(node)) && node.name)
      map.set(node.name.getText(), node.getText());
    ts.forEachChild(node, visit);
  };
  visit(sf);
  return map;
}

/** Üyenin kendisi ya da (bir sıçrama) çağırdığı `this.x(…)` yardımcısı onay soruyor mu. */
function confirmed(m, byName) {
  const text = m.getText();
  if (CONFIRM.test(text)) return true;
  for (const call of text.matchAll(/this\.(\w+)\(/g)) {
    const helper = byName.get(call[1]);
    if (helper && CONFIRM.test(helper)) return true;
  }
  return false;
}

export function scan(file, text) {
  const sf = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true);
  const byName = members(sf);
  const found = [];
  const visit = (node) => {
    let hit = null;
    if (
      ts.isCallExpression(node) &&
      ts.isPropertyAccessExpression(node.expression) &&
      node.expression.name.text === 'delete' &&
      /\b(api|http)$/.test(node.expression.expression.getText().replace(/\s+/g, ''))
    )
      hit = 'DELETE';
    else if (
      (ts.isStringLiteral(node) ||
        ts.isNoSubstitutionTemplateLiteral(node) ||
        ts.isTemplateTail(node)) &&
      !inTypePosition(node) &&
      !ts.isImportDeclaration(node.parent) &&
      destructiveLiteral(node.text)
    )
      hit = node.text;
    if (hit) {
      const m = member(node);
      const line = sf.getLineAndCharacterOfPosition(node.getStart()).line + 1;
      found.push({ uye: memberName(m), satir: line, cagri: hit, onayli: confirmed(m, byName) });
    }
    ts.forEachChild(node, visit);
  };
  visit(sf);
  return found;
}

const all = files(APP).flatMap((f) => {
  const rel = relative(ROOT, f);
  return scan(rel, readFileSync(f, 'utf8')).map((x) => ({ ...x, dosya: rel }));
});

if (process.argv.includes('--listele')) {
  for (const x of all)
    console.log(`${x.onayli ? 'onaylı ' : 'ONAYSIZ'} ${x.dosya}:${x.satir} ${x.uye} ${x.cagri}`);
}

const errors = [];
const used = new Set();
for (const x of all.filter((y) => !y.onayli)) {
  const key = `${x.dosya}:${x.uye}`;
  if (EXCEPTIONS.has(key)) {
    used.add(key);
    continue;
  }
  errors.push(`${x.dosya}:${x.satir} (${x.uye}) → ${x.cagri}`);
}
for (const [key, e] of EXCEPTIONS) {
  if (!e.neden?.trim()) errors.push(`İstisna gerekçesiz: ${key}`);
  if (!used.has(key)) errors.push(`Bayat istisna (artık onaysız yıkıcı çağrı yok): ${key}`);
  if (e.kanit && !readFileSync(join(ROOT, e.kanit[0]), 'utf8').includes(e.kanit[1]))
    errors.push(`İstisnanın kanıtı kayboldu: ${key} → ${e.kanit[0]} içinde "${e.kanit[1]}" yok`);
}
// Tarama çalışıyor mu: bugün ölçülen alt sınır (ölçerek güncelleyin; taramanın kendi sayımından türetmeyin).
const MIN = 40; // 2026-09-26 ölçümü: 43 çağrı
if (all.length < MIN)
  errors.push(`Tarama şüpheli: yalnız ${all.length} yıkıcı çağrı bulundu (beklenen ≥ ${MIN}).`);

if (errors.length) {
  console.error(
    'Yıkıcı işlem onaysız (ConfirmService.ask({…}) ya da MoneySubmission confirm: aynı üyede olmalı):\n  ' +
      errors.join('\n  '),
  );
  process.exit(1);
}
console.log(
  `Yıkıcı işlem onay denetimi: ${all.length} çağrı, hepsi onaylı ya da gerekçeli istisna.`,
);
