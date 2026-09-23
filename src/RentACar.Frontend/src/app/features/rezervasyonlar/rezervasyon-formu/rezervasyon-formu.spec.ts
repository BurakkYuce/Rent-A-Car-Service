import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { type Observable, Subject, firstValueFrom, of, throwError } from 'rxjs';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { UyariBandiServisi } from '@core/geri-bildirim/uyari-bandi-servisi';
import { provideCeviri } from '@core/i18n/ceviri';

import type { RezervasyonDetayYaniti } from '../rezervasyon-modeli';
import { REZ_ID, rezervasyonDetayi } from '../rezervasyon-test-verisi';
import { RezervasyonFormuSayfasi } from './rezervasyon-formu';

/**
 * Kayıtlı rezervasyonun sürüm akışı: PUT detaydaki `surum`u taşır; 409 `cakisma` formu SİLMEZ — güncel kayıt
 * okunur, dokunulmamış alan güncellenir, dokunulan korunur, ikisi de değiştiyse alan işaretlenir + bant; kullanıcı
 * yeniden kaydedince YENİ sürüm gider. Beklenenler elle kurulmuş sunucu durumlarından.
 */
interface Istek {
  readonly yol: string;
  readonly govde: Record<string, unknown>;
}

const cakisma = () =>
  new HttpErrorResponse({
    status: 409,
    error: { status: 409, kod: 'cakisma', detail: 'Rezervasyon başka bir oturumda değişti.' },
  });

async function kur(put: (n: number) => Observable<unknown>, id: string | null = REZ_ID) {
  const detaylar = new Subject<RezervasyonDetayYaniti>();
  const putlar: Istek[] = [];
  const postlar: Istek[] = [];
  const api = {
    get: (yol: string) => {
      if (yol === `/api/ui/v1/rezervasyonlar/${REZ_ID}`) return detaylar.asObservable();
      if (yol.endsWith('/form-secenekleri'))
        return of({
          varsayilanFiyatTuru: 'Günlük',
          fiyatTurleri: ['Otomatik', 'Günlük'],
          talepTurleri: ['Bireysel'],
        });
      return of([]);
    },
    put: (yol: string, govde: Record<string, unknown>) => {
      putlar.push({ yol, govde });
      return put(putlar.length);
    },
    post: (yol: string, govde: Record<string, unknown>) => {
      postlar.push({ yol, govde });
      return of({ id: REZ_ID, no: 'RZ-000042' });
    },
  };
  const router = { navigate: vi.fn(async () => true) };
  TestBed.configureTestingModule({
    providers: [
      ...provideCeviri(),
      { provide: ApiIstemcisi, useValue: api },
      {
        provide: ToastServisi,
        useValue: { basari: vi.fn(), bilgi: vi.fn(), uyari: vi.fn(), hata: vi.fn() },
      },
      { provide: OnayServisi, useValue: { sor: vi.fn(async () => true) } },
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
  TestBed.overrideComponent(RezervasyonFormuSayfasi, { set: { template: '', imports: [] } });
  await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  const fixture = TestBed.createComponent(RezervasyonFormuSayfasi);
  const s = fixture.componentInstance;
  const kaydet = () => (s as unknown as { kaydet(): void }).kaydet();
  const detayVer = (d: RezervasyonDetayYaniti) => {
    detaylar.next(d);
    TestBed.tick();
  };
  TestBed.tick();
  return { s, kaydet, detayVer, putlar, postlar, router, bant: TestBed.inject(UyariBandiServisi) };
}

describe('Rezervasyon formu sürüm akışı', () => {
  it('PUT tüm alanları + detaydaki sürümü taşır', async () => {
    const { s, kaydet, detayVer, putlar } = await kur(() => of(rezervasyonDetayi()));
    detayVer(rezervasyonDetayi());
    s.form.controls.projeAdi.setValue('Kongre');
    s.form.controls.projeAdi.markAsDirty();
    kaydet();
    expect(putlar).toHaveLength(1);
    expect(putlar[0]?.yol).toBe(`/api/ui/v1/rezervasyonlar/${REZ_ID}`);
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
    const { s, kaydet, detayVer, putlar, bant } = await kur((n) =>
      n === 1 ? throwError(cakisma) : of(rezervasyonDetayi({ surum: '900' })),
    );
    detayVer(rezervasyonDetayi());
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
    detayVer(rezervasyonDetayi({ surum: '813', gunlukUcret: 1400, onayKodu: 'ONY-2' }));
    expect(s.form.controls.projeAdi.value).toBe('Kongre');
    expect(s.form.controls.gunlukUcret.value).toBe('1300.00');
    expect(s.form.controls.onayKodu.value).toBe('ONY-2');
    expect(s.form.controls.gunlukUcret.errors).toEqual({
      sunucu: ['Bu alan başka bir oturumda da değişti; kontrol edip yeniden kaydedin.'],
    });
    expect(bant.bant()?.kod).toBe('cakisma');

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
    const { s, kaydet, detayVer, putlar } = await kur(() => of(rezervasyonDetayi()));
    const d = rezervasyonDetayi({
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
    const { s, kaydet, postlar, router } = await kur(() => of(null), null);
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
    expect(router.navigate).toHaveBeenCalledWith(['/rezervasyonlar', REZ_ID]);
    expect(s.form.dirty).toBe(false);
    expect(s.form.controls.musteri.value).toBeNull();
  });
});
