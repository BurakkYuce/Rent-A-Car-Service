import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { type Observable, Subject, firstValueFrom, of, throwError } from 'rxjs';
import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi, type IstekSecenekleri } from '@core/api/api-istemcisi';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { UyariBandiServisi } from '@core/geri-bildirim/uyari-bandi-servisi';
import { provideCeviri } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { KiraFormuDurumu } from './kira-formu-durumu';
import type { KiraDetayYaniti, KiraSozlesmesi } from './kira-tipleri';

/**
 * #261 yeniden doğrulama N1/N2 (F4.4'te kapatıldı) — kayıtlı kirada sürüm (`surum`) akışı:
 * - N1: işlem/Kaydet sonrası kayıt yeniden okunurken Kaydet pasif (kendi değişikliğiyle 409 almasın).
 * - N2: sürüm çakışmasında (409 `cakisma`) birleştirmede kullanıcının DOKUNDUĞU alanlarla çakışan sunucu
 *   değişikliği yoksa birleştirilmiş gövde yeni sürümle SESSİZ ve TEK SEFER yeniden gönderilir.
 * Beklenenler elle kurulmuş sunucu durumlarından (formül yok).
 */
const KIRA_ID = '0b0e7c1a-1111-4aaa-8bbb-000000000001';
const MUSTERI_ID = '0b0e7c1a-2222-4aaa-8bbb-000000000002';
const ARAC_ID = '0b0e7c1a-3333-4aaa-8bbb-000000000003';
const SURUCU_ID = '0b0e7c1a-4444-4aaa-8bbb-000000000004';
const PERSONEL_ID = '0b0e7c1a-5555-4aaa-8bbb-000000000005';

function kira(ek: Partial<KiraSozlesmesi> = {}): KiraSozlesmesi {
  return {
    id: KIRA_ID,
    sozlesmeNo: '2026220901001',
    durum: 'Kirada',
    reservationId: null,
    musteriId: MUSTERI_ID,
    vehicleId: ARAC_ID,
    basTar: '2026-09-22T06:00:00+00:00',
    bitTar: '2026-09-25T06:00:00+00:00',
    cikisOfisi: 'Merkez',
    cikisSubeId: null,
    donusOfisi: 'Havalimanı',
    kmLimit: 300,
    fazlaKmUcret: 2.5,
    yakitBirimUcret: 45.75,
    cikisKm: null,
    cikisYakit: null,
    donusKm: null,
    donusYakit: null,
    gercekDonusTar: null,
    fazlaKm: 0,
    fazlaKmBedeli: 0,
    eksikYakit: 0,
    yakitBedeli: 0,
    uzatmaGun: 0,
    uzatmaBedeli: 0,
    kmHediye: null,
    bitisSebebi: null,
    teslimAlanPersonelId: null,
    teslimEdenPersonelId: PERSONEL_ID,
    odemeSekli: 'Nakit',
    ikinciSurucuId: SURUCU_ID,
    ikinciSurucuSerbestAd: null,
    ikinciSurucuSerbestSoyad: null,
    ikinciSurucuSerbestTel: null,
    ikinciSurucuSerbestEhliyetSinifi: null,
    hediyeGun: null,
    faturalananGun: null,
    vadeTar: null,
    iskontoTutar: null,
    haftaSonuFark: null,
    gun: 3,
    gunlukUcret: 1000,
    tutar: 3000,
    genelToplam: 3000,
    tahsilat: 0,
    bakiye: 3000,
    provizyon: 5000,
    depozito: null,
    komisyonOran: 10,
    komisyonTutar: null,
    dropUcreti: 250.5,
    sonraOdeOran: null,
    aciklama: 'Açıklama',
    kaynak: 'Web',
    kampanyaKodu: null,
    uyariAciklama: null,
    ozelFaturaAciklama: null,
    faturaListesindeGizle: null,
    ucusNo: 'TK1923',
    provizyonNo: 'P-1',
    provizyonTarih: '2026-09-10T00:00:00+00:00',
    provizyonDurum: 'Yok',
    provizyonKapamaTarih: null,
    provizyonKapamaTutar: null,
    onayKodu: null,
    firmaKodu: null,
    projeAdi: null,
    ozelKod: null,
    talepTuru: null,
    geldigiBirim: null,
    kefilBilgisi: null,
    assistFirma: null,
    ozelSoforBilgisi: null,
    ekKosullar: 'Sigara içilmez',
    belgeSablonId: null,
    opsiyonNet: null,
    opsiyonGun: 2,
    riskOnay: false,
    manuelFindexPuan: 1450,
    kabisCikis: true,
    kabisDonus: null,
    otomatikUzat: false,
    aksYedekAnahtarCikis: true,
    aksYedekAnahtarDonus: null,
    aksStepneCikis: null,
    aksStepneDonus: null,
    aksZincirCikis: null,
    aksZincirDonus: null,
    aksIlkYardimCikis: null,
    aksIlkYardimDonus: null,
    aksLastikCikis: 'iyi',
    aksLastikDonus: null,
    kiralamaTuru: 'Kısa Kiralama',
    faturalamaTipi: null,
    fiyatTuru: 'KDV Dahil Günlük',
    doviz: 'TL',
    kurSnapshot: 1,
    donemselFaturalama: false,
    kdvOranSnapshot: 0.2,
    ozelKdvOran: 0.1,
    damgaVergisi: null,
    createdAtUtc: '2026-09-22T06:00:00+00:00',
    updatedAtUtc: null,
    surum: '4711',
    ...ek,
  };
}

function detay(ek: Partial<KiraSozlesmesi> = {}): KiraDetayYaniti {
  return {
    kira: kira(ek),
    musteri: { id: MUSTERI_ID, ad: 'Ayşe Yılmaz' },
    ikinciSurucu: { id: SURUCU_ID, ad: 'Mehmet Kaya' },
    arac: {
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
      konum: 'Otopark',
    },
    islemSubeAdi: 'Merkez Şube',
    teslimAlanPersonelAd: null,
    teslimEdenPersonelAd: 'Ali Veli',
    ekHizmetler: [],
    doviz: null,
    paylasim: null,
    yetkiler: { operasyon: true, silme: true, finans: true },
    toplamlar: { ekHizmetToplam: 0, cezaToplam: 0 },
    tahsilat: null,
  };
}

const cakisma = () =>
  apiHatasinaCevir(
    new HttpErrorResponse({
      status: 409,
      error: { status: 409, kod: 'cakisma', detail: 'Kira başka bir oturumda değişti.' },
    }),
  );

interface Put {
  readonly govde: Record<string, unknown>;
  readonly secenek?: IstekSecenekleri;
}

async function kur(put: (g: Record<string, unknown>, n: number) => Observable<unknown>) {
  const detaylar = new Subject<KiraDetayYaniti>();
  const putlar: Put[] = [];
  const api = {
    get: (yol: string) => {
      if (yol === `/api/ui/v1/kiralar/${KIRA_ID}`) return detaylar.asObservable().pipe((o) => o);
      if (yol.endsWith('/form-varsayilanlari')) return of({ cikisYakit: 8, fiyatTuru: null });
      return of([]);
    },
    put: (_yol: string, govde: Record<string, unknown>, secenek?: IstekSecenekleri) => {
      putlar.push({ govde, secenek });
      return put(govde, putlar.length);
    },
    post: (yol: string) =>
      yol.endsWith('/provizyon/al') ? of(kira({ provizyonDurum: 'Alindi', surum: 'v2' })) : of({}),
    delete: () => of({}),
  };
  TestBed.configureTestingModule({
    providers: [
      ...provideCeviri(),
      KiraFormuDurumu,
      { provide: ApiIstemcisi, useValue: api },
      {
        provide: ToastServisi,
        useValue: { basari: vi.fn(), bilgi: vi.fn(), uyari: vi.fn(), hata: vi.fn() },
      },
      { provide: OnayServisi, useValue: { sor: vi.fn(async () => true) } },
      { provide: Router, useValue: { navigate: vi.fn(async () => true) } },
      { provide: OturumServisi, useValue: { izinVar: () => true, ben: () => ({ rol: 'Admin' }) } },
      {
        provide: ActivatedRoute,
        useValue: {
          snapshot: {
            paramMap: convertToParamMap({ id: KIRA_ID }),
            queryParamMap: convertToParamMap({}),
            routeConfig: null,
            pathFromRoot: [],
            params: {},
          },
          queryParamMap: of(convertToParamMap({})),
          fragment: of(null),
        },
      },
    ],
  });
  await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  const d = TestBed.inject(KiraFormuDurumu);
  TestBed.tick();
  const detayVer = async (x: KiraDetayYaniti) => {
    detaylar.next(x);
    TestBed.tick();
    await Promise.resolve(); // sessiz yeniden gönderim mikro görevde
    TestBed.tick();
  };
  /** Süren detay okumasını HTTP hatasıyla bitirir (kodsuz gövde: 5xx → `sunucu`, 404 → `bilinmeyen`). */
  const detayHatasi = (status: number) => {
    detaylar.error(new HttpErrorResponse({ status, error: { status, detail: 'Sunucu hatası' } }));
    TestBed.tick();
  };
  return { d, putlar, detayVer, detayHatasi, bant: TestBed.inject(UyariBandiServisi) };
}

describe('Kira formu sürüm akışı (#261 N1/N2)', () => {
  it('N1: işlem sonrası kayıt yeniden okunurken Kaydet pasif; okuma bitince açık', async () => {
    const { d, detayVer } = await kur(() => of(kira()));
    await detayVer(detay({ surum: 'v1' }));
    expect(d.kaydedilebilir()).toBe(true);
    d.provizyonAl(); // 2xx → yenile() → detay yükleniyor
    expect(d.kayitTazeleniyor()).toBe(true);
    expect(d.kaydedilebilir()).toBe(false);
    await detayVer(detay({ surum: 'v2', provizyonDurum: 'Alindi' }));
    expect(d.kaydedilebilir()).toBe(true);
  });

  it('L5: tazeleme 5xx → form ve finans paneli son iyi veriyle kalır, hata bandı çıkar, Kaydet pasif', async () => {
    const { d, detayVer, detayHatasi } = await kur(() => of(kira()));
    const iyi = detay({ surum: 'v1' });
    await detayVer(iyi);
    d.yenile();
    detayHatasi(503);
    expect(d.detay.tur()).toBe('hata');
    expect(d.gorunenDetay()).toBe(iyi); // panel girdisi (`[detay]`) aynı nesne — kaybolmaz
    expect(d.kira()?.surum).toBe('v1');
    expect(d.tazelemeHatasi()?.kod).toBe('sunucu');
    expect(d.kaydedilebilir()).toBe(false);
  });

  it('L5: tazeleme 404 (kayıt silinmiş) → eski veri GÖSTERİLMEZ, bant yok', async () => {
    const { d, detayVer, detayHatasi } = await kur(() => of(kira()));
    await detayVer(detay({ surum: 'v1' }));
    d.yenile();
    detayHatasi(404);
    expect(d.gorunenDetay()).toBeNull();
    expect(d.kira()).toBeNull();
    expect(d.tazelemeHatasi()).toBeNull();
  });

  it('N2: başka oturum dokunulmayan alanı değiştirdi → TEK sefer sessiz yeniden gönderim, yeni sürümle', async () => {
    const { d, putlar, detayVer, bant } = await kur((g) =>
      g['surum'] === 'v2' ? of(kira({ surum: 'v3' })) : throwError(() => cakisma()),
    );
    await detayVer(detay({ surum: 'v1', dropUcreti: null }));
    d.form.controls.aciklama.setValue('benim notum');
    d.form.controls.aciklama.markAsDirty();
    d.kaydet(() => undefined);
    expect(putlar).toHaveLength(1);
    bant.goster({ tur: 'uyari', mesaj: 'Kira başka bir oturumda değişti.', kod: 'cakisma' }); // interceptor'ın bandı
    // Güncel kayıt: başka sekme drop ücretini yazdı (+ tahsilat → sürüm v2); açıklamaya dokunmadı.
    await detayVer(detay({ surum: 'v2', dropUcreti: 300 }));
    expect(putlar).toHaveLength(2);
    expect(putlar[1]?.govde).toMatchObject({
      surum: 'v2',
      aciklama: 'benim notum',
      dropUcreti: 300,
    });
    expect(bant.bant()).toBeNull(); // 409 bandı kapandı
  });

  it('N2: aynı alana başka oturum yazdı → çakışma işaretlenir, otomatik gönderim YOK', async () => {
    const { d, putlar, detayVer, bant } = await kur(() => throwError(() => cakisma()));
    await detayVer(detay({ surum: 'v1', aciklama: null }));
    d.form.controls.aciklama.setValue('benim notum');
    d.form.controls.aciklama.markAsDirty();
    d.kaydet(() => undefined);
    await detayVer(detay({ surum: 'v2', aciklama: 'öteki notu' }));
    expect(putlar).toHaveLength(1);
    expect(d.form.controls.aciklama.value).toBe('benim notum');
    expect(bant.bant()?.kod).toBe('cakisma');
  });

  it('N2: sürüm DEĞİŞMEDİYSE (başka tür çakışma, ör. müsaitlik) yeniden gönderilmez', async () => {
    const { d, putlar, detayVer } = await kur(() => throwError(() => cakisma()));
    await detayVer(detay({ surum: 'v1' }));
    d.form.controls.aciklama.setValue('x');
    d.form.controls.aciklama.markAsDirty();
    d.kaydet(() => undefined);
    await detayVer(detay({ surum: 'v1' }));
    expect(putlar).toHaveLength(1);
  });

  it('N2: sessiz yeniden gönderim de 409 alırsa ÜÇÜNCÜ gönderim yok (tek seferlik)', async () => {
    const { d, putlar, detayVer } = await kur(() => throwError(() => cakisma()));
    await detayVer(detay({ surum: 'v1' }));
    d.form.controls.aciklama.setValue('x');
    d.form.controls.aciklama.markAsDirty();
    d.kaydet(() => undefined);
    await detayVer(detay({ surum: 'v2' }));
    expect(putlar).toHaveLength(2);
    await detayVer(detay({ surum: 'v3' }));
    expect(putlar).toHaveLength(2);
  });
});
