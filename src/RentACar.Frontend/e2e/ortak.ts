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

/** Oturum açık: `ben` 200 döner, XSRF çerezi yazılı. */
export async function oturumAc(page: Page, ben: object = BEN): Promise<void> {
  await xsrfYaz(page, 'eski-belirtec');
  await page.route('**/api/ui/v1/oturum/ben', (route) => route.fulfill({ json: ben }));
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
