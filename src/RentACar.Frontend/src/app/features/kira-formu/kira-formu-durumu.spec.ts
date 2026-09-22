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
const TANIM_ID = '0b0e7c1a-6666-4aaa-8bbb-000000000006';

const bekle = (ms: number) => new Promise((r) => setTimeout(r, ms));

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
    // F4.3b kimlikle etiket uçları (PII yok: kimlik + ad + tip / plaka + grup + durum).
    if (yol === `/api/ui/v1/secim/musteri/${MUSTERI_ID}`) {
      return of({ id: MUSTERI_ID, etiket: 'Ayşe Yılmaz', tip: 'Bireysel' });
    }
    if (yol === `/api/ui/v1/secim/arac/${DIGER_ARAC}`) {
      return of({
        id: DIGER_ARAC,
        etiket: '06 XYZ 42 — Renault Clio',
        plaka: '06 XYZ 42',
        grup: 'B',
        durum: 'Musait',
      });
    }
    if (yol.endsWith('/ek-hizmet-katalogu')) {
      return of({
        ogeler: [
          { id: TANIM_ID, kod: 'BEBEK', ad: 'Bebek koltuğu', birimUcret: 75.5, kdvOrani: 0.1 },
        ],
        toplam: 1,
      });
    }
    if (/^\/api\/ui\/v1\/secim\/(musteri|arac)\//.test(yol)) {
      return throwError(() => sunucuHatasi(404, 'bulunamadi', 'Bulunamadı.'));
    }
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
  it('?varac&vfrom&vto&musteriId ile form dolu açılır (etiketler sunucudan), kirli değildir', async () => {
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
    // F4.3b: "Bağlantıdaki müşteri" yerine kimlikle çözülen gerçek ad (secim/musteri/{id}).
    expect(f.musteri.value).toEqual({ id: MUSTERI_ID, etiket: 'Ayşe Yılmaz' });
    expect(f.ayna.controls.musteri.value?.etiket).toBe('Ayşe Yılmaz');
    expect(cagrilar.some((c) => c.yol === `/api/ui/v1/secim/musteri/${MUSTERI_ID}`)).toBe(true);
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
  it('penceresiz ?varac= plaka etiketini kimlikle çözer (secim/arac/{id})', async () => {
    const { d, cagrilar } = await kur({ varac: DIGER_ARAC });
    expect(d.form.controls.arac.value).toMatchObject({
      id: DIGER_ARAC,
      etiket: '06 XYZ 42 — Renault Clio',
      plaka: '06 XYZ 42',
    });
    expect(cagrilar.some((c) => c.yol === `/api/ui/v1/secim/arac/${DIGER_ARAC}`)).toBe(true);
    expect(d.kirliMi()).toBe(false);
  });

  it('çözülemeyen kimlik (404) geçici etiketi korur; kayıt yine kimlikle yapılır', async () => {
    const BILINMEYEN = '0b0e7c1a-2222-4aaa-8bbb-00000000ffff';
    const { d } = await kur({ musteriId: BILINMEYEN });
    expect(d.form.controls.musteri.value).toEqual({
      id: BILINMEYEN,
      etiket: 'Bağlantıdaki müşteri',
    });
  });

  it('kaynak / özel kod önerileri yazılanla sunucuda aranır (q), boşken q yok', async () => {
    const { d, cagrilar } = await kur({});
    await bekle(300);
    const son = (uc: string) =>
      cagrilar.filter((c) => c.yol === `/api/ui/v1/secim/${uc}`).at(-1)?.secenek?.parametreler;
    expect(son('rezervasyon-kaynagi')).toEqual({ q: null, limit: 20 });
    expect(son('ozel-kod')).toEqual({ q: null, limit: 20 });
    // Hızlı Giriş aynası da kanoniği sürer → arama.
    d.form.controls.ayna.controls.kaynak.setValue(' Web ');
    d.form.controls.ozelKod.setValue('KAMP');
    await bekle(300);
    expect(son('rezervasyon-kaynagi')).toEqual({ q: 'Web', limit: 20 });
    expect(son('ozel-kod')).toEqual({ q: 'KAMP', limit: 20 });
  });

  it('ek hizmet matrisi: işaret satır ekler, kaldırma çıkarır; hesap parametresine girer', async () => {
    const { d } = await kur({ varac: ARAC_ID, vfrom: '2026-10-01', vto: '2026-10-04' });
    const oge = d.ekHizmetKatalogu.veri()?.ogeler[0];
    expect(oge?.ad).toBe('Bebek koltuğu');
    if (!oge) return;
    d.ekHizmetSecimi(oge, true);
    d.ekHizmetSecimi(oge, true); // ikinci işaret yinelenmez
    expect(d.form.controls.ekHizmetler.getRawValue()).toEqual([
      { tanim: { id: TANIM_ID, etiket: 'Bebek koltuğu' }, miktar: 1 },
    ]);
    expect(d.form.controls.ekHizmetler.dirty).toBe(true);
    d.ekHizmetSecimi(oge, false);
    expect(d.form.controls.ekHizmetler.length).toBe(0);
  });

  it('kayıtlı kiraya ekleme kaynağı katalogdan: etiket "Ad (birim net)", SYS yok', async () => {
    const { d } = await kur({});
    const liste = await firstValueFrom(d.ekHizmetKaynagi('bebek', 20));
    expect(liste).toEqual([{ id: TANIM_ID, etiket: 'Bebek koltuğu (75,50 ₺ net)' }]);
  });
});
