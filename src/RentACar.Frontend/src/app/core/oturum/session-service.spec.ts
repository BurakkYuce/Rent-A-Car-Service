import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideApiClient } from '@core/api/api-istemcisi';
import { contextOfSession } from '@core/form/money-attempts';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { WarningBannerService } from '@core/geri-bildirim/warning-banner-service';
import { provideTranslation } from '@core/i18n/ceviri';
import { THEME_KEY } from '@core/tema/tema-servisi';

import { sessionInterceptor } from './session-interceptor';
import { SessionService } from './session-service';
import type { Ben } from './oturum-tipleri';

const BEN: Ben = {
  kullanici: { id: 'u-1', kullaniciAdi: 'ayse', adSoyad: 'Ayşe Yılmaz' },
  kiraci: { id: 't-1', kod: 'pilot', ad: 'Pilot Firma' },
  rol: 'Operator',
  izinler: ['OperationsWrite', 'ViewReports'],
  subeKapsami: { tumSubeler: false, subeId: 's-9', subeAd: 'Merkez' },
  moduller: { webSitesi: false },
  renkler: { gecikenler: '#C0392B', 'bilinmeyen-ad': '#000000', opsiyonlu: 'kırmızı' },
  pilot: true,
};

describe('OturumServisi', () => {
  let http: HttpTestingController;
  let session: SessionService;
  const root = document.documentElement;

  async function login(ben: Ben = BEN): Promise<void> {
    const entry = session.login({ firma: 'pilot', kullanici: 'ayse', sifre: 'test-parolasi' });
    const xsrf = http.expectOne('/api/ui/v1/oturum/xsrf');
    expect(xsrf.request.method).toBe('GET');
    xsrf.flush(null, { status: 204, statusText: 'No Content' });
    const request = await vi.waitFor(() => http.expectOne('/api/ui/v1/oturum/giris'));
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      firma: 'pilot',
      kullanici: 'ayse',
      sifre: 'test-parolasi',
    });
    request.flush(ben);
    await entry;
  }

  beforeEach(async () => {
    localStorage.clear();
    sessionStorage.clear();
    root.removeAttribute('style');
    TestBed.configureTestingModule({
      providers: [
        ...provideTranslation(),
        provideRouter([]),
        provideApiClient(sessionInterceptor),
        provideHttpClientTesting(),
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
    http = TestBed.inject(HttpTestingController);
    session = TestBed.inject(SessionService);
  });

  afterEach(() => http.verify());

  it('giriş: önce XSRF, sonra giris; ben, izinler, bağlam ve geçerli firma renkleri uygulanır', async () => {
    expect(session.context()).toBeNull();
    await login();

    expect(session.loggedIn()).toBe(true);
    expect(session.ben()?.kullanici.kullaniciAdi).toBe('ayse');
    expect(session.izinVar('OperationsWrite')).toBe(true);
    expect(session.izinVar('FinanceWrite')).toBe(false);
    // Düğme kapısı: izinlerin HEPSİ (grup + dar izin) gerekir.
    expect(session.hasPermissions(['OperationsWrite'])).toBe(true);
    expect(session.hasPermissions(['OperationsWrite', 'FinanceWrite'])).toBe(false);
    expect(session.hasPermissions([])).toBe(true);
    expect(session.context()).toEqual({ anahtar: 't-1|u-1|s-9' });
    expect(root.style.getPropertyValue('--rc-kiraci-renk-gecikenler')).toBe('#c0392b');
    expect(root.style.getPropertyValue('--rc-kiraci-renk-opsiyonlu')).toBe('');
    expect(root.style.getPropertyValue('--rc-kiraci-renk-bilinmeyen-ad')).toBe('');
  });

  it('F13 sonrası: pilot bayrağı band üretmez (pilot kapısı kalktı)', async () => {
    await login({ ...BEN, pilot: false });
    expect(TestBed.inject(WarningBannerService).bant()).toBeNull();
  });

  it('giriş hatası ApiHatasi olarak çağırana gider; oturum açılmaz', async () => {
    const entry = session.login({ firma: 'pilot', kullanici: 'ayse', sifre: 'yanlis-parola' });
    http.expectOne('/api/ui/v1/oturum/xsrf').flush(null, { status: 204, statusText: 'No Content' });
    (await vi.waitFor(() => http.expectOne('/api/ui/v1/oturum/giris'))).flush(
      { status: 400, kod: 'dogrulama', detail: 'Firma kodu, kullanıcı adı ya da şifre hatalı.' },
      { status: 400, statusText: 'Bad Request' },
    );
    await expect(entry).rejects.toMatchObject({ kod: 'dogrulama' });
    expect(session.loggedIn()).toBe(false);
    expect(TestBed.inject(ToastService).toasts()).toEqual([]); // sessiz: mesajı form seçer
  });

  it('#330 L4: açık oturumda ben yenilemesi ağ/sunucu hatası alırsa oturum KORUNUR (bağlam değişmez); 401 kapatır', async () => {
    await login();
    const context = session.context();
    expect(context).not.toBeNull();

    const ag = session.yukle();
    http.expectOne('/api/ui/v1/oturum/ben').error(new ProgressEvent('error'));
    await expect(ag).resolves.toBeNull(); // dönen değer taze değil → null; sinyal korunur
    expect(session.context()).toBe(context);
    expect(session.loggedIn()).toBe(true);

    const server = session.yukle();
    http
      .expectOne('/api/ui/v1/oturum/ben')
      .flush({ status: 500 }, { status: 500, statusText: 'Internal Server Error' });
    await expect(server).resolves.toBeNull();
    expect(session.context()).toBe(context);
    expect(TestBed.inject(ToastService).toasts().length).toBeGreaterThan(0);

    const closed = session.yukle();
    http
      .expectOne('/api/ui/v1/oturum/ben')
      .flush({ status: 401, kod: 'oturum_yok' }, { status: 401, statusText: 'Unauthorized' });
    await expect(closed).resolves.toBeNull();
    expect(session.context()).toBeNull();
  });

  it('ilk yükleme: 401 → oturum yok (toast yok); sonuç önbelleklenir', async () => {
    const first = session.initialLoad();
    http
      .expectOne('/api/ui/v1/oturum/ben')
      .flush({ status: 401, kod: 'oturum_yok' }, { status: 401, statusText: 'Unauthorized' });
    await expect(first).resolves.toBeNull();
    await expect(session.initialLoad()).resolves.toBeNull();
    http.expectNone('/api/ui/v1/oturum/ben');
    expect(TestBed.inject(ToastService).toasts()).toEqual([]);
  });

  it('çıkış: cikis çağrılır, rc.* depo anahtarları (tema hariç) ve kayıtlı önbellekler temizlenir, girişe gidilir', async () => {
    await login();
    localStorage.setItem(THEME_KEY, 'koyu');
    localStorage.setItem('rc.sekmeler', '[{"rota":"/kiralar/5"}]');
    localStorage.setItem('blazor.ayar', 'dokunma');
    sessionStorage.setItem('rc.taslak', 'x');
    const cleaner = vi.fn();
    session.registerCleanup(cleaner);
    TestBed.inject(ToastService).bilgi('eski bildirim');
    const router = TestBed.inject(Router);
    const gez = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    const pickup = session.logout();
    const request = http.expectOne('/api/ui/v1/oturum/cikis');
    expect(request.request.method).toBe('POST');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await pickup;

    expect(session.ben()).toBeNull();
    expect(session.context()).toBeNull();
    expect(cleaner).toHaveBeenCalledTimes(1);
    expect(localStorage.getItem(THEME_KEY)).toBe('koyu');
    expect(localStorage.getItem('rc.sekmeler')).toBeNull();
    expect(localStorage.getItem('blazor.ayar')).toBe('dokunma');
    expect(sessionStorage.getItem('rc.taslak')).toBeNull();
    expect(root.style.getPropertyValue('--rc-kiraci-renk-gecikenler')).toBe('');
    expect(TestBed.inject(ToastService).toasts()).toEqual([]);
    expect(gez).toHaveBeenCalledWith(['/giris'], {
      queryParams: { neden: 'cikis' },
      replaceUrl: true,
    });
  });

  it('çıkış isteği başarısız olsa da yerel temizlik yapılır', async () => {
    await login();
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const pickup = session.logout();
    http.expectOne('/api/ui/v1/oturum/cikis').error(new ProgressEvent('error'));
    await pickup;
    expect(session.loggedIn()).toBe(false);
  });

  // Güvenlik kilidi: para denemeleri `contextOfSession` ile yazılır, bağlam `baglam` ile okunur. İkisi farklı biçim
  // üretirse deneme ya hiç okunmaz ya da başka kullanıcıya geçişte düşmez. Beklenen anahtarlar ELLE yazıldı.
  describe('bağlam anahtarı paritesi (contextOfSession)', () => {
    const fixtures: readonly [string, Ben, string][] = [
      [
        'tüm şubeler',
        { ...BEN, subeKapsami: { tumSubeler: true, subeId: null, subeAd: null } },
        't-1|u-1|*',
      ],
      ['şubeli', BEN, 't-1|u-1|s-9'],
      [
        'şubesiz',
        { ...BEN, subeKapsami: { tumSubeler: false, subeId: null, subeAd: null } },
        't-1|u-1|-',
      ],
    ];
    for (const [name, ben, expected] of fixtures) {
      it(`${name}: baglam ile para denemesi bağlamı aynı`, async () => {
        await login(ben);
        expect(session.context()?.anahtar).toBe(expected);
        expect(contextOfSession(ben)).toBe(expected);
        expect(session.context()?.anahtar).toBe(contextOfSession(ben));
      });
    }
  });
});
