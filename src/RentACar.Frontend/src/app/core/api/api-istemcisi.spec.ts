import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ApiHatasi } from './api-hatasi';
import { ApiIstemcisi, ApiPath, provideApiClient } from './api-istemcisi';

describe('ApiIstemcisi', () => {
  let api: ApiIstemcisi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideApiClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(ApiIstemcisi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    document.cookie = 'XSRF-TOKEN=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/';
  });

  it('göreli URL, withCredentials, sorgu parametreleri (Türkçe metin, dizi, null atlanır)', () => {
    let result: unknown;
    api
      .get<{ ad: string }>('/api/ui/v1/secim/musteri', {
        parametreler: {
          q: 'Şükrü Öğüt',
          limit: 20,
          etkin: true,
          durum: ['acik', 'kapali'],
          bos: null,
        },
      })
      .subscribe((v) => (result = v));

    const request = http.expectOne((r) => r.url === '/api/ui/v1/secim/musteri');
    expect(request.request.method).toBe('GET');
    expect(request.request.withCredentials).toBe(true);
    expect(request.request.params.get('q')).toBe('Şükrü Öğüt');
    expect(request.request.params.get('limit')).toBe('20');
    expect(request.request.params.get('etkin')).toBe('true');
    expect(request.request.params.getAll('durum')).toEqual(['acik', 'kapali']);
    expect(request.request.params.has('bos')).toBe(false);
    expect(request.request.urlWithParams).toBe(
      '/api/ui/v1/secim/musteri?q=%C5%9E%C3%BCkr%C3%BC%20%C3%96%C4%9F%C3%BCt&limit=20&etkin=true&durum=acik&durum=kapali',
    );
    request.flush({ ad: 'ok' });
    expect(result).toEqual({ ad: 'ok' });
  });

  it('güvensiz istekte XSRF çerezi X-XSRF-TOKEN başlığı olarak gider; GET’te gitmez', () => {
    document.cookie = 'XSRF-TOKEN=belirtec-123; path=/';

    api.post('/api/ui/v1/oturum/cikis', {}).subscribe();
    const post = http.expectOne('/api/ui/v1/oturum/cikis');
    expect(post.request.headers.get('X-XSRF-TOKEN')).toBe('belirtec-123');
    post.flush(null);

    api.get('/api/ui/v1/oturum/ben').subscribe();
    const get = http.expectOne('/api/ui/v1/oturum/ben');
    expect(get.request.headers.has('X-XSRF-TOKEN')).toBe(false);
    get.flush({});
  });

  it('islemAnahtari → Idempotency-Key başlığı', () => {
    api
      .post('/api/ui/v1/kasa/tahsilat', { tutar: 1 }, { islemAnahtari: 'anahtar-0123456789abcdef' })
      .subscribe();
    const request = http.expectOne('/api/ui/v1/kasa/tahsilat');
    expect(request.request.headers.get('Idempotency-Key')).toBe('anahtar-0123456789abcdef');
    expect(request.request.body).toEqual({ tutar: 1 });
    request.flush({});
  });

  it('hata ApiHatasi olarak akar (ProblemDetails eşlemesi)', () => {
    let error: unknown;
    api.put('/api/ui/v1/araclar/1', {}).subscribe({ error: (h: unknown) => (error = h) });
    http.expectOne('/api/ui/v1/araclar/1').flush(
      {
        title: 'Çakışma',
        status: 409,
        detail: 'Plaka zaten kayıtlı.',
        kod: 'cakisma',
        errors: { Plaka: ['Plaka zaten kayıtlı.'] },
      },
      { status: 409, statusText: 'Conflict' },
    );
    expect(error).toBeInstanceOf(ApiHatasi);
    expect(error).toMatchObject({
      status: 409,
      kod: 'cakisma',
      alanlar: { Plaka: ['Plaka zaten kayıtlı.'] },
    });
  });

  it('otomatik yeniden deneme YOK: mukerrer tek istekte kalır', () => {
    let error: ApiHatasi | undefined;
    api.post('/api/ui/v1/kasa/tahsilat', {}).subscribe({ error: (h: ApiHatasi) => (error = h) });
    http
      .expectOne('/api/ui/v1/kasa/tahsilat')
      .flush(
        { status: 409, detail: 'Mükerrer.', kod: 'mukerrer' },
        { status: 409, statusText: 'Conflict' },
      );
    http.expectNone('/api/ui/v1/kasa/tahsilat');
    expect(error?.kod).toBe('mukerrer');
  });

  it.each([
    // Mutlak URL kaynakta lint'le yasak; reddedildiğini sınamak için çalışma zamanında kurulur.
    'https' + '://baska.site/api/ui/v1/x',
    '/api/ui/v1',
    '/api/ui/v1/',
    '/api/ui/v1/musteri?q=1',
    '/api/ui/v1/musteri#x',
    '/api/ui/v1/../login',
    '/api/ui/v1/a//b',
    '/api/ui/v1/a\\b',
    'api/ui/v1/x',
  ])('geçersiz yol "%s" eşzamanlı reddedilir, istek gitmez', (path) => {
    expect(() => api.get(path as ApiPath)).toThrow(TypeError);
    http.expectNone(() => true);
  });

  it('uzun ömürlü abonelikten çıkınca istek iptal edilir', () => {
    const subscription = api.get('/api/ui/v1/menu').subscribe();
    const request = http.expectOne('/api/ui/v1/menu');
    subscription.unsubscribe();
    expect(request.cancelled).toBe(true);
  });
});
