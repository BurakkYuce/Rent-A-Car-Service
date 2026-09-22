import { HttpXsrfTokenExtractor } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideApiIstemcisi } from '@core/api/api-istemcisi';
import type { PanelDonusSatiri, PanelOzetiYaniti } from '@core/api/ui-tipleri';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { provideCeviri } from '@core/i18n/ceviri';
import { oturumInterceptor } from '@core/oturum/oturum-interceptor';
import { YenidenGirisServisi } from '@core/oturum/yeniden-giris-servisi';

import { PanelSayfasi } from './panel-sayfasi';

const OZET = '/api/ui/v1/panel/ozet';
const TAHSILAT = '/api/ui/v1/finans/tahsilat';
const HESAPLAR = '/api/ui/v1/finans/hesaplar';
const ANAHTAR = '0f9b2c1e-6a1d-5b7e-9c3a-2d4e6f8a0b1c';

function donus(no: string, ek: Partial<PanelDonusSatiri> = {}): PanelDonusSatiri {
  return {
    rentalId: `kira-${no}`,
    sozlesmeNo: no,
    tarih: '2026-09-21T09:00:00Z',
    musteriAd: 'Ayşe Yılmaz',
    plaka: '34 ABC 123',
    ofis: 'Merkez',
    bakiye: 1250.5,
    doviz: 'TRY',
    tahsilat: null,
    ...ek,
  };
}

function ozet(ek: Partial<PanelOzetiYaniti> = {}): PanelOzetiYaniti {
  const kademe = { yediGun: 0, otuzGun: 0, gecmis: 0 };
  return {
    bugun: '2026-09-22',
    kpi: {
      toplamArac: 10,
      kirada: 4,
      musait: 5,
      serviste: 1,
      acikRezervasyon: 3,
      kmGecenBakim: 0,
      gorulmeyenRezervasyon: 0,
      siteTalebi: null,
    },
    vade: {
      trafik: kademe,
      kasko: kademe,
      muayene: kademe,
      gecmisUyari: 0,
      yaklasanUyari: 0,
      acikSikayet: 0,
    },
    donusler: { gecikmis: [], bugun: [], yarin: [], varsayilanSekme: 'bugun' },
    cikislar: { gecikmis: [], bugun: [], yarin: [], varsayilanSekme: 'bugun' },
    finans: null,
    ...ek,
  };
}

const FINANS: NonNullable<PanelOzetiYaniti['finans']> = {
  kasaBakiye: 1000,
  bankaBakiye: 2000,
  acikBakiye: 300,
  bugunTahsilatTutar: 150,
  bugunTahsilatAdet: 2,
  filoDolulukYuzde: 64.2,
  revPacd: 820,
  adr: 1450,
  gelirTrendi: [
    { ayBas: '2026-08-01T00:00:00+03:00', gelir: 100000 },
    { ayBas: '2026-09-01T00:00:00+03:00', gelir: 50000 },
  ],
};

class SahteXsrf implements HttpXsrfTokenExtractor {
  getToken(): string | null {
    return 'belirtec';
  }
}

describe('PanelSayfasi', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        ...provideCeviri(),
        provideRouter([]),
        provideApiIstemcisi(oturumInterceptor),
        provideHttpClientTesting(),
        { provide: HttpXsrfTokenExtractor, useClass: SahteXsrf },
        { provide: YenidenGirisServisi, useValue: { iste: () => Promise.resolve(false) } },
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function ac(yanit: PanelOzetiYaniti) {
    const fixture = TestBed.createComponent(PanelSayfasi);
    await fixture.whenStable();
    http.expectOne(OZET).flush(yanit);
    await fixture.whenStable();
    const kok = fixture.nativeElement as HTMLElement;
    const basili = (bolum: string) =>
      kok
        .querySelector(`[aria-labelledby="${bolum}"] .cip[aria-pressed="true"]`)
        ?.getAttribute('data-sekme');
    const satirlar = (bolum: string) =>
      [...kok.querySelectorAll(`[aria-labelledby="${bolum}"] tbody tr`)].map((tr) =>
        tr.textContent?.replace(/\s+/g, ' ').trim(),
      );
    return { fixture, kok, basili, satirlar, stabil: () => fixture.whenStable() };
  }

  it('gecikmiş dönüş varsa varsayılan sekme Gecikmiş; çıkışlar yine Bugün', async () => {
    const { kok, basili, satirlar, stabil } = await ac(
      ozet({
        donusler: {
          gecikmis: [donus('2026200901001')],
          bugun: [donus('2026220901002')],
          yarin: [],
          varsayilanSekme: 'gec',
        },
        cikislar: {
          gecikmis: [
            {
              reservationId: 'r-1',
              reservationNo: 'RZ1',
              tarih: '2026-09-20T09:00:00Z',
              musteriAd: 'Gelmeyen',
              plaka: '06 XY 1',
              ofis: null,
            },
          ],
          bugun: [],
          yarin: [],
          varsayilanSekme: 'bugun',
        },
      }),
    );
    expect(basili('panel-donusler')).toBe('gec');
    expect(satirlar('panel-donusler')).toEqual([expect.stringContaining('2026200901001')]);
    // Gecikmiş çipi acil (Blazor CipSinifi), çıkışlarda da.
    expect(kok.querySelectorAll('.cip--acil')).toHaveLength(2);
    expect(basili('panel-cikislar')).toBe('bugun');
    expect(satirlar('panel-cikislar')).toEqual(['Kayıt yok.']);

    // Açık seçim varsayılanı ezer.
    kok
      .querySelector<HTMLButtonElement>('[aria-labelledby="panel-donusler"] [data-sekme="bugun"]')
      ?.click();
    await stabil();
    expect(basili('panel-donusler')).toBe('bugun');
    expect(satirlar('panel-donusler')).toEqual([expect.stringContaining('2026220901002')]);
  });

  it('finans bloğu yanıtta yoksa çizilmez; varsa özet + filo KPI + trend görünür', async () => {
    const yok = await ac(ozet());
    expect(yok.kok.querySelector('#panel-finans')).toBeNull();
    expect(yok.kok.textContent).not.toContain('Kasa bakiye');
    yok.fixture.destroy();

    const var_ = await ac(ozet({ finans: FINANS }));
    expect(var_.kok.querySelector('#panel-finans')?.textContent).toContain('Finans özeti');
    expect(var_.kok.textContent).toContain('Kasa bakiye');
    expect(var_.kok.textContent).toContain('1.000,00 ₺');
    expect(var_.kok.textContent).toContain('%64,2');
    expect(var_.kok.querySelectorAll('.trend__sutun')).toHaveLength(2);
  });

  it('KPI: toplamdan yüzde ve vade kademesi', async () => {
    const { kok } = await ac(ozet());
    const kartlar = [...kok.querySelectorAll('.kpi')].map((k) =>
      [...k.querySelectorAll('span')].map((s) => s.textContent?.trim()).filter(Boolean),
    );
    expect(kartlar[0]).toEqual(['Kiradaki araçlar', '4', 'Toplamdan %40']);
    expect(kartlar[3]).toEqual(['Açık rezervasyonlar', '3', 'Bekleyen rezervasyon']);
    expect(kok.querySelectorAll('.vade__kutu')).toHaveLength(11);
  });

  it('tahsilat anahtarı yoksa "Tahsil Et" ve İşlem sütunu yok (FinanceWrite sunucuda)', async () => {
    const { kok } = await ac(
      ozet({
        donusler: { gecikmis: [], bugun: [donus('A1')], yarin: [], varsayilanSekme: 'bugun' },
      }),
    );
    expect(kok.querySelector('th.islem')).toBeNull();
    expect(kok.textContent).not.toContain('Tahsil Et');
  });

  describe('Tahsil Et', () => {
    const bilgi = {
      anahtar: ANAHTAR,
      cariId: 'cari-1',
      rentalId: 'kira-A1',
      doviz: 'TRY',
      varsayilanTutar: 1250.5,
    };
    const yanit = () =>
      ozet({
        donusler: {
          gecikmis: [],
          bugun: [donus('A1', { tahsilat: bilgi }), donus('A2')],
          yarin: [],
          varsayilanSekme: 'bugun',
        },
      });

    async function formuAc() {
      const s = await ac(yanit());
      const dugmeler = s.kok.querySelectorAll<HTMLButtonElement>('td.islem button');
      expect(dugmeler).toHaveLength(1); // yalnız anahtarlı satırda
      dugmeler[0]?.click();
      await s.stabil();
      http.expectOne(HESAPLAR).flush([
        {
          id: 'h-1',
          etiket: 'Kasa · Merkez',
          kod: 'K1',
          ad: 'Merkez',
          tur: 'Kasa',
          doviz: null,
        },
      ]);
      await s.stabil();
      const gonder = () => {
        s.kok
          .querySelector<HTMLButtonElement>('rc-panel-tahsilat-formu button[type=submit]')
          ?.click();
        return s.stabil();
      };
      return { ...s, gonder };
    }

    it('anahtarı AYNEN gövdede gönderir; istemci anahtarı üretmez; kilit çift gönderimi keser; 2xx sonrası panel tazelenir', async () => {
      const s = await formuAc();
      expect(s.kok.querySelector('rc-panel-tahsilat-formu')?.textContent).toContain(
        'Tahsilat — 34 ABC 123 · A1',
      );
      await s.gonder();
      await s.gonder(); // uçarken ikinci tık
      const istekler = http.match(TAHSILAT);
      expect(istekler).toHaveLength(1);
      const istek = istekler[0]!;
      expect(istek.request.body).toEqual({
        cariId: 'cari-1',
        kiraId: 'kira-A1',
        tutar: '1250.50',
        hesap: 'Kasa',
        hesapId: null,
        doviz: 'TRY',
        kanal: 'Masaüstü',
        aciklama: 'Hızlı tahsilat (pano) — 34 ABC 123',
        tahsilatAnahtar: ANAHTAR,
      });
      expect(istek.request.headers.has('Idempotency-Key')).toBe(false);
      expect(
        s.kok.querySelector<HTMLButtonElement>('rc-panel-tahsilat-formu button[type=submit]')
          ?.disabled,
      ).toBe(true);

      istek.flush({ id: 'islem-1' });
      await s.stabil();
      // Yeni bakiye → yeni anahtar: panel yeniden yüklenir, form kapanır.
      http.expectOne(OZET).flush(ozet());
      await s.stabil();
      expect(s.kok.querySelector('rc-panel-tahsilat-formu')).toBeNull();
      expect(TestBed.inject(ToastServisi).toastlar()).toEqual([
        expect.objectContaining({
          durum: 'basari',
          mesaj: 'Tahsilat kaydedildi: 1.250,50 ₺ (34 ABC 123).',
        }),
      ]);
    });

    it.each([
      [
        'bayat anahtar',
        'Kiranın bakiyesi ya da kasa işlemleri bu ekran açıldıktan sonra değişti ya da tahsilat anahtarı bu kiraya ait değil; kaydı yeniden yükleyip tekrar deneyin.',
      ],
      ['gerçek çift gönderim', 'Bu işlem zaten kaydedilmiş (çift gönderim / mükerrer).'],
    ])(
      '409 mukerrer (%s): yeniden GÖNDERİLMEZ; panel yeniden yüklenir; sunucunun detail’ı nötr başlıkla (genel "Mükerrer işlem" YOK)',
      async (_, detail) => {
        const s = await formuAc();
        await s.gonder();
        http
          .expectOne(TAHSILAT)
          .flush(
            { type: 'about:blank', title: 'Mükerrer', status: 409, detail, kod: 'mukerrer' },
            { status: 409, statusText: 'Conflict' },
          );
        await s.stabil();
        http.expectOne(OZET).flush(yanit());
        await s.stabil();
        http.expectNone(TAHSILAT);
        expect(s.kok.querySelector('rc-panel-tahsilat-formu')).toBeNull();
        expect(TestBed.inject(ToastServisi).toastlar()).toEqual([
          expect.objectContaining({
            durum: 'uyari',
            baslik: 'Tahsilat gönderimi durduruldu',
            mesaj: `${detail} Panel yeniden yüklendi; güncel bakiyeyi kontrol edin.`,
          }),
        ]);
      },
    );

    it('5xx: form açık kalır, "kaydedilmemiş olabilir" uyarısı formda; yeniden gönderim yok', async () => {
      const s = await formuAc();
      await s.gonder();
      http
        .expectOne(TAHSILAT)
        .flush(
          { type: 'about:blank', title: 'Hata', status: 500, detail: 'x', kod: 'sunucu' },
          { status: 500, statusText: 'Server Error' },
        );
      await s.stabil();
      http.expectNone(TAHSILAT);
      http.expectNone(OZET);
      expect(s.kok.querySelector('rc-panel-tahsilat-formu [role=alert]')?.textContent).toContain(
        'İşlem kaydedilmemiş olabilir',
      );
    });

    it('form açıkken panel tazelenip yeni anahtar gelse de form AÇILDIĞI anahtarla gönderir (bayat → 409)', async () => {
      const s = await formuAc();
      [...s.kok.querySelectorAll<HTMLButtonElement>('.ust button')]
        .find((d) => d.textContent?.includes('Yenile'))
        ?.click();
      await s.stabil();
      const yeni = yanit();
      yeni.donusler.bugun[0] = donus('A1', {
        tahsilat: { ...bilgi, anahtar: '99999999-9999-4999-8999-999999999999' },
      });
      http.expectOne(OZET).flush(yeni);
      await s.stabil();
      expect(s.kok.querySelector('rc-panel-tahsilat-formu')).not.toBeNull();
      await s.gonder();
      const istek = http.expectOne(TAHSILAT);
      expect((istek.request.body as { tahsilatAnahtar: string }).tahsilatAnahtar).toBe(ANAHTAR);
      istek.flush({ id: 'x' });
      await s.stabil();
      http.expectOne(OZET).flush(yanit());
      await s.stabil();
    });

    it('doğrulama hatası formda kalır, değer korunur, istek tekrarlanmaz', async () => {
      const s = await formuAc();
      await s.gonder();
      http.expectOne(TAHSILAT).flush(
        {
          type: 'about:blank',
          title: 'Doğrulama',
          status: 400,
          detail: 'Geçersiz.',
          kod: 'dogrulama',
          errors: { tutar: ['Tutar pozitif olmalıdır.'] },
        },
        { status: 400, statusText: 'Bad Request' },
      );
      await s.stabil();
      http.expectNone(OZET);
      expect(s.kok.querySelector('rc-panel-tahsilat-formu')?.textContent).toContain(
        'Tutar pozitif olmalıdır.',
      );
    });
  });
});
