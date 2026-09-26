import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { type Observable, Subject, firstValueFrom, of, throwError } from 'rxjs';
import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi, type IstekSecenekleri } from '@core/api/api-istemcisi';
import { SERVER_ERROR } from '@core/form/sunucu-hatalari';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { provideTranslation } from '@core/i18n/ceviri';
import { DUPLICATE_CALLER_SHOWS, REFRESH_ON_DUPLICATE, SILENT } from '@core/oturum/request-context';
import { SessionService } from '@core/oturum/session-service';
import type { RentalDetailResponse, RentalContract } from '../kira-tipleri';
import type { CollectionInfo } from './finans-tipleri';
import { RentalFinanceState } from './rental-finance-state';

const RENTAL_ID = '0b0e7c1a-1111-4aaa-8bbb-000000000001';
const CUSTOMER_ID = '0b0e7c1a-2222-4aaa-8bbb-000000000002';
const K1 = 'aaaaaaaa-0000-4000-8000-000000000001';
const K2 = 'aaaaaaaa-0000-4000-8000-000000000002';

const serverError = (status: number, code: string, detail: string, errors?: object) =>
  toApiError(new HttpErrorResponse({ status, error: { status, kod: code, detail, errors } }));
const networkError = () => toApiError(new HttpErrorResponse({ status: 0 }));

const tahsilat = (key: string, defaultAmount = 2600): CollectionInfo => ({
  anahtar: key,
  cariId: CUSTOMER_ID,
  rentalId: RENTAL_ID,
  doviz: 'TRY',
  varsayilanTutar: defaultAmount,
});

/** Yalnız panelin okuduğu alanlar dolu; tip gereği gerisi elle (formül/hesap yok). */
function detay(
  t: CollectionInfo | null,
  extra: Partial<RentalContract> = {},
): RentalDetailResponse {
  const rental = {
    id: RENTAL_ID,
    sozlesmeNo: '2026220901001',
    durum: 'Kirada',
    musteriId: CUSTOMER_ID,
    genelToplam: 3600,
    tahsilat: 1000,
    bakiye: 2600,
    doviz: 'TL',
    depozito: 500,
    damgaVergisi: null,
    ...extra,
  } as unknown as RentalContract;
  return {
    kira: rental,
    musteri: { id: CUSTOMER_ID, ad: 'Ayşe Yılmaz' },
    ikinciSurucu: null,
    arac: null,
    islemSubeAdi: null,
    teslimAlanPersonelAd: null,
    teslimEdenPersonelAd: null,
    ekHizmetler: [],
    doviz: null,
    paylasim: null,
    yetkiler: { operasyon: true, silme: true, finans: true },
    toplamlar: { ekHizmetToplam: 0, cezaToplam: 0 },
    tahsilat: t,
  };
}

interface Cagri {
  readonly yol: string;
  readonly govde: unknown;
  readonly secenek?: IstekSecenekleri;
}

async function exchangeRate(write: (c: Cagri) => Observable<unknown>) {
  const calls: Cagri[] = [];
  const api = {
    get: vi.fn(() => of([])),
    post: (path: string, body: unknown, option?: IstekSecenekleri) => {
      const c = { yol: path, govde: body, secenek: option };
      calls.push(c);
      return write(c);
    },
  };
  const toast = { basari: vi.fn(), bilgi: vi.fn(), uyari: vi.fn(), hata: vi.fn() };
  const approval = { ask: vi.fn(async () => true) };
  TestBed.configureTestingModule({
    providers: [
      ...provideTranslation(),
      RentalFinanceState,
      { provide: ApiIstemcisi, useValue: api },
      { provide: ToastService, useValue: toast },
      { provide: ConfirmService, useValue: approval },
      {
        provide: SessionService,
        useValue: { izinVar: () => true, registerCleanup: () => () => undefined },
      },
    ],
  });
  await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  const f = TestBed.inject(RentalFinanceState);
  const changed = vi.fn();
  f.changed = changed;
  const provideDetail = (d: RentalDetailResponse) => {
    f.setDetail(d);
    TestBed.tick();
  };
  return { f, cagrilar: calls, toast, onay: approval, degisti: changed, detayVer: provideDetail };
}

const collections = (c: readonly Cagri[]) => c.filter((x) => x.yol.endsWith('/finans/tahsilat'));
const body = (c: Cagri | undefined) => c?.govde as Record<string, unknown>;

describe('KiraFinansDurumu — tahsilat (deterministik anahtar)', () => {
  it('ön-doldurur; gövdede detaydaki anahtar, BAŞLIK YOK; 2xx sonrası yeni detayın anahtarıyla ikinci meşru tahsilat', async () => {
    const { f, cagrilar, degisti, detayVer, toast } = await exchangeRate(() => of({ id: 'c1' }));
    detayVer(detay(tahsilat(K1)));
    expect(f.nakit.form.getRawValue()).toMatchObject({ tutar: '2600.00', doviz: 'TRY' });

    f.nakit.form.controls.tutar.setValue('1500.50'); // rc-para-girdisi "1.500,50" yazımının değeri
    f.doCollection(f.nakit);
    const first = collections(cagrilar)[0];
    expect(body(first)).toMatchObject({
      cariId: CUSTOMER_ID,
      kiraId: RENTAL_ID,
      tahsilatAnahtar: K1,
      tutar: '1500.50',
      hesap: 'Kasa',
      doviz: 'TRY',
    });
    expect('kur' in body(first)).toBe(false); // TRY'de kur gönderilmez
    expect(first?.secenek?.islemAnahtari).toBeUndefined(); // deterministikte başlık tüketilmez
    expect(toast.basari).toHaveBeenCalledWith('Tahsilat kaydedildi.');
    expect(degisti).toHaveBeenCalledTimes(1);

    // Yeni detay gelene dek ikinci gönderim yok (eski anahtar bayat).
    expect(f.nakit.kopya.canSubmit()).toBe(false);
    f.doCollection(f.nakit);
    expect(collections(cagrilar)).toHaveLength(1);

    detayVer(detay(tahsilat(K2, 1099.5)));
    expect(f.nakit.form.getRawValue().tutar).toBe('1099.50');
    f.doCollection(f.nakit);
    expect(collections(cagrilar)).toHaveLength(2);
    expect(body(collections(cagrilar)[1])['tahsilatAnahtar']).toBe(K2);
  });

  it('çift tık tek istek (istek uçarken ikinci gönderim yok sayılır)', async () => {
    const response = new Subject<unknown>();
    const { f, cagrilar, detayVer } = await exchangeRate(() => response);
    detayVer(detay(tahsilat(K1)));
    f.doCollection(f.nakit);
    f.doCollection(f.nakit);
    expect(collections(cagrilar)).toHaveLength(1);
    response.next({ id: 'c1' });
    response.complete();
  });

  it('ağ hatası sonrası yeniden deneme AYNI anahtar + AYNI gövde — arada detay tazelense bile', async () => {
    let n = 0;
    const { f, cagrilar, detayVer } = await exchangeRate(() =>
      ++n === 1 ? throwError(() => networkError()) : of({ id: 'c1' }),
    );
    detayVer(detay(tahsilat(K1)));
    f.doCollection(f.nakit);
    // Başka bir işlem kirayı tazeledi (yeni anahtar geldi); ilk istek belki yazıldı → anahtar DONUK kalmalı.
    detayVer(detay(tahsilat(K2, 100)));
    f.doCollection(f.nakit);
    const [a, b] = collections(cagrilar);
    expect(body(b)['tahsilatAnahtar']).toBe(K1);
    expect(JSON.stringify(b?.govde)).toBe(JSON.stringify(a?.govde));
    expect(b?.secenek?.islemAnahtari).toBeUndefined(); // anahtarsız/başlıklı tekrar yok
  });

  it('açık (kirli) form eski satır kopyasını korur: bayat anahtar gönderilir → sunucu 409 verir', async () => {
    const { f, cagrilar, detayVer } = await exchangeRate(() => of({ id: 'c1' }));
    detayVer(detay(tahsilat(K1)));
    f.nakit.form.controls.tutar.setValue('200.00');
    f.nakit.form.markAsDirty();
    detayVer(detay(tahsilat(K2, 900)));
    expect(f.nakit.form.getRawValue().tutar).toBe('200.00'); // yazılan korunur
    f.doCollection(f.nakit);
    expect(body(collections(cagrilar)[0])['tahsilatAnahtar']).toBe(K1);
  });

  it("H1 (#316): Nakit uçarken ve sonuçlanıp tazeleme beklerken Kart gönderilemez; Kart'a yazılan tutar tazelemede EZİLMEZ ve aynen gider", async () => {
    const response = new Subject<unknown>();
    let n = 0;
    const { f, cagrilar, detayVer } = await exchangeRate(() =>
      ++n === 1 ? response : of({ id: 'c2' }),
    );
    detayVer(detay(tahsilat(K1)));
    expect(f.isCollectionBusy()).toBe(false);
    f.kart.form.controls.tutar.setValue('600.00'); // kullanıcı Kart/Havale'ye 600 yazdı
    f.kart.form.markAsDirty();

    f.nakit.form.controls.tutar.setValue('700.00');
    f.doCollection(f.nakit); // istek uçuyor (aynı anahtar K1)
    expect(f.isCollectionBusy()).toBe(true);
    f.doCollection(f.kart);
    expect(collections(cagrilar)).toHaveLength(1);

    response.next({ id: 'c1' }); // 2xx → kira tazeleniyor
    response.complete();
    expect(f.isCollectionBusy()).toBe(true);
    expect(f.isCollectionRefreshing()).toBe(true);
    f.doCollection(f.kart);
    expect(collections(cagrilar)).toHaveLength(1);

    detayVer(detay(tahsilat(K2, 1900))); // 2.600 − 700 = 1.900
    expect(f.isCollectionBusy()).toBe(false);
    expect(f.kart.form.getRawValue().tutar).toBe('600.00'); // öneri (1.900) yazılanı ezmedi
    f.doCollection(f.kart);
    const t = collections(cagrilar);
    // #318 L2: Nakit'in 2xx'i K1'i kesin tüketti → kirli Kart da K2'yi aldı (gereksiz 409 turu yok).
    expect(t.map((c) => [body(c)['hesap'], body(c)['tahsilatAnahtar'], body(c)['tutar']])).toEqual([
      ['Kasa', K1, '700.00'],
      ['Banka', K2, '600.00'],
    ]);
  });

  it('#318 L1: tahsilat sonrası tazeleme hata verdi → "Yeniden yükle" (yenile → sayfa), başarılı okumada düğmeler açılır', async () => {
    const { f, cagrilar, detayVer, degisti } = await exchangeRate(() => of({ id: 'c1' }));
    detayVer(detay(tahsilat(K1)));
    f.setDetailError(true); // ilgisiz hata bayrağı: tahsilat tazeleme beklemiyorsa gösterilmez
    expect(f.collectionLoadFailed()).toBe(false);
    f.setDetailError(false);

    f.doCollection(f.nakit);
    expect(degisti).toHaveBeenCalledTimes(1);
    f.setDetailError(true); // sayfa: tazeleme 503, ekran son iyi veriyle
    expect(f.collectionLoadFailed()).toBe(true);
    expect(f.isCollectionBusy()).toBe(true); // bayat K1 ile gönderim yok
    f.doCollection(f.kart);
    expect(collections(cagrilar)).toHaveLength(1);

    f.yenile(); // "Yeniden yükle"
    expect(degisti).toHaveBeenCalledTimes(2);
    f.setDetailError(false);
    detayVer(detay(tahsilat(K2, 1900)));
    expect(f.collectionLoadFailed()).toBe(false);
    expect(f.isCollectionBusy()).toBe(false);
    f.doCollection(f.kart);
    expect(body(collections(cagrilar)[1])['tahsilatAnahtar']).toBe(K2);
  });

  it('409 mukerrer: otomatik tekrar YOK; "Kira kaydı değişmiş" başlığı (Mükerrer işlem değil); sonra yeni anahtar', async () => {
    const { f, cagrilar, detayVer, degisti, toast } = await exchangeRate(() =>
      throwError(() =>
        serverError(409, 'mukerrer', 'Kiranın bakiyesi bu ekran açıldıktan sonra değişti.'),
      ),
    );
    detayVer(detay(tahsilat(K1)));
    f.nakit.form.controls.tutar.setValue('300.00');
    f.nakit.form.markAsDirty();
    f.doCollection(f.nakit);
    expect(collections(cagrilar)).toHaveLength(1);

    // mukerrer toast'u çağıranda (sınıf tekrar bilgisine bağlı, M-C): nötr başlık + sunucu detail'ı; istek sessiz
    // DEĞİL (ağ/5xx/yetki genel katmanda görünür).
    const context = collections(cagrilar)[0]?.secenek?.context;
    expect(context?.get(DUPLICATE_CALLER_SHOWS)).toBe(true);
    expect(context?.get(SILENT)).toBe(false);
    expect(toast.uyari).toHaveBeenCalledWith(
      'Kiranın bakiyesi bu ekran açıldıktan sonra değişti. Kayıt yeniden yüklendi.',
      { baslik: 'Kira kaydı değişmiş' },
    );
    context?.get(REFRESH_ON_DUPLICATE)?.(); // interceptor'ın yaptığı
    expect(degisti).toHaveBeenCalledTimes(1);
    expect(f.nakit.gonderim.genelHatalar()).toEqual([]); // form üstüne "mükerrer" yazılmaz
    // HIGH-1: tutar TEMİZLENİR (kullanıcı güncel bakiyeye bakıp bilinçli girer); diğer alanlar korunur.
    expect(f.nakit.form.getRawValue().tutar).toBeNull();

    expect(f.nakit.kopya.canSubmit()).toBe(false);
    detayVer(detay(tahsilat(K2, 1)));
    expect(f.nakit.kopya.kopya()?.anahtar).toBe(K2);
    expect(f.nakit.form.getRawValue().tutar).toBeNull(); // 409 sonrası yeniden ön-doldurulmaz
    expect(collections(cagrilar)).toHaveLength(1); // kendiliğinden yeniden gönderim yok
  });

  it('HIGH-1: kaybolan yanıt → AYNI anahtarla tekrar → 409 + mevcut: form temizlenir, ön-doldurulmaz, ikinci istek yok', async () => {
    let n = 0;
    const { f, cagrilar, detayVer } = await exchangeRate(() =>
      ++n === 1
        ? throwError(() => networkError()) // ilk istek yazıldı, yanıt kayboldu
        : throwError(() =>
            toApiError(
              new HttpErrorResponse({
                status: 409,
                error: {
                  status: 409,
                  kod: 'mukerrer',
                  detail:
                    'Bu tahsilat zaten kaydedildi (No T-000042, 500,00 TRY); yeni tahsilat yazılmadı.',
                  mevcut: {
                    id: 'c1',
                    belgeNo: 'T-000042',
                    tutar: 500,
                    doviz: 'TRY',
                    ayniIcerik: true,
                  },
                },
              }),
            ),
          ),
    );
    detayVer(detay(tahsilat(K1)));
    f.nakit.form.patchValue({ tutar: '500.00', aciklama: 'Ali' });
    f.nakit.form.markAsDirty();
    f.doCollection(f.nakit); // ağ hatası
    f.doCollection(f.nakit); // doğru tekrar: AYNI anahtar
    const [a, b] = collections(cagrilar);
    expect(body(b)['tahsilatAnahtar']).toBe(K1);
    expect(b?.govde).toEqual(a?.govde);
    // Form tamamen temizlendi (tutar + not); kaydı yeniden yükleyen detay yeni tutar ÖNERMEZ.
    expect(f.nakit.form.getRawValue()).toMatchObject({ tutar: null, aciklama: null });
    detayVer(detay(tahsilat(K2, 2100)));
    expect(f.nakit.form.getRawValue().tutar).toBeNull();
    expect(f.nakit.kopya.canSubmit()).toBe(true); // yeni anahtarla BİLİNÇLİ yeni tahsilat mümkün
    expect(collections(cagrilar)).toHaveLength(2);
  });

  it('dogrulama: alan hatası alana yazılır, değerler korunur', async () => {
    const { f, detayVer } = await exchangeRate(() =>
      throwError(() =>
        serverError(400, 'dogrulama', 'Kur pozitif olmalıdır.', {
          kur: ['Kur pozitif olmalıdır.'],
        }),
      ),
    );
    detayVer(detay(tahsilat(K1)));
    f.nakit.form.patchValue({ doviz: 'USD', kur: '32.5' });
    f.doCollection(f.nakit);
    expect(f.nakit.form.controls.kur.errors?.[SERVER_ERROR]).toEqual(['Kur pozitif olmalıdır.']);
    expect(f.nakit.form.getRawValue()).toMatchObject({ doviz: 'USD', kur: '32.5' });
  });

  it('dövizde boş kur gönderilmez (sunucu çözer), dolu kur gider', async () => {
    const { f, cagrilar, detayVer } = await exchangeRate(() => of({ id: 'c1' }));
    detayVer(detay(tahsilat(K1)));
    f.kart.form.patchValue({ doviz: 'USD', kur: null, tutar: '10.00' });
    f.doCollection(f.kart);
    expect(body(collections(cagrilar)[0])).toMatchObject({ doviz: 'USD', hesap: 'Banka' });
    expect('kur' in body(collections(cagrilar)[0])).toBe(false);
  });

  it('iptal/izinsiz kira (tahsilat satırı yok) → gönderim yapılmaz', async () => {
    const { f, cagrilar, detayVer } = await exchangeRate(() => of({ id: 'c1' }));
    detayVer(detay(null));
    f.doCollection(f.nakit);
    expect(cagrilar).toHaveLength(0);
  });
});

describe('KiraFinansDurumu — başlık anahtarlı işlemler', () => {
  it('ödeme: anahtar ilk gönderimde üretilir, ağ hatasında AYNI, 2xx sonrası YENİ; depozito kendi anahtarını kullanır', async () => {
    let n = 0;
    const { f, cagrilar, detayVer } = await exchangeRate((c) =>
      c.yol.endsWith('/odeme') && ++n === 1 ? throwError(() => networkError()) : of({ id: 'x' }),
    );
    detayVer(detay(tahsilat(K1)));
    f.paymentForm.patchValue({ tutar: '75.00' });
    f.makePayment(); // ağ hatası
    f.makePayment(); // yeniden deneme: aynı anahtar
    const payments = cagrilar.filter((c) => c.yol.endsWith('/finans/odeme'));
    expect(payments).toHaveLength(2);
    const [a, b] = payments;
    expect(a?.secenek?.islemAnahtari).toMatch(/^[0-9a-f-]{36}$/);
    expect(b?.secenek?.islemAnahtari).toBe(a?.secenek?.islemAnahtari);
    expect(b?.govde).toEqual(a?.govde);
    expect(body(a)).toMatchObject({ cariId: CUSTOMER_ID, hesap: 'Banka', tutar: '75.00' });
    expect('kiraId' in body(a)).toBe(false);

    // 2xx sonrası form sıfırlandı; ikinci meşru ödeme YENİ anahtarla.
    f.paymentForm.patchValue({ tutar: '20.00' });
    f.makePayment();
    const third = cagrilar.filter((c) => c.yol.endsWith('/finans/odeme'))[2];
    expect(third?.secenek?.islemAnahtari).not.toBe(a?.secenek?.islemAnahtari);

    // Başka işlem türü kendi anahtarıyla.
    f.takeDepositForm.patchValue({ tutar: '500.00', hesap: 'Kasa' });
    f.takeDeposit();
    const dep = cagrilar.find((c) => c.yol.endsWith('/depozito/al'));
    expect(dep?.secenek?.islemAnahtari).toBeDefined();
    expect(dep?.secenek?.islemAnahtari).not.toBe(third?.secenek?.islemAnahtari);
    // mukerrer bildirimi çekirdeğin form notunda (interceptor toast'u değil); kayıt settled'da yenilenir.
    expect(dep?.secenek?.context?.get(DUPLICATE_CALLER_SHOWS)).toBe(true);
  });

  it('409 mukerrer mevcutsuz (ödeme): otomatik tekrar yok; anahtar KORUNUR, gövde donar (DEVIR §5 gece dersi)', async () => {
    let n = 0;
    const { f, cagrilar, detayVer } = await exchangeRate(() =>
      ++n === 1
        ? throwError(() => serverError(409, 'mukerrer', 'Bu işlem zaten kaydedilmiş.'))
        : of({ id: 'x' }),
    );
    detayVer(detay(tahsilat(K1)));
    f.paymentForm.patchValue({ tutar: '75.00' });
    f.makePayment();
    expect(cagrilar).toHaveLength(1);
    expect(f.paymentSubmission.frozen()).not.toBeNull();
    f.paymentForm.patchValue({ tutar: '99.00' }); // kilitli formu yazılımla değiştirmek gövdeyi değiştirmez
    f.makePayment();
    expect(cagrilar[1]?.secenek?.islemAnahtari).toBe(cagrilar[0]?.secenek?.islemAnahtari);
    expect(cagrilar[1]?.govde).toEqual(cagrilar[0]?.govde);
  });

  it('3. tur M-A + L-2: 409 + mevcut İÇERİK FARKLI (başka sekme yazdı) → form SİLİNMEZ; dokunulmamış ön-dolu tutar yeni bakiyeyle yenilenir', async () => {
    let n = 0;
    const { f, cagrilar, detayVer, degisti, toast } = await exchangeRate(() =>
      ++n === 1
        ? throwError(() =>
            toApiError(
              new HttpErrorResponse({
                status: 409,
                error: {
                  status: 409,
                  kod: 'mukerrer',
                  detail:
                    'Bu ekran açıldıktan sonra başka bir tahsilat yazıldı (No T-9, 100,00 TRY); girdiğiniz 2.600,00 TRY YAZILMADI.',
                  mevcut: { id: 'a', belgeNo: 'T-9', tutar: 100, doviz: 'TRY', ayniIcerik: false },
                },
              }),
            ),
          )
        : of({ id: 'c2' }),
    );
    detayVer(detay(tahsilat(K1))); // ön-dolu 2.600 (dokunulmamış)
    f.nakit.form.patchValue({ aciklama: 'B kasası' });
    f.doCollection(f.nakit);
    const context = collections(cagrilar)[0]?.secenek?.context;
    context?.get(REFRESH_ON_DUPLICATE)?.();
    expect(degisti).toHaveBeenCalledTimes(1);
    expect(f.nakit.form.getRawValue()).toMatchObject({ tutar: '2600.00', aciklama: 'B kasası' });
    expect(toast.uyari).toHaveBeenCalledWith(
      expect.stringContaining('girdiğiniz 2.600,00 TRY YAZILMADI'),
      {
        baslik: 'Başka bir tahsilat yazıldı — tutarınız kaydedilmedi',
      },
    );
    // L-2: tazelenen kayıt (A'nın 100'ü yazıldı → yeni anahtar K2, yeni öneri 2.500). Tutar elle YAZILMADIĞI için
    // eski bakiye (2.600) yerine yeni öneri gelir; diğer alanlar korunur.
    detayVer(detay(tahsilat(K2, 2500)));
    expect(f.nakit.form.getRawValue()).toMatchObject({ tutar: '2500.00', aciklama: 'B kasası' });
    expect(f.nakit.kopya.kopya()?.anahtar).toBe(K2);
    // Sonraki (aynı anahtarlı) tazelemeler tutara bir daha dokunmaz.
    detayVer(detay(tahsilat(K2, 2400)));
    expect(f.nakit.form.getRawValue().tutar).toBe('2500.00');
    f.doCollection(f.nakit); // kullanıcı bakiyeye bakıp BİLİNÇLİ gönderir
    expect(collections(cagrilar)).toHaveLength(2);
    expect(body(collections(cagrilar)[1])).toMatchObject({
      tahsilatAnahtar: K2,
      tutar: '2500.00',
    });
  });

  it('L-2: M-A sonrası kullanıcının ELLE yazdığı tutar yeni bakiyeyle EZİLMEZ', async () => {
    const { f, detayVer } = await exchangeRate(() =>
      throwError(() =>
        toApiError(
          new HttpErrorResponse({
            status: 409,
            error: {
              status: 409,
              kod: 'mukerrer',
              detail: 'başka bir tahsilat yazıldı; girdiğiniz 700,00 TRY YAZILMADI.',
              mevcut: { id: 'a', belgeNo: 'T-9', tutar: 100, doviz: 'TRY', ayniIcerik: false },
            },
          }),
        ),
      ),
    );
    detayVer(detay(tahsilat(K1)));
    f.nakit.form.controls.tutar.setValue('700.00');
    f.nakit.form.controls.tutar.markAsDirty(); // kullanıcı yazdı
    f.doCollection(f.nakit);
    detayVer(detay(tahsilat(K2, 2500)));
    expect(f.nakit.form.getRawValue().tutar).toBe('700.00');
    expect(f.nakit.kopya.kopya()?.anahtar).toBe(K2);
  });

  /** M-C: sunucunun sonucu (elle): 1. istek 500 yazıldı ama yanıt kayboldu; 2. istek aynı anahtar + 600 → 409 mevcut{500, farklı}. */
  const previousAttemptSaved = () =>
    toApiError(
      new HttpErrorResponse({
        status: 409,
        error: {
          status: 409,
          kod: 'mukerrer',
          detail:
            'Bu ekran açıldıktan sonra başka bir tahsilat yazıldı (No T-42, 500,00 TRY); girdiğiniz 600,00 TRY YAZILMADI.',
          mevcut: { id: 'c1', belgeNo: 'T-42', tutar: 500, doviz: 'TRY', ayniIcerik: false },
        },
      }),
    );

  it('4. tur M-C: kaybolan yanıt → tutar değiştirilip AYNI anahtarla tekrar → "Önceki denemeniz kaydedilmiş … 600 YAZILMADI", tutar TEMİZLENİR, ikinci basış istek göndermez', async () => {
    let n = 0;
    const { f, cagrilar, detayVer, toast, degisti } = await exchangeRate(() =>
      ++n === 1 ? throwError(() => networkError()) : throwError(() => previousAttemptSaved()),
    );
    detayVer(detay(tahsilat(K1)));
    f.nakit.form.controls.tutar.setValue('500.00');
    f.nakit.form.controls.tutar.markAsDirty();
    f.doCollection(f.nakit); // yazıldı, yanıt kayboldu
    f.nakit.form.controls.tutar.setValue('600.00'); // kullanıcı tutarı düzeltti
    f.doCollection(f.nakit);
    const t = collections(cagrilar);
    expect(t.map((c) => body(c)['tahsilatAnahtar'])).toEqual([K1, K1]); // donmuş anahtar
    expect(body(t[1])['tutar']).toBe('600.00');
    t[1]?.secenek?.context?.get(REFRESH_ON_DUPLICATE)?.();
    expect(degisti).toHaveBeenCalledTimes(1);

    expect(toast.uyari).toHaveBeenCalledTimes(1);
    const [message, option] = toast.uyari.mock.calls[0] as unknown as [string, { baslik: string }];
    expect(message).toContain('Önceki denemeniz kaydedilmiş (No T-42, 500,00');
    expect(message).toContain('girdiğiniz 600,00');
    expect(message).toContain('YAZILMADI');
    expect(message).not.toContain('başka bir tahsilat');
    expect(option.baslik).toBe('Önceki denemeniz kaydedilmiş — yeni tutar yazılmadı');
    expect(f.nakit.form.getRawValue().tutar).toBeNull();

    // Tazelenen detay (yeni anahtar K2): tutar önerilmez (L-2 yalnız M-A'da); boş tutar → İSTEK YOK.
    detayVer(detay(tahsilat(K2, 2100)));
    expect(f.nakit.kopya.kopya()?.anahtar).toBe(K2);
    expect(f.nakit.form.getRawValue().tutar).toBeNull();
    f.doCollection(f.nakit);
    expect(collections(cagrilar)).toHaveLength(2);
  });

  it('5. tur MEDIUM-1: Nakit\'te kaybolan 500 → Kart/Havale\'de AYNI anahtarla 600 → "önceki denemeniz kaydedilmiş", Kart tutarı TEMİZLENİR, ikinci basış istek göndermez', async () => {
    let n = 0;
    const { f, cagrilar, detayVer, toast } = await exchangeRate(() =>
      ++n === 1 ? throwError(() => networkError()) : throwError(() => previousAttemptSaved()),
    );
    detayVer(detay(tahsilat(K1)));
    f.nakit.form.controls.tutar.setValue('500.00');
    f.nakit.form.controls.tutar.markAsDirty();
    f.doCollection(f.nakit); // yazıldı, yanıt kayboldu
    f.kart.form.controls.tutar.setValue('600.00');
    f.kart.form.controls.tutar.markAsDirty();
    f.doCollection(f.kart);
    const t = collections(cagrilar);
    expect(t.map((c) => [body(c)['tahsilatAnahtar'], body(c)['hesap']])).toEqual([
      [K1, 'Kasa'],
      [K1, 'Banka'],
    ]);
    expect(toast.uyari).toHaveBeenCalledWith(
      expect.stringContaining('Önceki denemeniz kaydedilmiş'),
      {
        baslik: 'Önceki denemeniz kaydedilmiş — yeni tutar yazılmadı',
      },
    );
    expect(f.kart.form.getRawValue().tutar).toBeNull();
    detayVer(detay(tahsilat(K2, 2100)));
    f.doCollection(f.kart); // boş tutar → istek YOK
    expect(collections(cagrilar)).toHaveLength(2);
  });

  it('M-C: aradaki kesin red (400) önceki bilinmeyen denemeyi KAPATMAZ; bilinmeyen deneme yoksa aynı 409 "başka tahsilat" (M-A) kalır', async () => {
    let n = 0;
    const { f, cagrilar, detayVer, toast } = await exchangeRate(() => {
      n++;
      if (n === 1) return throwError(() => serverError(500, '', ''));
      if (n === 2) return throwError(() => serverError(400, 'dogrulama', 'Kur girilemez.'));
      return throwError(() => previousAttemptSaved());
    });
    detayVer(detay(tahsilat(K1)));
    f.nakit.form.controls.tutar.setValue('500.00');
    f.doCollection(f.nakit); // 5xx: sonucu bilinmiyor
    f.doCollection(f.nakit); // 400: bu istek yazılmadı ama 1. deneme hâlâ belirsiz
    f.nakit.form.controls.tutar.setValue('600.00');
    f.doCollection(f.nakit);
    expect(collections(cagrilar)).toHaveLength(3);
    expect(toast.uyari).toHaveBeenLastCalledWith(
      expect.stringContaining('Önceki denemeniz kaydedilmiş'),
      expect.anything(),
    );
    expect(f.nakit.form.getRawValue().tutar).toBeNull();

    // Aynı yanıt, ÖNCESİNDE bilinmeyen deneme YOKKEN: başka sekmenin işlemi → form korunur (M-A).
    TestBed.resetTestingModule();
    const b = await exchangeRate(() => throwError(() => previousAttemptSaved()));
    b.detayVer(detay(tahsilat(K1)));
    b.f.nakit.form.controls.tutar.setValue('600.00');
    b.f.nakit.form.controls.tutar.markAsDirty();
    b.f.doCollection(b.f.nakit);
    expect(b.toast.uyari).toHaveBeenCalledWith(expect.stringContaining('başka bir tahsilat'), {
      baslik: 'Başka bir tahsilat yazıldı — tutarınız kaydedilmedi',
    });
    expect(b.f.nakit.form.getRawValue().tutar).toBe('600.00');
  });

  it('L2: depozito alındıktan sonra kiranın depozitosuyla yeniden ÖN-DOLDURULMAZ (ikinci tık ikinci depozito değil)', async () => {
    const { f, cagrilar, detayVer } = await exchangeRate(() => of({ id: 'd' }));
    detayVer(detay(tahsilat(K1)));
    expect(f.takeDepositForm.getRawValue().tutar).toBe('500.00');
    f.takeDeposit();
    expect(cagrilar.filter((c) => c.yol.endsWith('/depozito/al'))).toHaveLength(1);
    detayVer(detay(tahsilat(K2)));
    expect(f.takeDepositForm.getRawValue().tutar).toBeNull();
    f.takeDeposit(); // boş tutar → istemci doğrulaması, istek YOK
    expect(cagrilar.filter((c) => c.yol.endsWith('/depozito/al'))).toHaveLength(1);
  });

  it('L3: kirli panel formu ya da sonuçlanmamış gönderim "kaydedilmemiş" sayılır', async () => {
    const { f, detayVer } = await exchangeRate(() => throwError(() => networkError()));
    detayVer(detay(tahsilat(K1)));
    expect(f.isDirty()).toBe(false);
    f.doCollection(f.nakit); // ağ hatası: form temiz ama donmuş anahtar bekliyor
    expect(f.nakit.form.dirty).toBe(false);
    expect(f.isDirty()).toBe(true);
  });

  it('L3: başlık anahtarlı işlem sonuçlanmadıysa (bekleyen Idempotency-Key) kaydedilmemiş sayılır', async () => {
    const { f, detayVer } = await exchangeRate(() => throwError(() => networkError()));
    detayVer(detay(tahsilat(K1)));
    f.paymentForm.patchValue({ tutar: '75.00' });
    f.makePayment();
    f.paymentForm.markAsPristine();
    expect(f.isDirty()).toBe(true);
  });

  it('L6: dövizi değiştirince DOKUNULMAMIŞ ön-dolu tutar temizlenir; yazılmış tutar korunur', async () => {
    const { f, detayVer } = await exchangeRate(() => of({ id: 'c' }));
    detayVer(detay(tahsilat(K1)));
    expect(f.nakit.form.getRawValue().tutar).toBe('2600.00');
    f.nakit.form.controls.doviz.markAsDirty(); // rc-secim kullanıcı seçiminde kontrolü kirletir
    f.nakit.form.controls.doviz.setValue('USD');
    expect(f.nakit.form.getRawValue().tutar).toBeNull();
    f.kart.form.controls.tutar.setValue('10.00');
    f.kart.form.controls.tutar.markAsDirty();
    f.kart.form.controls.doviz.markAsDirty();
    f.kart.form.controls.doviz.setValue('EUR');
    expect(f.kart.form.getRawValue().tutar).toBe('10.00');
  });

  it('depozito ön-doldurma kiranın depozitosu; irat önce onay ister, vazgeçilirse istek yok', async () => {
    const { f, cagrilar, detayVer, onay } = await exchangeRate(() => of({ id: 'x' }));
    detayVer(detay(tahsilat(K1)));
    expect(f.takeDepositForm.getRawValue().tutar).toBe('500.00');
    onay.ask.mockResolvedValueOnce(false);
    f.forfeitForm.patchValue({ tutar: '100.00' });
    await f.depositForfeit();
    expect(cagrilar).toHaveLength(0);
    await f.depositForfeit();
    expect(body(cagrilar[0])).toEqual({
      cariId: CUSTOMER_ID,
      kiraId: RENTAL_ID,
      tutar: '100.00',
      aciklama: null,
    });
  });
});

describe('KiraFinansDurumu — yapısal işlemler', () => {
  it('L7: dönem kesiminde 400 → plan ve kira yeniden yüklenir (degisti)', async () => {
    const { f, detayVer, degisti, toast } = await exchangeRate(() =>
      throwError(() => serverError(400, 'dogrulama', 'Dönem zaten kesildi.')),
    );
    detayVer(detay(tahsilat(K1)));
    f.issuePeriod({
      donemSira: 1,
      donemBas: '2026-10-01T00:00:00Z',
      donemBit: '2026-10-31T00:00:00Z',
      durum: 'Planlandi',
      tahakkuk: 3100,
      invoiceId: null,
      kesilenTutar: null,
    });
    expect(toast.hata).toHaveBeenCalledWith('Dönem zaten kesildi.');
    expect(degisti).toHaveBeenCalledTimes(1);
    expect(f.submittedPeriod()).toBeNull();
  });

  it('dönem kes + tahsil: tahsilatYazildi=false iken sunucu bilgisi gizlenmez', async () => {
    const info = 'Bu dönemin tahsilatı daha önce alınmış; yeni tahsilat yazılmadı.';
    const { f, cagrilar, detayVer, toast } = await exchangeRate(() =>
      of({ faturaId: 'f1', tahsilatYazildi: false, bilgi: info }),
    );
    detayVer(detay(tahsilat(K1)));
    f.periodForm(2).patchValue({ tahsilat: true, hesap: 'Banka' });
    f.issuePeriod({
      donemSira: 2,
      donemBas: '2026-10-01T00:00:00Z',
      donemBit: '2026-10-31T00:00:00Z',
      durum: 'Planlandi',
      tahakkuk: 3100,
      invoiceId: null,
      kesilenTutar: null,
    });
    expect(body(cagrilar[0])).toEqual({
      kiraId: RENTAL_ID,
      donemSira: 2,
      tahsilat: true,
      hesap: 'Banka',
    });
    expect(cagrilar[0]?.secenek).toBeUndefined(); // yapısal: başlık yok
    expect(toast.bilgi).toHaveBeenCalledWith(info, { baslik: 'Tahsilat yazılmadı' });
    expect(f.periodInfo()).toBe(info);
    expect(toast.basari).toHaveBeenCalledWith('Dönem faturası kesildi.');
  });

  it('dış hizmet iptali: onay → POST …/iptal; 400 doğrulama hata bildirimi', async () => {
    const { f, cagrilar, detayVer, toast } = await exchangeRate(() =>
      throwError(() => serverError(400, 'dogrulama', 'Kayıt zaten iptal edilmiş.')),
    );
    detayVer(detay(tahsilat(K1)));
    await f.cancelOutsourcedService({
      id: 'd1',
      no: 'DH-1',
      alinanHizmet: 'Çekici',
      hizmetAlinanFirma: null,
      hizmetBedeli: 1000,
      currency: 'TRY',
      tedarikciKomisyonOran: 10,
      durum: 'Kayitli',
      tarih: '2026-09-22T06:00:00Z',
    });
    expect(cagrilar[0]?.yol).toBe('/api/ui/v1/finans/dis-hizmet/d1/iptal');
    expect(toast.hata).toHaveBeenCalledWith('Kayıt zaten iptal edilmiş.');
  });

  it('yetki_yok (bant interceptor’da) forma ya da toast’a ikinci kez yazılmaz', async () => {
    const { f, detayVer, toast } = await exchangeRate(() =>
      throwError(() => serverError(403, 'yetki_yok', 'Bu işlem için yetkiniz yok.')),
    );
    detayVer(detay(tahsilat(K1)));
    await f.cancelOutsourcedService({
      id: 'd1',
      no: 'DH-1',
      alinanHizmet: 'Çekici',
      hizmetAlinanFirma: null,
      hizmetBedeli: 1000,
      currency: 'TRY',
      tedarikciKomisyonOran: 10,
      durum: 'Kayitli',
      tarih: '2026-09-22T06:00:00Z',
    });
    expect(toast.hata).not.toHaveBeenCalled();
    f.issueInvoice();
    expect(f.invoiceSubmission.genelHatalar()).toEqual([]);
  });
});
