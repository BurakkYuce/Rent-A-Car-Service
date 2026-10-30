import { HttpClient, HttpXsrfTokenExtractor } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { ApiHatasi } from '@core/api/api-hatasi';
import { ApiIstemcisi, provideApiClient } from '@core/api/api-istemcisi';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { WarningBannerService } from '@core/geri-bildirim/warning-banner-service';
import { provideTranslation } from '@core/i18n/ceviri';

import { requestContext } from './request-context';
import { sessionInterceptor } from './session-interceptor';
import { SessionService } from './session-service';
import type { Ben } from './oturum-tipleri';
import { ReloginService } from './relogin-service';

const BEN: Ben = {
  kullanici: { id: 'u-1', kullaniciAdi: 'ayse', adSoyad: 'Ayşe Yılmaz' },
  kiraci: { id: 't-1', kod: 'pilot', ad: 'Pilot Firma' },
  rol: 'Admin',
  izinler: ['OperationsWrite'],
  subeKapsami: { tumSubeler: true, subeId: null, subeAd: null },
  moduller: { webSitesi: false },
  renkler: {},
  pilot: true,
};

const RECORD = '/api/ui/v1/vitrin/kayit';

function problem(code: string, status: number, extra: Record<string, unknown> = {}) {
  return {
    govde: {
      type: 'about:blank',
      title: 'Başlık',
      status,
      detail: `${code} ayrıntısı`,
      kod: code,
      ...extra,
    },
    secenek: { status, statusText: 'Hata' },
  };
}

/** XSRF çerez okuyucusu: testte çerez yerine değişken. */
let xsrfToken: string | null = 'eski-belirtec';
class FakeXsrf implements HttpXsrfTokenExtractor {
  getToken(): string | null {
    return xsrfToken;
  }
}

describe('oturumInterceptor (kod bazlı)', () => {
  let http: HttpTestingController;
  let api: ApiIstemcisi;
  let toast: ToastService;
  let banner: WarningBannerService;
  const relogin = { request: vi.fn<() => Promise<boolean>>() };

  async function logIn(): Promise<void> {
    const session = TestBed.inject(SessionService);
    const loading = session.yukle();
    http.expectOne('/api/ui/v1/oturum/ben').flush(BEN);
    await loading;
    expect(session.loggedIn()).toBe(true);
  }

  const wait = (url: string) => vi.waitFor(() => http.expectOne(url));

  beforeEach(async () => {
    xsrfToken = 'eski-belirtec';
    relogin.request.mockReset();
    TestBed.configureTestingModule({
      providers: [
        ...provideTranslation(),
        provideRouter([]),
        provideApiClient(sessionInterceptor),
        provideHttpClientTesting(),
        { provide: HttpXsrfTokenExtractor, useClass: FakeXsrf },
        { provide: ReloginService, useValue: relogin },
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
    http = TestBed.inject(HttpTestingController);
    api = TestBed.inject(ApiIstemcisi);
    toast = TestBed.inject(ToastService);
    banner = TestBed.inject(WarningBannerService);
  });

  afterEach(() => {
    http.verify();
    toast.clear();
  });

  it('oturum_yok → yerinde giriş, AYNI istek (gövde + anahtar) TAZE XSRF ile tekrarlanır, sonuç çağırana', async () => {
    await logIn();
    relogin.request.mockImplementation(async () => {
      xsrfToken = 'yeni-belirtec'; // giriş yeni kimliğe bağlı belirteç verdi
      return true;
    });
    const body = { plaka: '34 ABC 123', aciklama: 'Form verisi' };
    const result = firstValueFrom(
      api.post<{ id: string }>(RECORD, body, { islemAnahtari: 'anahtar-0123456789abcdef' }),
    );

    const first = http.expectOne(RECORD);
    expect(first.request.headers.get('X-XSRF-TOKEN')).toBe('eski-belirtec');
    const { govde: p, secenek } = problem('oturum_yok', 401);
    first.flush(p, secenek);

    const repeat = await wait(RECORD);
    expect(repeat.request.method).toBe('POST');
    expect(repeat.request.body).toEqual(body);
    expect(repeat.request.headers.get('Idempotency-Key')).toBe('anahtar-0123456789abcdef');
    expect(repeat.request.headers.get('X-XSRF-TOKEN')).toBe('yeni-belirtec');
    expect(repeat.request.withCredentials).toBe(true);
    repeat.flush({ id: 'kayit-1' });

    await expect(result).resolves.toEqual({ id: 'kayit-1' });
    expect(relogin.request).toHaveBeenCalledTimes(1);
    expect(banner.bant()).toBeNull();
  });

  it('oturum_yok + vazgeç → tekrar YOK, çağıran oturum_yok alır (form yerinde kalır)', async () => {
    await logIn();
    relogin.request.mockResolvedValue(false);
    const result = firstValueFrom(api.post(RECORD, { plaka: 'x' }));
    const { govde, secenek } = problem('oturum_yok', 401);
    http.expectOne(RECORD).flush(govde, secenek);

    await expect(result).rejects.toMatchObject({ kod: 'oturum_yok', status: 401 });
    expect(relogin.request).toHaveBeenCalledTimes(1);
  });

  it('aynı anda düşen iki istek TEK diyalog paylaşır, ikisi de tekrarlanır', async () => {
    await logIn();
    let resolve: (entered: boolean) => void = () => undefined;
    const dialog = new Promise<boolean>((r) => (resolve = r));
    relogin.request.mockReturnValue(dialog);

    const a = firstValueFrom(api.post<{ id: string }>(RECORD, { n: 1 }));
    const b = firstValueFrom(api.get<{ liste: number[] }>('/api/ui/v1/menu'));
    const { govde, secenek } = problem('oturum_yok', 401);
    http.expectOne(RECORD).flush(govde, secenek);
    http.expectOne('/api/ui/v1/menu').flush(govde, secenek);
    resolve(true);

    (await wait(RECORD)).flush({ id: 'a' });
    (await wait('/api/ui/v1/menu')).flush({ liste: [1] });
    await expect(a).resolves.toEqual({ id: 'a' });
    await expect(b).resolves.toEqual({ liste: [1] });
    // Servis tek sözü paylaştırır; interceptor her istek için sorar.
    expect(relogin.request).toHaveBeenCalledTimes(2);
  });

  it('tekrarlanan istek yine oturum_yok alırsa ikinci diyalog açılmaz (döngü yok)', async () => {
    await logIn();
    relogin.request.mockResolvedValue(true);
    const result = firstValueFrom(api.post(RECORD, {}));
    const { govde, secenek } = problem('oturum_yok', 401);
    http.expectOne(RECORD).flush(govde, secenek);
    (await wait(RECORD)).flush(govde, secenek);

    await expect(result).rejects.toMatchObject({ kod: 'oturum_yok' });
    expect(relogin.request).toHaveBeenCalledTimes(1);
  });

  it('oturum hiç yokken (açılış/giriş sayfası) ya da yenidenGirisYok bayrağında diyalog açılmaz', async () => {
    const { govde, secenek } = problem('oturum_yok', 401);
    const anonymous = firstValueFrom(api.get('/api/ui/v1/menu'));
    http.expectOne('/api/ui/v1/menu').flush(govde, secenek);
    await expect(anonymous).rejects.toMatchObject({ kod: 'oturum_yok' });

    await logIn();
    const flagged = firstValueFrom(
      api.get('/api/ui/v1/menu', { context: requestContext({ yenidenGirisYok: true }) }),
    );
    http.expectOne('/api/ui/v1/menu').flush(govde, secenek);
    await expect(flagged).rejects.toMatchObject({ kod: 'oturum_yok' });
    expect(relogin.request).not.toHaveBeenCalled();
  });

  it('xsrf_gecersiz → GET oturum/xsrf ile yenilenir ve istek BİR kez taze başlıkla tekrarlanır', async () => {
    const result = firstValueFrom(api.post<{ id: string }>(RECORD, { a: 1 }));
    const { govde, secenek } = problem('xsrf_gecersiz', 400);
    http.expectOne(RECORD).flush(govde, secenek);

    const xsrf = await wait('/api/ui/v1/oturum/xsrf');
    expect(xsrf.request.method).toBe('GET');
    xsrfToken = 'tazelenmis';
    xsrf.flush(null, { status: 204, statusText: 'No Content' });

    const repeat = await wait(RECORD);
    expect(repeat.request.headers.get('X-XSRF-TOKEN')).toBe('tazelenmis');
    expect(repeat.request.body).toEqual({ a: 1 });
    repeat.flush({ id: 'x' });
    await expect(result).resolves.toEqual({ id: 'x' });
  });

  it('xsrf_gecersiz ikinci kez gelirse bir daha denenmez, çağırana gider', async () => {
    const result = firstValueFrom(api.post(RECORD, {}));
    const { govde, secenek } = problem('xsrf_gecersiz', 400);
    http.expectOne(RECORD).flush(govde, secenek);
    (await wait('/api/ui/v1/oturum/xsrf')).flush(null, { status: 204, statusText: 'No Content' });
    (await wait(RECORD)).flush(govde, secenek);

    await expect(result).rejects.toMatchObject({ kod: 'xsrf_gecersiz' });
  });

  it('mukerrer → yeni anahtarla TEKRAR GÖNDERİLMEZ; kayıt yenileme çağrılır + bilgi toast’u', async () => {
    const refresh = vi.fn();
    const result = firstValueFrom(
      api.post(
        RECORD,
        { tutar: 100 },
        {
          islemAnahtari: 'anahtar-0123456789abcdef',
          context: requestContext({ mukerrerdeYenile: refresh }),
        },
      ),
    );
    const { govde, secenek } = problem('mukerrer', 409, {
      detail: 'Bu işlem farklı içerikle zaten kaydedilmiş; kaydı kontrol edin.',
    });
    http.expectOne(RECORD).flush(govde, secenek);

    await expect(result).rejects.toBeInstanceOf(ApiHatasi);
    await expect(result).rejects.toMatchObject({ kod: 'mukerrer', status: 409 });
    http.expectNone(RECORD); // ikinci gönderim yok
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(toast.toasts()).toEqual([
      expect.objectContaining({
        durum: 'bilgi',
        baslik: 'Mükerrer işlem',
        mesaj:
          'Bu işlem farklı içerikle zaten kaydedilmiş; kaydı kontrol edin. Kayıt yeniden yüklendi.',
      }),
    ]);
  });

  it('mukerrer + mukerrerBasligi → sunucu detayı UYARI toast’u o başlıkla (“mükerrer kaydedildi” izlenimi yok); tekrar yok', async () => {
    const refresh = vi.fn();
    const result = firstValueFrom(
      api.post(
        RECORD,
        { tutar: 100 },
        {
          islemAnahtari: 'anahtar-0123456789abcdef',
          context: requestContext({
            mukerrerdeYenile: refresh,
            mukerrerBasligi: 'Kira kaydı değişmiş',
          }),
        },
      ),
    );
    const { govde, secenek } = problem('mukerrer', 409, {
      detail:
        'Kiranın bakiyesi bu ekran açıldıktan sonra değişti; kaydı yeniden yükleyip tekrar deneyin.',
    });
    http.expectOne(RECORD).flush(govde, secenek);

    await expect(result).rejects.toMatchObject({ kod: 'mukerrer', status: 409 });
    http.expectNone(RECORD);
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(toast.toasts()).toEqual([
      expect.objectContaining({
        durum: 'uyari',
        baslik: 'Kira kaydı değişmiş',
        mesaj:
          'Kiranın bakiyesi bu ekran açıldıktan sonra değişti; kaydı yeniden yükleyip tekrar deneyin. Kayıt yeniden yüklendi.',
      }),
    ]);
  });

  it('mukerrer + mevcut (işlem zaten yazıldı) → "İşlem zaten kaydedildi" bilgisi (mukerrerBasligi’ne rağmen); tekrar yok', async () => {
    const refresh = vi.fn();
    const result = firstValueFrom(
      api.post(
        RECORD,
        { tutar: 500 },
        {
          context: requestContext({
            mukerrerdeYenile: refresh,
            mukerrerBasligi: 'Kira kaydı değişmiş',
          }),
        },
      ),
    );
    const { govde, secenek } = problem('mukerrer', 409, {
      detail: 'Bu tahsilat zaten kaydedildi (No T-1, 500,00 TRY); yeni tahsilat yazılmadı.',
      mevcut: { id: 'c1', belgeNo: 'T-1', tutar: 500, doviz: 'TRY', ayniIcerik: true },
    });
    http.expectOne(RECORD).flush(govde, secenek);
    await expect(result).rejects.toMatchObject({ kod: 'mukerrer', mevcut: { belgeNo: 'T-1' } });
    http.expectNone(RECORD);
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(toast.toasts()).toEqual([
      expect.objectContaining({
        durum: 'bilgi',
        baslik: 'İşlem zaten kaydedildi',
        mesaj:
          'Bu tahsilat zaten kaydedildi (No T-1, 500,00 TRY); yeni tahsilat yazılmadı. Kayıt yeniden yüklendi.',
      }),
    ]);
  });

  it('mukerrer + mevcut İÇERİK FARKLI (başka tahsilat yazıldı) → UYARI "Başka bir tahsilat yazıldı", bilgi değil', async () => {
    const result = firstValueFrom(
      api.post(
        RECORD,
        { tutar: 3000 },
        {
          context: requestContext({
            mukerrerdeYenile: vi.fn(),
            mukerrerBasligi: 'Kira kaydı değişmiş',
          }),
        },
      ),
    );
    const detail =
      'Bu ekran açıldıktan sonra başka bir tahsilat yazıldı (No T-1, 100,00 TRY); girdiğiniz 3.000,00 TRY YAZILMADI. Güncel bakiyeyi kontrol edin.';
    const { govde, secenek } = problem('mukerrer', 409, {
      detail,
      mevcut: { id: 'c1', belgeNo: 'T-1', tutar: 100, doviz: 'TRY', ayniIcerik: false },
    });
    http.expectOne(RECORD).flush(govde, secenek);
    await expect(result).rejects.toMatchObject({ kod: 'mukerrer', mevcut: { ayniIcerik: false } });
    expect(toast.toasts()).toEqual([
      expect.objectContaining({
        durum: 'uyari',
        baslik: 'Başka bir tahsilat yazıldı — tutarınız kaydedilmedi',
        mesaj: `${detail} Kayıt yeniden yüklendi.`,
      }),
    ]);
  });

  it('dogrulama → yalnız çağırana, alan hatalarıyla; bant/toast yok', async () => {
    const result = firstValueFrom(api.post(RECORD, {}));
    const { govde, secenek } = problem('dogrulama', 400, {
      errors: { Plaka: ['Plaka zorunludur.'] },
    });
    http.expectOne(RECORD).flush(govde, secenek);

    await expect(result).rejects.toMatchObject({
      kod: 'dogrulama',
      alanlar: { Plaka: ['Plaka zorunludur.'] },
    });
    expect(banner.bant()).toBeNull();
    expect(toast.toasts()).toEqual([]);
  });

  it('cakisma alanlıysa form hatası (bant yok); alansızsa uyarı bandı', async () => {
    const withFields = firstValueFrom(api.post(RECORD, {}));
    const a = problem('cakisma', 409, { errors: { Plaka: ['Plaka kayıtlı.'] } });
    http.expectOne(RECORD).flush(a.govde, a.secenek);
    await expect(withFields).rejects.toMatchObject({ kod: 'cakisma' });
    expect(banner.bant()).toBeNull();

    const withoutFields = firstValueFrom(api.post(RECORD, {}));
    const b = problem('cakisma', 409, { detail: 'Araç bu tarihlerde müsait değil.' });
    http.expectOne(RECORD).flush(b.govde, b.secenek);
    await expect(withoutFields).rejects.toMatchObject({ kod: 'cakisma' });
    expect(banner.bant()).toEqual({
      tur: 'uyari',
      mesaj: 'Araç bu tarihlerde müsait değil.',
      kod: 'cakisma',
    });
    http.expectNone(RECORD);
  });

  it.each(['yetki_yok', 'pilot_degil'])('%s → uyarı bandı (form hatası değil)', async (code) => {
    const result = firstValueFrom(api.get('/api/ui/v1/menu'));
    const { govde, secenek } = problem(code, 403);
    http.expectOne('/api/ui/v1/menu').flush(govde, secenek);
    await expect(result).rejects.toMatchObject({ kod: code });
    expect(banner.bant()).toMatchObject({ tur: 'uyari', kod: code, mesaj: `${code} ayrıntısı` });
  });

  it('kiraci_kapali → tam temizlik + mesajlı giriş sayfası', async () => {
    const closed = vi.spyOn(TestBed.inject(SessionService), 'tenantClosed').mockResolvedValue();
    const result = firstValueFrom(api.get('/api/ui/v1/menu'));
    const { govde, secenek } = problem('kiraci_kapali', 401);
    http.expectOne('/api/ui/v1/menu').flush(govde, secenek);
    await expect(result).rejects.toMatchObject({ kod: 'kiraci_kapali' });
    expect(closed).toHaveBeenCalledTimes(1);
    expect(relogin.request).not.toHaveBeenCalled();
  });

  it('5xx ve ağ hatası → hata toast’u; cok_istek → uyarı toast’u; sessiz bayrağında hiçbiri', async () => {
    const s = firstValueFrom(api.get('/api/ui/v1/menu'));
    http
      .expectOne('/api/ui/v1/menu')
      .flush({ title: 'Sunucu hatası', status: 500 }, { status: 500, statusText: 'Hata' });
    await expect(s).rejects.toMatchObject({ kod: 'sunucu' });

    const a = firstValueFrom(api.get('/api/ui/v1/menu'));
    http.expectOne('/api/ui/v1/menu').error(new ProgressEvent('error'));
    await expect(a).rejects.toMatchObject({ kod: 'ag' });

    const c = firstValueFrom(api.post(RECORD, {}));
    const { govde: body, secenek: option } = problem('cok_istek', 429);
    http.expectOne(RECORD).flush(body, option);
    await expect(c).rejects.toMatchObject({ kod: 'cok_istek' });

    expect(toast.toasts().map((t) => t.durum)).toEqual(['hata', 'hata', 'uyari']);

    toast.clear();
    const silent = firstValueFrom(
      api.get('/api/ui/v1/menu', { context: requestContext({ sessiz: true }) }),
    );
    http
      .expectOne('/api/ui/v1/menu')
      .flush({ title: 'Sunucu hatası', status: 500 }, { status: 500, statusText: 'Hata' });
    await expect(silent).rejects.toMatchObject({ kod: 'sunucu' });
    expect(toast.toasts()).toEqual([]);
  });

  it('başarılı yanıta ve /api/ui dışındaki isteklere dokunmaz', async () => {
    const isOk = firstValueFrom(api.get<{ ok: boolean }>('/api/ui/v1/menu'));
    http.expectOne('/api/ui/v1/menu').flush({ ok: true });
    await expect(isOk).resolves.toEqual({ ok: true });

    const other = firstValueFrom(TestBed.inject(HttpClient).get('/baska/uc'));
    http.expectOne('/baska/uc').flush('x', { status: 500, statusText: 'Hata' });
    await expect(other).rejects.toMatchObject({ status: 500 });
    expect(toast.toasts()).toEqual([]);
    expect(banner.bant()).toBeNull();
  });

  describe('#417 X-Beklenen-Oturum (anahtarın bağlı olduğu kimlik)', () => {
    // Kimlikler elle yazılır (kiracı|kullanıcı) — biçim üretim kodundan TÜRETİLMEZ.
    const IDENTITY_A = 't-1|u-1';
    const IDENTITY_B = 't-1|u-2';
    const BEN_B: Ben = {
      ...BEN,
      kullanici: { id: 'u-2', kullaniciAdi: 'mehmet', adSoyad: 'Mehmet Kaya' },
    };
    const KEY = 'anahtar-0123456789abcdef';
    const header = (r: { request: { headers: { get(n: string): string | null } } }) =>
      r.request.headers.get('X-Beklenen-Oturum');

    async function switchTo(ben: Ben): Promise<void> {
      const loading = TestBed.inject(SessionService).yukle();
      const request = http.expectOne('/api/ui/v1/oturum/ben');
      expect(header(request)).toBeNull();
      request.flush(ben);
      await loading;
    }

    it('L1: anahtarlı yazma ilk kimliği taşır; başka sekmede kullanıcı değişse de AYNI anahtarın tekrarı ilk kimlikle gider (409, bant/toast yok); yeni anahtar yeni kimlikle', async () => {
      await logIn();
      const first = firstValueFrom(api.post(RECORD, { tutar: '500.00' }, { islemAnahtari: KEY }));
      const sent = http.expectOne(RECORD);
      expect(header(sent)).toBe(IDENTITY_A);
      sent.error(new ProgressEvent('error')); // sonucu bilinmiyor
      await expect(first).rejects.toMatchObject({ kod: 'ag' });
      toast.clear();

      await switchTo(BEN_B); // öteki sekmede B girdi; bu sekme ben'i tazeledi

      const retry = firstValueFrom(api.post(RECORD, { tutar: '500.00' }, { islemAnahtari: KEY }));
      const repeated = http.expectOne(RECORD);
      expect(header(repeated)).toBe(IDENTITY_A);
      const { govde, secenek } = problem('oturum_degisti', 409);
      repeated.flush(govde, secenek);
      await expect(retry).rejects.toMatchObject({ kod: 'oturum_degisti', status: 409 });
      expect(toast.toasts()).toEqual([]);
      expect(banner.bant()).toBeNull();

      // Aynı anahtar bağını korur (yeni kimliğe kaymaz); YENİ anahtar güncel kimliğe bağlanır.
      void firstValueFrom(api.put(RECORD, {}, { islemAnahtari: KEY }));
      expect(header(http.expectOne(RECORD))).toBe(IDENTITY_A);
      void firstValueFrom(api.post(RECORD, {}, { islemAnahtari: 'anahtar-yeni-0123456789' }));
      expect(header(http.expectOne(RECORD))).toBe(IDENTITY_B);
    });

    it('çağıranın verdiği beklenenOturum (para çekirdeği) ezilmez; oturum yokken başlık eklenmez ve anahtar bağlanmaz', async () => {
      void firstValueFrom(api.post(RECORD, {}, { islemAnahtari: KEY }));
      expect(header(http.expectOne(RECORD))).toBeNull();

      await logIn();
      void firstValueFrom(
        api.post(RECORD, {}, { islemAnahtari: 'para-anahtari-0123456789', beklenenOturum: 'x|y' }),
      );
      expect(header(http.expectOne(RECORD))).toBe('x|y');
      // Oturumsuz gönderilen anahtar bağlanmamıştı: şimdi güncel kimliğe bağlanır.
      void firstValueFrom(api.post(RECORD, {}, { islemAnahtari: KEY }));
      expect(header(http.expectOne(RECORD))).toBe(IDENTITY_A);
    });

    it('I3: GET, oturum uçları (xsrf/giris/cikis/ben) ve platform — anahtarlı da anahtarsız da — başlığı ASLA almaz', async () => {
      const session = TestBed.inject(SessionService);
      const login = session.login({ firma: 'pilot', kullanici: 'ayse', sifre: 'x' });
      const xsrf = http.expectOne('/api/ui/v1/oturum/xsrf');
      expect(header(xsrf)).toBeNull();
      xsrf.flush({});
      const giris = await wait('/api/ui/v1/oturum/giris');
      expect(header(giris)).toBeNull();
      giris.flush(BEN);
      await login;
      expect(session.loggedIn()).toBe(true);

      void firstValueFrom(api.get('/api/ui/v1/menu', { islemAnahtari: KEY }));
      expect(header(http.expectOne((r) => r.url === '/api/ui/v1/menu'))).toBeNull();
      void firstValueFrom(api.get('/api/ui/v1/menu'));
      expect(header(http.expectOne((r) => r.url === '/api/ui/v1/menu'))).toBeNull();
      for (const path of [
        '/api/ui/v1/oturum/giris',
        '/api/ui/v1/oturum/cikis',
        '/api/ui/v1/platform/kiracilar',
        '/api/ui/v1/platform',
      ] as const) {
        void firstValueFrom(api.post(path, {}, { islemAnahtari: KEY }));
        expect(header(http.expectOne(path))).toBeNull();
        void firstValueFrom(api.post(path, {}));
        expect(header(http.expectOne(path))).toBeNull();
        void firstValueFrom(api.delete(path));
        expect(header(http.expectOne(path))).toBeNull();
      }
      await switchTo(BEN); // ben GET'i de başlıksız (switchTo denetler)
    });

    it('adv-419 (5): muafiyet segment eşleşmesi — platformx/oturumx/PLATFORM muaf DEĞİL, başlık alır', async () => {
      await logIn();
      for (const path of [
        '/api/ui/v1/platformx/kiracilar',
        '/api/ui/v1/oturumx/giris',
        '/api/ui/v1/PLATFORM/kiracilar',
        '/api/ui/v1/finans/platform/x',
      ] as const) {
        void firstValueFrom(api.post(path, {}));
        expect(header(http.expectOne(path))).toBe(IDENTITY_A);
      }
    });

    it('adv-419 L3: anahtarsız POST/PUT/PATCH/DELETE sekmenin O AN bildiği kimliği taşır (bağ yok); ben yokken başlık yok', async () => {
      void firstValueFrom(api.post(RECORD, {}));
      expect(header(http.expectOne(RECORD))).toBeNull(); // ben henüz yok

      await logIn();
      void firstValueFrom(api.post(RECORD, {}));
      expect(header(http.expectOne(RECORD))).toBe(IDENTITY_A);
      void firstValueFrom(api.put(RECORD, {}));
      expect(header(http.expectOne(RECORD))).toBe(IDENTITY_A);
      void firstValueFrom(api.patch(RECORD, {}));
      expect(header(http.expectOne(RECORD))).toBe(IDENTITY_A);
      void firstValueFrom(api.delete(RECORD));
      expect(header(http.expectOne(RECORD))).toBe(IDENTITY_A);

      // ben tazelendi (öteki sekmede B girdi): anahtarsız yazma bağ taşımaz, güncel kimlikle gider.
      await switchTo(BEN_B);
      void firstValueFrom(api.post(RECORD, {}));
      expect(header(http.expectOne(RECORD))).toBe(IDENTITY_B);

      TestBed.inject(SessionService).clear();
      void firstValueFrom(api.post(RECORD, {}));
      expect(header(http.expectOne(RECORD))).toBeNull();
    });

    it('adv-419 L3: bayat sekmenin anahtarsız yazması 409 oturum_degisti → hata aynen çağırana (sunucu metni), bant/toast/gezinme yok, tekrar yok', async () => {
      await logIn();
      const router = TestBed.inject(Router);
      const navigate = vi.spyOn(router, 'navigate');
      const reversal = firstValueFrom(api.post('/api/ui/v1/finans/kasa/islemler/k-1/ters', null));
      const sent = http.expectOne('/api/ui/v1/finans/kasa/islemler/k-1/ters');
      expect(header(sent)).toBe(IDENTITY_A);
      const { govde, secenek } = problem('oturum_degisti', 409, {
        detail: 'İşlem yazılmadı. Sayfayı yenileyip yeniden deneyin.',
      });
      sent.flush(govde, secenek);
      await expect(reversal).rejects.toMatchObject({
        kod: 'oturum_degisti',
        status: 409,
        detay: 'İşlem yazılmadı. Sayfayı yenileyip yeniden deneyin.',
      });
      http.expectNone('/api/ui/v1/finans/kasa/islemler/k-1/ters');
      expect(toast.toasts()).toEqual([]);
      expect(banner.bant()).toBeNull();
      expect(navigate).not.toHaveBeenCalled();
      expect(TestBed.inject(SessionService).loggedIn()).toBe(true);
    });

    it('adv-419 L1: anahtarın TÜM denemeleri kesin reddedilince bağ düşer — aynı anahtar artık güncel kimliğe bağlanır', async () => {
      await logIn();
      const first = firstValueFrom(api.post(RECORD, {}, { islemAnahtari: KEY }));
      const sent = http.expectOne(RECORD);
      expect(header(sent)).toBe(IDENTITY_A);
      const { govde, secenek } = problem('dogrulama', 400);
      sent.flush(govde, secenek);
      await expect(first).rejects.toMatchObject({ kod: 'dogrulama' });

      await switchTo(BEN_B);
      void firstValueFrom(api.post(RECORD, {}, { islemAnahtari: KEY }));
      expect(header(http.expectOne(RECORD))).toBe(IDENTITY_B);
    });

    it('adv-419 L1: 403/404/alansız cakisma/oturum_yok-vazgeç de kesin red; her biri bağı düşürür', async () => {
      await logIn();
      relogin.request.mockResolvedValue(false);
      for (const [code, status] of [
        ['yetki_yok', 403],
        ['bulunamadi', 404],
        ['cakisma', 409],
        ['cok_istek', 429],
        ['oturum_yok', 401],
      ] as const) {
        const key = `anahtar-${code}-0123456789`;
        const attempt = firstValueFrom(api.post(RECORD, {}, { islemAnahtari: key }));
        const sent = http.expectOne(RECORD);
        expect(header(sent)).toBe(IDENTITY_A);
        const { govde, secenek } = problem(code, status);
        sent.flush(govde, secenek);
        await expect(attempt).rejects.toMatchObject({ status });
      }
      banner.kapat();
      toast.clear();
      await switchTo(BEN_B);
      for (const code of ['yetki_yok', 'bulunamadi', 'cakisma', 'cok_istek', 'oturum_yok']) {
        void firstValueFrom(api.post(RECORD, {}, { islemAnahtari: `anahtar-${code}-0123456789` }));
        expect(header(http.expectOne(RECORD))).toBe(IDENTITY_B);
      }
    });

    it('adv-419 L1: başarı, mukerrer, oturum_degisti, 5xx ve ağ hatası bağı KORUR (yazılmış ya da belirsiz)', async () => {
      await logIn();
      const outcomes: readonly [string, (r: ReturnType<typeof http.expectOne>) => void][] = [
        ['basari', (r) => r.flush({ id: 'x' })],
        [
          'mukerrer',
          (r) => r.flush(problem('mukerrer', 409).govde, problem('mukerrer', 409).secenek),
        ],
        [
          'oturum_degisti',
          (r) =>
            r.flush(problem('oturum_degisti', 409).govde, problem('oturum_degisti', 409).secenek),
        ],
        ['sunucu', (r) => r.flush(problem('sunucu', 500).govde, problem('sunucu', 500).secenek)],
        ['ag', (r) => r.error(new ProgressEvent('error'))],
      ];
      for (const [name, reply] of outcomes) {
        const attempt = firstValueFrom(
          api.post(RECORD, {}, { islemAnahtari: `anahtar-${name}-0123456789` }),
        );
        reply(http.expectOne(RECORD));
        await attempt.catch(() => undefined);
      }
      toast.clear();
      await switchTo(BEN_B);
      for (const [name] of outcomes) {
        void firstValueFrom(api.post(RECORD, {}, { islemAnahtari: `anahtar-${name}-0123456789` }));
        expect(header(http.expectOne(RECORD))).toBe(IDENTITY_A);
      }
    });

    it('adv-419 L1: belirsiz bir denemeden SONRA gelen kesin red bağı düşürmez (önceki deneme yazılmış olabilir)', async () => {
      await logIn();
      const first = firstValueFrom(api.post(RECORD, {}, { islemAnahtari: KEY }));
      http.expectOne(RECORD).error(new ProgressEvent('error'));
      await expect(first).rejects.toMatchObject({ kod: 'ag' });
      const second = firstValueFrom(api.post(RECORD, {}, { islemAnahtari: KEY }));
      const { govde, secenek } = problem('dogrulama', 400);
      http.expectOne(RECORD).flush(govde, secenek);
      await expect(second).rejects.toMatchObject({ kod: 'dogrulama' });
      // Yanıtı hiç görülmeyen (iptal) deneme de açık sayılır.
      const pending = api
        .post(RECORD, {}, { islemAnahtari: 'anahtar-iptal-0123456789' })
        .subscribe();
      http.expectOne(RECORD);
      pending.unsubscribe();
      const after = firstValueFrom(
        api.post(RECORD, {}, { islemAnahtari: 'anahtar-iptal-0123456789' }),
      );
      http
        .expectOne(RECORD)
        .flush(problem('dogrulama', 400).govde, problem('dogrulama', 400).secenek);
      await expect(after).rejects.toMatchObject({ kod: 'dogrulama' });
      toast.clear();

      await switchTo(BEN_B);
      void firstValueFrom(api.post(RECORD, {}, { islemAnahtari: KEY }));
      expect(header(http.expectOne(RECORD))).toBe(IDENTITY_A);
      void firstValueFrom(api.post(RECORD, {}, { islemAnahtari: 'anahtar-iptal-0123456789' }));
      expect(header(http.expectOne(RECORD))).toBe(IDENTITY_A);
    });

    it('çıkışta (clear) bağlar silinir: aynı sekmede sonra giren kullanıcının anahtarı kendi kimliğine bağlanır', async () => {
      await logIn();
      void firstValueFrom(api.post(RECORD, {}, { islemAnahtari: KEY }));
      expect(header(http.expectOne(RECORD))).toBe(IDENTITY_A);
      TestBed.inject(SessionService).clear();
      await switchTo(BEN_B);
      void firstValueFrom(api.post(RECORD, {}, { islemAnahtari: KEY }));
      expect(header(http.expectOne(RECORD))).toBe(IDENTITY_B);
    });
  });
});

describe('genelGosterilir (form üstünde ikinci kez gösterilmeyecek kodlar)', () => {
  it('bant/toast kodları true; forma ait kodlar false', async () => {
    const { genelGosterilir: shownGenerally } = await import('./session-interceptor');
    for (const code of [
      'yetki_yok',
      'pilot_degil',
      'mukerrer',
      'cok_istek',
      'sunucu',
      'ag',
    ] as const) {
      expect(shownGenerally({ kod: code })).toBe(true);
    }
    for (const code of ['dogrulama', 'cakisma', 'oturum_yok', 'bilinmeyen'] as const) {
      expect(shownGenerally({ kod: code })).toBe(false);
    }
  });
});
