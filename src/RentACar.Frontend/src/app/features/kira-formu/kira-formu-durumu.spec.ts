import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { type Observable, Subject, firstValueFrom, of, throwError } from 'rxjs';
import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi, type IstekSecenekleri } from '@core/api/api-istemcisi';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { provideCeviri } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { KiraFormuDurumu } from './kira-formu-durumu';

const ARAC_ID = '0b0e7c1a-3333-4aaa-8bbb-000000000003';
const DIGER_ARAC = '0b0e7c1a-3333-4aaa-8bbb-000000000009';
const MUSTERI_ID = '0b0e7c1a-2222-4aaa-8bbb-000000000002';

const MUSAIT = [
  {
    id: ARAC_ID,
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

const VARSAYILANLAR = {
  cikisYakit: 8,
  fiyatTuru: 'KDV Dahil Günlük',
  fiyatTurleri: ['KDV Dahil Günlük'],
  kiralamaTurleri: ['Kısa Kiralama'],
  faturalamaTipleri: [],
  dovizler: ['TL'],
  odemeSekilleri: [],
  basTarEnGec: '2027-09-22T00:00:00Z',
};

const sunucuHatasi = (status: number, kod: string, detail: string, errors?: object) =>
  apiHatasinaCevir(new HttpErrorResponse({ status, error: { status, kod, detail, errors } }));

interface Cagri {
  readonly yontem: string;
  readonly yol: string;
  readonly govde?: unknown;
  readonly secenek?: IstekSecenekleri;
}

/** Sahte ApiIstemcisi: GET yanıtları yola göre; yazmalar testin verdiği akışla. */
function sahteApi(yazma: (c: Cagri) => Observable<unknown>) {
  const cagrilar: Cagri[] = [];
  const get = (yol: string, secenek?: IstekSecenekleri): Observable<unknown> => {
    cagrilar.push({ yontem: 'GET', yol, secenek });
    if (yol.endsWith('/form-varsayilanlari')) return of(VARSAYILANLAR);
    if (yol.endsWith('/musait-arac')) return of(MUSAIT);
    if (yol.endsWith('/hesapla')) return of({ ok: false, hata: 'Araç seçin.' });
    if (yol.startsWith('/api/ui/v1/secim/')) return of([]);
    return throwError(() => sunucuHatasi(404, 'bilinmeyen', 'yok'));
  };
  const yaz = (yontem: string) => (yol: string, govde?: unknown, secenek?: IstekSecenekleri) => {
    const c = { yontem, yol, govde, secenek };
    cagrilar.push(c);
    return yazma(c);
  };
  return { cagrilar, api: { get, post: yaz('POST'), put: yaz('PUT'), delete: yaz('DELETE') } };
}

async function kur(
  sorgu: Record<string, string>,
  yazma: (c: Cagri) => Observable<unknown> = () => of({}),
) {
  const sahte = sahteApi(yazma);
  const toast = { basari: vi.fn(), bilgi: vi.fn(), uyari: vi.fn(), hata: vi.fn() };
  TestBed.configureTestingModule({
    providers: [
      ...provideCeviri(),
      KiraFormuDurumu,
      { provide: ApiIstemcisi, useValue: sahte.api },
      { provide: ToastServisi, useValue: toast },
      { provide: OnayServisi, useValue: { sor: vi.fn(async () => true) } },
      { provide: Router, useValue: { navigate: vi.fn(async () => true) } },
      {
        provide: OturumServisi,
        useValue: { izinVar: () => true, ben: () => ({ rol: 'Operator' }) },
      },
      {
        provide: ActivatedRoute,
        useValue: {
          snapshot: {
            paramMap: convertToParamMap({}),
            queryParamMap: convertToParamMap(sorgu),
            routeConfig: null,
            pathFromRoot: [],
            params: {},
          },
          queryParamMap: of(convertToParamMap(sorgu)),
          fragment: of(null),
        },
      },
    ],
  });
  await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  const d = TestBed.inject(KiraFormuDurumu);
  TestBed.tick();
  return { d, toast, cagrilar: sahte.cagrilar };
}

describe('KiraFormuDurumu (yeni kira)', () => {
  it('?varac&vfrom&vto&musteriId ile form dolu açılır (etiket müsait listeden), kirli değildir', async () => {
    const { d, cagrilar } = await kur({
      varac: ARAC_ID,
      vfrom: '2026-10-01',
      vto: '2026-10-04',
      musteriId: MUSTERI_ID,
    });
    const f = d.form.controls;
    expect(f.arac.value?.id).toBe(ARAC_ID);
    expect(f.arac.value?.etiket).toBe('34 ABC 123 — Fiat Egea');
    expect(f.ayna.controls.arac.value?.etiket).toBe('34 ABC 123 — Fiat Egea');
    expect(f.basTar.value).toBe('2026-10-01T06:00:00.000Z');
    expect(f.bitTar.value).toBe('2026-10-04T06:00:00.000Z');
    expect(f.musteri.value).toEqual({ id: MUSTERI_ID, etiket: 'Bağlantıdaki müşteri' });
    expect(f.fiyatTuru.value).toBe('KDV Dahil Günlük');
    expect(d.musaitFormu.getRawValue()).toEqual({
      vfrom: '2026-10-01',
      vto: '2026-10-04',
      vgrup: null,
    });
    expect(d.kirliMi()).toBe(false);
    const musait = cagrilar.find((c) => c.yol.endsWith('/musait-arac'));
    expect(musait?.secenek?.parametreler).toEqual({
      vfrom: '2026-10-01',
      vto: '2026-10-04',
      vgrup: null,
    });
  });

  it('bağlantıdaki araç o aralıkta müsait değilse seçim düşer ve not gösterilir', async () => {
    const { d } = await kur({ varac: DIGER_ARAC, vfrom: '2026-10-01', vto: '2026-10-04' });
    expect(d.form.controls.arac.value).toBeNull();
    expect(d.musaitNotu()).toBe('Seçili araç bu aralıkta müsait değil; seçim kaldırıldı.');
  });

  it('doğrulama hatası: alan işaretlenir, değerler korunur, hatalı alana gidilir', async () => {
    const yanit = new Subject<unknown>();
    const { d, cagrilar } = await kur(
      { varac: ARAC_ID, vfrom: '2026-10-01', vto: '2026-10-04', musteriId: MUSTERI_ID },
      () => yanit,
    );
    d.form.controls.gunlukUcret.setValue('-5');
    d.form.controls.aciklama.setValue('Uzun açıklama');
    const git = vi.fn();
    d.kaydet(git);
    d.kaydet(git); // çift tık: tek istek
    expect(cagrilar.filter((c) => c.yontem === 'POST')).toHaveLength(1);
    yanit.error(
      sunucuHatasi(400, 'dogrulama', 'Günlük ücret negatif olamaz.', {
        gunlukUcret: ['Günlük ücret negatif olamaz.'],
      }),
    );
    expect(d.form.controls.gunlukUcret.errors?.['sunucu']).toEqual([
      'Günlük ücret negatif olamaz.',
    ]);
    expect(d.form.controls.ayna.controls.gunlukUcret.errors?.['sunucu']).toBeDefined();
    expect(d.form.controls.gunlukUcret.value).toBe('-5');
    expect(d.form.controls.aciklama.value).toBe('Uzun açıklama');
    expect(git).toHaveBeenCalled();
  });

  it('müsaitlik `cakisma` (alansız) formu silmez; mesaj form üstünde', async () => {
    const { d } = await kur(
      { varac: ARAC_ID, vfrom: '2026-10-01', vto: '2026-10-04', musteriId: MUSTERI_ID },
      () => throwError(() => sunucuHatasi(409, 'cakisma', 'Araç bu tarihlerde müsait değil.')),
    );
    d.form.controls.aciklama.setValue('Korunacak');
    d.form.controls.aciklama.markAsDirty(); // kullanıcı yazdı
    d.kaydet(() => undefined);
    expect(d.kayit.genelHatalar()).toEqual(['Araç bu tarihlerde müsait değil.']);
    expect(d.form.controls.aciklama.value).toBe('Korunacak');
    expect(d.form.controls.arac.value?.id).toBe(ARAC_ID);
    expect(d.kirliMi()).toBe(true);
  });

  it('F7: yeni müşteri dolu ama ana form geçersiz (araç yok) → cari AÇILMAZ (yetim PII yok)', async () => {
    const { d, cagrilar } = await kur({ vfrom: '2026-10-01', vto: '2026-10-04' });
    d.form.controls.arac.setValue(null);
    d.yeniMusteriFormu.patchValue({ ad: 'Yetim', tcKimlik: '10000000146' });
    const git = vi.fn();
    d.kaydet(git);
    expect(cagrilar.filter((c) => c.yontem === 'POST')).toEqual([]);
    expect(git).toHaveBeenCalled();
    expect(d.form.controls.arac.touched).toBe(true);
    expect(d.yeniMusteriFormu.getRawValue().ad).toBe('Yetim'); // girilen bilgi korunur
  });

  it('müşteri seçilmemiş ama yeni müşteri alanları doluysa önce cari açılır, sonra kira', async () => {
    const { d, cagrilar } = await kur(
      { varac: ARAC_ID, vfrom: '2026-10-01', vto: '2026-10-04' },
      (c) =>
        c.yol.endsWith('/kiralar/musteri')
          ? of({ id: MUSTERI_ID, etiket: 'Ayşe Yılmaz' })
          : of({ id: 'k1', sozlesmeNo: '2026011001001', uyari: null }),
    );
    d.yeniMusteriFormu.patchValue({ ad: 'Ayşe', soyad: 'Yılmaz', tcKimlik: '10000000146' });
    d.kaydet(() => undefined);
    const yazmalar = cagrilar.filter((c) => c.yontem === 'POST').map((c) => c.yol);
    expect(yazmalar).toEqual(['/api/ui/v1/kiralar/musteri', '/api/ui/v1/kiralar']);
    const kira = cagrilar.find((c) => c.yol === '/api/ui/v1/kiralar');
    expect((kira?.govde as { musteriId: string }).musteriId).toBe(MUSTERI_ID);
    // PII formda kalmaz; yeni kira formu sıfırlanır (kayıt yapıldı).
    expect(d.yeniMusteriFormu.getRawValue().tcKimlik).toBeNull();
    expect(d.kirliMi()).toBe(false);
  });
});
