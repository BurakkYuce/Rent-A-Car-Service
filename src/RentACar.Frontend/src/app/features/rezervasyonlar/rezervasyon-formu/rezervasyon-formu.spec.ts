import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { type Observable, Subject, firstValueFrom, of, throwError } from 'rxjs';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { WarningBannerService } from '@core/geri-bildirim/warning-banner-service';
import { provideTranslation } from '@core/i18n/ceviri';

import type { ReservationDetailResponse } from '../rezervasyon-modeli';
import { RES_ID, reservationDetail } from '../rezervasyon-test-verisi';
import { ReservationFormPage } from './rezervasyon-formu';

/**
 * Kayıtlı rezervasyonun sürüm akışı: PUT detaydaki `surum`u taşır; 409 `cakisma` formu SİLMEZ — güncel kayıt
 * okunur, dokunulmamış alan güncellenir, dokunulan korunur, ikisi de değiştiyse alan işaretlenir + bant; kullanıcı
 * yeniden kaydedince YENİ sürüm gider. Beklenenler elle kurulmuş sunucu durumlarından.
 */
interface Istek {
  readonly yol: string;
  readonly govde: Record<string, unknown>;
}

const conflict = () =>
  new HttpErrorResponse({
    status: 409,
    error: { status: 409, kod: 'cakisma', detail: 'Rezervasyon başka bir oturumda değişti.' },
  });

async function exchangeRate(put: (n: number) => Observable<unknown>, id: string | null = RES_ID) {
  const details = new Subject<ReservationDetailResponse>();
  const puts: Istek[] = [];
  const posts: Istek[] = [];
  const api = {
    get: (path: string) => {
      if (path === `/api/ui/v1/rezervasyonlar/${RES_ID}`) return details.asObservable();
      if (path.endsWith('/form-secenekleri'))
        return of({
          varsayilanFiyatTuru: 'Günlük',
          fiyatTurleri: ['Otomatik', 'Günlük'],
          talepTurleri: ['Bireysel'],
        });
      return of([]);
    },
    put: (path: string, body: Record<string, unknown>) => {
      puts.push({ yol: path, govde: body });
      return put(puts.length);
    },
    post: (path: string, body: Record<string, unknown>) => {
      posts.push({ yol: path, govde: body });
      return of({ id: RES_ID, no: 'RZ-000042' });
    },
  };
  const router = { navigate: vi.fn(async () => true) };
  TestBed.configureTestingModule({
    providers: [
      ...provideTranslation(),
      { provide: ApiIstemcisi, useValue: api },
      {
        provide: ToastService,
        useValue: { basari: vi.fn(), bilgi: vi.fn(), uyari: vi.fn(), hata: vi.fn() },
      },
      { provide: ConfirmService, useValue: { ask: vi.fn(async () => true) } },
      { provide: Router, useValue: router },
      {
        provide: ActivatedRoute,
        useValue: {
          snapshot: {
            paramMap: convertToParamMap(id ? { id } : {}),
            queryParamMap: convertToParamMap({}),
            routeConfig: null,
            pathFromRoot: [],
            params: {},
          },
        },
      },
    ],
  });
  TestBed.overrideComponent(ReservationFormPage, { set: { template: '', imports: [] } });
  await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  const fixture = TestBed.createComponent(ReservationFormPage);
  const s = fixture.componentInstance;
  const save = () => (s as unknown as { kaydet(): void }).kaydet();
  const provideDetail = (d: ReservationDetailResponse) => {
    details.next(d);
    TestBed.tick();
  };
  TestBed.tick();
  return {
    s,
    kaydet: save,
    detayVer: provideDetail,
    putlar: puts,
    postlar: posts,
    router,
    bant: TestBed.inject(WarningBannerService),
  };
}

describe('Rezervasyon formu sürüm akışı', () => {
  it('PUT tüm alanları + detaydaki sürümü taşır', async () => {
    const { s, kaydet, detayVer, putlar } = await exchangeRate(() => of(reservationDetail()));
    detayVer(reservationDetail());
    s.form.controls.projeAdi.setValue('Kongre');
    s.form.controls.projeAdi.markAsDirty();
    kaydet();
    expect(putlar).toHaveLength(1);
    expect(putlar[0]?.yol).toBe(`/api/ui/v1/rezervasyonlar/${RES_ID}`);
    expect(putlar[0]?.govde).toMatchObject({
      surum: '812',
      projeAdi: 'Kongre',
      gunlukUcret: 1250.5,
      cikisOfisi: 'Merkez',
      otaCdw: 99.9,
      provizyon: 5000,
    });
    expect(Object.keys(putlar[0]?.govde ?? {})).toHaveLength(33); // 32 whitelist alanı + surum
  });

  it('409 cakisma formu SİLMEZ: güncel kayıt birleşir, çakışan alan işaretlenir, yeniden kayıt YENİ sürümle', async () => {
    const {
      s,
      kaydet,
      detayVer,
      putlar,
      bant: banner,
    } = await exchangeRate((n) =>
      n === 1 ? throwError(conflict) : of(reservationDetail({ surum: '900' })),
    );
    detayVer(reservationDetail());
    s.form.controls.projeAdi.setValue('Kongre');
    s.form.controls.projeAdi.markAsDirty();
    s.form.controls.gunlukUcret.setValue('1300.00');
    s.form.controls.gunlukUcret.markAsDirty();
    kaydet();
    expect(putlar).toHaveLength(1);
    // Formdaki değerler yerinde (hata dalı değerlere dokunmaz).
    expect(s.form.controls.projeAdi.value).toBe('Kongre');
    expect(s.form.dirty).toBe(true);

    // Başka oturum günlük ücreti ve onay kodunu değiştirmiş (sürüm 813).
    detayVer(reservationDetail({ surum: '813', gunlukUcret: 1400, onayKodu: 'ONY-2' }));
    expect(s.form.controls.projeAdi.value).toBe('Kongre');
    expect(s.form.controls.gunlukUcret.value).toBe('1300.00');
    expect(s.form.controls.onayKodu.value).toBe('ONY-2');
    expect(s.form.controls.gunlukUcret.errors).toEqual({
      sunucu: ['Bu alan başka bir oturumda da değişti; kontrol edip yeniden kaydedin.'],
    });
    expect(banner.bant()?.kod).toBe('cakisma');

    kaydet();
    expect(putlar).toHaveLength(2);
    expect(putlar[1]?.govde).toMatchObject({
      surum: '813',
      gunlukUcret: '1300.00',
      projeAdi: 'Kongre',
      onayKodu: 'ONY-2',
    });
  });

  it('düzenlenemeyen kayıt (Kiraya çevrildi) form kilitli; kaydet istek göndermez', async () => {
    const { s, kaydet, detayVer, putlar } = await exchangeRate(() => of(reservationDetail()));
    const d = reservationDetail({
      durum: 'KirayaCevrildi',
      kiraId: '0b0e7c1a-1111-4aaa-8bbb-000000000001',
    });
    detayVer({
      ...d,
      yetkiler: { duzenle: false, onayla: false, kirayaCevir: false, iptal: false },
    });
    expect(s.form.disabled).toBe(true);
    kaydet();
    expect(putlar).toHaveLength(0);
  });

  it('yeni: POST gövdesi, başarıda kayda gidilir ve sekme temiz forma döner', async () => {
    const { s, kaydet, postlar, router } = await exchangeRate(() => of(null), null);
    expect(s.form.controls.fiyatTuru.value).toBe('Günlük'); // tenant varsayılanı ön-seçim
    s.form.patchValue({
      musteri: { id: '0b0e7c1a-2222-4aaa-8bbb-000000000002', etiket: 'Ayşe' },
      arac: { id: '0b0e7c1a-3333-4aaa-8bbb-000000000003', etiket: '34 ABC 123' },
      talepTuru: 'Bireysel',
    });
    s.form.markAsDirty();
    kaydet();
    expect(postlar).toHaveLength(1);
    expect(postlar[0]?.govde).toMatchObject({ talepTuru: 'Bireysel', fiyatTuru: 'Günlük' });
    expect(postlar[0]?.govde).not.toHaveProperty('surum');
    expect(router.navigate).toHaveBeenCalledWith(['/rezervasyonlar', RES_ID]);
    expect(s.form.dirty).toBe(false);
    expect(s.form.controls.musteri.value).toBeNull();
  });
});
