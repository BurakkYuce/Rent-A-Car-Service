import type { Page, Route } from '@playwright/test';

import { BEN, kaydet, type KayitliIstek } from './ortak';

/** F11.2b sistem/web ekranlarının sahte `/api/ui/v1` uçları (yanıt biçimleri OpenAPI DTO'larıyla birebir). */
export const ADMIN_BEN = {
  ...BEN,
  izinler: [...BEN.izinler, 'ManageUsers'],
  moduller: { webSitesi: true },
};

export interface Write extends KayitliIstek {
  readonly method: string;
  readonly path: string;
}

export function record(route: Route): Write {
  const req = route.request();
  return { ...kaydet(req), method: req.method(), path: new URL(req.url()).pathname };
}

export const settings = (extra: Record<string, unknown> = {}) => ({
  firmaUnvan: 'Örnek Otomotiv A.Ş.',
  firmaVergiDairesi: null,
  firmaVergiNo: null,
  firmaAdres: null,
  firmaTel: '0212 000 00 00',
  firmaEmail: 'info@ornek.test',
  firmaMobilTel: null,
  firmaMarka: 'Örnek',
  eFaturaKullanici: null,
  eFaturaSifreTanimli: false,
  smsBaslik: null,
  smsApiKeyTanimli: false,
  posMerchantId: null,
  posApiKeyTanimli: false,
  logoUrl: null,
  logoVar: false,
  varsayilanDoviz: 'TRY',
  varsayilanKdvOrani: 0.2,
  varsayilanGrupId: null,
  varsayilanFiyatTuru: null,
  varsayilanYakitSeviyesi: null,
  dropMesafeYokIseSifir: null,
  saatFarkiToleransDk: null,
  iadeIslemSaatSiniri: null,
  kurElleGirisKilitli: false,
  renkGecikenler: null,
  renkBugunDonecekler: null,
  renkBugunCikacaklar: null,
  renkOpsiyonlu: null,
  renkLimitBakiye: null,
  renkAlacakli: null,
  renkRezAtananPlaka: null,
  renkKiralanmayan: null,
  donemselFaturalamaJob: false,
  donemselOtomatikTahsilat: false,
  minKiraGun: null,
  maxKiraGun: null,
  rezOnayZorunlu: null,
  smtpHost: 'mail.ornek.test',
  smtpPort: 587,
  smtpKullanici: 'rez@ornek.test',
  smtpSifreTanimli: true,
  smtpSsl: true,
  smtpGonderenAdres: null,
  smtpGonderenAd: null,
  faturaSeriKodu: 'RNT',
  whatsAppNumarasi: null,
  whatsAppGunlukOzet: false,
  webSitesiAcik: true,
  webSitesiAdresi: 'ornek.site.test',
  domainler: [
    {
      host: 'kirala.ornek.test',
      tur: 'Custom',
      durum: 'PendingVerification',
      dogrulamaKaydi: '_racar-verify.kirala.ornek.test',
      dogrulamaDegeri: 'racar-0123456789abcdef',
    },
  ],
  yeniArayuzPilot: true,
  twilioTanimli: false,
  whatsAppGonderimleri: [],
  surum: 'v1',
  ...extra,
});

type Handler = (route: Route, w: Write) => Promise<void> | void;

/** `/ayarlar` GET + PUT (+ alt uçlar). `put` verilmezse PUT gövdeyi kaydeder ve güncel ayarı döner. */
export async function settingsEndpoints(
  page: Page,
  opts: { current?: () => unknown; put?: Handler; post?: Handler } = {},
): Promise<Write[]> {
  const writes: Write[] = [];
  await page.route('**/api/ui/v1/arac-gruplari?*', (r) =>
    r.fulfill({ json: { kayitlar: [], toplam: 0, sayfaNo: 1, boyut: 200 } }),
  );
  await page.route(
    (u) => u.pathname.startsWith('/api/ui/v1/ayarlar'),
    async (r) => {
      const req = r.request();
      if (req.method() === 'GET') return r.fulfill({ json: opts.current?.() ?? settings() });
      const w = record(r);
      writes.push(w);
      if (req.method() === 'PUT' && opts.put) return opts.put(r, w);
      if (req.method() === 'POST' && opts.post) return opts.post(r, w);
      return r.fulfill({ json: opts.current?.() ?? settings() });
    },
  );
  return writes;
}

export const USER_ADMIN = '6f1c2c8e-0000-4000-8000-0000000000a1';
export const USER_OP = '6f1c2c8e-0000-4000-8000-0000000000b2';

export const users = () => [
  {
    id: USER_ADMIN,
    kullaniciAdi: 'patron',
    gorunenAd: 'Patron',
    rol: 'Admin',
    aktif: true,
    atanmisSube: null,
    istisnalar: [],
    etkinIzinler: ['ManageUsers'],
  },
  {
    id: USER_OP,
    kullaniciAdi: 'operator1',
    gorunenAd: 'Operatör Bir',
    rol: 'Operator',
    aktif: true,
    atanmisSube: 'Merkez',
    istisnalar: [
      { izin: 'FinanceWrite', ver: true, tanimlayan: 'patron', tarihUtc: '2026-09-01T09:00:00Z' },
    ],
    etkinIzinler: ['OperationsWrite', 'FinanceWrite'],
  },
];

export async function usersEndpoints(page: Page): Promise<Write[]> {
  const writes: Write[] = [];
  await page.route('**/api/ui/v1/subeler', (r) =>
    r.fulfill({
      json: [
        { id: 'b1', ad: 'Merkez', aktif: true },
        { id: 'b2', ad: 'Eski Şube', aktif: false },
      ],
    }),
  );
  await page.route(
    (u) => u.pathname.startsWith('/api/ui/v1/kullanicilar'),
    (r) => {
      if (r.request().method() === 'GET') return r.fulfill({ json: users() });
      writes.push(record(r));
      return r.request().method() === 'DELETE'
        ? r.fulfill({ status: 204 })
        : r.fulfill({ status: 200, json: users()[1] });
    },
  );
  return writes;
}
