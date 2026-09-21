import AxeBuilder from '@axe-core/playwright';
import type { Page, Request, Route } from '@playwright/test';

/** Konsol hatalarını (CSP ihlali dahil) ve çalışma zamanı hatalarını toplar. */
export function hatalariTopla(page: Page, beklenen: RegExp[] = []): string[] {
  const hatalar: string[] = [];
  page.on('console', (mesaj) => {
    if (mesaj.type() !== 'error') return;
    const metin = mesaj.text();
    if (!beklenen.some((k) => k.test(metin))) hatalar.push(metin);
  });
  page.on('pageerror', (hata) => hatalar.push(hata.message));
  return hatalar;
}

export async function ciddiIhlaller(page: Page, kapsam?: string): Promise<string[]> {
  const axe = new AxeBuilder({ page });
  if (kapsam) axe.include(kapsam);
  const sonuc = await axe.analyze();
  return sonuc.violations
    .filter((ihlal) => ihlal.impact === 'serious' || ihlal.impact === 'critical')
    .map(
      (ihlal) =>
        `${ihlal.id}: ${ihlal.help} → ` +
        ihlal.nodes.map((n) => `${n.target.join(' ')} (${n.any[0]?.message ?? ''})`).join('; '),
    );
}

/**
 * Sahte `/api/ui/v1` (e2e harness'i yalnız statik SPA sunar; F2.2 e2e işi backend kaldırmaz).
 * Yanıtlar backend `UiHata` sözleşmesinin birebir biçimi: `application/problem+json` + `kod`.
 */
export const BEN = {
  kullanici: {
    id: '6f1c2c8e-0000-4000-8000-000000000001',
    kullaniciAdi: 'ayse',
    adSoyad: 'Ayşe Yılmaz',
  },
  kiraci: { id: '6f1c2c8e-0000-4000-8000-0000000000aa', kod: 'pilot', ad: 'Pilot Firma' },
  rol: 'Admin',
  izinler: ['OperationsWrite', 'FinanceWrite', 'ViewReports'],
  subeKapsami: { tumSubeler: true, subeId: null, subeAd: null },
  moduller: { webSitesi: false },
  renkler: {},
  pilot: true,
};

export function problem(
  route: Route,
  status: number,
  kod: string,
  detail: string,
  ek: object = {},
) {
  return route.fulfill({
    status,
    contentType: 'application/problem+json',
    body: JSON.stringify({ type: 'about:blank', title: 'Hata', status, detail, kod, ...ek }),
  });
}

/** XSRF çerezini sunucu gibi yazar (SPA `document.cookie`'den okur, başlığa koyar). */
export async function xsrfYaz(page: Page, deger: string): Promise<void> {
  const url = new URL(page.url() === 'about:blank' ? 'http://127.0.0.1' : page.url());
  await page
    .context()
    .addCookies([{ name: 'XSRF-TOKEN', value: deger, domain: url.hostname, path: '/' }]);
}

/**
 * Sahte `GET /api/ui/v1/menu` (sunucu izne göre süzmüş biçimde; Finans yok). Vitrin öğeleri `spa`,
 * diğerleri Blazor (tam sayfa). Etiketler sekme başlıklarından farklı: e2e seçicileri karışmasın.
 */
export const MENU = {
  ogeler: [
    {
      rota: '/rezervasyonlar',
      etiket: 'Yeni Rezervasyon',
      grup: 'Kısa Yollar',
      sira: 10,
      sahip: 'blazor',
      rozetKodu: null,
      hizliBaglanti: true,
    },
    {
      rota: '/',
      etiket: 'Panel',
      grup: '',
      sira: 20,
      sahip: 'blazor',
      rozetKodu: null,
      hizliBaglanti: false,
    },
    {
      rota: '/vitrin/geri-bildirim',
      etiket: 'Geri bildirim',
      grup: 'Vitrin',
      sira: 30,
      sahip: 'spa',
      rozetKodu: null,
      hizliBaglanti: false,
    },
    {
      rota: '/vitrin/form',
      etiket: 'Form seti',
      grup: 'Vitrin',
      sira: 40,
      sahip: 'spa',
      rozetKodu: null,
      hizliBaglanti: false,
    },
    {
      rota: '/vitrin/tanim',
      etiket: 'Tanım CRUD',
      grup: 'Vitrin',
      sira: 50,
      sahip: 'spa',
      rozetKodu: null,
      hizliBaglanti: false,
    },
    {
      rota: '/kiralar',
      etiket: 'Kiralar',
      grup: 'Kira',
      sira: 60,
      sahip: 'blazor',
      rozetKodu: null,
      hizliBaglanti: false,
    },
    {
      rota: '/is-emirleri',
      etiket: 'İş Emirleri',
      grup: 'Servis',
      sira: 70,
      sahip: 'blazor',
      rozetKodu: null,
      hizliBaglanti: false,
    },
    {
      rota: '/bildirimler',
      etiket: 'Bildirimler',
      grup: '',
      sira: 80,
      sahip: 'blazor',
      rozetKodu: 'okunmamis-bildirim',
      hizliBaglanti: false,
    },
  ],
  rozetler: { 'okunmamis-bildirim': 4 },
};

export async function menuyuSahtele(page: Page, menu: object = MENU): Promise<void> {
  await page.route('**/api/ui/v1/menu', (route) => route.fulfill({ json: menu }));
}

/** Oturum açık: `ben` 200 döner, XSRF çerezi yazılı, kabuk menüsü sahte. */
export async function oturumAc(page: Page, ben: object = BEN): Promise<void> {
  await xsrfYaz(page, 'eski-belirtec');
  await page.route('**/api/ui/v1/oturum/ben', (route) => route.fulfill({ json: ben }));
  await menuyuSahtele(page);
}

export interface KayitliIstek {
  readonly govde: string | null;
  readonly xsrf: string | undefined;
  readonly anahtar: string | undefined;
}

export function kaydet(istek: Request): KayitliIstek {
  const h = istek.headers();
  return { govde: istek.postData(), xsrf: h['x-xsrf-token'], anahtar: h['idempotency-key'] };
}
