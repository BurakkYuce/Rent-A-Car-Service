import type { Page, Route } from '@playwright/test';

import { kaydet, problem, type KayitliIstek } from './ortak';

/** F11.2a tanım ekranlarının sahte `/api/ui/v1` uçları (yanıt biçimi `DefinitionEndpoints` sözleşmesi). */
export const BRAND_1 = '11111111-0000-4000-8000-00000000b001';
export const BRANCH_1 = '22222222-0000-4000-8000-00000000c001';
export const BRANCH_2 = '22222222-0000-4000-8000-00000000c002';

export const brand = (extra: Record<string, unknown> = {}) => ({
  id: BRAND_1,
  kod: 'FIAT',
  ad: 'Fiat',
  aktif: true,
  surum: 'b-1',
  ...extra,
});

export interface DefinitionWrite extends KayitliIstek {
  readonly method: string;
  readonly path: string;
}

type Handler = (route: Route, write: DefinitionWrite) => Promise<void> | void;

/**
 * Genel tanım ucu sahtesi: `GET kok` düz dizi, `GET kok/{id}` tekil, yazımlar `write` ile (varsayılan 200/201/204).
 */
export async function definitionEndpoints(
  page: Page,
  root: string,
  opts: { rows: () => unknown[]; one?: () => unknown; write?: Handler },
): Promise<DefinitionWrite[]> {
  const writes: DefinitionWrite[] = [];
  await page.route(
    (u) => u.pathname === `/api/ui/v1/${root}` || u.pathname.startsWith(`/api/ui/v1/${root}/`),
    async (r) => {
      const req = r.request();
      const path = new URL(req.url()).pathname;
      if (req.method() === 'GET') {
        if (path === `/api/ui/v1/${root}`) return r.fulfill({ json: opts.rows() });
        return r.fulfill({ json: opts.one?.() ?? opts.rows()[0] });
      }
      const w = { ...kaydet(req), method: req.method(), path };
      writes.push(w);
      if (opts.write) return opts.write(r, w);
      if (req.method() === 'DELETE') return r.fulfill({ status: 204 });
      return r.fulfill({ status: req.method() === 'POST' ? 201 : 200, json: opts.rows()[0] ?? {} });
    },
  );
  return writes;
}

export const branch = (
  id: string,
  kod: string,
  ad: string,
  extra: Record<string, unknown> = {},
) => ({
  id,
  kod,
  ad,
  adres: null,
  telefon: '0212 000 00 00',
  eposta: null,
  il: 'İstanbul',
  ilce: 'Kadıköy',
  yetkili: 'Ali',
  calismaSaatleri: null,
  komisyonOran: 0.1,
  evrakNoOnek: null,
  webIsim: null,
  firmaUnvani: null,
  webRezOncesiSaat: null,
  enlem: null,
  boylam: null,
  hizmetKomisyonOran: null,
  rezervasyonRengi: null,
  alisSubesiDegilMi: false,
  webSira: null,
  webOtoparkId: null,
  bayiCariKod: null,
  bayiOfisId: null,
  komisyonHesabi: null,
  onlineRezId: null,
  sozlesmeNoFormati: null,
  nakitHesapId: '33333333-0000-4000-8000-00000000d001',
  bankaHesapId: null,
  entegrasyonKodu: null,
  resimDosyasi: null,
  haftalikCalismaSaatleri: null,
  aktif: true,
  surum: `s-${kod}`,
  ...extra,
});

/** Şube sayfası: liste + hizmetler + birleştirme önizleme/onay. */
export async function branchEndpoints(page: Page) {
  const rows = [branch(BRANCH_1, 'MRK', 'Merkez'), branch(BRANCH_2, 'KDK', 'Kadıköy')];
  const merges: DefinitionWrite[] = [];
  const writes: DefinitionWrite[] = [];
  await page.route('**/api/ui/v1/subeler/birlestir/onizleme?*', (r) =>
    r.fulfill({
      json: {
        kaynakAd: 'Kadıköy',
        hedefAd: 'Merkez',
        etkilenen: [
          { tablo: 'Araçlar', adet: 3 },
          { tablo: 'Kiralar', adet: 5 },
        ],
        toplam: 8,
      },
    }),
  );
  await page.route('**/api/ui/v1/subeler/birlestir', (r) => {
    merges.push({ ...kaydet(r.request()), method: 'POST', path: '/birlestir' });
    return r.fulfill({ json: { tasinanKayit: 8 } });
  });
  await page.route(/\/api\/ui\/v1\/subeler\/[0-9a-f-]+\/hizmetler/, (r) =>
    r.request().method() === 'GET'
      ? r.fulfill({
          json: r.request().url().includes(BRANCH_1)
            ? [
                {
                  id: '44444444-0000-4000-8000-00000000e001',
                  hizmetAdi: 'Havalimanı teslim',
                  aciklama: null,
                },
              ]
            : [],
        })
      : r.fulfill({ status: 201, json: { id: 'x', hizmetAdi: 'x', aciklama: null } }),
  );
  await page.route(
    (u) =>
      u.pathname === '/api/ui/v1/subeler' ||
      /^\/api\/ui\/v1\/subeler\/[0-9a-f-]+$/.test(u.pathname),
    (r) => {
      const req = r.request();
      if (req.method() !== 'GET') {
        writes.push({ ...kaydet(req), method: req.method(), path: new URL(req.url()).pathname });
      }
      return r.fulfill({
        json: req.method() === 'GET' && req.url().endsWith('subeler') ? rows : rows[0],
      });
    },
  );
  return { merges, writes };
}

export { problem };
