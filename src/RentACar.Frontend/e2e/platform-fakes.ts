import type { Page, Route } from '@playwright/test';

import { problem, writeXsrf } from './ortak';

/**
 * Stateful fake of `/api/ui/v1/platform/*` (F12.2 e2e). Shapes follow the OpenAPI contract
 * (`PlatformTenantDetailDto`, `PlatformConsoleDocumentDto`…); status/close rules mirror the server
 * (`PlatformApi.ChangeStatus`: →Kapali needs `onayKod` == code, otherwise 400 with `errors.onayKod`).
 */
export const TENANT_A = '7a000000-0000-4000-8000-00000000000a';
export const TENANT_B = '7a000000-0000-4000-8000-00000000000b';
export const DOC_1 = '7d000000-0000-4000-8000-000000000001';
export const OPERATOR = 'admin';
export const OPERATOR_SECRET = 'e2e-sahte-deger';

type Detail = Record<string, unknown> & { id: string; kod: string; ad: string; durum: string };

export function tenantDetail(
  id: string,
  code: string,
  name: string,
  extra: Partial<Detail> = {},
): Detail {
  return {
    id,
    kod: code,
    ad: name,
    durum: 'Aktif',
    kapanisTarihi: null,
    olusturma: '2026-01-10T09:00:00Z',
    guncelleme: null,
    surum: '638900000000000001',
    yetkiliAd: 'Ayşe Yılmaz',
    eposta: 'info@ornek.test',
    telefon: '0212 000 00 00',
    notlar: null,
    plan: 'Standart',
    kullaniciSayisi: 4,
    aracSayisi: 12,
    aktifKira: 3,
    toplamKira: 57,
    sonGiris: '2026-09-20T08:30:00Z',
    gelir30Gun: 125400.5,
    webSitesiModulu: false,
    yeniArayuzPilot: false,
    halkaAcikSite: true,
    domainler: [{ host: `${code}.ornek.test`, tur: 'Alt alan', durum: 'Aktif' }],
    logo: {
      var: false,
      bayt: null,
      genislik: null,
      yukseklik: null,
      baskiyaUygun: false,
      uyari: null,
    },
    ...extra,
  };
}

function row(d: Detail) {
  return {
    id: d.id,
    kod: d.kod,
    ad: d.ad,
    durum: d.durum,
    kapanisTarihi: d['kapanisTarihi'],
    olusturma: d['olusturma'],
    kullaniciSayisi: d['kullaniciSayisi'],
    aracSayisi: d['aracSayisi'],
    aktifKira: d['aktifKira'],
    sonGiris: d['sonGiris'],
  };
}

export interface FakePlatform {
  signedIn: boolean;
  readonly tenants: Detail[];
  readonly docs: Record<string, unknown>[];
  /** `METHOD /path` of every platform request. */
  readonly calls: string[];
  /** JSON bodies of writes, in order. */
  readonly bodies: unknown[];
}

export async function fakePlatformApi(
  page: Page,
  { signedIn = false }: { signedIn?: boolean } = {},
): Promise<FakePlatform> {
  const state: FakePlatform = {
    signedIn,
    tenants: [
      tenantDetail(TENANT_A, 'yucerent', 'Yüce Rent A Car'),
      tenantDetail(TENANT_B, 'demo', 'Demo Firma', { durum: 'Pasif', aktifKira: 0 }),
    ],
    docs: [
      {
        id: DOC_1,
        baslik: 'KVKK Aydınlatma Metni',
        aciklama: 'Personel için',
        dosyaAdi: 'kvkk.pdf',
        boyut: 20480,
        surum: 1,
        durum: 'Taslak',
        yalnizYoneticiler: false,
        guncelleme: '2026-09-01T10:00:00Z',
        yukleyenOperator: OPERATOR,
        hedefKodlar: [],
      },
    ],
    calls: [],
    bodies: [],
  };
  await writeXsrf(page, 'e2e-belirtec');
  await page.route('**/api/ui/v1/oturum/xsrf', (r) => r.fulfill({ status: 204 }));
  await page.route('**/api/ui/v1/platform/**', (route) => handle(route, state));
  return state;
}

const find = (s: FakePlatform, id: string) => s.tenants.find((t) => t.id === id);

async function handle(route: Route, s: FakePlatform): Promise<void> {
  const req = route.request();
  const method = req.method();
  const path = new URL(req.url()).pathname.replace('/api/ui/v1/platform', '');
  s.calls.push(`${method} ${path}`);
  const json = (): Record<string, unknown> => {
    try {
      return (req.postDataJSON() as Record<string, unknown>) ?? {};
    } catch {
      return {};
    }
  };
  if (method !== 'GET' && !req.headers()['content-type']?.startsWith('multipart/'))
    s.bodies.push(json());

  if (path === '/oturum/giris') {
    const b = json();
    if (b['kullanici'] !== OPERATOR || b['sifre'] !== OPERATOR_SECRET)
      return problem(route, 400, 'dogrulama', 'Kullanıcı adı veya şifre hatalı.');
    s.signedIn = true;
    return route.fulfill({ json: { kullanici: OPERATOR } });
  }
  if (path === '/oturum/cikis') {
    s.signedIn = false;
    return route.fulfill({ status: 204 });
  }
  if (!s.signedIn) return problem(route, 401, 'oturum_yok', 'Oturum yok.');
  if (path === '/oturum/ben') return route.fulfill({ json: { kullanici: OPERATOR } });

  if (path === '/ozet') {
    return route.fulfill({
      json: {
        toplamKiraci: s.tenants.length,
        aktif: s.tenants.filter((t) => t.durum === 'Aktif').length,
        pasif: s.tenants.filter((t) => t.durum === 'Pasif').length,
        kapali: s.tenants.filter((t) => t.durum === 'Kapali').length,
        toplamKullanici: 8,
        toplamArac: 24,
        aktifKira: 3,
        son30GunYeniKiraci: 1,
      },
    });
  }
  if (path === '/kiracilar' && method === 'GET') {
    const rows = s.tenants.map(row);
    return route.fulfill({ json: { kayitlar: rows, toplam: rows.length, sayfaNo: 1, boyut: 200 } });
  }
  if (path === '/kiracilar/secim') {
    return route.fulfill({
      json: s.tenants.map((t) => ({ id: t.id, kod: t.kod, ad: t.ad, durum: t.durum })),
    });
  }
  if (path === '/kiracilar' && method === 'POST') {
    const b = json();
    if (s.tenants.some((t) => t.kod === b['kod']))
      return problem(route, 409, 'cakisma', `'${String(b['kod'])}' kodlu firma zaten var.`, {
        errors: { kod: [`'${String(b['kod'])}' kodlu firma zaten var.`] },
      });
    const d = tenantDetail(
      '7a000000-0000-4000-8000-0000000000cc',
      String(b['kod']),
      String(b['ad']),
      {
        kullaniciSayisi: 1,
        aracSayisi: 0,
        aktifKira: 0,
        toplamKira: 0,
        sonGiris: null,
        gelir30Gun: 0,
        halkaAcikSite: false,
        domainler: [],
        plan: null,
      },
    );
    s.tenants.push(d);
    return route.fulfill({ status: 201, json: d });
  }
  if (path === '/belgeler' && method === 'GET') return route.fulfill({ json: s.docs });

  const m = /^\/kiracilar\/([^/]+)(\/[a-z-]+)?$/.exec(path);
  if (m) {
    const d = find(s, m[1]);
    if (!d) return problem(route, 404, 'bulunamadi', 'Firma bulunamadı.');
    const sub = m[2] ?? '';
    if (sub === '' && method === 'GET') return route.fulfill({ json: d });
    if (sub === '/durum') {
      const b = json();
      if (b['durum'] === 'Kapali') {
        if (b['onayKod'] !== d.kod) {
          const msg =
            'Onay kodu uyuşmadı — firma KAPATILMADI. Kapatmak için firma kodunu aynen yazın.';
          return problem(route, 400, 'dogrulama', msg, { errors: { onayKod: [msg] } });
        }
        d.durum = 'Kapali';
        d['kapanisTarihi'] = '2026-09-24T09:00:00Z';
      } else {
        d.durum = String(b['durum']);
        d['kapanisTarihi'] = null;
      }
      return route.fulfill({ json: d });
    }
    if (sub === '/web-sitesi-modulu') {
      d['webSitesiModulu'] = json()['aktif'] === true;
      return route.fulfill({ json: d });
    }
  }
  const dm = /^\/belgeler\/([^/]+)\/durum$/.exec(path);
  if (dm) {
    const doc = s.docs.find((x) => x['id'] === dm[1]);
    if (doc) doc['durum'] = json()['durum'];
    return route.fulfill({ json: doc });
  }
  return problem(route, 404, 'bulunamadi', `Sahte uç yok: ${method} ${path}`);
}
