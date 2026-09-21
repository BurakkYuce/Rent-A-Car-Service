import { HttpClient, HttpXsrfTokenExtractor } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { ApiHatasi } from '@core/api/api-hatasi';
import { ApiIstemcisi, provideApiIstemcisi } from '@core/api/api-istemcisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { UyariBandiServisi } from '@core/geri-bildirim/uyari-bandi-servisi';
import { provideCeviri } from '@core/i18n/ceviri';

import { istekBaglami } from './istek-baglami';
import { oturumInterceptor } from './oturum-interceptor';
import { OturumServisi } from './oturum-servisi';
import type { Ben } from './oturum-tipleri';
import { YenidenGirisServisi } from './yeniden-giris-servisi';

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

const KAYIT = '/api/ui/v1/vitrin/kayit';

function problem(kod: string, status: number, ek: Record<string, unknown> = {}) {
  return {
    govde: { type: 'about:blank', title: 'Başlık', status, detail: `${kod} ayrıntısı`, kod, ...ek },
    secenek: { status, statusText: 'Hata' },
  };
}

/** XSRF çerez okuyucusu: testte çerez yerine değişken. */
let xsrfBelirteci: string | null = 'eski-belirtec';
class SahteXsrf implements HttpXsrfTokenExtractor {
  getToken(): string | null {
    return xsrfBelirteci;
  }
}

describe('oturumInterceptor (kod bazlı)', () => {
  let http: HttpTestingController;
  let api: ApiIstemcisi;
  let toast: ToastServisi;
  let bant: UyariBandiServisi;
  const yenidenGiris = { iste: vi.fn<() => Promise<boolean>>() };

  async function oturumAc(): Promise<void> {
    const oturum = TestBed.inject(OturumServisi);
    const yukleme = oturum.yukle();
    http.expectOne('/api/ui/v1/oturum/ben').flush(BEN);
    await yukleme;
    expect(oturum.girisYapildi()).toBe(true);
  }

  const bekle = (url: string) => vi.waitFor(() => http.expectOne(url));

  beforeEach(async () => {
    xsrfBelirteci = 'eski-belirtec';
    yenidenGiris.iste.mockReset();
    TestBed.configureTestingModule({
      providers: [
        ...provideCeviri(),
        provideRouter([]),
        provideApiIstemcisi(oturumInterceptor),
        provideHttpClientTesting(),
        { provide: HttpXsrfTokenExtractor, useClass: SahteXsrf },
        { provide: YenidenGirisServisi, useValue: yenidenGiris },
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
    http = TestBed.inject(HttpTestingController);
    api = TestBed.inject(ApiIstemcisi);
    toast = TestBed.inject(ToastServisi);
    bant = TestBed.inject(UyariBandiServisi);
  });

  afterEach(() => {
    http.verify();
    toast.temizle();
  });

  it('oturum_yok → yerinde giriş, AYNI istek (gövde + anahtar) TAZE XSRF ile tekrarlanır, sonuç çağırana', async () => {
    await oturumAc();
    yenidenGiris.iste.mockImplementation(async () => {
      xsrfBelirteci = 'yeni-belirtec'; // giriş yeni kimliğe bağlı belirteç verdi
      return true;
    });
    const govde = { plaka: '34 ABC 123', aciklama: 'Form verisi' };
    const sonuc = firstValueFrom(
      api.post<{ id: string }>(KAYIT, govde, { islemAnahtari: 'anahtar-0123456789abcdef' }),
    );

    const ilk = http.expectOne(KAYIT);
    expect(ilk.request.headers.get('X-XSRF-TOKEN')).toBe('eski-belirtec');
    const { govde: p, secenek } = problem('oturum_yok', 401);
    ilk.flush(p, secenek);

    const tekrar = await bekle(KAYIT);
    expect(tekrar.request.method).toBe('POST');
    expect(tekrar.request.body).toEqual(govde);
    expect(tekrar.request.headers.get('Idempotency-Key')).toBe('anahtar-0123456789abcdef');
    expect(tekrar.request.headers.get('X-XSRF-TOKEN')).toBe('yeni-belirtec');
    expect(tekrar.request.withCredentials).toBe(true);
    tekrar.flush({ id: 'kayit-1' });

    await expect(sonuc).resolves.toEqual({ id: 'kayit-1' });
    expect(yenidenGiris.iste).toHaveBeenCalledTimes(1);
    expect(bant.bant()).toBeNull();
  });

  it('oturum_yok + vazgeç → tekrar YOK, çağıran oturum_yok alır (form yerinde kalır)', async () => {
    await oturumAc();
    yenidenGiris.iste.mockResolvedValue(false);
    const sonuc = firstValueFrom(api.post(KAYIT, { plaka: 'x' }));
    const { govde, secenek } = problem('oturum_yok', 401);
    http.expectOne(KAYIT).flush(govde, secenek);

    await expect(sonuc).rejects.toMatchObject({ kod: 'oturum_yok', status: 401 });
    expect(yenidenGiris.iste).toHaveBeenCalledTimes(1);
  });

  it('aynı anda düşen iki istek TEK diyalog paylaşır, ikisi de tekrarlanır', async () => {
    await oturumAc();
    let coz: (girildi: boolean) => void = () => undefined;
    const diyalog = new Promise<boolean>((r) => (coz = r));
    yenidenGiris.iste.mockReturnValue(diyalog);

    const a = firstValueFrom(api.post<{ id: string }>(KAYIT, { n: 1 }));
    const b = firstValueFrom(api.get<{ liste: number[] }>('/api/ui/v1/menu'));
    const { govde, secenek } = problem('oturum_yok', 401);
    http.expectOne(KAYIT).flush(govde, secenek);
    http.expectOne('/api/ui/v1/menu').flush(govde, secenek);
    coz(true);

    (await bekle(KAYIT)).flush({ id: 'a' });
    (await bekle('/api/ui/v1/menu')).flush({ liste: [1] });
    await expect(a).resolves.toEqual({ id: 'a' });
    await expect(b).resolves.toEqual({ liste: [1] });
    // Servis tek sözü paylaştırır; interceptor her istek için sorar.
    expect(yenidenGiris.iste).toHaveBeenCalledTimes(2);
  });

  it('tekrarlanan istek yine oturum_yok alırsa ikinci diyalog açılmaz (döngü yok)', async () => {
    await oturumAc();
    yenidenGiris.iste.mockResolvedValue(true);
    const sonuc = firstValueFrom(api.post(KAYIT, {}));
    const { govde, secenek } = problem('oturum_yok', 401);
    http.expectOne(KAYIT).flush(govde, secenek);
    (await bekle(KAYIT)).flush(govde, secenek);

    await expect(sonuc).rejects.toMatchObject({ kod: 'oturum_yok' });
    expect(yenidenGiris.iste).toHaveBeenCalledTimes(1);
  });

  it('oturum hiç yokken (açılış/giriş sayfası) ya da yenidenGirisYok bayrağında diyalog açılmaz', async () => {
    const { govde, secenek } = problem('oturum_yok', 401);
    const anonim = firstValueFrom(api.get('/api/ui/v1/menu'));
    http.expectOne('/api/ui/v1/menu').flush(govde, secenek);
    await expect(anonim).rejects.toMatchObject({ kod: 'oturum_yok' });

    await oturumAc();
    const bayrakli = firstValueFrom(
      api.get('/api/ui/v1/menu', { context: istekBaglami({ yenidenGirisYok: true }) }),
    );
    http.expectOne('/api/ui/v1/menu').flush(govde, secenek);
    await expect(bayrakli).rejects.toMatchObject({ kod: 'oturum_yok' });
    expect(yenidenGiris.iste).not.toHaveBeenCalled();
  });

  it('xsrf_gecersiz → GET oturum/xsrf ile yenilenir ve istek BİR kez taze başlıkla tekrarlanır', async () => {
    const sonuc = firstValueFrom(api.post<{ id: string }>(KAYIT, { a: 1 }));
    const { govde, secenek } = problem('xsrf_gecersiz', 400);
    http.expectOne(KAYIT).flush(govde, secenek);

    const xsrf = await bekle('/api/ui/v1/oturum/xsrf');
    expect(xsrf.request.method).toBe('GET');
    xsrfBelirteci = 'tazelenmis';
    xsrf.flush(null, { status: 204, statusText: 'No Content' });

    const tekrar = await bekle(KAYIT);
    expect(tekrar.request.headers.get('X-XSRF-TOKEN')).toBe('tazelenmis');
    expect(tekrar.request.body).toEqual({ a: 1 });
    tekrar.flush({ id: 'x' });
    await expect(sonuc).resolves.toEqual({ id: 'x' });
  });

  it('xsrf_gecersiz ikinci kez gelirse bir daha denenmez, çağırana gider', async () => {
    const sonuc = firstValueFrom(api.post(KAYIT, {}));
    const { govde, secenek } = problem('xsrf_gecersiz', 400);
    http.expectOne(KAYIT).flush(govde, secenek);
    (await bekle('/api/ui/v1/oturum/xsrf')).flush(null, { status: 204, statusText: 'No Content' });
    (await bekle(KAYIT)).flush(govde, secenek);

    await expect(sonuc).rejects.toMatchObject({ kod: 'xsrf_gecersiz' });
  });

  it('mukerrer → yeni anahtarla TEKRAR GÖNDERİLMEZ; kayıt yenileme çağrılır + bilgi toast’u', async () => {
    const yenile = vi.fn();
    const sonuc = firstValueFrom(
      api.post(
        KAYIT,
        { tutar: 100 },
        {
          islemAnahtari: 'anahtar-0123456789abcdef',
          context: istekBaglami({ mukerrerdeYenile: yenile }),
        },
      ),
    );
    const { govde, secenek } = problem('mukerrer', 409, {
      detail: 'Bu işlem farklı içerikle zaten kaydedilmiş; kaydı kontrol edin.',
    });
    http.expectOne(KAYIT).flush(govde, secenek);

    await expect(sonuc).rejects.toBeInstanceOf(ApiHatasi);
    await expect(sonuc).rejects.toMatchObject({ kod: 'mukerrer', status: 409 });
    http.expectNone(KAYIT); // ikinci gönderim yok
    expect(yenile).toHaveBeenCalledTimes(1);
    expect(toast.toastlar()).toEqual([
      expect.objectContaining({
        durum: 'bilgi',
        baslik: 'Mükerrer işlem',
        mesaj:
          'Bu işlem farklı içerikle zaten kaydedilmiş; kaydı kontrol edin. Kayıt yeniden yüklendi.',
      }),
    ]);
  });

  it('dogrulama → yalnız çağırana, alan hatalarıyla; bant/toast yok', async () => {
    const sonuc = firstValueFrom(api.post(KAYIT, {}));
    const { govde, secenek } = problem('dogrulama', 400, {
      errors: { Plaka: ['Plaka zorunludur.'] },
    });
    http.expectOne(KAYIT).flush(govde, secenek);

    await expect(sonuc).rejects.toMatchObject({
      kod: 'dogrulama',
      alanlar: { Plaka: ['Plaka zorunludur.'] },
    });
    expect(bant.bant()).toBeNull();
    expect(toast.toastlar()).toEqual([]);
  });

  it('cakisma alanlıysa form hatası (bant yok); alansızsa uyarı bandı', async () => {
    const alanli = firstValueFrom(api.post(KAYIT, {}));
    const a = problem('cakisma', 409, { errors: { Plaka: ['Plaka kayıtlı.'] } });
    http.expectOne(KAYIT).flush(a.govde, a.secenek);
    await expect(alanli).rejects.toMatchObject({ kod: 'cakisma' });
    expect(bant.bant()).toBeNull();

    const alansiz = firstValueFrom(api.post(KAYIT, {}));
    const b = problem('cakisma', 409, { detail: 'Araç bu tarihlerde müsait değil.' });
    http.expectOne(KAYIT).flush(b.govde, b.secenek);
    await expect(alansiz).rejects.toMatchObject({ kod: 'cakisma' });
    expect(bant.bant()).toEqual({
      tur: 'uyari',
      mesaj: 'Araç bu tarihlerde müsait değil.',
      kod: 'cakisma',
    });
    http.expectNone(KAYIT);
  });

  it.each(['yetki_yok', 'pilot_degil'])('%s → uyarı bandı (form hatası değil)', async (kod) => {
    const sonuc = firstValueFrom(api.get('/api/ui/v1/menu'));
    const { govde, secenek } = problem(kod, 403);
    http.expectOne('/api/ui/v1/menu').flush(govde, secenek);
    await expect(sonuc).rejects.toMatchObject({ kod });
    expect(bant.bant()).toMatchObject({ tur: 'uyari', kod, mesaj: `${kod} ayrıntısı` });
  });

  it('kiraci_kapali → tam temizlik + mesajlı giriş sayfası', async () => {
    const kapandi = vi.spyOn(TestBed.inject(OturumServisi), 'kiraciKapandi').mockResolvedValue();
    const sonuc = firstValueFrom(api.get('/api/ui/v1/menu'));
    const { govde, secenek } = problem('kiraci_kapali', 401);
    http.expectOne('/api/ui/v1/menu').flush(govde, secenek);
    await expect(sonuc).rejects.toMatchObject({ kod: 'kiraci_kapali' });
    expect(kapandi).toHaveBeenCalledTimes(1);
    expect(yenidenGiris.iste).not.toHaveBeenCalled();
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

    const c = firstValueFrom(api.post(KAYIT, {}));
    const { govde, secenek } = problem('cok_istek', 429);
    http.expectOne(KAYIT).flush(govde, secenek);
    await expect(c).rejects.toMatchObject({ kod: 'cok_istek' });

    expect(toast.toastlar().map((t) => t.durum)).toEqual(['hata', 'hata', 'uyari']);

    toast.temizle();
    const sessiz = firstValueFrom(
      api.get('/api/ui/v1/menu', { context: istekBaglami({ sessiz: true }) }),
    );
    http
      .expectOne('/api/ui/v1/menu')
      .flush({ title: 'Sunucu hatası', status: 500 }, { status: 500, statusText: 'Hata' });
    await expect(sessiz).rejects.toMatchObject({ kod: 'sunucu' });
    expect(toast.toastlar()).toEqual([]);
  });

  it('başarılı yanıta ve /api/ui dışındaki isteklere dokunmaz', async () => {
    const tamam = firstValueFrom(api.get<{ ok: boolean }>('/api/ui/v1/menu'));
    http.expectOne('/api/ui/v1/menu').flush({ ok: true });
    await expect(tamam).resolves.toEqual({ ok: true });

    const baska = firstValueFrom(TestBed.inject(HttpClient).get('/baska/uc'));
    http.expectOne('/baska/uc').flush('x', { status: 500, statusText: 'Hata' });
    await expect(baska).rejects.toMatchObject({ status: 500 });
    expect(toast.toastlar()).toEqual([]);
    expect(bant.bant()).toBeNull();
  });
});

describe('genelGosterilir (form üstünde ikinci kez gösterilmeyecek kodlar)', () => {
  it('bant/toast kodları true; forma ait kodlar false', async () => {
    const { genelGosterilir } = await import('./oturum-interceptor');
    for (const kod of [
      'yetki_yok',
      'pilot_degil',
      'mukerrer',
      'cok_istek',
      'sunucu',
      'ag',
    ] as const) {
      expect(genelGosterilir({ kod })).toBe(true);
    }
    for (const kod of ['dogrulama', 'cakisma', 'oturum_yok', 'bilinmeyen'] as const) {
      expect(genelGosterilir({ kod })).toBe(false);
    }
  });
});
