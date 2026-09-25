import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideApiIstemcisi } from '@core/api/api-istemcisi';
import { contextOfSession } from '@core/form/money-attempts';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { UyariBandiServisi } from '@core/geri-bildirim/uyari-bandi-servisi';
import { provideCeviri } from '@core/i18n/ceviri';
import { TEMA_ANAHTARI } from '@core/tema/tema-servisi';

import { oturumInterceptor } from './oturum-interceptor';
import { OturumServisi } from './oturum-servisi';
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
  let oturum: OturumServisi;
  const kok = document.documentElement;

  async function girisYap(ben: Ben = BEN): Promise<void> {
    const giris = oturum.girisYap({ firma: 'pilot', kullanici: 'ayse', sifre: 'test-parolasi' });
    const xsrf = http.expectOne('/api/ui/v1/oturum/xsrf');
    expect(xsrf.request.method).toBe('GET');
    xsrf.flush(null, { status: 204, statusText: 'No Content' });
    const istek = await vi.waitFor(() => http.expectOne('/api/ui/v1/oturum/giris'));
    expect(istek.request.method).toBe('POST');
    expect(istek.request.body).toEqual({
      firma: 'pilot',
      kullanici: 'ayse',
      sifre: 'test-parolasi',
    });
    istek.flush(ben);
    await giris;
  }

  beforeEach(async () => {
    localStorage.clear();
    sessionStorage.clear();
    kok.removeAttribute('style');
    TestBed.configureTestingModule({
      providers: [
        ...provideCeviri(),
        provideRouter([]),
        provideApiIstemcisi(oturumInterceptor),
        provideHttpClientTesting(),
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
    http = TestBed.inject(HttpTestingController);
    oturum = TestBed.inject(OturumServisi);
  });

  afterEach(() => http.verify());

  it('giriş: önce XSRF, sonra giris; ben, izinler, bağlam ve geçerli firma renkleri uygulanır', async () => {
    expect(oturum.baglam()).toBeNull();
    await girisYap();

    expect(oturum.girisYapildi()).toBe(true);
    expect(oturum.ben()?.kullanici.kullaniciAdi).toBe('ayse');
    expect(oturum.izinVar('OperationsWrite')).toBe(true);
    expect(oturum.izinVar('FinanceWrite')).toBe(false);
    // Düğme kapısı: izinlerin HEPSİ (grup + dar izin) gerekir.
    expect(oturum.izinlerVar(['OperationsWrite'])).toBe(true);
    expect(oturum.izinlerVar(['OperationsWrite', 'FinanceWrite'])).toBe(false);
    expect(oturum.izinlerVar([])).toBe(true);
    expect(oturum.baglam()).toEqual({ anahtar: 't-1|u-1|s-9' });
    expect(kok.style.getPropertyValue('--rc-kiraci-renk-gecikenler')).toBe('#c0392b');
    expect(kok.style.getPropertyValue('--rc-kiraci-renk-opsiyonlu')).toBe('');
    expect(kok.style.getPropertyValue('--rc-kiraci-renk-bilinmeyen-ad')).toBe('');
  });

  it('pilot olmayan firma: kalıcı "yeni arayüz açık değil" bandı', async () => {
    await girisYap({ ...BEN, pilot: false });
    expect(TestBed.inject(UyariBandiServisi).bant()).toMatchObject({
      kod: 'pilot_degil',
      kalici: true,
    });
  });

  it('giriş hatası ApiHatasi olarak çağırana gider; oturum açılmaz', async () => {
    const giris = oturum.girisYap({ firma: 'pilot', kullanici: 'ayse', sifre: 'yanlis-parola' });
    http.expectOne('/api/ui/v1/oturum/xsrf').flush(null, { status: 204, statusText: 'No Content' });
    (await vi.waitFor(() => http.expectOne('/api/ui/v1/oturum/giris'))).flush(
      { status: 400, kod: 'dogrulama', detail: 'Firma kodu, kullanıcı adı ya da şifre hatalı.' },
      { status: 400, statusText: 'Bad Request' },
    );
    await expect(giris).rejects.toMatchObject({ kod: 'dogrulama' });
    expect(oturum.girisYapildi()).toBe(false);
    expect(TestBed.inject(ToastServisi).toastlar()).toEqual([]); // sessiz: mesajı form seçer
  });

  it('#330 L4: açık oturumda ben yenilemesi ağ/sunucu hatası alırsa oturum KORUNUR (bağlam değişmez); 401 kapatır', async () => {
    await girisYap();
    const baglam = oturum.baglam();
    expect(baglam).not.toBeNull();

    const ag = oturum.yukle();
    http.expectOne('/api/ui/v1/oturum/ben').error(new ProgressEvent('error'));
    await expect(ag).resolves.toEqual(BEN);
    expect(oturum.baglam()).toBe(baglam);
    expect(oturum.girisYapildi()).toBe(true);

    const sunucu = oturum.yukle();
    http
      .expectOne('/api/ui/v1/oturum/ben')
      .flush({ status: 500 }, { status: 500, statusText: 'Internal Server Error' });
    await expect(sunucu).resolves.toEqual(BEN);
    expect(oturum.baglam()).toBe(baglam);
    expect(TestBed.inject(ToastServisi).toastlar().length).toBeGreaterThan(0);

    const kapali = oturum.yukle();
    http
      .expectOne('/api/ui/v1/oturum/ben')
      .flush({ status: 401, kod: 'oturum_yok' }, { status: 401, statusText: 'Unauthorized' });
    await expect(kapali).resolves.toBeNull();
    expect(oturum.baglam()).toBeNull();
  });

  it('ilk yükleme: 401 → oturum yok (toast yok); sonuç önbelleklenir', async () => {
    const ilk = oturum.ilkYukleme();
    http
      .expectOne('/api/ui/v1/oturum/ben')
      .flush({ status: 401, kod: 'oturum_yok' }, { status: 401, statusText: 'Unauthorized' });
    await expect(ilk).resolves.toBeNull();
    await expect(oturum.ilkYukleme()).resolves.toBeNull();
    http.expectNone('/api/ui/v1/oturum/ben');
    expect(TestBed.inject(ToastServisi).toastlar()).toEqual([]);
  });

  it('çıkış: cikis çağrılır, rc.* depo anahtarları (tema hariç) ve kayıtlı önbellekler temizlenir, girişe gidilir', async () => {
    await girisYap();
    localStorage.setItem(TEMA_ANAHTARI, 'koyu');
    localStorage.setItem('rc.sekmeler', '[{"rota":"/kiralar/5"}]');
    localStorage.setItem('blazor.ayar', 'dokunma');
    sessionStorage.setItem('rc.taslak', 'x');
    const temizleyici = vi.fn();
    oturum.temizlikKaydet(temizleyici);
    TestBed.inject(ToastServisi).bilgi('eski bildirim');
    const router = TestBed.inject(Router);
    const gez = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    const cikis = oturum.cikisYap();
    const istek = http.expectOne('/api/ui/v1/oturum/cikis');
    expect(istek.request.method).toBe('POST');
    istek.flush(null, { status: 204, statusText: 'No Content' });
    await cikis;

    expect(oturum.ben()).toBeNull();
    expect(oturum.baglam()).toBeNull();
    expect(temizleyici).toHaveBeenCalledTimes(1);
    expect(localStorage.getItem(TEMA_ANAHTARI)).toBe('koyu');
    expect(localStorage.getItem('rc.sekmeler')).toBeNull();
    expect(localStorage.getItem('blazor.ayar')).toBe('dokunma');
    expect(sessionStorage.getItem('rc.taslak')).toBeNull();
    expect(kok.style.getPropertyValue('--rc-kiraci-renk-gecikenler')).toBe('');
    expect(TestBed.inject(ToastServisi).toastlar()).toEqual([]);
    expect(gez).toHaveBeenCalledWith(['/giris'], {
      queryParams: { neden: 'cikis' },
      replaceUrl: true,
    });
  });

  it('çıkış isteği başarısız olsa da yerel temizlik yapılır', async () => {
    await girisYap();
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const cikis = oturum.cikisYap();
    http.expectOne('/api/ui/v1/oturum/cikis').error(new ProgressEvent('error'));
    await cikis;
    expect(oturum.girisYapildi()).toBe(false);
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
    for (const [ad, ben, beklenen] of fixtures) {
      it(`${ad}: baglam ile para denemesi bağlamı aynı`, async () => {
        await girisYap(ben);
        expect(oturum.baglam()?.anahtar).toBe(beklenen);
        expect(contextOfSession(ben)).toBe(beklenen);
        expect(oturum.baglam()?.anahtar).toBe(contextOfSession(ben));
      });
    }
  });
});
