import { expect, test } from '@playwright/test';

import { collectErrors, logIn, seriousViolations } from './ortak';
import { ADMIN_BEN } from './system-fakes';

/**
 * KABİS ekranları (sahte `/api/ui/v1`): ayarlar (şifre yalnız yazılır, PUT `surum` taşır, bağlantı sınaması sunucunun
 * dürüst sonucunu gösterir) ve bildirilemeyen kiralar listesi (satır kiraya bağlanır). Beklenen değerler sahte
 * yanıtlardan ELLE kurulur.
 */
const RENTAL_ID = '0b0e7c1a-1111-4aaa-8bbb-00000000ab01';

test.beforeEach(async ({ page }) => {
  await logIn(page, ADMIN_BEN);
});

test('KABİS ayarları: şifre boş ve password, kayıt surum + boş şifre taşır, sınama sonucu sunucudan; axe', async ({
  page,
}) => {
  const errors = collectErrors(page);
  const bodies: Record<string, unknown>[] = [];
  const current = {
    etkin: false,
    kurum: 'Emniyet',
    kullanici: 'firma01',
    sifreTanimli: true,
    teslimiEngelle: false,
    surum: 'v1',
  };
  await page.route(/\/api\/ui\/v1\/ayarlar\/kabis(\/baglanti-testi)?$/, async (route) => {
    const request = route.request();
    if (request.url().endsWith('/baglanti-testi')) {
      return route.fulfill({
        json: { ok: false, mesaj: 'KABİS bağlantısı yapılandırılmadı (bağlı değil).' },
      });
    }
    if (request.method() === 'PUT') {
      const body = JSON.parse(request.postData() ?? '{}') as Record<string, unknown>;
      bodies.push(body);
      return route.fulfill({ json: { ...current, etkin: body['etkin'], surum: 'v2' } });
    }
    return route.fulfill({ json: current });
  });

  await page.goto('/app/kabis-ayarlari');
  await expect(page.getByRole('heading', { name: 'KABİS Ayarları' })).toBeVisible();
  const password = page.getByLabel('KABİS şifresi', { exact: true });
  await expect(password).toHaveAttribute('type', 'password');
  await expect(password).toHaveValue('');
  await expect(password).toHaveAttribute('placeholder', /kayıtlı/);
  await expect(page.getByLabel('KABİS kullanıcı adı')).toHaveValue('firma01');
  expect(await seriousViolations(page)).toEqual([]);

  await page.getByText('Teslim ve dönüşte otomatik KABİS bildirimi yap').click();
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect.poll(() => bodies.length).toBe(1);
  expect(bodies[0]).toMatchObject({ etkin: true, kurum: 'Emniyet', sifre: null, surum: 'v1' });

  await page.getByRole('button', { name: 'Bağlantıyı sına' }).click();
  await expect(page.getByTestId('kabis-test-sonucu')).toHaveText(/bağlı değil/);
  expect(errors).toEqual([]);
});

test('bildirilemeyen kiralar: satır kiraya bağlanır, hata rozeti ve son hata görünür', async ({
  page,
}) => {
  const errors = collectErrors(page);
  await page.route(/\/api\/ui\/v1\/kabis\/bildirilemeyenler$/, (route) =>
    route.fulfill({
      json: [
        {
          kiraId: RENTAL_ID,
          sozlesmeNo: '2026150901001',
          plaka: '34 ABC 123',
          tur: 'Kiralama',
          durum: 'Hata',
          denemeSayisi: 8,
          sonHata: 'zaman aşımı',
          olayUtc: '2026-10-15T09:00:00Z',
          sonDenemeUtc: '2026-10-15T13:00:00Z',
        },
      ],
    }),
  );
  await page.goto('/app/kabis');
  const table = page.getByTestId('kabis-listesi');
  await expect(table.getByRole('link', { name: '2026150901001' })).toHaveAttribute(
    'href',
    `/app/kiralar/${RENTAL_ID}`,
  );
  await expect(table).toContainText('Kiralama (teslim)');
  await expect(table).toContainText('Hata');
  await expect(table).toContainText('zaman aşımı');
  expect(await seriousViolations(page)).toEqual([]);
  expect(errors).toEqual([]);
});
