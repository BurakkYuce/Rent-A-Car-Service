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

import { provideApiClient } from '@core/api/api-istemcisi';
import type { Sayfa } from '@core/api/sayfa';
import type { RentalListRow } from '@core/api/ui-tipleri';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { provideTranslation } from '@core/i18n/ceviri';
import { sessionInterceptor } from '@core/oturum/session-interceptor';
import { SessionService } from '@core/oturum/session-service';
import type { Ben } from '@core/oturum/oturum-tipleri';
import { ReloginService } from '@core/oturum/relogin-service';
import { parseQuery } from '@core/veri/liste-sorgusu';
import { provideTurkishLocale } from '@core/yerel/tr-yerel';
import { InMemoryTableLayoutStore, TableLayoutStore } from '@shared/tablo/table-layout-store';

import { RentalList } from './kira-listesi';
import { RENTAL_LIST, summaryParameters } from './rental-list.store';

const LISTE = '/api/ui/v1/kiralar';
const SUMMARY = '/api/ui/v1/kiralar/ozet';
const OPTION = '/api/ui/v1/kiralar/filtre-secenekleri';

function ben(permissions: string[]): Ben {
  return {
    kullanici: { id: 'u-1', kullaniciAdi: 'ayse', adSoyad: 'Ayşe Yılmaz' },
    kiraci: { id: 't-1', kod: 'pilot', ad: 'Pilot Firma' },
    rol: 'Admin',
    izinler: permissions,
    subeKapsami: { tumSubeler: true, subeId: null, subeAd: null },
    moduller: { webSitesi: false },
    renkler: {},
    pilot: true,
  };
}

function satir(no: string, extra: Partial<RentalListRow> = {}): RentalListRow {
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
    ...extra,
  };
}

const tahsilat = (s: RentalListRow, key: string) => ({
  anahtar: key,
  cariId: s.musteriId,
  rentalId: s.id,
  doviz: 'TRY',
  varsayilanTutar: 1200,
});

const sayfa = (records: RentalListRow[]): Sayfa<RentalListRow> => ({
  kayitlar: records,
  toplam: records.length,
  sayfaNo: 1,
  boyut: 50,
});

class FakeXsrf implements HttpXsrfTokenExtractor {
  getToken(): string | null {
    return 'belirtec';
  }
}

describe('KIRA_LISTESI sorgusu', () => {
  it('URL = API adları; bozuk durum/tarih/kimlik ve beyaz liste dışı sıralama düşer', () => {
    const query = parseQuery(RENTAL_LIST, {
      q: '  34 ABC ',
      durum: 'Kiralik',
      fatura: 'false',
      basMin: '2026-02-30',
      basMax: '2026-09-22',
      personelId: 'kimlik-degil',
      sirala: 'musteriAd',
    });
    expect(query.filtreler).toEqual({ q: '34 ABC', fatura: false, basMax: '2026-09-22' });
    expect(query.sirala).toBeNull();
    expect(query.boyut).toBe(50);
  });

  it('özet parametreleri süzgeçleri taşır, sayfa/boyut/sıralamayı taşımaz', () => {
    expect(
      summaryParameters({ sayfa: 3, boyut: 25, sirala: '-bakiye', durum: 'Kirada', q: 'x' }),
    ).toEqual({ durum: 'Kirada', q: 'x' });
  });
});

describe('KiraListesi sayfası', () => {
  let http: HttpTestingController;
  let toast: ToastService;

  async function exchangeRate(permissions: string[]) {
    TestBed.configureTestingModule({
      providers: [
        provideTurkishLocale(),
        ...provideTranslation(),
        provideRouter([]),
        provideApiClient(sessionInterceptor),
        provideHttpClientTesting(),
        { provide: HttpXsrfTokenExtractor, useClass: FakeXsrf },
        { provide: ReloginService, useValue: { request: vi.fn() } },
        { provide: TableLayoutStore, useValue: new InMemoryTableLayoutStore() },
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
    http = TestBed.inject(HttpTestingController);
    toast = TestBed.inject(ToastService);
    const loading = TestBed.inject(SessionService).yukle();
    http.expectOne('/api/ui/v1/oturum/ben').flush(ben(permissions));
    await loading;

    const fixture = TestBed.createComponent(RentalList);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const wait = () => fixture.whenStable();
    const listRequest = (): TestRequest => http.expectOne((r) => r.url === LISTE);
    const helpers = {
      fixture,
      kok: root,
      bekle: wait,
      listeIstegi: listRequest,
      /** Açılış: liste + özet + öneri listeleri. */
      async ac(records: RentalListRow[]) {
        http.expectOne(OPTION).flush({ sahipler: ['Filo A'], gruplar: ['Ekonomi'] });
        http.expectOne((r) => r.url === SUMMARY).flush({ toplam: 2, kirada: 2, faturasiz: 1 });
        listRequest().flush(sayfa(records));
        await wait();
      },
      dugme: (text: RegExp) =>
        [...root.querySelectorAll<HTMLButtonElement>('button')].filter((b) =>
          text.test(b.textContent ?? ''),
        ),
      baglanti: (text: RegExp) =>
        [...root.querySelectorAll<HTMLAnchorElement>('a')].filter((a) =>
          text.test(a.textContent ?? ''),
        ),
    };
    return helpers;
  }

  afterEach(() => {
    http.verify();
    toast.clear();
  });

  it('liste sunucudan sayfalı gelir; özet, PDF, dışa aktarma ve kira formu bağlantıları doğru yere gider', async () => {
    const { ac, kok, baglanti } = await exchangeRate([
      'OperationsWrite',
      'FinanceWrite',
      'ViewReports',
    ]);
    await ac([satir('2026220901001'), satir('2026220901002', { faturali: true })]);

    expect(kok.querySelector('h1')?.textContent).toContain('Kira listesi');
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
    const { ac, dugme, baglanti: link } = await exchangeRate(['FinanceWrite']);
    const a = satir('2026220901001');
    await ac([
      { ...a, tahsilat: tahsilat(a, 'aaaaaaaa-0000-5000-8000-000000000001') },
      satir('2026220901002'),
    ]);

    expect(dugme(/Tahsil et/)).toHaveLength(1);
    expect(link(/Excel/)).toHaveLength(0);
    expect(link(/PDF/).filter((x) => x.getAttribute('href')?.endsWith('/pdf'))).toHaveLength(0);
    expect(link(/Yeni kira/)).toHaveLength(0);
    expect(dugme(/İptal/)).toHaveLength(0);
  });

  it('Tahsil Et 2xx → panel kapanır, liste ve özet YENİDEN yüklenir (yeni anahtar gelir)', async () => {
    const { ac, dugme, bekle, kok, listeIstegi } = await exchangeRate(['FinanceWrite']);
    const a = satir('2026220901001');
    const key = 'aaaaaaaa-0000-5000-8000-000000000001';
    await ac([{ ...a, tahsilat: tahsilat(a, key) }]);

    dugme(/Tahsil et/)[0]?.click();
    await bekle();
    http.expectOne('/api/ui/v1/finans/hesaplar').flush([]);
    await bekle();
    expect(kok.querySelector('rc-kira-tahsil-paneli')).not.toBeNull();

    kok
      .querySelector('rc-kira-tahsil-paneli form')
      ?.dispatchEvent(new Event('submit', { cancelable: true }));
    await bekle();
    const request = http.expectOne('/api/ui/v1/finans/tahsilat');
    expect(request.request.body.tahsilatAnahtar).toBe(key);
    // Uçarken satırın "Tahsil et" düğmesi de kilitli (başka satır açılıp istek iptal edilemez).
    expect(dugme(/Tahsil et/).every((d) => d.disabled)).toBe(true);
    request.flush({ id: 'x' });
    await bekle();

    expect(kok.querySelector('rc-kira-tahsil-paneli')).toBeNull();
    http.expectOne((r) => r.url === SUMMARY).flush({ toplam: 1, kirada: 1, faturasiz: 1 });
    listeIstegi().flush(sayfa([{ ...a, bakiye: 0 }]));
    await bekle();
    expect(dugme(/Tahsil et/)).toHaveLength(0);
  });

  it('açık panelin satırı yeni anahtarla gelirse panel kapanır (bayat anahtarla gönderim yok)', async () => {
    const { ac, dugme: button, bekle, kok, listeIstegi } = await exchangeRate(['FinanceWrite']);
    const a = satir('2026220901001');
    await ac([{ ...a, tahsilat: tahsilat(a, 'aaaaaaaa-0000-5000-8000-000000000001') }]);
    button(/Tahsil et/)[0]?.click();
    await bekle();
    http.expectOne('/api/ui/v1/finans/hesaplar').flush([]);
    await bekle();

    await TestBed.inject(Router).navigate([], { queryParams: { durum: 'Kirada' } });
    await bekle();
    http.expectOne((r) => r.url === SUMMARY).flush({ toplam: 1, kirada: 1, faturasiz: 1 });
    const newItem = listeIstegi();
    expect(newItem.request.params.get('durum')).toBe('Kirada');
    newItem.flush(sayfa([{ ...a, tahsilat: tahsilat(a, 'bbbbbbbb-0000-5000-8000-000000000002') }]));
    await bekle();

    expect(kok.querySelector('rc-kira-tahsil-paneli')).toBeNull();
    expect(toast.toasts()).toEqual([
      expect.objectContaining({ durum: 'uyari', mesaj: expect.stringContaining('2026220901001') }),
    ]);
  });

  it('süzgeç formu URL’e yazar; API aynı adlarla (sayfa 1’e dönerek) çağrılır', async () => {
    const { ac, bekle, kok, listeIstegi } = await exchangeRate(['OperationsWrite', 'FinanceWrite']);
    await ac([satir('2026220901001')]);

    const search = kok.querySelector<HTMLInputElement>('rc-metin-girdisi input');
    if (search === null) throw new Error('arama kutusu yok');
    search.value = 'Yılmaz';
    search.dispatchEvent(new Event('input'));
    kok.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
    await bekle();

    http.expectOne((r) => r.url === SUMMARY).flush({ toplam: 1, kirada: 1, faturasiz: 1 });
    const request = listeIstegi();
    expect(request.request.params.get('q')).toBe('Yılmaz');
    expect(request.request.params.get('sayfa')).toBe('1');
    expect(request.request.params.has('sirala')).toBe(false);
    request.flush(sayfa([]));
    await bekle();
    expect(TestBed.inject(Router).url).toContain('q=Y%C4%B1lmaz');
  });
  it('?gorunum= ön ayarı mevcut süzgeçlere çevrilir; süzgeç değişince görünüm URL’den düşer', async () => {
    const { ac: open, bekle, kok, listeIstegi } = await exchangeRate(['OperationsWrite']);
    await open([satir('2026220901001')]);
    const router = TestBed.inject(Router);

    // Kenar çubuğu bağlantısı yalnız `gorunum` taşır → liste ön ayarı (durum=Kirada) uygular.
    await router.navigateByUrl('/?gorunum=kirada');
    await bekle();
    http.expectOne((r) => r.url === SUMMARY).flush({ toplam: 1, kirada: 1, faturasiz: 1 });
    const request = listeIstegi();
    expect(request.request.params.get('durum')).toBe('Kirada');
    request.flush(sayfa([satir('2026220901001')]));
    await bekle();
    expect(router.url).toBe('/?gorunum=kirada&durum=Kirada');
    const active = kok.querySelector('rc-gorunum-cipleri [aria-current="page"]');
    expect(active?.textContent).toContain('Kiradaki araçlar');
    // Bant pill'i etkin görünümü ve kayıt sayısını söyler.
    expect(kok.querySelector('rc-sayfa-bandi')?.textContent).toContain(
      'Kiradaki araçlar · 1 kayıt',
    );

    // Kullanıcı süzgeci değiştirir → görünüm artık o değil: `gorunum` düşer, süzgeç kalır.
    await router.navigateByUrl('/?gorunum=kirada&durum=Tamamlandi');
    await bekle();
    http.expectOne((r) => r.url === SUMMARY).flush({ toplam: 0, kirada: 0, faturasiz: 0 });
    listeIstegi().flush(sayfa([]));
    await bekle();
    expect(router.url).toBe('/?durum=Tamamlandi');
    expect(kok.querySelector('rc-gorunum-cipleri [aria-current="page"]')).toBeNull();
  });
});
