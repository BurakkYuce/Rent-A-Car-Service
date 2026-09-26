import { HttpXsrfTokenExtractor } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideApiClient } from '@core/api/api-istemcisi';
import type { PanelReturnRow, PanelSummaryResponse } from '@core/api/ui-tipleri';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { provideTranslation } from '@core/i18n/ceviri';
import { sessionInterceptor } from '@core/oturum/session-interceptor';
import { ReloginService } from '@core/oturum/relogin-service';

import { PanelPage } from './panel-page';

const SUMMARY = '/api/ui/v1/panel/ozet';
const COLLECTION = '/api/ui/v1/finans/tahsilat';
const ACCOUNTS = '/api/ui/v1/finans/hesaplar';
const KEY = '0f9b2c1e-6a1d-5b7e-9c3a-2d4e6f8a0b1c';

function donus(no: string, extra: Partial<PanelReturnRow> = {}): PanelReturnRow {
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
    ...extra,
  };
}

function ozet(extra: Partial<PanelSummaryResponse> = {}): PanelSummaryResponse {
  const tier = { yediGun: 0, otuzGun: 0, gecmis: 0 };
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
      trafik: tier,
      kasko: tier,
      muayene: tier,
      gecmisUyari: 0,
      yaklasanUyari: 0,
      acikSikayet: 0,
    },
    donusler: { gecikmis: [], bugun: [], yarin: [], varsayilanSekme: 'bugun' },
    cikislar: { gecikmis: [], bugun: [], yarin: [], varsayilanSekme: 'bugun' },
    finans: null,
    ...extra,
  };
}

const FINANCE: NonNullable<PanelSummaryResponse['finans']> = {
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

class FakeXsrf implements HttpXsrfTokenExtractor {
  getToken(): string | null {
    return 'belirtec';
  }
}

describe('PanelSayfasi', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        ...provideTranslation(),
        provideRouter([]),
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

  async function open(response: PanelSummaryResponse) {
    const fixture = TestBed.createComponent(PanelPage);
    await fixture.whenStable();
    http.expectOne(SUMMARY).flush(response);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const pressed = (section: string) =>
      root
        .querySelector(`[aria-labelledby="${section}"] .cip[aria-pressed="true"]`)
        ?.getAttribute('data-sekme');
    const rows = (section: string) =>
      [...root.querySelectorAll(`[aria-labelledby="${section}"] tbody tr`)].map((tr) =>
        tr.textContent?.replace(/\s+/g, ' ').trim(),
      );
    return {
      fixture,
      kok: root,
      basili: pressed,
      satirlar: rows,
      stabil: () => fixture.whenStable(),
    };
  }

  it('gecikmiş dönüş varsa varsayılan sekme Gecikmiş; çıkışlar yine Bugün', async () => {
    const {
      kok,
      basili,
      satirlar,
      stabil: stable,
    } = await open(
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
    await stable();
    expect(basili('panel-donusler')).toBe('bugun');
    expect(satirlar('panel-donusler')).toEqual([expect.stringContaining('2026220901002')]);
  });

  it('finans bloğu yanıtta yoksa çizilmez; varsa özet + filo KPI + trend görünür', async () => {
    const none = await open(ozet());
    expect(none.kok.querySelector('#panel-finans')).toBeNull();
    expect(none.kok.textContent).not.toContain('Kasa bakiye');
    none.fixture.destroy();

    const var_ = await open(ozet({ finans: FINANCE }));
    expect(var_.kok.querySelector('#panel-finans')?.textContent).toContain('Finans özeti');
    expect(var_.kok.textContent).toContain('Kasa bakiye');
    expect(var_.kok.textContent).toContain('1.000,00 ₺');
    expect(var_.kok.textContent).toContain('%64,2');
    expect(var_.kok.querySelectorAll('.trend__sutun')).toHaveLength(2);
  });

  it('KPI: tabela kartları (toplamdan yüzde), hatırlatma matrisi ve rozet satırları', async () => {
    const { kok } = await open(ozet());
    const cards = [...kok.querySelectorAll('rc-tabela-karti')].map((k) =>
      [...k.querySelectorAll('p')].map((s) => s.textContent?.trim()).filter(Boolean),
    );
    expect(cards[0]).toEqual(['Kiradaki araçlar', '4', 'Toplamdan %40']);
    expect(cards[3]).toEqual(['Açık rezervasyonlar', '3', 'Bekleyen rezervasyon']);
    expect(kok.querySelector('rc-tabela-karti')?.getAttribute('data-durum')).toBe('kirada');
    expect(kok.querySelectorAll('rc-hatirlatma-listesi tbody tr')).toHaveLength(3);
    expect(kok.querySelectorAll('rc-hatirlatma-listesi .rozet-satiri')).toHaveLength(2);
    // Band: sayfanın tek h1'i; alt metin sunucunun günü (saat dilimi kaydırmadan) + toplam araç.
    expect(kok.querySelectorAll('h1')).toHaveLength(1);
    expect(kok.querySelector('.bant__alt')?.textContent?.trim()).toBe(
      'Salı, 22 Eylül 2026 · 10 araç',
    );
    // Oturum yok (izin yok) → hızlı işlemler bölümü çizilmez.
    expect(kok.querySelector('rc-hizli-islemler')).toBeNull();
  });

  it('tahsilat anahtarı yoksa "Tahsil Et" ve İşlem sütunu yok (FinanceWrite sunucuda)', async () => {
    const { kok } = await open(
      ozet({
        donusler: { gecikmis: [], bugun: [donus('A1')], yarin: [], varsayilanSekme: 'bugun' },
      }),
    );
    expect(kok.querySelector('th.islem')).toBeNull();
    expect(kok.textContent).not.toContain('Tahsil et');
  });

  describe('Tahsil Et', () => {
    const info = {
      anahtar: KEY,
      cariId: 'cari-1',
      rentalId: 'kira-A1',
      doviz: 'TRY',
      varsayilanTutar: 1250.5,
    };
    const response = () =>
      ozet({
        donusler: {
          gecikmis: [],
          bugun: [donus('A1', { tahsilat: info }), donus('A2')],
          yarin: [],
          varsayilanSekme: 'bugun',
        },
      });

    async function openForm() {
      const s = await open(response());
      const buttons = s.kok.querySelectorAll<HTMLButtonElement>('td.islem button');
      expect(buttons).toHaveLength(1); // yalnız anahtarlı satırda
      buttons[0]?.click();
      await s.stabil();
      http.expectOne(ACCOUNTS).flush([
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
      const s = await openForm();
      expect(s.kok.querySelector('rc-panel-tahsilat-formu')?.textContent).toContain(
        'Tahsilat — 34 ABC 123 · A1',
      );
      await s.gonder();
      await s.gonder(); // uçarken ikinci tık
      const requests = http.match(COLLECTION);
      expect(requests).toHaveLength(1);
      const request = requests[0]!;
      expect(request.request.body).toEqual({
        cariId: 'cari-1',
        kiraId: 'kira-A1',
        tutar: '1250.50',
        hesap: 'Kasa',
        hesapId: null,
        doviz: 'TRY',
        kanal: 'Masaüstü',
        aciklama: 'Hızlı tahsilat (pano) — 34 ABC 123',
        tahsilatAnahtar: KEY,
      });
      expect(request.request.headers.has('Idempotency-Key')).toBe(false);
      expect(
        s.kok.querySelector<HTMLButtonElement>('rc-panel-tahsilat-formu button[type=submit]')
          ?.disabled,
      ).toBe(true);

      request.flush({ id: 'islem-1' });
      await s.stabil();
      // Yeni bakiye → yeni anahtar: panel yeniden yüklenir, form kapanır.
      http.expectOne(SUMMARY).flush(ozet());
      await s.stabil();
      expect(s.kok.querySelector('rc-panel-tahsilat-formu')).toBeNull();
      expect(TestBed.inject(ToastService).toasts()).toEqual([
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
        const s = await openForm();
        await s.gonder();
        http
          .expectOne(COLLECTION)
          .flush(
            { type: 'about:blank', title: 'Mükerrer', status: 409, detail, kod: 'mukerrer' },
            { status: 409, statusText: 'Conflict' },
          );
        await s.stabil();
        http.expectOne(SUMMARY).flush(response());
        await s.stabil();
        http.expectNone(COLLECTION);
        expect(s.kok.querySelector('rc-panel-tahsilat-formu')).toBeNull();
        expect(TestBed.inject(ToastService).toasts()).toEqual([
          expect.objectContaining({
            durum: 'uyari',
            baslik: 'Kira kaydı değişmiş',
            mesaj: `${detail} Panel yeniden yüklendi; güncel bakiyeyi kontrol edin.`,
          }),
        ]);
      },
    );

    it('409 mukerrer + mevcut (işlem ZATEN yazıldı): "İşlem zaten kaydedildi" bilgisi, form kapanır, tekrar yok', async () => {
      const s = await openForm();
      await s.gonder();
      const detail =
        'Bu tahsilat zaten kaydedildi (No T-000042, 1.250,50 TRY); yeni tahsilat yazılmadı.';
      http.expectOne(COLLECTION).flush(
        {
          type: 'about:blank',
          title: 'Mükerrer',
          status: 409,
          detail,
          kod: 'mukerrer',
          mevcut: {
            id: 'x',
            belgeNo: 'T-000042',
            tutar: '1250.50',
            doviz: 'TRY',
            ayniIcerik: true,
          },
        },
        { status: 409, statusText: 'Conflict' },
      );
      await s.stabil();
      http.expectOne(SUMMARY).flush(response());
      await s.stabil();
      http.expectNone(COLLECTION);
      expect(s.kok.querySelector('rc-panel-tahsilat-formu')).toBeNull();
      expect(TestBed.inject(ToastService).toasts()).toEqual([
        expect.objectContaining({
          durum: 'bilgi',
          baslik: 'İşlem zaten kaydedildi',
          mesaj: `${detail} Panel yeniden yüklendi; güncel bakiyeyi kontrol edin.`,
        }),
      ]);
    });

    it('5xx: form açık kalır, "kaydedilmemiş olabilir" uyarısı formda; yeniden gönderim yok', async () => {
      const s = await openForm();
      await s.gonder();
      http
        .expectOne(COLLECTION)
        .flush(
          { type: 'about:blank', title: 'Hata', status: 500, detail: 'x', kod: 'sunucu' },
          { status: 500, statusText: 'Server Error' },
        );
      await s.stabil();
      http.expectNone(COLLECTION);
      http.expectNone(SUMMARY);
      expect(s.kok.querySelector('rc-panel-tahsilat-formu [role=alert]')?.textContent).toContain(
        'İşlem kaydedilmemiş olabilir',
      );
    });

    it('3. tur M-A + L-2: 409 + mevcut İÇERİK FARKLI → UYARI; form AÇIK; dokunulmamış ön-dolu tutar yeni bakiyeyle yenilenir; YENİ anahtarla gönderilir', async () => {
      const s = await openForm();
      await s.gonder();
      const detail =
        'Bu ekran açıldıktan sonra başka bir tahsilat yazıldı (No T-9, 100,00 TRY); girdiğiniz 1.250,50 TRY YAZILMADI. Güncel bakiyeyi kontrol edin.';
      http.expectOne(COLLECTION).flush(
        {
          type: 'about:blank',
          title: 'Mükerrer',
          status: 409,
          detail,
          kod: 'mukerrer',
          mevcut: { id: 'a', belgeNo: 'T-9', tutar: 100, doviz: 'TRY', ayniIcerik: false },
        },
        { status: 409, statusText: 'Conflict' },
      );
      await s.stabil();
      const NEW = '99999999-9999-4999-8999-999999999999';
      const newItem = response();
      newItem.donusler.bugun[0] = donus('A1', {
        tahsilat: { ...info, anahtar: NEW, varsayilanTutar: 1150.5 },
      });
      http.expectOne(SUMMARY).flush(newItem);
      await s.stabil();
      expect(TestBed.inject(ToastService).toasts()).toEqual([
        expect.objectContaining({
          durum: 'uyari',
          baslik: 'Başka bir tahsilat yazıldı — tutarınız kaydedilmedi',
          mesaj: `${detail} Panel yeniden yüklendi; güncel bakiyeyi kontrol edin.`,
        }),
      ]);
      expect(s.kok.querySelector('rc-panel-tahsilat-formu')).not.toBeNull(); // form AÇIK
      await s.gonder();
      const request = http.expectOne(COLLECTION);
      // L-2: tutar elle yazılmadı → eski bakiye (1.250,50) yerine yeni öneri (1.150,50).
      expect(request.request.body).toMatchObject({ tahsilatAnahtar: NEW, tutar: '1150.50' });
      request.flush({ id: 'y' });
      await s.stabil();
      http.expectOne(SUMMARY).flush(response());
      await s.stabil();
    });

    it('4. tur M-C: 5xx (yazıldı, yanıt kayboldu) → tutar değiştirilip AYNI anahtarla tekrar → "Önceki denemeniz kaydedilmiş", tutar TEMİZLENİR, ikinci basış istek göndermez', async () => {
      const s = await openForm();
      const amountInput = () =>
        s.kok.querySelector<HTMLInputElement>('rc-panel-tahsilat-formu rc-para-girdisi input')!;
      const write = async (text: string) => {
        amountInput().value = text;
        amountInput().dispatchEvent(new Event('input'));
        await s.stabil();
      };
      await write('500');
      await s.gonder();
      http
        .expectOne(COLLECTION)
        .flush(
          { type: 'about:blank', title: 'Hata', status: 500, detail: 'x', kod: 'sunucu' },
          { status: 500, statusText: 'Server Error' },
        );
      await s.stabil();
      await write('600');
      await s.gonder();
      const repeat = http.expectOne(COLLECTION);
      expect(repeat.request.body).toMatchObject({ tahsilatAnahtar: KEY, tutar: '600.00' });
      repeat.flush(
        {
          type: 'about:blank',
          title: 'Mükerrer',
          status: 409,
          detail:
            'Bu ekran açıldıktan sonra başka bir tahsilat yazıldı (No T-42, 500,00 TRY); girdiğiniz 600,00 TRY YAZILMADI.',
          kod: 'mukerrer',
          mevcut: { id: 'c1', belgeNo: 'T-42', tutar: 500, doviz: 'TRY', ayniIcerik: false },
        },
        { status: 409, statusText: 'Conflict' },
      );
      await s.stabil();
      const NEW = '99999999-9999-4999-8999-999999999999';
      const newItem = response();
      newItem.donusler.bugun[0] = donus('A1', {
        tahsilat: { ...info, anahtar: NEW, varsayilanTutar: 750.5 },
      });
      http.expectOne(SUMMARY).flush(newItem);
      await s.stabil();

      const toasts = TestBed.inject(ToastService).toasts();
      expect(toasts).toHaveLength(1);
      expect(toasts[0]).toEqual(
        expect.objectContaining({
          durum: 'uyari',
          baslik: 'Önceki denemeniz kaydedilmiş — yeni tutar yazılmadı',
        }),
      );
      expect(toasts[0]?.mesaj).toMatch(
        /^Önceki denemeniz kaydedilmiş \(No T-42, 500,00 ₺\); girdiğiniz 600,00 ₺ YAZILMADI\./,
      );
      expect(s.kok.querySelector('rc-panel-tahsilat-formu')).not.toBeNull(); // form AÇIK
      expect(amountInput().value).toBe(''); // yeni öneri de basılmaz (L-2 yalnız M-A'da)
      await s.gonder(); // boş tutar → istemci doğrulaması
      http.expectNone(COLLECTION);
    });

    it('form açıkken panel tazelenip yeni anahtar gelse de form AÇILDIĞI anahtarla gönderir (bayat → 409)', async () => {
      const s = await openForm();
      [...s.kok.querySelectorAll<HTMLButtonElement>('rc-sayfa-bandi button')]
        .find((d) => d.textContent?.includes('Yenile'))
        ?.click();
      await s.stabil();
      const newItem = response();
      newItem.donusler.bugun[0] = donus('A1', {
        tahsilat: { ...info, anahtar: '99999999-9999-4999-8999-999999999999' },
      });
      http.expectOne(SUMMARY).flush(newItem);
      await s.stabil();
      expect(s.kok.querySelector('rc-panel-tahsilat-formu')).not.toBeNull();
      await s.gonder();
      const request = http.expectOne(COLLECTION);
      expect((request.request.body as { tahsilatAnahtar: string }).tahsilatAnahtar).toBe(KEY);
      request.flush({ id: 'x' });
      await s.stabil();
      http.expectOne(SUMMARY).flush(response());
      await s.stabil();
    });

    it('adversarial F1: A formu açıkken B "Tahsil Et" → form YENİDEN oluşur; tutar/anahtar/cari/kira B’nin', async () => {
      const infoB = {
        anahtar: 'bbbbbbbb-6a1d-5b7e-9c3a-2d4e6f8a0b1c',
        cariId: 'cari-B',
        rentalId: 'kira-B1',
        doviz: 'TRY',
        varsayilanTutar: 90,
      };
      const s = await open(
        ozet({
          donusler: {
            gecikmis: [],
            bugun: [
              donus('A1', { tahsilat: info }),
              donus('B1', { plaka: '06 BBB 02', bakiye: 90, tahsilat: infoB }),
            ],
            yarin: [],
            varsayilanSekme: 'bugun',
          },
        }),
      );
      const buttons = () => s.kok.querySelectorAll<HTMLButtonElement>('td.islem button');
      buttons()[0]?.click();
      await s.stabil();
      http.expectOne(ACCOUNTS).flush([]);
      await s.stabil();
      const firstForm = s.kok.querySelector('rc-panel-tahsilat-formu');
      buttons()[1]?.click();
      await s.stabil();
      http.expectOne(ACCOUNTS).flush([]);
      await s.stabil();
      const form = s.kok.querySelector('rc-panel-tahsilat-formu');
      expect(form).not.toBe(firstForm); // aynı örnek korunmadı
      expect(form?.textContent).toContain('Tahsilat — 06 BBB 02 · B1');
      form?.querySelector<HTMLButtonElement>('button[type=submit]')?.click();
      await s.stabil();
      const request = http.expectOne(COLLECTION);
      expect(request.request.body).toMatchObject({
        kiraId: 'kira-B1',
        cariId: 'cari-B',
        tutar: '90.00',
        tahsilatAnahtar: infoB.anahtar,
      });
      // Uçarken: tablodaki TÜM "Tahsil Et"ler ve "Yenile" pasif; başka satır açılamaz.
      expect([...buttons()].map((d) => d.disabled)).toEqual([true, true]);
      buttons()[0]?.click();
      await s.stabil();
      expect(s.kok.querySelector('rc-panel-tahsilat-formu')?.textContent).toContain('06 BBB 02');
      request.flush({ id: 'x' });
      await s.stabil();
      http.expectOne(SUMMARY).flush(response());
      await s.stabil();
      expect(TestBed.inject(ToastService).toasts()).toEqual([
        expect.objectContaining({ mesaj: 'Tahsilat kaydedildi: 90,00 ₺ (06 BBB 02).' }),
      ]);
    });

    it('doğrulama hatası formda kalır, değer korunur, istek tekrarlanmaz', async () => {
      const s = await openForm();
      await s.gonder();
      http.expectOne(COLLECTION).flush(
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
      http.expectNone(SUMMARY);
      expect(s.kok.querySelector('rc-panel-tahsilat-formu')?.textContent).toContain(
        'Tutar pozitif olmalıdır.',
      );
    });
  });
});
