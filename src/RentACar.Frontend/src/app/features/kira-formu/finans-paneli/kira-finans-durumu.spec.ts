import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { type Observable, Subject, firstValueFrom, of, throwError } from 'rxjs';
import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi, type IstekSecenekleri } from '@core/api/api-istemcisi';
import { SUNUCU_HATASI } from '@core/form/sunucu-hatalari';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { provideCeviri } from '@core/i18n/ceviri';
import { MUKERRER_BASLIGI, MUKERRERDE_YENILE, SESSIZ } from '@core/oturum/istek-baglami';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import type { KiraDetayYaniti, KiraSozlesmesi } from '../kira-tipleri';
import type { TahsilatBilgisi } from './finans-tipleri';
import { KiraFinansDurumu } from './kira-finans-durumu';

const KIRA_ID = '0b0e7c1a-1111-4aaa-8bbb-000000000001';
const MUSTERI_ID = '0b0e7c1a-2222-4aaa-8bbb-000000000002';
const K1 = 'aaaaaaaa-0000-4000-8000-000000000001';
const K2 = 'aaaaaaaa-0000-4000-8000-000000000002';

const sunucuHatasi = (status: number, kod: string, detail: string, errors?: object) =>
  apiHatasinaCevir(new HttpErrorResponse({ status, error: { status, kod, detail, errors } }));
const agHatasi = () => apiHatasinaCevir(new HttpErrorResponse({ status: 0 }));

const tahsilat = (anahtar: string, varsayilanTutar = 2600): TahsilatBilgisi => ({
  anahtar,
  cariId: MUSTERI_ID,
  rentalId: KIRA_ID,
  doviz: 'TRY',
  varsayilanTutar,
});

/** Yalnız panelin okuduğu alanlar dolu; tip gereği gerisi elle (formül/hesap yok). */
function detay(t: TahsilatBilgisi | null, ek: Partial<KiraSozlesmesi> = {}): KiraDetayYaniti {
  const kira = {
    id: KIRA_ID,
    sozlesmeNo: '2026220901001',
    durum: 'Kirada',
    musteriId: MUSTERI_ID,
    genelToplam: 3600,
    tahsilat: 1000,
    bakiye: 2600,
    doviz: 'TL',
    depozito: 500,
    damgaVergisi: null,
    ...ek,
  } as unknown as KiraSozlesmesi;
  return {
    kira,
    musteri: { id: MUSTERI_ID, ad: 'Ayşe Yılmaz' },
    ikinciSurucu: null,
    arac: null,
    islemSubeAdi: null,
    teslimAlanPersonelAd: null,
    teslimEdenPersonelAd: null,
    ekHizmetler: [],
    doviz: null,
    paylasim: null,
    yetkiler: { operasyon: true, silme: true, finans: true },
    tahsilat: t,
  };
}

interface Cagri {
  readonly yol: string;
  readonly govde: unknown;
  readonly secenek?: IstekSecenekleri;
}

async function kur(yazma: (c: Cagri) => Observable<unknown>) {
  const cagrilar: Cagri[] = [];
  const api = {
    get: vi.fn(() => of([])),
    post: (yol: string, govde: unknown, secenek?: IstekSecenekleri) => {
      const c = { yol, govde, secenek };
      cagrilar.push(c);
      return yazma(c);
    },
  };
  const toast = { basari: vi.fn(), bilgi: vi.fn(), uyari: vi.fn(), hata: vi.fn() };
  const onay = { sor: vi.fn(async () => true) };
  TestBed.configureTestingModule({
    providers: [
      ...provideCeviri(),
      KiraFinansDurumu,
      { provide: ApiIstemcisi, useValue: api },
      { provide: ToastServisi, useValue: toast },
      { provide: OnayServisi, useValue: onay },
      { provide: OturumServisi, useValue: { izinVar: () => true } },
    ],
  });
  await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  const f = TestBed.inject(KiraFinansDurumu);
  const degisti = vi.fn();
  f.degisti = degisti;
  const detayVer = (d: KiraDetayYaniti) => {
    f.detayAyarla(d);
    TestBed.tick();
  };
  return { f, cagrilar, toast, onay, degisti, detayVer };
}

const tahsilatlar = (c: readonly Cagri[]) => c.filter((x) => x.yol.endsWith('/finans/tahsilat'));
const govdesi = (c: Cagri | undefined) => c?.govde as Record<string, unknown>;

describe('KiraFinansDurumu — tahsilat (deterministik anahtar)', () => {
  it('ön-doldurur; gövdede detaydaki anahtar, BAŞLIK YOK; 2xx sonrası yeni detayın anahtarıyla ikinci meşru tahsilat', async () => {
    const { f, cagrilar, degisti, detayVer, toast } = await kur(() => of({ id: 'c1' }));
    detayVer(detay(tahsilat(K1)));
    expect(f.nakit.form.getRawValue()).toMatchObject({ tutar: '2600.00', doviz: 'TRY' });

    f.nakit.form.controls.tutar.setValue('1500.50'); // rc-para-girdisi "1.500,50" yazımının değeri
    f.tahsilatYap(f.nakit);
    const ilk = tahsilatlar(cagrilar)[0];
    expect(govdesi(ilk)).toMatchObject({
      cariId: MUSTERI_ID,
      kiraId: KIRA_ID,
      tahsilatAnahtar: K1,
      tutar: '1500.50',
      hesap: 'Kasa',
      doviz: 'TRY',
    });
    expect('kur' in govdesi(ilk)).toBe(false); // TRY'de kur gönderilmez
    expect(ilk?.secenek?.islemAnahtari).toBeUndefined(); // deterministikte başlık tüketilmez
    expect(toast.basari).toHaveBeenCalledWith('Tahsilat kaydedildi.');
    expect(degisti).toHaveBeenCalledTimes(1);

    // Yeni detay gelene dek ikinci gönderim yok (eski anahtar bayat).
    expect(f.nakit.kopya.gonderilebilir()).toBe(false);
    f.tahsilatYap(f.nakit);
    expect(tahsilatlar(cagrilar)).toHaveLength(1);

    detayVer(detay(tahsilat(K2, 1099.5)));
    expect(f.nakit.form.getRawValue().tutar).toBe('1099.50');
    f.tahsilatYap(f.nakit);
    expect(tahsilatlar(cagrilar)).toHaveLength(2);
    expect(govdesi(tahsilatlar(cagrilar)[1])['tahsilatAnahtar']).toBe(K2);
  });

  it('çift tık tek istek (istek uçarken ikinci gönderim yok sayılır)', async () => {
    const yanit = new Subject<unknown>();
    const { f, cagrilar, detayVer } = await kur(() => yanit);
    detayVer(detay(tahsilat(K1)));
    f.tahsilatYap(f.nakit);
    f.tahsilatYap(f.nakit);
    expect(tahsilatlar(cagrilar)).toHaveLength(1);
    yanit.next({ id: 'c1' });
    yanit.complete();
  });

  it('ağ hatası sonrası yeniden deneme AYNI anahtar + AYNI gövde — arada detay tazelense bile', async () => {
    let n = 0;
    const { f, cagrilar, detayVer } = await kur(() =>
      ++n === 1 ? throwError(() => agHatasi()) : of({ id: 'c1' }),
    );
    detayVer(detay(tahsilat(K1)));
    f.tahsilatYap(f.nakit);
    // Başka bir işlem kirayı tazeledi (yeni anahtar geldi); ilk istek belki yazıldı → anahtar DONUK kalmalı.
    detayVer(detay(tahsilat(K2, 100)));
    f.tahsilatYap(f.nakit);
    const [a, b] = tahsilatlar(cagrilar);
    expect(govdesi(b)['tahsilatAnahtar']).toBe(K1);
    expect(JSON.stringify(b?.govde)).toBe(JSON.stringify(a?.govde));
    expect(b?.secenek?.islemAnahtari).toBeUndefined(); // anahtarsız/başlıklı tekrar yok
  });

  it('açık (kirli) form eski satır kopyasını korur: bayat anahtar gönderilir → sunucu 409 verir', async () => {
    const { f, cagrilar, detayVer } = await kur(() => of({ id: 'c1' }));
    detayVer(detay(tahsilat(K1)));
    f.nakit.form.controls.tutar.setValue('200.00');
    f.nakit.form.markAsDirty();
    detayVer(detay(tahsilat(K2, 900)));
    expect(f.nakit.form.getRawValue().tutar).toBe('200.00'); // yazılan korunur
    f.tahsilatYap(f.nakit);
    expect(govdesi(tahsilatlar(cagrilar)[0])['tahsilatAnahtar']).toBe(K1);
  });

  it('409 mukerrer: otomatik tekrar YOK; çekirdek "Kira kaydı değişmiş" başlığı (Mükerrer işlem değil); sonra yeni anahtar', async () => {
    const { f, cagrilar, detayVer, degisti } = await kur(() =>
      throwError(() =>
        sunucuHatasi(409, 'mukerrer', 'Kiranın bakiyesi bu ekran açıldıktan sonra değişti.'),
      ),
    );
    detayVer(detay(tahsilat(K1)));
    f.nakit.form.controls.tutar.setValue('300.00');
    f.nakit.form.markAsDirty();
    f.tahsilatYap(f.nakit);
    expect(tahsilatlar(cagrilar)).toHaveLength(1);

    // Bildirim çekirdek interceptor'da: nötr başlık + sunucu detail'ı; istek sessiz DEĞİL (genel hatalar görünür).
    const baglam = tahsilatlar(cagrilar)[0]?.secenek?.context;
    expect(baglam?.get(MUKERRER_BASLIGI)).toBe('Kira kaydı değişmiş');
    expect(baglam?.get(SESSIZ)).toBe(false);
    baglam?.get(MUKERRERDE_YENILE)?.(); // interceptor'ın yaptığı
    expect(degisti).toHaveBeenCalledTimes(1);
    expect(f.nakit.gonderim.genelHatalar()).toEqual([]); // form üstüne "mükerrer" yazılmaz
    expect(f.nakit.form.getRawValue().tutar).toBe('300.00'); // form silinmez

    expect(f.nakit.kopya.gonderilebilir()).toBe(false);
    detayVer(detay(tahsilat(K2, 1)));
    expect(f.nakit.kopya.kopya()?.anahtar).toBe(K2);
    expect(f.nakit.form.getRawValue().tutar).toBe('300.00'); // kullanıcının yazdığı yine korunur
    expect(tahsilatlar(cagrilar)).toHaveLength(1); // kendiliğinden yeniden gönderim yok
  });

  it('dogrulama: alan hatası alana yazılır, değerler korunur', async () => {
    const { f, detayVer } = await kur(() =>
      throwError(() =>
        sunucuHatasi(400, 'dogrulama', 'Kur pozitif olmalıdır.', {
          kur: ['Kur pozitif olmalıdır.'],
        }),
      ),
    );
    detayVer(detay(tahsilat(K1)));
    f.nakit.form.patchValue({ doviz: 'USD', kur: '32.5' });
    f.tahsilatYap(f.nakit);
    expect(f.nakit.form.controls.kur.errors?.[SUNUCU_HATASI]).toEqual(['Kur pozitif olmalıdır.']);
    expect(f.nakit.form.getRawValue()).toMatchObject({ doviz: 'USD', kur: '32.5' });
  });

  it('dövizde boş kur gönderilmez (sunucu çözer), dolu kur gider', async () => {
    const { f, cagrilar, detayVer } = await kur(() => of({ id: 'c1' }));
    detayVer(detay(tahsilat(K1)));
    f.kart.form.patchValue({ doviz: 'USD', kur: null, tutar: '10.00' });
    f.tahsilatYap(f.kart);
    expect(govdesi(tahsilatlar(cagrilar)[0])).toMatchObject({ doviz: 'USD', hesap: 'Banka' });
    expect('kur' in govdesi(tahsilatlar(cagrilar)[0])).toBe(false);
  });

  it('iptal/izinsiz kira (tahsilat satırı yok) → gönderim yapılmaz', async () => {
    const { f, cagrilar, detayVer } = await kur(() => of({ id: 'c1' }));
    detayVer(detay(null));
    f.tahsilatYap(f.nakit);
    expect(cagrilar).toHaveLength(0);
  });
});

describe('KiraFinansDurumu — başlık anahtarlı işlemler', () => {
  it('ödeme: anahtar ilk gönderimde üretilir, ağ hatasında AYNI, 2xx sonrası YENİ; depozito kendi anahtarını kullanır', async () => {
    let n = 0;
    const { f, cagrilar, detayVer } = await kur((c) =>
      c.yol.endsWith('/odeme') && ++n === 1 ? throwError(() => agHatasi()) : of({ id: 'x' }),
    );
    detayVer(detay(tahsilat(K1)));
    f.odemeFormu.patchValue({ tutar: '75.00' });
    f.odemeYap(); // ağ hatası
    f.odemeYap(); // yeniden deneme: aynı anahtar
    const odemeler = cagrilar.filter((c) => c.yol.endsWith('/finans/odeme'));
    expect(odemeler).toHaveLength(2);
    const [a, b] = odemeler;
    expect(a?.secenek?.islemAnahtari).toMatch(/^[0-9a-f-]{36}$/);
    expect(b?.secenek?.islemAnahtari).toBe(a?.secenek?.islemAnahtari);
    expect(b?.govde).toEqual(a?.govde);
    expect(govdesi(a)).toMatchObject({ cariId: MUSTERI_ID, hesap: 'Banka', tutar: '75.00' });
    expect('kiraId' in govdesi(a)).toBe(false);

    // 2xx sonrası form sıfırlandı; ikinci meşru ödeme YENİ anahtarla.
    f.odemeFormu.patchValue({ tutar: '20.00' });
    f.odemeYap();
    const ucuncu = cagrilar.filter((c) => c.yol.endsWith('/finans/odeme'))[2];
    expect(ucuncu?.secenek?.islemAnahtari).not.toBe(a?.secenek?.islemAnahtari);

    // Başka işlem türü kendi anahtarıyla.
    f.depozitoAlFormu.patchValue({ tutar: '500.00', hesap: 'Kasa' });
    f.depozitoAl();
    const dep = cagrilar.find((c) => c.yol.endsWith('/depozito/al'));
    expect(dep?.secenek?.islemAnahtari).toBeDefined();
    expect(dep?.secenek?.islemAnahtari).not.toBe(ucuncu?.secenek?.islemAnahtari);
    expect(dep?.secenek?.context?.get(MUKERRERDE_YENILE)).toBeTypeOf('function');
  });

  it('409 mukerrer (ödeme): otomatik tekrar yok, sonraki gönderim YENİ anahtar', async () => {
    let n = 0;
    const { f, cagrilar, detayVer } = await kur(() =>
      ++n === 1
        ? throwError(() => sunucuHatasi(409, 'mukerrer', 'Bu işlem zaten kaydedilmiş.'))
        : of({ id: 'x' }),
    );
    detayVer(detay(tahsilat(K1)));
    f.odemeFormu.patchValue({ tutar: '75.00' });
    f.odemeYap();
    expect(cagrilar).toHaveLength(1);
    f.odemeYap();
    expect(cagrilar[1]?.secenek?.islemAnahtari).not.toBe(cagrilar[0]?.secenek?.islemAnahtari);
  });

  it('depozito ön-doldurma kiranın depozitosu; irat önce onay ister, vazgeçilirse istek yok', async () => {
    const { f, cagrilar, detayVer, onay } = await kur(() => of({ id: 'x' }));
    detayVer(detay(tahsilat(K1)));
    expect(f.depozitoAlFormu.getRawValue().tutar).toBe('500.00');
    onay.sor.mockResolvedValueOnce(false);
    f.iratFormu.patchValue({ tutar: '100.00' });
    await f.depozitoIrat();
    expect(cagrilar).toHaveLength(0);
    await f.depozitoIrat();
    expect(govdesi(cagrilar[0])).toEqual({
      cariId: MUSTERI_ID,
      kiraId: KIRA_ID,
      tutar: '100.00',
      aciklama: null,
    });
  });
});

describe('KiraFinansDurumu — yapısal işlemler', () => {
  it('dönem kes + tahsil: tahsilatYazildi=false iken sunucu bilgisi gizlenmez', async () => {
    const bilgi = 'Bu dönemin tahsilatı daha önce alınmış; yeni tahsilat yazılmadı.';
    const { f, cagrilar, detayVer, toast } = await kur(() =>
      of({ faturaId: 'f1', tahsilatYazildi: false, bilgi }),
    );
    detayVer(detay(tahsilat(K1)));
    f.donemFormu(2).patchValue({ tahsilat: true, hesap: 'Banka' });
    f.donemKes({
      donemSira: 2,
      donemBas: '2026-10-01T00:00:00Z',
      donemBit: '2026-10-31T00:00:00Z',
      durum: 'Planlandi',
      tahakkuk: 3100,
      invoiceId: null,
      kesilenTutar: null,
    });
    expect(govdesi(cagrilar[0])).toEqual({
      kiraId: KIRA_ID,
      donemSira: 2,
      tahsilat: true,
      hesap: 'Banka',
    });
    expect(cagrilar[0]?.secenek).toBeUndefined(); // yapısal: başlık yok
    expect(toast.bilgi).toHaveBeenCalledWith(bilgi, { baslik: 'Tahsilat yazılmadı' });
    expect(f.donemBilgisi()).toBe(bilgi);
    expect(toast.basari).toHaveBeenCalledWith('Dönem faturası kesildi.');
  });

  it('dış hizmet iptali: onay → POST …/iptal; 400 doğrulama hata bildirimi', async () => {
    const { f, cagrilar, detayVer, toast } = await kur(() =>
      throwError(() => sunucuHatasi(400, 'dogrulama', 'Kayıt zaten iptal edilmiş.')),
    );
    detayVer(detay(tahsilat(K1)));
    await f.disHizmetIptal({
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
    const { f, detayVer, toast } = await kur(() =>
      throwError(() => sunucuHatasi(403, 'yetki_yok', 'Bu işlem için yetkiniz yok.')),
    );
    detayVer(detay(tahsilat(K1)));
    await f.disHizmetIptal({
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
    f.faturaKes();
    expect(f.faturaGonderimi.genelHatalar()).toEqual([]);
  });
});
