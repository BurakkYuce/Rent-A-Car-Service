import { expect, test, type Page } from '@playwright/test';

import { seriousViolations, collectErrors, logIn, problem } from './ortak';
import {
  VEHICLE_1,
  AVAILABILITY_RESPONSE,
  sharedEndpoints,
  calendarResponse,
} from './planlama-sahte';
import { waitReady, measureOverflow, type VitrinSayfasi } from './vitrin-sayfalari';

/**
 * F5.2b takvim (`/app/takvim`) ve müsaitlik (`/app/musaitlik`), sahte `/api/ui/v1` ile: doluluk sunucudan
 * çizilir, süzgeçler URL'e yazılır (ay gezinmesi süzgeci korur), müsaitlik penceresi İSTANBUL saatiyle
 * gösterilir ve "Kirala" kira formuna sunucunun çözdüğü pencereyi taşır (`?varac&vfrom&vto&vgrup`).
 */
const NETWORK_ERROR = [/Failed to load resource: the server responded with a status of 4\d\d/];

async function calendarEndpoints(page: Page): Promise<URL[]> {
  const requests: URL[] = [];
  await page.route(
    (url) => url.pathname === '/api/ui/v1/takvim',
    (route) => {
      const url = new URL(route.request().url());
      requests.push(url);
      return route.fulfill({ json: calendarResponse(url.searchParams.get('ay') ?? '2026-10') });
    },
  );
  await page.route('**/api/ui/v1/takvim/secenekler', (route) =>
    route.fulfill({ json: { subeler: ['Merkez', 'Havalimanı'], gruplar: ['C', 'D'] } }),
  );
  return requests;
}

async function availabilityEndpoints(
  page: Page,
  response: (url: URL) => Promise<void> | void,
): Promise<void> {
  await page.route(
    (url) => url.pathname === '/api/ui/v1/musaitlik',
    async (route) => {
      await response(new URL(route.request().url()));
      return route.fulfill({ json: AVAILABILITY_RESPONSE });
    },
  );
  await page.route('**/api/ui/v1/musaitlik/secenekler', (route) =>
    route.fulfill({
      json: {
        subeler: ['Merkez'],
        gruplar: ['C'],
        kaynaklar: ['Web', 'Broker A'],
        dovizler: ['TRY'],
      },
    }),
  );
}

const CALENDAR: VitrinSayfasi = {
  ad: 'takvim',
  yol: '/app/takvim?ay=2026-10',
  baslik: 'Rezervasyon Takvimi',
  hazir: async (page) => {
    await expect(page.getByRole('link', { name: /34 ABC 123/ })).toBeVisible();
  },
};

const MUSAITLIK: VitrinSayfasi = {
  ad: 'musaitlik',
  yol: '/app/musaitlik?basGun=2026-10-01&gun=3&basSaat=09:00&grup=C',
  baslik: 'Müsait Araç Ara',
  hazir: async (page) => {
    await expect(page.getByRole('grid', { name: 'Müsait araçlar' })).not.toHaveAttribute(
      'aria-busy',
      'true',
    );
    await expect(page.getByRole('gridcell', { name: '34 ABC 123', exact: true })).toBeVisible();
  },
};

test.beforeEach(async ({ page }) => {
  await logIn(page);
  await sharedEndpoints(page);
});

test('takvim: doluluk sunucudan (K/R), plaka → kira formu ?varac=, ay gezinmesi süzgeci korur; axe iki tema', async ({
  page,
}) => {
  const errors = collectErrors(page);
  const requests = await calendarEndpoints(page);
  await page.goto(CALENDAR.yol);
  await waitReady(page, CALENDAR);

  await expect(page.getByText('Ekim 2026', { exact: true })).toBeVisible();
  await expect(page.getByTitle('34ABC123 — Kira')).toHaveCount(3);
  await expect(page.getByTitle('34ABC123 — Rezervasyon')).toHaveCount(1);
  await expect(page.getByText('Araç: 2')).toBeVisible();
  await expect(page.getByRole('link', { name: /34 ABC 123/ })).toHaveAttribute(
    'href',
    `/app/kiralar/yeni?varac=${VEHICLE_1}`,
  );
  expect(await seriousViolations(page), 'açık tema').toEqual([]);
  await page.emulateMedia({ colorScheme: 'dark' });
  expect(await seriousViolations(page), 'koyu tema').toEqual([]);

  await page.getByRole('combobox', { name: 'Şube / bölge' }).selectOption({ label: 'Merkez' });
  await page.getByRole('button', { name: 'Filtrele' }).click();
  await expect(page).toHaveURL(/ay=2026-10/);
  await expect(page).toHaveURL(/sube=Merkez/);
  await page.getByRole('button', { name: /Kasım 2026/ }).click();
  await expect(page).toHaveURL(/ay=2026-11/);
  await expect(page).toHaveURL(/sube=Merkez/);
  await expect.poll(() => requests.at(-1)?.searchParams.get('ay')).toBe('2026-11');
  expect(requests.at(-1)?.searchParams.get('sube')).toBe('Merkez');
  expect(requests.at(-1)?.searchParams.has('sayfa')).toBe(false);
  expect(errors).toEqual([]);
});

test('müsaitlik: pencere İstanbul saatiyle, Kirala çözülmüş pencereyi taşır, broker notu; axe', async ({
  page,
}) => {
  const errors = collectErrors(page);
  const requests: URL[] = [];
  await availabilityEndpoints(page, (u) => void requests.push(u));
  await page.goto('/app/musaitlik');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Müsait Araç Ara');
  await expect(page.getByText(/Aramak için başlangıç tarihi/)).toBeVisible();
  expect(requests).toHaveLength(0); // ilk açılışta arama yok (Blazor)

  await page.getByRole('textbox', { name: 'Başlangıç', exact: true }).fill('01.10.2026');
  await page.getByRole('textbox', { name: 'Gün', exact: true }).fill('3');
  await page.getByRole('textbox', { name: 'Alış saati' }).fill('9');
  await page.getByRole('combobox', { name: 'Grup', exact: true }).fill('C');
  await page.getByRole('button', { name: 'Ara', exact: true }).click();

  await expect.poll(() => requests.length).toBe(1);
  const q = requests[0]!.searchParams;
  expect(Object.fromEntries(q)).toEqual({
    basGun: '2026-10-01',
    gun: '3',
    basSaat: '09:00',
    grup: 'C',
  });
  await waitReady(page, MUSAITLIK);
  await expect(
    page.getByText('01.10.2026 09:00 – 04.10.2026 09:00 arası 1 müsait araç.'),
  ).toBeVisible();
  await expect(page.getByText(/1 araç, seçili kaynak/)).toBeVisible();
  await expect(page.getByRole('gridcell', { name: '1.500,00 EUR' })).toBeVisible();
  await expect(page.getByRole('gridcell', { name: '3.751,50 ₺' })).toBeVisible();
  await expect(page.getByRole('link', { name: /Kirala 34ABC123/ })).toHaveAttribute(
    'href',
    `/app/kiralar/yeni?varac=${VEHICLE_1}&vfrom=2026-10-01&vto=2026-10-04&vgrup=C`,
  );
  expect(await seriousViolations(page)).toEqual([]);
  expect(errors).toEqual([]);
});

test('müsaitlik: bitiş ve gün yoksa sunucu 400 mesajı gösterilir, form yerinde', async ({
  page,
}) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  await page.route(
    (url) => url.pathname === '/api/ui/v1/musaitlik',
    (route) =>
      problem(route, 400, 'dogrulama', 'Bitiş tarihi ya da gün sayısından birini girin.', {
        errors: { bitGun: ['Bitiş tarihi ya da gün sayısından birini girin.'] },
      }),
  );
  await page.goto('/app/musaitlik?basGun=2026-10-01&plaka=34');
  await expect(page.locator('.rc-form-mesaji--hata')).toContainText(
    'Bitiş tarihi ya da gün sayısından birini girin.',
  );
  await expect(page.getByRole('textbox', { name: 'Başlangıç', exact: true })).toHaveValue(
    '01.10.2026',
  );
  await expect(page.getByRole('textbox', { name: 'Plaka' })).toHaveValue('34');
  expect(errors).toEqual([]);
});

for (const pageRef of [CALENDAR, MUSAITLIK]) {
  test.describe(`${pageRef.ad}: mobil taşma (dokunmatik öykünme)`, () => {
    test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });
    test(`${pageRef.yol}: 320/390/768 px gövde yatay taşması yok`, async ({ page }) => {
      await calendarEndpoints(page);
      await availabilityEndpoints(page, () => undefined);
      for (const width of [320, 390, 768]) {
        await page.setViewportSize({ width: width, height: 844 });
        await page.goto(pageRef.yol);
        await waitReady(page, pageRef);
        expect(await measureOverflow(page), `${width}px`).toEqual({ tasma: 0, suclular: [] });
      }
    });
  });

  test(`${pageRef.yol}: 1440 px gövde yatay taşması yok`, async ({ page }) => {
    await calendarEndpoints(page);
    await availabilityEndpoints(page, () => undefined);
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(pageRef.yol);
    await waitReady(page, pageRef);
    expect(await measureOverflow(page)).toEqual({ tasma: 0, suclular: [] });
  });
}
