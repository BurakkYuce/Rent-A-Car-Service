import type { Page, Route } from '@playwright/test';

import { kaydet } from './ortak';
import type { DefinitionWrite } from './definition-fakes';

/** F11.2c tanım ekranlarının sahte uçları. */
export const VAT_1 = '55555555-0000-4000-8000-00000000f001';
export const SOURCE_1 = '55555555-0000-4000-8000-00000000f101';
export const SOURCE_2 = '55555555-0000-4000-8000-00000000f102';
export const GROUP_1 = '55555555-0000-4000-8000-00000000f201';
export const TEMPLATE_1 = '55555555-0000-4000-8000-00000000f301';
export const INSURER_1 = '55555555-0000-4000-8000-00000000f401';

type Handler = (route: Route, write: DefinitionWrite) => Promise<void> | void;

/**
 * F11.1b sayfalı tanım ucu sahtesi: `GET kok` → `Sayfa` (satırda `surum: null`), `GET kok/{id}` tekil (sürümlü),
 * yazımlar `write` ile (varsayılan 200/201/204). `pages` sayfa isteklerini (sorgu dizesiyle) kaydeder.
 */
export async function pagedEndpoints(
  page: Page,
  root: string,
  opts: { rows: () => Record<string, unknown>[]; one?: () => unknown; write?: Handler },
): Promise<{ writes: DefinitionWrite[]; pages: string[] }> {
  const writes: DefinitionWrite[] = [];
  const pages: string[] = [];
  await page.route(
    (u) => u.pathname === `/api/ui/v1/${root}` || u.pathname.startsWith(`/api/ui/v1/${root}/`),
    async (r) => {
      const req = r.request();
      const url = new URL(req.url());
      if (req.method() === 'GET') {
        if (url.pathname === `/api/ui/v1/${root}`) {
          pages.push(url.search);
          const rows = opts.rows().map((x) => ({ ...x, surum: null }));
          return r.fulfill({
            json: { kayitlar: rows, toplam: rows.length, sayfaNo: 1, boyut: 200 },
          });
        }
        return r.fulfill({ json: opts.one?.() ?? opts.rows()[0] });
      }
      const w = { ...kaydet(req), method: req.method(), path: url.pathname };
      writes.push(w);
      if (opts.write) return opts.write(r, w);
      if (req.method() === 'DELETE') return r.fulfill({ status: 204 });
      return r.fulfill({ status: req.method() === 'POST' ? 201 : 200, json: opts.rows()[0] ?? {} });
    },
  );
  return { writes, pages };
}

export const vatRate = (extra: Record<string, unknown> = {}) => ({
  id: VAT_1,
  kod: 'KDV20',
  ad: 'Genel %20',
  oran: 0.2,
  aktif: true,
  surum: 'k-1',
  ...extra,
});

export const insurer = (extra: Record<string, unknown> = {}) => ({
  id: INSURER_1,
  kod: 'ANDK',
  ad: 'Anadolu Sigorta',
  telefon: '0850 000 00 00',
  aktif: true,
  surum: 'i-1',
  ...extra,
});

export const template = (extra: Record<string, unknown> = {}) => ({
  id: TEMPLATE_1,
  belgeTuru: 'KiraSozlesmesi',
  ad: 'Kurumsal',
  varsayilanMi: true,
  aktif: true,
  belgeBasligi: 'KİRA SÖZLEŞMESİ',
  hukukiMetinSol: 'Sol metin <b>kalın değil</b>',
  hukukiMetinSag: null,
  ekKosullarVarsayilan: null,
  altBilgi: '{FirmaMarka} — {BelgeNo}',
  imzaAlaniGoster: true,
  surum: 't-1',
  ...extra,
});

const sourceBase = {
  tedarikci: null,
  kiraOrani: null,
  hizmetOrani: null,
  dropOrani: null,
  kaynakGrubu: null,
  uzatamaz: false,
  rezTarihleriDegisemez: false,
  provizyonYok: false,
  kmSinirsiz: false,
  ayniYonDrop: false,
  maxGun: null,
  maliyetYansitma: false,
  matrisErken: false,
  matrisGecikme: false,
  matrisIptal: false,
  matrisNoShow: false,
  matrisUzatma: false,
  sigortaKaynakNo: null,
  dropKaynakNo: null,
  provizyonSecenek: null,
  muafiyatSecenek: null,
  scdwDahil: false,
  cdwDahil: false,
  lcfDahil: false,
  paiDahil: false,
  bebekKoltugu: null,
  navigasyon: null,
  ekSurucu: null,
  wifi: null,
  komisyonOrani: null,
  onOdemeOrani: null,
  indirimOrani: null,
  puanOrani: null,
  mailAdres: null,
  otomatikMailGitme: false,
  riskAnalizYapma: false,
  subeGor: false,
  acenteFiyatDegistir: false,
  gizle: false,
  sadeceMusteriOdeme: false,
  aktif: true,
};

export const source = (
  id: string,
  code: string,
  name: string,
  extra: Record<string, unknown> = {},
) => ({
  id,
  kod: code,
  ad: name,
  ...sourceBase,
  surum: `r-${code}`,
  ...extra,
});

export const group = (extra: Record<string, unknown> = {}) => ({
  id: GROUP_1,
  kod: 'EKO',
  ad: 'Ekonomik',
  aciklama: null,
  sipp: 'CDMR',
  segment: 'C',
  kasaTuru: null,
  marka: null,
  tipi: null,
  koltukSayisi: 5,
  kapiSayisi: 4,
  bagajSayisi: null,
  kucukBagaj: null,
  buyukBagaj: null,
  surucuMinYas: 21,
  gencSurucuYas: null,
  gencSurucuUcretGunluk: null,
  ekSurucuUcretGunluk: null,
  ehliyetMinYil: 2,
  gencEhliyetMinYil: null,
  provizyon: 5000,
  provizyon2: null,
  muafiyetTutari: null,
  muafiyet2: null,
  gunlukKmLimiti: 300,
  aylikMaxKm: null,
  asimKmUcreti: null,
  yakitFiyati: null,
  sonraOdeOran: null,
  krediKartiSart: null,
  webSira: null,
  upgradeSira: null,
  provizyonDoviz: 'TRY',
  provizyon2Doviz: null,
  yakitTuru: 'Dizel',
  vites: null,
  entegrasyonKod1: null,
  webId: null,
  servisId: null,
  aktif: true,
  aracSayisi: 4,
  surum: 'g-1',
  ...extra,
});

/** Araç grupları: sayfalı CRUD + eşleşmeyen değerler + ata. */
export async function vehicleGroupEndpoints(page: Page) {
  // Genel uç ÖNCE: Playwright'ta sonra kaydedilen rota önceliklidir (eslesmeyen/ata genel ucu ezmeli).
  const crud = await pagedEndpoints(page, 'arac-gruplari', { rows: () => [group()] });
  const assigns: DefinitionWrite[] = [];
  await page.route('**/api/ui/v1/arac-gruplari/eslesmeyen', (r) =>
    r.fulfill({
      json: [
        { grup: 'Ekonomi', aracSayisi: 3, bos: false },
        { grup: '', aracSayisi: 2, bos: true },
      ],
    }),
  );
  await page.route('**/api/ui/v1/arac-gruplari/ata', (r) => {
    assigns.push({ ...kaydet(r.request()), method: 'POST', path: '/ata' });
    return r.fulfill({ json: { tasinan: 2 } });
  });
  return { assigns, ...crud };
}
