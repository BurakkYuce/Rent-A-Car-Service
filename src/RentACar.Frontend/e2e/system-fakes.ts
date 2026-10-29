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
  firmaIl: 'İstanbul',
  firmaIlce: 'Şişli',
  eFaturaKullanici: null,
  eFaturaSifreTanimli: false,
  eFaturaEntegrator: null,
  eFaturaGondericiEtiket: null,
  eFaturaPostaKutusuEtiket: null,
  eFaturaCanliOrtam: false,
  eFaturaSenaryo: null,
  eFaturaBagli: false,
  eFaturaDurumMesaji: 'e-Fatura gönderilmedi: entegratör bağlı değil (entegratör seçilmedi).',
  eFaturaEntegratorSecenekleri: [],
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
  siteVurguRengi: '#0b5d6b',
  siteTemasi: null,
  sitePaleti: null,
  sitePaletSecenekleri: [
    {
      tema: 'anahtar',
      paletler: [
        {
          anahtar: 'zumrut',
          ad: 'Zümrüt ve kehribar',
          zemin: '#fbf8f3',
          murekkep: '#1e2329',
          ana: '#0f6e5a',
          vurgu: '#f2b33d',
          geceZemin: '#111614',
        },
      ],
    },
    {
      tema: 'pazar',
      paletler: [
        {
          anahtar: 'petrol',
          ad: 'Petrol ve yol sarısı',
          zemin: '#f5f6f3',
          murekkep: '#1d2227',
          ana: '#0b5d6b',
          vurgu: '#f2c230',
          geceZemin: '#121417',
        },
        {
          anahtar: 'lacivert',
          ad: 'Lacivert ve amber',
          zemin: '#f4f6f9',
          murekkep: '#17202e',
          ana: '#1f4e9c',
          vurgu: '#f0a020',
          geceZemin: '#10151d',
        },
      ],
    },
    {
      tema: 'vitrin',
      paletler: [
        {
          anahtar: 'grafit',
          ad: 'Porselen ve grafit',
          zemin: '#f7f7f5',
          murekkep: '#2a3036',
          ana: '#0b5d6b',
          vurgu: null,
          geceZemin: '#14171a',
        },
        {
          anahtar: 'bordo',
          ad: 'Bordo',
          zemin: '#f7f6f5',
          murekkep: '#2a2526',
          ana: '#7b1e2e',
          vurgu: null,
          geceZemin: '#161213',
        },
      ],
    },
    {
      tema: 'rota',
      paletler: [
        {
          anahtar: 'deniz',
          ad: 'Deniz',
          zemin: '#f1f5f6',
          murekkep: '#0e2a33',
          ana: '#0b5e6b',
          vurgu: '#e2b02a',
          geceZemin: '#0b171b',
        },
      ],
    },
  ],
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
  eFaturaSeriKodu: null,
  eArsivSeriKodu: null,
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
    // Oturumdaki kullanıcının kendisi (BEN.kullanici): kendi satırında parola sıfırlama yok (M1).
    id: BEN.kullanici.id,
    kullaniciAdi: 'ayse',
    gorunenAd: 'Ayşe Yılmaz',
    rol: 'Yonetici',
    aktif: true,
    atanmisSube: null,
    istisnalar: [],
    etkinIzinler: ['ManageUsers'],
  },
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
