import { HttpErrorResponse } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { provideApiIstemcisi } from '@core/api/api-istemcisi';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { ParcaHatasiServisi } from '@core/surum/parca-hatasi';
import { SurumServisi } from '@core/surum/surum-servisi';

import {
  EN_FAZLA_RAPOR,
  IstemciHataRaporlayici,
  RAPOR_SINIRLARI,
  RcHataIsleyici,
} from './istemci-hata';

describe('İstemci hata raporu', () => {
  const girisli = signal(true);
  const parca = { isle: vi.fn(() => true) };
  let http: HttpTestingController;

  beforeEach(() => {
    girisli.set(true);
    parca.isle.mockClear();
    TestBed.configureTestingModule({
      providers: [
        provideApiIstemcisi(),
        provideHttpClientTesting(),
        { provide: OturumServisi, useValue: { girisYapildi: girisli.asReadonly() } },
        { provide: SurumServisi, useValue: { mevcut: 'main-ABCD1234.js' } },
        { provide: ParcaHatasiServisi, useValue: parca },
        RcHataIsleyici,
      ],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
  });

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  it('yakalanmamış hata: mesaj, yığın (kırpılmış), yalnız YOL (sorgu yok) ve sürüm gönderilir', () => {
    const hata = new TypeError('x.y undefined');
    hata.stack = 'TypeError: x.y undefined\n' + 'at a (chunk-1.js:1:1)\n'.repeat(500);
    TestBed.inject(RcHataIsleyici).handleError(hata);

    const istek = http.expectOne('/api/ui/v1/istemci-hata');
    expect(istek.request.method).toBe('POST');
    const govde = istek.request.body as Record<string, string>;
    expect(govde['mesaj']).toBe('TypeError: x.y undefined');
    expect(govde['yigin']?.length).toBe(RAPOR_SINIRLARI.yigin);
    expect(govde['url']).toBe(location.pathname);
    expect(govde['url']).not.toContain('?');
    expect(govde['surum']).toBe('main-ABCD1234.js');
    istek.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('oturum yoksa, HTTP hatasında ve aynı mesajın tekrarında gönderilmez; sayfa başına sınır', () => {
    const rapor = TestBed.inject(IstemciHataRaporlayici);
    expect(rapor.raporla(new HttpErrorResponse({ status: 500 }))).toBe(false);
    girisli.set(false);
    expect(rapor.raporla(new Error('oturumsuz'))).toBe(false);
    girisli.set(true);

    expect(rapor.raporla(new Error('tekrar'))).toBe(true);
    expect(rapor.raporla(new Error('tekrar'))).toBe(false);
    for (let i = 1; i < EN_FAZLA_RAPOR + 5; i++) rapor.raporla(new Error(`hata ${i}`));
    const istekler = http.match('/api/ui/v1/istemci-hata');
    expect(istekler).toHaveLength(EN_FAZLA_RAPOR);
    for (const i of istekler) i.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('ChunkLoadError raporlanmaz, kontrollü yenilemeye gider', () => {
    TestBed.inject(RcHataIsleyici).handleError(
      new TypeError('Failed to fetch dynamically imported module: /app/chunk-X.js'),
    );
    expect(parca.isle).toHaveBeenCalledTimes(1);
    http.expectNone('/api/ui/v1/istemci-hata');
  });

  it('rapor isteği hata alırsa sessiz (döngü yok)', () => {
    TestBed.inject(RcHataIsleyici).handleError(new Error('rapor edilecek'));
    http
      .expectOne('/api/ui/v1/istemci-hata')
      .flush({ status: 429, kod: 'cok_istek' }, { status: 429, statusText: 'Too Many' });
    http.expectNone('/api/ui/v1/istemci-hata');
  });
});
