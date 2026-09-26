import { HttpXsrfTokenExtractor } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideApiClient } from '@core/api/api-istemcisi';
import { provideTranslation } from '@core/i18n/ceviri';
import { sessionInterceptor } from '@core/oturum/session-interceptor';
import { ReloginService } from '@core/oturum/relogin-service';

import { ReportPage } from './report-page';

/** Ortak rapor ekranı: tanımdan çizim, dört durum, 403 mesajı, export yalnız bağlantı varsa. */
const INCOME = '/api/ui/v1/raporlar/gelir-gider';

class FakeXsrf implements HttpXsrfTokenExtractor {
  getToken(): string | null {
    return 'belirtec';
  }
}

const RESPONSE = {
  donem: { bas: null, bit: null },
  ozet: {
    gelirToplam: '1000.5',
    giderToplam: 400,
    kdvTahsil: 0,
    kdvIndirilecek: 0,
    netKar: -250,
    gelirKirilim: [{ sourceType: 'Fatura', tutar: 1000.5 }],
    giderKirilim: [],
  },
  export: {
    excel: '/raporlar/export/gelir-gider?format=excel&from=2026-09-01',
    csv: '/raporlar/export/gelir-gider?format=csv&from=2026-09-01',
    pdf: null,
  },
};

describe('ReportPage', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        ...provideTranslation(),
        provideRouter([
          { path: 'raporlar/gelir-gider', component: ReportPage, data: { rapor: 'gelir-gider' } },
        ]),
        provideApiClient(sessionInterceptor),
        provideHttpClientTesting(),
        { provide: HttpXsrfTokenExtractor, useClass: FakeXsrf },
        { provide: ReloginService, useValue: { request: () => Promise.resolve(false) } },
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function open(url = '/raporlar/gelir-gider') {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url);
    await harness.fixture.whenStable();
    const root = harness.routeNativeElement as HTMLElement;
    return { harness, root, stable: () => harness.fixture.whenStable() };
  }

  it('özet kartları (negatif işaretli), kırılım tablosu, boş kırılım ve export bağlantısı', async () => {
    const { root, stable } = await open('/raporlar/gelir-gider?bas=2026-09-01&bit=2026-09-30');
    const req = http.expectOne((r) => r.url === INCOME);
    expect(req.request.params.get('bas')).toBe('2026-09-01');
    expect(req.request.params.get('bit')).toBe('2026-09-30');
    req.flush(RESPONSE);
    await stable();
    const cards = [...root.querySelectorAll('.ozet-serit dd')].map((d) => d.textContent?.trim());
    expect(cards).toEqual(['1.000,50 ₺', '400,00 ₺', '0,00 ₺', '0,00 ₺', '-250,00 ₺']);
    expect(root.querySelector('.ozet-serit dd.eksi')?.textContent?.trim()).toBe('-250,00 ₺');
    expect(root.querySelector('[aria-labelledby="rc-rapor-b-gelir"] tbody')?.textContent).toContain(
      'Fatura',
    );
    expect(root.querySelector('[aria-labelledby="rc-rapor-b-gider"]')?.textContent).toContain(
      'Kayıt bulunamadı.',
    );
    const hrefs = [...root.querySelectorAll('rc-sayfa-bandi a')].map((a) => a.getAttribute('href'));
    expect(hrefs).toEqual([
      '/raporlar/export/gelir-gider?format=excel&from=2026-09-01',
      '/raporlar/export/gelir-gider?format=csv&from=2026-09-01',
    ]);
  });

  it('export bağlantısı gelmezse düğme yok', async () => {
    const { root, stable } = await open();
    http.expectOne((r) => r.url === INCOME).flush({ ...RESPONSE, export: null });
    await stable();
    expect(root.querySelectorAll('rc-sayfa-bandi a')).toHaveLength(0);
  });

  it('403: yetki mesajı, özet yok (hata "kayıt yok" gibi görünmez)', async () => {
    const { root, stable } = await open();
    http
      .expectOne((r) => r.url === INCOME)
      .flush(
        { status: 403, kod: 'yetki_yok', detail: 'Bu rapor firma genelidir.' },
        { status: 403, statusText: 'Forbidden' },
      );
    await stable();
    expect(root.querySelector('[data-rapor-durum="yetki"]')?.textContent).toContain(
      'firma genelidir',
    );
    expect(root.querySelector('.ozet-serit')).toBeNull();
    expect(root.textContent).not.toContain('Kayıt bulunamadı.');
  });

  it('sunucu hatası: hata bandı + yeniden dene aynı isteği tekrarlar', async () => {
    const { root, stable } = await open();
    http
      .expectOne((r) => r.url === INCOME)
      .flush({ status: 500, detail: 'Beklenmeyen hata.' }, { status: 500, statusText: 'Error' });
    await stable();
    const band = root.querySelector('[data-rapor-durum="hata"]');
    expect(band).not.toBeNull();
    (band?.querySelector('button') as HTMLButtonElement).click();
    await stable();
    http.expectOne((r) => r.url === INCOME).flush(RESPONSE);
    await stable();
    expect(root.querySelector('[data-rapor-durum="hata"]')).toBeNull();
    expect(root.querySelectorAll('.ozet-serit dd')).toHaveLength(5);
  });
});
