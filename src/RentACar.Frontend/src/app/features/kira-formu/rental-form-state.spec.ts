import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { type Observable, Subject, firstValueFrom, of, throwError } from 'rxjs';
import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi, type IstekSecenekleri } from '@core/api/api-istemcisi';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { provideTranslation } from '@core/i18n/ceviri';
import { SessionService } from '@core/oturum/session-service';
import { RentalFormState } from './rental-form-state';

const VEHICLE_ID = '0b0e7c1a-3333-4aaa-8bbb-000000000003';
const OTHER_VEHICLE = '0b0e7c1a-3333-4aaa-8bbb-000000000009';
const CUSTOMER_ID = '0b0e7c1a-2222-4aaa-8bbb-000000000002';
const DEFINITION_ID = '0b0e7c1a-6666-4aaa-8bbb-000000000006';

const NAV_ID = '0b0e7c1a-6666-4aaa-8bbb-000000000016';

const wait = (ms: number) => new Promise((r) => setTimeout(r, ms));

const FULL_CATALOG = {
  ogeler: [
    { id: DEFINITION_ID, kod: 'BEBEK', ad: 'Bebek koltuğu', birimUcret: 75.5, kdvOrani: 0.1 },
  ],
  toplam: 1,
};
/** Testin değiştirebildiği katalog yanıtı (her testten önce tam kataloğa döner). */
let catalogResponse: { ogeler: typeof FULL_CATALOG.ogeler; toplam: number } = FULL_CATALOG;
beforeEach(() => {
  catalogResponse = FULL_CATALOG;
});

const AVAILABLE = [
  {
    id: VEHICLE_ID,
    plaka: '34 ABC 123',
    marka: 'Fiat',
    tip: 'Egea',
    modelYili: 2024,
    vites: 'Manuel',
    yakit: 'Dizel',
    grup: 'C',
    segment: 'Orta',
    km: 12000,
    sube: 'Merkez',
    konum: null,
  },
];

const DEFAULTS = {
  cikisYakit: 8,
  fiyatTuru: 'KDV Dahil Günlük',
  fiyatTurleri: ['KDV Dahil Günlük'],
  kiralamaTurleri: ['Kısa Kiralama'],
  faturalamaTipleri: [],
  dovizler: ['TL'],
  odemeSekilleri: [],
  basTarEnGec: '2027-09-22T00:00:00Z',
};

const serverError = (status: number, code: string, detail: string, errors?: object) =>
  toApiError(new HttpErrorResponse({ status, error: { status, kod: code, detail, errors } }));

interface Cagri {
  readonly yontem: string;
  readonly yol: string;
  readonly govde?: unknown;
  readonly secenek?: IstekSecenekleri;
}

/** Sahte ApiIstemcisi: GET yanıtları yola göre; yazmalar testin verdiği akışla. */
function fakeApi(write: (c: Cagri) => Observable<unknown>) {
  const calls: Cagri[] = [];
  const get = (path: string, option?: IstekSecenekleri): Observable<unknown> => {
    calls.push({ yontem: 'GET', yol: path, secenek: option });
    if (path.endsWith('/form-varsayilanlari')) return of(DEFAULTS);
    if (path.endsWith('/musait-arac')) return of(AVAILABLE);
    if (path.endsWith('/hesapla')) return of({ ok: false, hata: 'Araç seçin.' });
    // F4.3b kimlikle etiket uçları (PII yok: kimlik + ad + tip / plaka + grup + durum).
    if (path === `/api/ui/v1/secim/musteri/${CUSTOMER_ID}`) {
      return of({ id: CUSTOMER_ID, etiket: 'Ayşe Yılmaz', tip: 'Bireysel' });
    }
    if (path === `/api/ui/v1/secim/arac/${OTHER_VEHICLE}`) {
      return of({
        id: OTHER_VEHICLE,
        etiket: '06 XYZ 42 — Renault Clio',
        plaka: '06 XYZ 42',
        grup: 'B',
        durum: 'Musait',
      });
    }
    if (path.endsWith('/ek-hizmet-katalogu')) return of(catalogResponse);
    if (path === '/api/ui/v1/secim/ek-hizmet') {
      return of([
        { id: DEFINITION_ID, etiket: 'Bebek koltuğu', kod: 'BEBEK' },
        { id: NAV_ID, etiket: 'Navigasyon', kod: 'NAV' },
        { id: 'sys-1', etiket: 'Genç sürücü', kod: 'SYS-GENC' },
      ]);
    }
    if (/^\/api\/ui\/v1\/secim\/(musteri|arac)\//.test(path)) {
      return throwError(() => serverError(404, 'bulunamadi', 'Bulunamadı.'));
    }
    if (path.startsWith('/api/ui/v1/secim/')) return of([]);
    return throwError(() => serverError(404, 'bilinmeyen', 'yok'));
  };
  const writeValue =
    (method: string) => (path: string, body?: unknown, option?: IstekSecenekleri) => {
      const c = { yontem: method, yol: path, govde: body, secenek: option };
      calls.push(c);
      return write(c);
    };
  return {
    cagrilar: calls,
    api: { get, post: writeValue('POST'), put: writeValue('PUT'), delete: writeValue('DELETE') },
  };
}

async function exchangeRate(
  query: Record<string, string>,
  write: (c: Cagri) => Observable<unknown> = () => of({}),
) {
  const fake = fakeApi(write);
  const toast = { basari: vi.fn(), bilgi: vi.fn(), uyari: vi.fn(), hata: vi.fn() };
  TestBed.configureTestingModule({
    providers: [
      ...provideTranslation(),
      RentalFormState,
      { provide: ApiIstemcisi, useValue: fake.api },
      { provide: ToastService, useValue: toast },
      { provide: ConfirmService, useValue: { ask: vi.fn(async () => true) } },
      { provide: Router, useValue: { navigate: vi.fn(async () => true) } },
      {
        provide: SessionService,
        useValue: { izinVar: () => true, ben: () => ({ rol: 'Operator' }) },
      },
      {
        provide: ActivatedRoute,
        useValue: {
          snapshot: {
            paramMap: convertToParamMap({}),
            queryParamMap: convertToParamMap(query),
            routeConfig: null,
            pathFromRoot: [],
            params: {},
          },
          queryParamMap: of(convertToParamMap(query)),
          fragment: of(null),
        },
      },
    ],
  });
  await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  const d = TestBed.inject(RentalFormState);
  TestBed.tick();
  return { d, toast, cagrilar: fake.cagrilar };
}

describe('KiraFormuDurumu (yeni kira)', () => {
  it('?varac&vfrom&vto&musteriId ile form dolu açılır (etiketler sunucudan), kirli değildir', async () => {
    const { d, cagrilar } = await exchangeRate({
      varac: VEHICLE_ID,
      vfrom: '2026-10-01',
      vto: '2026-10-04',
      musteriId: CUSTOMER_ID,
    });
    const f = d.form.controls;
    expect(f.arac.value?.id).toBe(VEHICLE_ID);
    expect(f.arac.value?.etiket).toBe('34 ABC 123 — Fiat Egea');
    expect(f.ayna.controls.arac.value?.etiket).toBe('34 ABC 123 — Fiat Egea');
    expect(f.basTar.value).toBe('2026-10-01T06:00:00.000Z');
    expect(f.bitTar.value).toBe('2026-10-04T06:00:00.000Z');
    // F4.3b: "Bağlantıdaki müşteri" yerine kimlikle çözülen gerçek ad (secim/musteri/{id}).
    expect(f.musteri.value).toEqual({ id: CUSTOMER_ID, etiket: 'Ayşe Yılmaz' });
    expect(f.ayna.controls.musteri.value?.etiket).toBe('Ayşe Yılmaz');
    expect(cagrilar.some((c) => c.yol === `/api/ui/v1/secim/musteri/${CUSTOMER_ID}`)).toBe(true);
    expect(f.fiyatTuru.value).toBe('KDV Dahil Günlük');
    expect(d.availabilityForm.getRawValue()).toEqual({
      vfrom: '2026-10-01',
      vto: '2026-10-04',
      vgrup: null,
    });
    expect(d.isDirty()).toBe(false);
    const available = cagrilar.find((c) => c.yol.endsWith('/musait-arac'));
    expect(available?.secenek?.parametreler).toEqual({
      vfrom: '2026-10-01',
      vto: '2026-10-04',
      vgrup: null,
    });
  });

  it('bağlantıdaki araç o aralıkta müsait değilse seçim düşer ve not gösterilir', async () => {
    const { d } = await exchangeRate({
      varac: OTHER_VEHICLE,
      vfrom: '2026-10-01',
      vto: '2026-10-04',
    });
    expect(d.form.controls.arac.value).toBeNull();
    expect(d.availabilityNote()).toBe('Seçili araç bu aralıkta müsait değil; seçim kaldırıldı.');
  });

  it('doğrulama hatası: alan işaretlenir, değerler korunur, hatalı alana gidilir', async () => {
    const response = new Subject<unknown>();
    const { d, cagrilar } = await exchangeRate(
      { varac: VEHICLE_ID, vfrom: '2026-10-01', vto: '2026-10-04', musteriId: CUSTOMER_ID },
      () => response,
    );
    d.form.controls.gunlukUcret.setValue('-5');
    d.form.controls.aciklama.setValue('Uzun açıklama');
    const go = vi.fn();
    d.kaydet(go);
    d.kaydet(go); // çift tık: tek istek
    expect(cagrilar.filter((c) => c.yontem === 'POST')).toHaveLength(1);
    response.error(
      serverError(400, 'dogrulama', 'Günlük ücret negatif olamaz.', {
        gunlukUcret: ['Günlük ücret negatif olamaz.'],
      }),
    );
    expect(d.form.controls.gunlukUcret.errors?.['sunucu']).toEqual([
      'Günlük ücret negatif olamaz.',
    ]);
    expect(d.form.controls.ayna.controls.gunlukUcret.errors?.['sunucu']).toBeDefined();
    expect(d.form.controls.gunlukUcret.value).toBe('-5');
    expect(d.form.controls.aciklama.value).toBe('Uzun açıklama');
    expect(go).toHaveBeenCalled();
  });

  it('müsaitlik `cakisma` (alansız) formu silmez; mesaj form üstünde', async () => {
    const { d } = await exchangeRate(
      { varac: VEHICLE_ID, vfrom: '2026-10-01', vto: '2026-10-04', musteriId: CUSTOMER_ID },
      () => throwError(() => serverError(409, 'cakisma', 'Araç bu tarihlerde müsait değil.')),
    );
    d.form.controls.aciklama.setValue('Korunacak');
    d.form.controls.aciklama.markAsDirty(); // kullanıcı yazdı
    d.kaydet(() => undefined);
    expect(d.kayit.genelHatalar()).toEqual(['Araç bu tarihlerde müsait değil.']);
    expect(d.form.controls.aciklama.value).toBe('Korunacak');
    expect(d.form.controls.arac.value?.id).toBe(VEHICLE_ID);
    expect(d.isDirty()).toBe(true);
  });

  it('F7: yeni müşteri dolu ama ana form geçersiz (araç yok) → cari AÇILMAZ (yetim PII yok)', async () => {
    const { d, cagrilar } = await exchangeRate({ vfrom: '2026-10-01', vto: '2026-10-04' });
    d.form.controls.arac.setValue(null);
    d.newCustomerForm.patchValue({ ad: 'Yetim', tcKimlik: '10000000146' });
    const go = vi.fn();
    d.kaydet(go);
    expect(cagrilar.filter((c) => c.yontem === 'POST')).toEqual([]);
    expect(go).toHaveBeenCalled();
    expect(d.form.controls.arac.touched).toBe(true);
    expect(d.newCustomerForm.getRawValue().ad).toBe('Yetim'); // girilen bilgi korunur
  });

  it('müşteri seçilmemiş ama yeni müşteri alanları doluysa önce cari açılır, sonra kira', async () => {
    const { d, cagrilar } = await exchangeRate(
      { varac: VEHICLE_ID, vfrom: '2026-10-01', vto: '2026-10-04' },
      (c) =>
        c.yol.endsWith('/kiralar/musteri')
          ? of({ id: CUSTOMER_ID, etiket: 'Ayşe Yılmaz' })
          : of({ id: 'k1', sozlesmeNo: '2026011001001', uyari: null }),
    );
    d.newCustomerForm.patchValue({ ad: 'Ayşe', soyad: 'Yılmaz', tcKimlik: '10000000146' });
    d.kaydet(() => undefined);
    const writes = cagrilar.filter((c) => c.yontem === 'POST').map((c) => c.yol);
    expect(writes).toEqual(['/api/ui/v1/kiralar/musteri', '/api/ui/v1/kiralar']);
    const rental = cagrilar.find((c) => c.yol === '/api/ui/v1/kiralar');
    expect((rental?.govde as { musteriId: string }).musteriId).toBe(CUSTOMER_ID);
    // PII formda kalmaz; yeni kira formu sıfırlanır (kayıt yapıldı).
    expect(d.newCustomerForm.getRawValue().tcKimlik).toBeNull();
    expect(d.isDirty()).toBe(false);
  });
  it('penceresiz ?varac= plaka etiketini kimlikle çözer (secim/arac/{id})', async () => {
    const { d, cagrilar } = await exchangeRate({ varac: OTHER_VEHICLE });
    expect(d.form.controls.arac.value).toMatchObject({
      id: OTHER_VEHICLE,
      etiket: '06 XYZ 42 — Renault Clio',
      plaka: '06 XYZ 42',
    });
    expect(cagrilar.some((c) => c.yol === `/api/ui/v1/secim/arac/${OTHER_VEHICLE}`)).toBe(true);
    expect(d.isDirty()).toBe(false);
  });

  it('çözülemeyen kimlik (404) geçici etiketi korur; kayıt yine kimlikle yapılır', async () => {
    const UNKNOWN = '0b0e7c1a-2222-4aaa-8bbb-00000000ffff';
    const { d } = await exchangeRate({ musteriId: UNKNOWN });
    expect(d.form.controls.musteri.value).toEqual({
      id: UNKNOWN,
      etiket: 'Bağlantıdaki müşteri',
    });
  });

  it('kaynak / özel kod önerileri yazılanla sunucuda aranır (q), boşken q yok', async () => {
    const { d, cagrilar } = await exchangeRate({});
    await wait(300);
    const last = (endpoint: string) =>
      cagrilar.filter((c) => c.yol === `/api/ui/v1/secim/${endpoint}`).at(-1)?.secenek
        ?.parametreler;
    expect(last('rezervasyon-kaynagi')).toEqual({ q: null, limit: 20 });
    expect(last('ozel-kod')).toEqual({ q: null, limit: 20 });
    // Hızlı Giriş aynası da kanoniği sürer → arama.
    d.form.controls.ayna.controls.kaynak.setValue(' Web ');
    d.form.controls.ozelKod.setValue('KAMP');
    await wait(300);
    expect(last('rezervasyon-kaynagi')).toEqual({ q: 'Web', limit: 20 });
    expect(last('ozel-kod')).toEqual({ q: 'KAMP', limit: 20 });
  });

  it('ek hizmet matrisi: işaret satır ekler, kaldırma çıkarır; hesap parametresine girer', async () => {
    const { d } = await exchangeRate({ varac: VEHICLE_ID, vfrom: '2026-10-01', vto: '2026-10-04' });
    const oge = d.addOnCatalog.veri()?.ogeler[0];
    expect(oge?.ad).toBe('Bebek koltuğu');
    if (!oge) return;
    d.addOnSelection(oge, true);
    d.addOnSelection(oge, true); // ikinci işaret yinelenmez
    expect(d.form.controls.ekHizmetler.getRawValue()).toEqual([
      { tanim: { id: DEFINITION_ID, etiket: 'Bebek koltuğu' }, miktar: 1 },
    ]);
    expect(d.form.controls.ekHizmetler.dirty).toBe(true);
    d.addOnSelection(oge, false);
    expect(d.form.controls.ekHizmetler.length).toBe(0);
  });

  it('kayıtlı kiraya ekleme kaynağı katalogdan: etiket "Ad (birim net)", SYS yok', async () => {
    const { d } = await exchangeRate({});
    const list = await firstValueFrom(d.addOnSource('bebek', 20));
    expect(list).toEqual([{ id: DEFINITION_ID, etiket: 'Bebek koltuğu (75,50 ₺ net)' }]);
  });
  it('katalog KESİKSE (toplam > satır) ekleme kaynağı sunucuda arar (q); katalogdaki öğe fiyatlı, SYS yok', async () => {
    catalogResponse = { ...FULL_CATALOG, toplam: 206 };
    const { d, cagrilar: calls } = await exchangeRate({});
    const list = await firstValueFrom(d.addOnSource('nav', 20));
    const search = calls.filter((c) => c.yol === '/api/ui/v1/secim/ek-hizmet').at(-1);
    expect(search?.secenek?.parametreler).toEqual({ q: 'nav', limit: 20 });
    expect(list).toEqual([
      { id: DEFINITION_ID, etiket: 'Bebek koltuğu (75,50 ₺ net)' },
      { id: NAV_ID, etiket: 'Navigasyon' },
    ]);
  });
});
