import { HttpXsrfTokenExtractor } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
  type TestRequest,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideApiIstemcisi } from '@core/api/api-istemcisi';
import type { Sayfa } from '@core/api/sayfa';
import type { KiraListeSatiri } from '@core/api/ui-tipleri';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { provideCeviri } from '@core/i18n/ceviri';
import { oturumInterceptor } from '@core/oturum/oturum-interceptor';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import type { Ben } from '@core/oturum/oturum-tipleri';
import { YenidenGirisServisi } from '@core/oturum/yeniden-giris-servisi';
import { sorguyuCoz } from '@core/veri/liste-sorgusu';
import { provideTurkceYerel } from '@core/yerel/tr-yerel';
import { BellekTabloDuzeniDeposu, TabloDuzeniDeposu } from '@shared/tablo/tablo-duzeni-deposu';

import { KiraListesi } from './kira-listesi';
import { KIRA_LISTESI, ozetParametreleri } from './kira-listesi.store';

const LISTE = '/api/ui/v1/kiralar';
const OZET = '/api/ui/v1/kiralar/ozet';
const SECENEK = '/api/ui/v1/kiralar/filtre-secenekleri';

function ben(izinler: string[]): Ben {
  return {
    kullanici: { id: 'u-1', kullaniciAdi: 'ayse', adSoyad: 'Ayşe Yılmaz' },
    kiraci: { id: 't-1', kod: 'pilot', ad: 'Pilot Firma' },
    rol: 'Admin',
    izinler,
    subeKapsami: { tumSubeler: true, subeId: null, subeAd: null },
    moduller: { webSitesi: false },
    renkler: {},
    pilot: true,
  };
}

function satir(no: string, ek: Partial<KiraListeSatiri> = {}): KiraListeSatiri {
  const id = `${no.padStart(8, '0')}-0000-4000-8000-000000000000`;
  return {
    id,
    sozlesmeNo: no,
    musteriId: 'c0000000-0000-4000-8000-000000000001',
    musteriAd: 'Ayşe Yılmaz',
    plaka: '34 ABC 123',
    basTar: '2026-09-20T07:00:00Z',
    bitTar: '2026-09-23T07:00:00Z',
    vadeTar: null,
    gun: 3,
    hediyeGun: null,
    faturalananGun: null,
    tutar: 3600,
    bakiye: 1200,
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
    tahsilat: null,
    ...ek,
  };
}

const tahsilat = (s: KiraListeSatiri, anahtar: string) => ({
  anahtar,
  cariId: s.musteriId,
  rentalId: s.id,
  doviz: 'TRY',
  varsayilanTutar: 1200,
});

const sayfa = (kayitlar: KiraListeSatiri[]): Sayfa<KiraListeSatiri> => ({
  kayitlar,
  toplam: kayitlar.length,
  sayfaNo: 1,
  boyut: 50,
});

class SahteXsrf implements HttpXsrfTokenExtractor {
  getToken(): string | null {
    return 'belirtec';
  }
}

describe('KIRA_LISTESI sorgusu', () => {
  it('URL = API adları; bozuk durum/tarih/kimlik ve beyaz liste dışı sıralama düşer', () => {
    const sorgu = sorguyuCoz(KIRA_LISTESI, {
      q: '  34 ABC ',
      durum: 'Kiralik',
      fatura: 'false',
      basMin: '2026-02-30',
      basMax: '2026-09-22',
      personelId: 'kimlik-degil',
      sirala: 'musteriAd',
    });
    expect(sorgu.filtreler).toEqual({ q: '34 ABC', fatura: false, basMax: '2026-09-22' });
    expect(sorgu.sirala).toBeNull();
    expect(sorgu.boyut).toBe(50);
  });

  it('özet parametreleri süzgeçleri taşır, sayfa/boyut/sıralamayı taşımaz', () => {
    expect(
      ozetParametreleri({ sayfa: 3, boyut: 25, sirala: '-bakiye', durum: 'Kirada', q: 'x' }),
    ).toEqual({ durum: 'Kirada', q: 'x' });
  });
});

describe('KiraListesi sayfası', () => {
  let http: HttpTestingController;
  let toast: ToastServisi;

  async function kur(izinler: string[]) {
    TestBed.configureTestingModule({
      providers: [
        provideTurkceYerel(),
        ...provideCeviri(),
        provideRouter([]),
        provideApiIstemcisi(oturumInterceptor),
        provideHttpClientTesting(),
        { provide: HttpXsrfTokenExtractor, useClass: SahteXsrf },
        { provide: YenidenGirisServisi, useValue: { iste: vi.fn() } },
        { provide: TabloDuzeniDeposu, useValue: new BellekTabloDuzeniDeposu() },
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
    http = TestBed.inject(HttpTestingController);
    toast = TestBed.inject(ToastServisi);
    const yukleme = TestBed.inject(OturumServisi).yukle();
    http.expectOne('/api/ui/v1/oturum/ben').flush(ben(izinler));
    await yukleme;

    const fixture = TestBed.createComponent(KiraListesi);
    await fixture.whenStable();
    const kok = fixture.nativeElement as HTMLElement;
    const bekle = () => fixture.whenStable();
    const listeIstegi = (): TestRequest => http.expectOne((r) => r.url === LISTE);
    const yardimcilar = {
      fixture,
      kok,
      bekle,
      listeIstegi,
      /** Açılış: liste + özet + öneri listeleri. */
      async ac(kayitlar: KiraListeSatiri[]) {
        http.expectOne(SECENEK).flush({ sahipler: ['Filo A'], gruplar: ['Ekonomi'] });
        http.expectOne((r) => r.url === OZET).flush({ toplam: 2, kirada: 2, faturasiz: 1 });
        listeIstegi().flush(sayfa(kayitlar));
        await bekle();
      },
      dugme: (metin: RegExp) =>
        [...kok.querySelectorAll<HTMLButtonElement>('button')].filter((b) =>
          metin.test(b.textContent ?? ''),
        ),
      baglanti: (metin: RegExp) =>
        [...kok.querySelectorAll<HTMLAnchorElement>('a')].filter((a) =>
          metin.test(a.textContent ?? ''),
        ),
    };
    return yardimcilar;
  }

  afterEach(() => {
    http.verify();
    toast.temizle();
  });

  it('liste sunucudan sayfalı gelir; özet, PDF, dışa aktarma ve kira formu bağlantıları doğru yere gider', async () => {
    const { ac, kok, baglanti } = await kur(['OperationsWrite', 'FinanceWrite', 'ViewReports']);
    await ac([satir('2026220901001'), satir('2026220901002', { faturali: true })]);

    expect(kok.querySelector('h1')?.textContent).toContain('Kira Sözleşmeleri');
    expect(kok.textContent).toContain('2 sözleşme · 2 kirada · 1 faturasız');

    // Sözleşme no → SPA kira formu rotası (form F4.3'te; burada yalnız bağlantı).
    const no = baglanti(/2026220901001/)[0];
    expect(no?.getAttribute('href')).toBe(`/kiralar/${satir('2026220901001').id}`);
    // PDF: Blazor GET ucu, yeni sekme (SPA'ya yönlenmez).
    const pdf = baglanti(/PDF/).filter((a) => a.getAttribute('href')?.endsWith('/pdf'));
    expect(pdf.map((a) => a.getAttribute('href'))).toContain(
      `/kiralar/${satir('2026220901001').id}/pdf`,
    );
    expect(pdf.every((a) => a.target === '_blank' && a.rel.includes('noopener'))).toBe(true);
    // Dışa aktarma: sunucu ucu, istemcide dosya üretilmez.
    const excel = baglanti(/Excel/)[0];
    expect(excel?.getAttribute('href')).toBe('/listeler/export/kiralar?format=excel');
  });

  it('izinsiz düğmeler çizilmez: Tahsil Et yalnız sunucu tahsilat verdiyse; PDF OW, dışa aktarma ViewReports ister', async () => {
    const { ac, dugme, baglanti } = await kur(['FinanceWrite']);
    const a = satir('2026220901001');
    await ac([
      { ...a, tahsilat: tahsilat(a, 'aaaaaaaa-0000-5000-8000-000000000001') },
      satir('2026220901002'),
    ]);

    expect(dugme(/Tahsil et/)).toHaveLength(1);
    expect(baglanti(/Excel/)).toHaveLength(0);
    expect(baglanti(/PDF/).filter((x) => x.getAttribute('href')?.endsWith('/pdf'))).toHaveLength(0);
    expect(baglanti(/Yeni kira/)).toHaveLength(0);
    expect(dugme(/İptal/)).toHaveLength(0);
  });

  it('Tahsil Et 2xx → panel kapanır, liste ve özet YENİDEN yüklenir (yeni anahtar gelir)', async () => {
    const { ac, dugme, bekle, kok, listeIstegi } = await kur(['FinanceWrite']);
    const a = satir('2026220901001');
    const anahtar = 'aaaaaaaa-0000-5000-8000-000000000001';
    await ac([{ ...a, tahsilat: tahsilat(a, anahtar) }]);

    dugme(/Tahsil et/)[0]?.click();
    await bekle();
    http.expectOne('/api/ui/v1/finans/hesaplar').flush([]);
    await bekle();
    expect(kok.querySelector('rc-kira-tahsil-paneli')).not.toBeNull();

    kok
      .querySelector('rc-kira-tahsil-paneli form')
      ?.dispatchEvent(new Event('submit', { cancelable: true }));
    await bekle();
    const istek = http.expectOne('/api/ui/v1/finans/tahsilat');
    expect(istek.request.body.tahsilatAnahtar).toBe(anahtar);
    // Uçarken satırın "Tahsil et" düğmesi de kilitli (başka satır açılıp istek iptal edilemez).
    expect(dugme(/Tahsil et/).every((d) => d.disabled)).toBe(true);
    istek.flush({ id: 'x' });
    await bekle();

    expect(kok.querySelector('rc-kira-tahsil-paneli')).toBeNull();
    http.expectOne((r) => r.url === OZET).flush({ toplam: 1, kirada: 1, faturasiz: 1 });
    listeIstegi().flush(sayfa([{ ...a, bakiye: 0 }]));
    await bekle();
    expect(dugme(/Tahsil et/)).toHaveLength(0);
  });

  it('açık panelin satırı yeni anahtarla gelirse panel kapanır (bayat anahtarla gönderim yok)', async () => {
    const { ac, dugme, bekle, kok, listeIstegi } = await kur(['FinanceWrite']);
    const a = satir('2026220901001');
    await ac([{ ...a, tahsilat: tahsilat(a, 'aaaaaaaa-0000-5000-8000-000000000001') }]);
    dugme(/Tahsil et/)[0]?.click();
    await bekle();
    http.expectOne('/api/ui/v1/finans/hesaplar').flush([]);
    await bekle();

    await TestBed.inject(Router).navigate([], { queryParams: { durum: 'Kirada' } });
    await bekle();
    http.expectOne((r) => r.url === OZET).flush({ toplam: 1, kirada: 1, faturasiz: 1 });
    const yeni = listeIstegi();
    expect(yeni.request.params.get('durum')).toBe('Kirada');
    yeni.flush(sayfa([{ ...a, tahsilat: tahsilat(a, 'bbbbbbbb-0000-5000-8000-000000000002') }]));
    await bekle();

    expect(kok.querySelector('rc-kira-tahsil-paneli')).toBeNull();
    expect(toast.toastlar()).toEqual([
      expect.objectContaining({ durum: 'uyari', mesaj: expect.stringContaining('2026220901001') }),
    ]);
  });

  it('süzgeç formu URL’e yazar; API aynı adlarla (sayfa 1’e dönerek) çağrılır', async () => {
    const { ac, bekle, kok, listeIstegi } = await kur(['OperationsWrite', 'FinanceWrite']);
    await ac([satir('2026220901001')]);

    const ara = kok.querySelector<HTMLInputElement>('rc-metin-girdisi input');
    if (ara === null) throw new Error('arama kutusu yok');
    ara.value = 'Yılmaz';
    ara.dispatchEvent(new Event('input'));
    kok.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
    await bekle();

    http.expectOne((r) => r.url === OZET).flush({ toplam: 1, kirada: 1, faturasiz: 1 });
    const istek = listeIstegi();
    expect(istek.request.params.get('q')).toBe('Yılmaz');
    expect(istek.request.params.get('sayfa')).toBe('1');
    expect(istek.request.params.has('sirala')).toBe(false);
    istek.flush(sayfa([]));
    await bekle();
    expect(TestBed.inject(Router).url).toContain('q=Y%C4%B1lmaz');
  });
});
