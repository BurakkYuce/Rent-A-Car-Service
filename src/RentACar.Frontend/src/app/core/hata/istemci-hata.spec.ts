import { HttpErrorResponse } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { provideApiClient } from '@core/api/api-istemcisi';
import { SessionService } from '@core/oturum/session-service';
import { ChunkErrorService } from '@core/surum/parca-hatasi';
import { VersionService } from '@core/surum/version-service';

import { MAX_REPORTS, ClientErrorReporter, REPORT_LIMITS, RcErrorHandler } from './istemci-hata';

describe('İstemci hata raporu', () => {
  const loggedIn = signal(true);
  const part = { isle: vi.fn(() => true) };
  let http: HttpTestingController;

  beforeEach(() => {
    loggedIn.set(true);
    part.isle.mockClear();
    TestBed.configureTestingModule({
      providers: [
        provideApiClient(),
        provideHttpClientTesting(),
        { provide: SessionService, useValue: { loggedIn: loggedIn.asReadonly() } },
        { provide: VersionService, useValue: { mevcut: 'main-ABCD1234.js' } },
        { provide: ChunkErrorService, useValue: part },
        RcErrorHandler,
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
    const error = new TypeError('x.y undefined');
    error.stack = 'TypeError: x.y undefined\n' + 'at a (chunk-1.js:1:1)\n'.repeat(500);
    TestBed.inject(RcErrorHandler).handleError(error);

    const request = http.expectOne('/api/ui/v1/istemci-hata');
    expect(request.request.method).toBe('POST');
    const body = request.request.body as Record<string, string>;
    expect(body['mesaj']).toBe('TypeError: x.y undefined');
    expect(body['yigin']?.length).toBe(REPORT_LIMITS.yigin);
    expect(body['url']).toBe(location.pathname);
    expect(body['url']).not.toContain('?');
    expect(body['surum']).toBe('main-ABCD1234.js');
    request.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('oturum yoksa, HTTP hatasında ve aynı mesajın tekrarında gönderilmez; sayfa başına sınır', () => {
    const report = TestBed.inject(ClientErrorReporter);
    expect(report.report(new HttpErrorResponse({ status: 500 }))).toBe(false);
    loggedIn.set(false);
    expect(report.report(new Error('oturumsuz'))).toBe(false);
    loggedIn.set(true);

    expect(report.report(new Error('tekrar'))).toBe(true);
    expect(report.report(new Error('tekrar'))).toBe(false);
    for (let i = 1; i < MAX_REPORTS + 5; i++) report.report(new Error(`hata ${i}`));
    const requests = http.match('/api/ui/v1/istemci-hata');
    expect(requests).toHaveLength(MAX_REPORTS);
    for (const i of requests) i.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('ChunkLoadError raporlanmaz, kontrollü yenilemeye gider', () => {
    TestBed.inject(RcErrorHandler).handleError(
      new TypeError('Failed to fetch dynamically imported module: /app/chunk-X.js'),
    );
    expect(part.isle).toHaveBeenCalledTimes(1);
    http.expectNone('/api/ui/v1/istemci-hata');
  });

  it('rapor isteği hata alırsa sessiz (döngü yok)', () => {
    TestBed.inject(RcErrorHandler).handleError(new Error('rapor edilecek'));
    http
      .expectOne('/api/ui/v1/istemci-hata')
      .flush({ status: 429, kod: 'cok_istek' }, { status: 429, statusText: 'Too Many' });
    http.expectNone('/api/ui/v1/istemci-hata');
  });
});
