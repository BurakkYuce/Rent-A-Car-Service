import { HttpXsrfTokenExtractor } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideApiClient } from '@core/api/api-istemcisi';
import type { RentalListRow } from '@core/api/ui-tipleri';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { provideTranslation } from '@core/i18n/ceviri';
import { sessionInterceptor } from '@core/oturum/session-interceptor';
import { ReloginService } from '@core/oturum/relogin-service';
import { provideTurkishLocale } from '@core/yerel/tr-yerel';

import { CollectPanel } from './collect-panel';

/**
 * PARA — "Tahsil Et" sözleşmesi (bağımsız oracle: beklenen değerler elle kurulmuş satırdan, üretim
 * kodundan değil): DTO anahtarı AYNEN geri gider, istek uçarken kilit, 409 `mukerrer`'de yeniden
 * gönderim YOK + yeniden yükleme, 2xx'te yeniden yükleme, `cakisma`/doğrulama değerleri silmez.
 */

const COLLECT_ENDPOINT = '/api/ui/v1/finans/tahsilat';
const ACCOUNTS_ENDPOINT = '/api/ui/v1/finans/hesaplar';
const KEY = '3f8d2c1a-9b7e-5d4c-8a21-0f6e5d4c3b2a';

const ROW: RentalListRow = {
  id: '11111111-1111-4111-8111-111111111111',
  sozlesmeNo: '2026220901007',
  musteriId: '22222222-2222-4222-8222-222222222222',
  musteriAd: 'Ayşe Yılmaz',
  plaka: '34 ABC 123',
  basTar: '2026-09-20T07:00:00Z',
  bitTar: '2026-09-23T07:00:00Z',
  vadeTar: null,
  gun: 3,
  hediyeGun: null,
  faturalananGun: null,
  tutar: 3600,
  bakiye: 1234.5,
  doviz: 'TRY',
  kaynak: null,
  cikisOfisi: 'Merkez',
  donusOfisi: 'Merkez',
  provizyon: null,
  depozito: null,
  komisyonOran: null,
  komisyonTutar: null,
  onayKodu: null,
  projeAdi: null,
  assistFirma: null,
  ozelSoforBilgisi: null,
  durum: 'Kirada',
  faturali: false,
  tahsilat: {
    anahtar: KEY,
    cariId: '22222222-2222-4222-8222-222222222222',
    rentalId: '11111111-1111-4111-8111-111111111111',
    doviz: 'TRY',
    varsayilanTutar: 1234.5,
  },
};

@Component({
  selector: 'rc-tahsil-deneme',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CollectPanel],
  template: `<rc-kira-tahsil-paneli
    [satir]="satir()"
    (kapat)="closings = closings + 1"
    (sonuclandi)="results = results + 1"
    (anahtarTazele)="refreshes = refreshes + 1"
  />`,
})
class TestHost {
  readonly satir = signal<RentalListRow>(ROW);
  closings = 0;
  results = 0;
  refreshes = 0;
}

class FakeXsrf implements HttpXsrfTokenExtractor {
  getToken(): string | null {
    return 'belirtec';
  }
}

function problem(code: string, status: number, extra: Record<string, unknown> = {}) {
  return {
    govde: {
      type: 'about:blank',
      title: 'Hata',
      status,
      detail: `${code} ayrıntısı`,
      kod: code,
      ...extra,
    },
    secenek: { status, statusText: 'Hata' },
  };
}

describe('TahsilPaneli (PARA)', () => {
  let http: HttpTestingController;
  let toast: ToastService;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideTurkishLocale(),
        ...provideTranslation(),
        provideRouter([]),
        provideApiClient(sessionInterceptor),
        provideHttpClientTesting(),
        { provide: HttpXsrfTokenExtractor, useClass: FakeXsrf },
        { provide: ReloginService, useValue: { request: vi.fn() } },
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
    http = TestBed.inject(HttpTestingController);
    toast = TestBed.inject(ToastService);
  });

  afterEach(() => {
    http.verify();
    toast.clear();
  });

  async function exchangeRate(accounts: unknown[] = []) {
    const fixture = TestBed.createComponent(TestHost);
    await fixture.whenStable();
    http.expectOne(ACCOUNTS_ENDPOINT).flush(accounts);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const submitButton = () => {
      const d = root.querySelector<HTMLButtonElement>('button[type="submit"]');
      if (d === null) throw new Error('gönder düğmesi yok');
      return d;
    };
    const amountInput = () => {
      const g = root.querySelector<HTMLInputElement>('rc-para-girdisi input');
      if (g === null) throw new Error('tutar girdisi yok');
      return g;
    };
    const gonder = async () => {
      root.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
      await fixture.whenStable();
    };
    return {
      fixture,
      kok: root,
      d: fixture.componentInstance,
      gonderDugmesi: submitButton,
      tutarGirdisi: amountInput,
      gonder,
    };
  }

  it('sunucunun TahsilatAnahtar’ı gövdede ve başlıkta AYNEN gider; cari/kira/döviz satırdan, tutar invariant metin', async () => {
    const { fixture, gonder, tutarGirdisi } = await exchangeRate();
    // Panel açılınca odak tutarda (düzenleme metni); bırakınca tr biçimi. Öneri = kalan bakiye.
    expect(document.activeElement).toBe(tutarGirdisi());
    expect(tutarGirdisi().value).toBe('1234,50');
    // Öneri SEÇİLİ: doğrudan yazılan tutar önerinin yerine geçer, sonuna eklenmez (adversarial F3).
    expect([tutarGirdisi().selectionStart, tutarGirdisi().selectionEnd]).toEqual([0, 7]);
    tutarGirdisi().dispatchEvent(new Event('blur'));
    await fixture.whenStable();
    expect(tutarGirdisi().value).toBe('1.234,50');

    await gonder();
    const request = http.expectOne(COLLECT_ENDPOINT);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      cariId: '22222222-2222-4222-8222-222222222222',
      tutar: '1234.50',
      hesap: 'Kasa',
      kiraId: '11111111-1111-4111-8111-111111111111',
      doviz: 'TRY',
      hesapId: null,
      kanal: 'Masaüstü',
      aciklama: 'Hızlı tahsilat (liste) — 2026220901007',
      tahsilatAnahtar: KEY,
    });
    // İstemci anahtar ÜRETMEZ: başlık da sunucunun deterministik anahtarı.
    expect(request.request.headers.get('Idempotency-Key')).toBe(KEY);
    request.flush({ id: '99999999-9999-4999-8999-999999999999' });
  });

  it('istek uçarken düğme kilitli ve ikinci gönderim yeni istek üretmez; 2xx → başarı + yeniden yükleme', async () => {
    const { fixture, d, gonder, gonderDugmesi } = await exchangeRate();
    await gonder();
    const request = http.expectOne(COLLECT_ENDPOINT);
    expect(gonderDugmesi().disabled).toBe(true);

    await gonder();
    await gonder();
    http.expectNone(COLLECT_ENDPOINT); // çift tık tek istek

    request.flush({ id: '99999999-9999-4999-8999-999999999999' });
    await fixture.whenStable();
    expect(d.results).toBe(1);
    expect(gonderDugmesi().disabled).toBe(false);
    expect(toast.toasts()).toEqual([
      expect.objectContaining({
        durum: 'basari',
        mesaj: '2026220901007: 1.234,50 ₺ tahsil edildi.',
      }),
    ]);
  });

  it('409 mukerrer (sunucu anahtarı yeniden hesapladı, tutmadı) → yeniden GÖNDERİLMEZ; liste yenilenir, sunucu detayı "kayıt değişmiş" uyarısıyla', async () => {
    const { fixture, d, gonder } = await exchangeRate();
    await gonder();
    const DETAIL =
      'Kiranın bakiyesi ya da kasa işlemleri bu ekran açıldıktan sonra değişti ya da tahsilat anahtarı bu ' +
      'kiraya ait değil; kaydı yeniden yükleyip tekrar deneyin.';
    const { govde, secenek } = problem('mukerrer', 409, { detail: DETAIL });
    http.expectOne(COLLECT_ENDPOINT).flush(govde, secenek);
    await fixture.whenStable();

    http.expectNone(COLLECT_ENDPOINT); // ne aynı ne yeni anahtarla, ne anahtarsız tekrar
    expect(d.results).toBe(1);
    expect(toast.toasts()).toEqual([
      expect.objectContaining({
        durum: 'uyari',
        baslik: 'Kira kaydı değişmiş',
        mesaj: `${DETAIL} Kayıt yeniden yüklendi.`,
      }),
    ]);
    // "Mükerrer işlem kaydedildi" izlenimi yok: ne başarı ne "Mükerrer işlem" başlığı.
    expect(toast.toasts().some((t) => t.durum === 'basari')).toBe(false);
    expect(toast.toasts().some((t) => t.baslik === 'Mükerrer işlem')).toBe(false);
  });

  it('409 mukerrer + mevcut (işlem ZATEN yazıldı — kaybolan yanıt) → "İşlem zaten kaydedildi" bilgisi; tekrar yok', async () => {
    const { fixture, d, gonder } = await exchangeRate();
    await gonder();
    const DETAIL =
      'Bu tahsilat zaten kaydedildi (No T-000042, 500,00 TRY); yeni tahsilat yazılmadı.';
    const { govde, secenek } = problem('mukerrer', 409, {
      detail: DETAIL,
      mevcut: { id: 'x', belgeNo: 'T-000042', tutar: 500, doviz: 'TRY', ayniIcerik: true },
    });
    http.expectOne(COLLECT_ENDPOINT).flush(govde, secenek);
    await fixture.whenStable();

    http.expectNone(COLLECT_ENDPOINT);
    expect(d.results).toBe(1);
    expect(toast.toasts()).toEqual([
      expect.objectContaining({
        durum: 'bilgi',
        baslik: 'İşlem zaten kaydedildi',
        mesaj: `${DETAIL} Kayıt yeniden yüklendi.`,
      }),
    ]);
  });

  it('3. tur M-A + L-2: 409 + mevcut İÇERİK FARKLI → "Başka bir tahsilat yazıldı" UYARISI; panel AÇIK; dokunulmamış ön-dolu tutar yeni bakiyeyle yenilenir; yeni anahtarla gönderilir', async () => {
    const { fixture, d, gonder, tutarGirdisi } = await exchangeRate();
    await gonder();
    const DETAIL =
      'Bu ekran açıldıktan sonra başka bir tahsilat yazıldı (No T-9, 100,00 TRY); girdiğiniz 1.234,50 TRY YAZILMADI. Güncel bakiyeyi kontrol edin.';
    const { govde, secenek } = problem('mukerrer', 409, {
      detail: DETAIL,
      mevcut: { id: 'a', belgeNo: 'T-9', tutar: 100, doviz: 'TRY', ayniIcerik: false },
    });
    http.expectOne(COLLECT_ENDPOINT).flush(govde, secenek);
    await fixture.whenStable();

    http.expectNone(COLLECT_ENDPOINT);
    expect(d.results).toBe(0); // panel kapanmadı
    expect(d.refreshes).toBe(1); // sayfa listeyi yeniden yükler
    expect(toast.toasts()).toEqual([
      expect.objectContaining({
        durum: 'uyari',
        baslik: 'Başka bir tahsilat yazıldı — tutarınız kaydedilmedi',
        mesaj: `${DETAIL} Kayıt yeniden yüklendi.`,
      }),
    ]);
    // Sayfa aynı kiranın güncel satırını (yeni anahtar, yeni öneri) verir: form SIFIRLANMAZ, ama tutar elle
    // yazılmadığı için eski bakiye (1.234,50) yerine yeni öneri (1.134,50) gelir (L-2: fazla tahsilat gitmesin).
    const NEW = '44444444-4444-4444-8444-444444444444';
    d.satir.set({
      ...ROW,
      bakiye: 1134.5,
      tahsilat: { ...ROW.tahsilat!, anahtar: NEW, varsayilanTutar: 1134.5 },
    });
    await fixture.whenStable();
    expect(tutarGirdisi().value).toMatch(/^1\.?134,50$/);
    await gonder();
    const second = http.expectOne(COLLECT_ENDPOINT);
    expect(second.request.body.tahsilatAnahtar).toBe(NEW);
    expect(second.request.body.tutar).toBe('1134.50');
    second.flush({ id: 'y' });
    await fixture.whenStable();
    expect(d.results).toBe(1);
  });

  it('L-2: M-A sonrası kullanıcının ELLE yazdığı tutar yeni satırın önerisiyle EZİLMEZ', async () => {
    const { fixture, d, gonder, tutarGirdisi } = await exchangeRate();
    tutarGirdisi().value = '700';
    tutarGirdisi().dispatchEvent(new Event('input'));
    await fixture.whenStable();
    await gonder();
    const { govde, secenek } = problem('mukerrer', 409, {
      detail: 'başka bir tahsilat yazıldı; girdiğiniz 700,00 TRY YAZILMADI.',
      mevcut: { id: 'a', belgeNo: 'T-9', tutar: 100, doviz: 'TRY', ayniIcerik: false },
    });
    http.expectOne(COLLECT_ENDPOINT).flush(govde, secenek);
    await fixture.whenStable();
    const NEW = '44444444-4444-4444-8444-444444444444';
    d.satir.set({
      ...ROW,
      tahsilat: { ...ROW.tahsilat!, anahtar: NEW, varsayilanTutar: 1134.5 },
    });
    await fixture.whenStable();
    expect(tutarGirdisi().value).toMatch(/^700(,00)?$/);
    toast.clear();
  });

  it('4. tur M-C: kaybolan yanıt → tutar değiştirilip AYNI anahtarla tekrar → "Önceki denemeniz kaydedilmiş", tutar TEMİZLENİR, panel açık; ikinci basış istek göndermez', async () => {
    const { fixture, d, gonder, tutarGirdisi } = await exchangeRate();
    tutarGirdisi().value = '500';
    tutarGirdisi().dispatchEvent(new Event('input'));
    await fixture.whenStable();
    await gonder();
    http.expectOne(COLLECT_ENDPOINT).error(new ProgressEvent('error')); // yazıldı, yanıt kayboldu
    await fixture.whenStable();
    toast.clear(); // ağ hatası toast'u (interceptor)

    tutarGirdisi().value = '600';
    tutarGirdisi().dispatchEvent(new Event('input'));
    await fixture.whenStable();
    await gonder();
    const second = http.expectOne(COLLECT_ENDPOINT);
    expect(second.request.body.tahsilatAnahtar).toBe(KEY); // donmuş anahtar
    expect(second.request.body.tutar).toBe('600.00');
    const { govde: body, secenek: option } = problem('mukerrer', 409, {
      detail:
        'Bu ekran açıldıktan sonra başka bir tahsilat yazıldı (No T-42, 500,00 TRY); girdiğiniz 600,00 TRY YAZILMADI.',
      mevcut: { id: 'c1', belgeNo: 'T-42', tutar: 500, doviz: 'TRY', ayniIcerik: false },
    });
    second.flush(body, option);
    await fixture.whenStable();

    expect(d.results).toBe(0);
    expect(d.refreshes).toBe(1);
    const t = toast.toasts();
    expect(t).toHaveLength(1);
    expect(t[0]).toEqual(
      expect.objectContaining({
        durum: 'uyari',
        baslik: 'Önceki denemeniz kaydedilmiş — yeni tutar yazılmadı',
      }),
    );
    expect(t[0]?.mesaj).toMatch(
      /^Önceki denemeniz kaydedilmiş \(No T-42, 500,00 ₺\); girdiğiniz 600,00 ₺ YAZILMADI\./,
    );
    expect(tutarGirdisi().value).toBe('');

    await gonder(); // boş tutar → istemci doğrulaması
    http.expectNone(COLLECT_ENDPOINT);
    toast.clear();
  });

  it('ağ hatasından sonra yeniden deneme AYNI tahsilatAnahtar’ı taşır (anahtarsız tekrar yok)', async () => {
    const { fixture, d, gonder } = await exchangeRate();
    await gonder();
    http.expectOne(COLLECT_ENDPOINT).error(new ProgressEvent('error'));
    await fixture.whenStable();
    expect(d.results).toBe(0);

    await gonder();
    const second = http.expectOne(COLLECT_ENDPOINT);
    expect(second.request.body.tahsilatAnahtar).toBe(KEY);
    expect(second.request.headers.get('Idempotency-Key')).toBe(KEY);
    second.flush({ id: 'x' });
    await fixture.whenStable();
    expect(d.results).toBe(1);
  });

  it('409 cakisma ve alan hatası formu SİLMEZ; panel açık kalır, aynı anahtarla düzeltilip gönderilir', async () => {
    const { fixture, kok, d, gonder, tutarGirdisi } = await exchangeRate();
    tutarGirdisi().value = '500,25';
    tutarGirdisi().dispatchEvent(new Event('input'));
    await fixture.whenStable();

    await gonder();
    const conflict = problem('cakisma', 409, { detail: 'Kayıt başka bir işlemle değişti.' });
    http.expectOne(COLLECT_ENDPOINT).flush(conflict.govde, conflict.secenek);
    await fixture.whenStable();
    expect(d.results).toBe(0);
    expect(tutarGirdisi().value).toBe('500,25');

    await gonder();
    const withFields = problem('dogrulama', 400, { errors: { tutar: ['Tutar çok yüksek.'] } });
    const second = http.expectOne(COLLECT_ENDPOINT);
    expect(second.request.body.tutar).toBe('500.25');
    expect(second.request.body.tahsilatAnahtar).toBe(KEY);
    second.flush(withFields.govde, withFields.secenek);
    await fixture.whenStable();
    expect(d.results).toBe(0);
    expect(kok.textContent).toContain('Tutar çok yüksek.');
    expect(tutarGirdisi().value).toBe('500,25');
  });

  it('2’den fazla ondalık (1,555 ya da öneri+“90” = 1234,5090) alan hatası; yuvarlanıp GÖNDERİLMEZ', async () => {
    const { fixture, kok, gonder, tutarGirdisi } = await exchangeRate();
    for (const written of ['1,555', '1234,5090']) {
      tutarGirdisi().value = written;
      tutarGirdisi().dispatchEvent(new Event('input'));
      await fixture.whenStable();
      await gonder();
      http.expectNone(COLLECT_ENDPOINT);
      expect(kok.textContent).toContain('En fazla 2 ondalık hane girilebilir.');
      expect(tutarGirdisi().value).toBe(written);
    }
  });

  it('tutar sıfır ya da boşsa istek GİTMEZ', async () => {
    const { fixture, kok, gonder, tutarGirdisi } = await exchangeRate();
    tutarGirdisi().value = '0';
    tutarGirdisi().dispatchEvent(new Event('input'));
    await fixture.whenStable();
    await gonder();
    http.expectNone(COLLECT_ENDPOINT);
    expect(kok.textContent).toContain('Tutar sıfırdan büyük olmalıdır.');

    tutarGirdisi().value = '';
    tutarGirdisi().dispatchEvent(new Event('input'));
    await gonder();
    http.expectNone(COLLECT_ENDPOINT);
  });

  it('hesap seçici yalnız seçili türün hesaplarını listeler; tür değişince uyumsuz seçim düşer', async () => {
    const { fixture, kok, gonder } = await exchangeRate([
      {
        id: 'k1',
        etiket: 'Kasa · Merkez (MRK)',
        kod: 'MRK',
        ad: 'Merkez',
        tur: 'Kasa',
        doviz: null,
      },
      {
        id: 'b1',
        etiket: 'Banka · Ziraat (ZRT)',
        kod: 'ZRT',
        ad: 'Ziraat',
        tur: 'Banka',
        doviz: null,
      },
      { id: 'x1', etiket: 'Belirsiz (X)', kod: 'X', ad: 'X', tur: null, doviz: null },
    ]);
    const selections = () => [...kok.querySelectorAll<HTMLSelectElement>('rc-secim select')];
    const options = (s: HTMLSelectElement) =>
      [...s.options].map((o) => o.textContent?.trim() ?? '');
    expect(selections()).toHaveLength(2);
    const [type, account] = selections();
    if (type === undefined || account === undefined) throw new Error('seçim yok');
    expect(options(account)).toEqual(['Hesap belirtilmemiş', 'Kasa · Merkez (MRK)']);

    account.value = '0';
    account.dispatchEvent(new Event('change'));
    type.value = '1'; // Banka
    type.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    const [, newAccount] = selections();
    if (newAccount === undefined) throw new Error('seçim yok');
    expect(options(newAccount)).toEqual(['Hesap belirtilmemiş', 'Banka · Ziraat (ZRT)']);

    await gonder();
    const request = http.expectOne(COLLECT_ENDPOINT);
    expect(request.request.body).toMatchObject({ hesap: 'Banka', hesapId: null });
    request.flush({ id: 'x' });
  });
});
