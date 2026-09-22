import { HttpXsrfTokenExtractor } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideApiIstemcisi } from '@core/api/api-istemcisi';
import type { KiraListeSatiri } from '@core/api/ui-tipleri';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { provideCeviri } from '@core/i18n/ceviri';
import { oturumInterceptor } from '@core/oturum/oturum-interceptor';
import { YenidenGirisServisi } from '@core/oturum/yeniden-giris-servisi';
import { provideTurkceYerel } from '@core/yerel/tr-yerel';

import { TahsilPaneli } from './tahsil-paneli';

/**
 * PARA — "Tahsil Et" sözleşmesi (bağımsız oracle: beklenen değerler elle kurulmuş satırdan, üretim
 * kodundan değil): DTO anahtarı AYNEN geri gider, istek uçarken kilit, 409 `mukerrer`'de yeniden
 * gönderim YOK + yeniden yükleme, 2xx'te yeniden yükleme, `cakisma`/doğrulama değerleri silmez.
 */

const TAHSIL_UCU = '/api/ui/v1/finans/tahsilat';
const HESAP_UCU = '/api/ui/v1/finans/hesaplar';
const ANAHTAR = '3f8d2c1a-9b7e-5d4c-8a21-0f6e5d4c3b2a';

const SATIR: KiraListeSatiri = {
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
    anahtar: ANAHTAR,
    cariId: '22222222-2222-4222-8222-222222222222',
    rentalId: '11111111-1111-4111-8111-111111111111',
    doviz: 'TRY',
    varsayilanTutar: 1234.5,
  },
};

@Component({
  selector: 'rc-tahsil-deneme',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TahsilPaneli],
  template: `<rc-kira-tahsil-paneli
    [satir]="satir()"
    (kapat)="kapanislar = kapanislar + 1"
    (sonuclandi)="sonuclar = sonuclar + 1"
  />`,
})
class Deneme {
  readonly satir = signal<KiraListeSatiri>(SATIR);
  kapanislar = 0;
  sonuclar = 0;
}

class SahteXsrf implements HttpXsrfTokenExtractor {
  getToken(): string | null {
    return 'belirtec';
  }
}

function problem(kod: string, status: number, ek: Record<string, unknown> = {}) {
  return {
    govde: { type: 'about:blank', title: 'Hata', status, detail: `${kod} ayrıntısı`, kod, ...ek },
    secenek: { status, statusText: 'Hata' },
  };
}

describe('TahsilPaneli (PARA)', () => {
  let http: HttpTestingController;
  let toast: ToastServisi;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideTurkceYerel(),
        ...provideCeviri(),
        provideRouter([]),
        provideApiIstemcisi(oturumInterceptor),
        provideHttpClientTesting(),
        { provide: HttpXsrfTokenExtractor, useClass: SahteXsrf },
        { provide: YenidenGirisServisi, useValue: { iste: vi.fn() } },
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
    http = TestBed.inject(HttpTestingController);
    toast = TestBed.inject(ToastServisi);
  });

  afterEach(() => {
    http.verify();
    toast.temizle();
  });

  async function kur(hesaplar: unknown[] = []) {
    const fixture = TestBed.createComponent(Deneme);
    await fixture.whenStable();
    http.expectOne(HESAP_UCU).flush(hesaplar);
    await fixture.whenStable();
    const kok = fixture.nativeElement as HTMLElement;
    const gonderDugmesi = () => {
      const d = kok.querySelector<HTMLButtonElement>('button[type="submit"]');
      if (d === null) throw new Error('gönder düğmesi yok');
      return d;
    };
    const tutarGirdisi = () => {
      const g = kok.querySelector<HTMLInputElement>('rc-para-girdisi input');
      if (g === null) throw new Error('tutar girdisi yok');
      return g;
    };
    const gonder = async () => {
      kok.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
      await fixture.whenStable();
    };
    return { fixture, kok, d: fixture.componentInstance, gonderDugmesi, tutarGirdisi, gonder };
  }

  it('sunucunun TahsilatAnahtar’ı gövdede ve başlıkta AYNEN gider; cari/kira/döviz satırdan, tutar invariant metin', async () => {
    const { fixture, gonder, tutarGirdisi } = await kur();
    // Panel açılınca odak tutarda (düzenleme metni); bırakınca tr biçimi. Öneri = kalan bakiye.
    expect(document.activeElement).toBe(tutarGirdisi());
    expect(tutarGirdisi().value).toBe('1234,50');
    tutarGirdisi().dispatchEvent(new Event('blur'));
    await fixture.whenStable();
    expect(tutarGirdisi().value).toBe('1.234,50');

    await gonder();
    const istek = http.expectOne(TAHSIL_UCU);
    expect(istek.request.method).toBe('POST');
    expect(istek.request.body).toEqual({
      cariId: '22222222-2222-4222-8222-222222222222',
      tutar: '1234.50',
      hesap: 'Kasa',
      kiraId: '11111111-1111-4111-8111-111111111111',
      doviz: 'TRY',
      hesapId: null,
      kanal: 'Masaüstü',
      aciklama: 'Hızlı tahsilat (liste) — 2026220901007',
      tahsilatAnahtar: ANAHTAR,
    });
    // İstemci anahtar ÜRETMEZ: başlık da sunucunun deterministik anahtarı.
    expect(istek.request.headers.get('Idempotency-Key')).toBe(ANAHTAR);
    istek.flush({ id: '99999999-9999-4999-8999-999999999999' });
  });

  it('istek uçarken düğme kilitli ve ikinci gönderim yeni istek üretmez; 2xx → başarı + yeniden yükleme', async () => {
    const { fixture, d, gonder, gonderDugmesi } = await kur();
    await gonder();
    const istek = http.expectOne(TAHSIL_UCU);
    expect(gonderDugmesi().disabled).toBe(true);

    await gonder();
    await gonder();
    http.expectNone(TAHSIL_UCU); // çift tık tek istek

    istek.flush({ id: '99999999-9999-4999-8999-999999999999' });
    await fixture.whenStable();
    expect(d.sonuclar).toBe(1);
    expect(gonderDugmesi().disabled).toBe(false);
    expect(toast.toastlar()).toEqual([
      expect.objectContaining({
        durum: 'basari',
        mesaj: '2026220901007: 1.234,50 ₺ tahsil edildi.',
      }),
    ]);
  });

  it('409 mukerrer (sunucu anahtarı yeniden hesapladı, tutmadı) → yeniden GÖNDERİLMEZ; liste yenilenir, sunucu detayı "kayıt değişmiş" uyarısıyla', async () => {
    const { fixture, d, gonder } = await kur();
    await gonder();
    const DETAY =
      'Kiranın bakiyesi ya da kasa işlemleri bu ekran açıldıktan sonra değişti ya da tahsilat anahtarı bu ' +
      'kiraya ait değil; kaydı yeniden yükleyip tekrar deneyin.';
    const { govde, secenek } = problem('mukerrer', 409, { detail: DETAY });
    http.expectOne(TAHSIL_UCU).flush(govde, secenek);
    await fixture.whenStable();

    http.expectNone(TAHSIL_UCU); // ne aynı ne yeni anahtarla, ne anahtarsız tekrar
    expect(d.sonuclar).toBe(1);
    expect(toast.toastlar()).toEqual([
      expect.objectContaining({
        durum: 'uyari',
        baslik: 'Kira kaydı değişmiş',
        mesaj: `${DETAY} Kayıt yeniden yüklendi.`,
      }),
    ]);
    // "Mükerrer işlem kaydedildi" izlenimi yok: ne başarı ne "Mükerrer işlem" başlığı.
    expect(toast.toastlar().some((t) => t.durum === 'basari')).toBe(false);
    expect(toast.toastlar().some((t) => t.baslik === 'Mükerrer işlem')).toBe(false);
  });

  it('ağ hatasından sonra yeniden deneme AYNI tahsilatAnahtar’ı taşır (anahtarsız tekrar yok)', async () => {
    const { fixture, d, gonder } = await kur();
    await gonder();
    http.expectOne(TAHSIL_UCU).error(new ProgressEvent('error'));
    await fixture.whenStable();
    expect(d.sonuclar).toBe(0);

    await gonder();
    const ikinci = http.expectOne(TAHSIL_UCU);
    expect(ikinci.request.body.tahsilatAnahtar).toBe(ANAHTAR);
    expect(ikinci.request.headers.get('Idempotency-Key')).toBe(ANAHTAR);
    ikinci.flush({ id: 'x' });
    await fixture.whenStable();
    expect(d.sonuclar).toBe(1);
  });

  it('409 cakisma ve alan hatası formu SİLMEZ; panel açık kalır, aynı anahtarla düzeltilip gönderilir', async () => {
    const { fixture, kok, d, gonder, tutarGirdisi } = await kur();
    tutarGirdisi().value = '500,25';
    tutarGirdisi().dispatchEvent(new Event('input'));
    await fixture.whenStable();

    await gonder();
    const cakisma = problem('cakisma', 409, { detail: 'Kayıt başka bir işlemle değişti.' });
    http.expectOne(TAHSIL_UCU).flush(cakisma.govde, cakisma.secenek);
    await fixture.whenStable();
    expect(d.sonuclar).toBe(0);
    expect(tutarGirdisi().value).toBe('500,25');

    await gonder();
    const alanli = problem('dogrulama', 400, { errors: { tutar: ['Tutar çok yüksek.'] } });
    const ikinci = http.expectOne(TAHSIL_UCU);
    expect(ikinci.request.body.tutar).toBe('500.25');
    expect(ikinci.request.body.tahsilatAnahtar).toBe(ANAHTAR);
    ikinci.flush(alanli.govde, alanli.secenek);
    await fixture.whenStable();
    expect(d.sonuclar).toBe(0);
    expect(kok.textContent).toContain('Tutar çok yüksek.');
    expect(tutarGirdisi().value).toBe('500,25');
  });

  it('tutar sıfır ya da boşsa istek GİTMEZ', async () => {
    const { fixture, kok, gonder, tutarGirdisi } = await kur();
    tutarGirdisi().value = '0';
    tutarGirdisi().dispatchEvent(new Event('input'));
    await fixture.whenStable();
    await gonder();
    http.expectNone(TAHSIL_UCU);
    expect(kok.textContent).toContain('Tutar sıfırdan büyük olmalıdır.');

    tutarGirdisi().value = '';
    tutarGirdisi().dispatchEvent(new Event('input'));
    await gonder();
    http.expectNone(TAHSIL_UCU);
  });

  it('hesap seçici yalnız seçili türün hesaplarını listeler; tür değişince uyumsuz seçim düşer', async () => {
    const { fixture, kok, gonder } = await kur([
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
    const secimler = () => [...kok.querySelectorAll<HTMLSelectElement>('rc-secim select')];
    const secenekler = (s: HTMLSelectElement) =>
      [...s.options].map((o) => o.textContent?.trim() ?? '');
    expect(secimler()).toHaveLength(2);
    const [tur, hesap] = secimler();
    if (tur === undefined || hesap === undefined) throw new Error('seçim yok');
    expect(secenekler(hesap)).toEqual(['Hesap belirtilmemiş', 'Kasa · Merkez (MRK)']);

    hesap.value = '0';
    hesap.dispatchEvent(new Event('change'));
    tur.value = '1'; // Banka
    tur.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    const [, yeniHesap] = secimler();
    if (yeniHesap === undefined) throw new Error('seçim yok');
    expect(secenekler(yeniHesap)).toEqual(['Hesap belirtilmemiş', 'Banka · Ziraat (ZRT)']);

    await gonder();
    const istek = http.expectOne(TAHSIL_UCU);
    expect(istek.request.body).toMatchObject({ hesap: 'Banka', hesapId: null });
    istek.flush({ id: 'x' });
  });
});
