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
import { MUKERRER_CAGIRAN_GOSTERIR, MUKERRERDE_YENILE, SESSIZ } from '@core/oturum/istek-baglami';
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
    toplamlar: { ekHizmetToplam: 0, cezaToplam: 0 },
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
      {
        provide: OturumServisi,
        useValue: { izinVar: () => true, temizlikKaydet: () => () => undefined },
      },
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

  it("H1 (#316): Nakit uçarken ve sonuçlanıp tazeleme beklerken Kart gönderilemez; Kart'a yazılan tutar tazelemede EZİLMEZ ve aynen gider", async () => {
    const yanit = new Subject<unknown>();
    let n = 0;
    const { f, cagrilar, detayVer } = await kur(() => (++n === 1 ? yanit : of({ id: 'c2' })));
    detayVer(detay(tahsilat(K1)));
    expect(f.tahsilatMesgul()).toBe(false);
    f.kart.form.controls.tutar.setValue('600.00'); // kullanıcı Kart/Havale'ye 600 yazdı
    f.kart.form.markAsDirty();

    f.nakit.form.controls.tutar.setValue('700.00');
    f.tahsilatYap(f.nakit); // istek uçuyor (aynı anahtar K1)
    expect(f.tahsilatMesgul()).toBe(true);
    f.tahsilatYap(f.kart);
    expect(tahsilatlar(cagrilar)).toHaveLength(1);

    yanit.next({ id: 'c1' }); // 2xx → kira tazeleniyor
    yanit.complete();
    expect(f.tahsilatMesgul()).toBe(true);
    expect(f.tahsilatTazeleniyor()).toBe(true);
    f.tahsilatYap(f.kart);
    expect(tahsilatlar(cagrilar)).toHaveLength(1);

    detayVer(detay(tahsilat(K2, 1900))); // 2.600 − 700 = 1.900
    expect(f.tahsilatMesgul()).toBe(false);
    expect(f.kart.form.getRawValue().tutar).toBe('600.00'); // öneri (1.900) yazılanı ezmedi
    f.tahsilatYap(f.kart);
    const t = tahsilatlar(cagrilar);
    // #318 L2: Nakit'in 2xx'i K1'i kesin tüketti → kirli Kart da K2'yi aldı (gereksiz 409 turu yok).
    expect(
      t.map((c) => [govdesi(c)['hesap'], govdesi(c)['tahsilatAnahtar'], govdesi(c)['tutar']]),
    ).toEqual([
      ['Kasa', K1, '700.00'],
      ['Banka', K2, '600.00'],
    ]);
  });

  it('#318 L1: tahsilat sonrası tazeleme hata verdi → "Yeniden yükle" (yenile → sayfa), başarılı okumada düğmeler açılır', async () => {
    const { f, cagrilar, detayVer, degisti } = await kur(() => of({ id: 'c1' }));
    detayVer(detay(tahsilat(K1)));
    f.detayHatasiAyarla(true); // ilgisiz hata bayrağı: tahsilat tazeleme beklemiyorsa gösterilmez
    expect(f.tahsilatYuklenemedi()).toBe(false);
    f.detayHatasiAyarla(false);

    f.tahsilatYap(f.nakit);
    expect(degisti).toHaveBeenCalledTimes(1);
    f.detayHatasiAyarla(true); // sayfa: tazeleme 503, ekran son iyi veriyle
    expect(f.tahsilatYuklenemedi()).toBe(true);
    expect(f.tahsilatMesgul()).toBe(true); // bayat K1 ile gönderim yok
    f.tahsilatYap(f.kart);
    expect(tahsilatlar(cagrilar)).toHaveLength(1);

    f.yenile(); // "Yeniden yükle"
    expect(degisti).toHaveBeenCalledTimes(2);
    f.detayHatasiAyarla(false);
    detayVer(detay(tahsilat(K2, 1900)));
    expect(f.tahsilatYuklenemedi()).toBe(false);
    expect(f.tahsilatMesgul()).toBe(false);
    f.tahsilatYap(f.kart);
    expect(govdesi(tahsilatlar(cagrilar)[1])['tahsilatAnahtar']).toBe(K2);
  });

  it('409 mukerrer: otomatik tekrar YOK; "Kira kaydı değişmiş" başlığı (Mükerrer işlem değil); sonra yeni anahtar', async () => {
    const { f, cagrilar, detayVer, degisti, toast } = await kur(() =>
      throwError(() =>
        sunucuHatasi(409, 'mukerrer', 'Kiranın bakiyesi bu ekran açıldıktan sonra değişti.'),
      ),
    );
    detayVer(detay(tahsilat(K1)));
    f.nakit.form.controls.tutar.setValue('300.00');
    f.nakit.form.markAsDirty();
    f.tahsilatYap(f.nakit);
    expect(tahsilatlar(cagrilar)).toHaveLength(1);

    // mukerrer toast'u çağıranda (sınıf tekrar bilgisine bağlı, M-C): nötr başlık + sunucu detail'ı; istek sessiz
    // DEĞİL (ağ/5xx/yetki genel katmanda görünür).
    const baglam = tahsilatlar(cagrilar)[0]?.secenek?.context;
    expect(baglam?.get(MUKERRER_CAGIRAN_GOSTERIR)).toBe(true);
    expect(baglam?.get(SESSIZ)).toBe(false);
    expect(toast.uyari).toHaveBeenCalledWith(
      'Kiranın bakiyesi bu ekran açıldıktan sonra değişti. Kayıt yeniden yüklendi.',
      { baslik: 'Kira kaydı değişmiş' },
    );
    baglam?.get(MUKERRERDE_YENILE)?.(); // interceptor'ın yaptığı
    expect(degisti).toHaveBeenCalledTimes(1);
    expect(f.nakit.gonderim.genelHatalar()).toEqual([]); // form üstüne "mükerrer" yazılmaz
    // HIGH-1: tutar TEMİZLENİR (kullanıcı güncel bakiyeye bakıp bilinçli girer); diğer alanlar korunur.
    expect(f.nakit.form.getRawValue().tutar).toBeNull();

    expect(f.nakit.kopya.gonderilebilir()).toBe(false);
    detayVer(detay(tahsilat(K2, 1)));
    expect(f.nakit.kopya.kopya()?.anahtar).toBe(K2);
    expect(f.nakit.form.getRawValue().tutar).toBeNull(); // 409 sonrası yeniden ön-doldurulmaz
    expect(tahsilatlar(cagrilar)).toHaveLength(1); // kendiliğinden yeniden gönderim yok
  });

  it('HIGH-1: kaybolan yanıt → AYNI anahtarla tekrar → 409 + mevcut: form temizlenir, ön-doldurulmaz, ikinci istek yok', async () => {
    let n = 0;
    const { f, cagrilar, detayVer } = await kur(() =>
      ++n === 1
        ? throwError(() => agHatasi()) // ilk istek yazıldı, yanıt kayboldu
        : throwError(() =>
            apiHatasinaCevir(
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
    f.tahsilatYap(f.nakit); // ağ hatası
    f.tahsilatYap(f.nakit); // doğru tekrar: AYNI anahtar
    const [a, b] = tahsilatlar(cagrilar);
    expect(govdesi(b)['tahsilatAnahtar']).toBe(K1);
    expect(b?.govde).toEqual(a?.govde);
    // Form tamamen temizlendi (tutar + not); kaydı yeniden yükleyen detay yeni tutar ÖNERMEZ.
    expect(f.nakit.form.getRawValue()).toMatchObject({ tutar: null, aciklama: null });
    detayVer(detay(tahsilat(K2, 2100)));
    expect(f.nakit.form.getRawValue().tutar).toBeNull();
    expect(f.nakit.kopya.gonderilebilir()).toBe(true); // yeni anahtarla BİLİNÇLİ yeni tahsilat mümkün
    expect(tahsilatlar(cagrilar)).toHaveLength(2);
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
    // mukerrer bildirimi çekirdeğin form notunda (interceptor toast'u değil); kayıt settled'da yenilenir.
    expect(dep?.secenek?.context?.get(MUKERRER_CAGIRAN_GOSTERIR)).toBe(true);
  });

  it('409 mukerrer mevcutsuz (ödeme): otomatik tekrar yok; anahtar KORUNUR, gövde donar (DEVIR §5 gece dersi)', async () => {
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
    expect(f.odemeGonderimi.frozen()).not.toBeNull();
    f.odemeFormu.patchValue({ tutar: '99.00' }); // kilitli formu yazılımla değiştirmek gövdeyi değiştirmez
    f.odemeYap();
    expect(cagrilar[1]?.secenek?.islemAnahtari).toBe(cagrilar[0]?.secenek?.islemAnahtari);
    expect(cagrilar[1]?.govde).toEqual(cagrilar[0]?.govde);
  });

  it('3. tur M-A + L-2: 409 + mevcut İÇERİK FARKLI (başka sekme yazdı) → form SİLİNMEZ; dokunulmamış ön-dolu tutar yeni bakiyeyle yenilenir', async () => {
    let n = 0;
    const { f, cagrilar, detayVer, degisti, toast } = await kur(() =>
      ++n === 1
        ? throwError(() =>
            apiHatasinaCevir(
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
    f.tahsilatYap(f.nakit);
    const baglam = tahsilatlar(cagrilar)[0]?.secenek?.context;
    baglam?.get(MUKERRERDE_YENILE)?.();
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
    f.tahsilatYap(f.nakit); // kullanıcı bakiyeye bakıp BİLİNÇLİ gönderir
    expect(tahsilatlar(cagrilar)).toHaveLength(2);
    expect(govdesi(tahsilatlar(cagrilar)[1])).toMatchObject({
      tahsilatAnahtar: K2,
      tutar: '2500.00',
    });
  });

  it('L-2: M-A sonrası kullanıcının ELLE yazdığı tutar yeni bakiyeyle EZİLMEZ', async () => {
    const { f, detayVer } = await kur(() =>
      throwError(() =>
        apiHatasinaCevir(
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
    f.tahsilatYap(f.nakit);
    detayVer(detay(tahsilat(K2, 2500)));
    expect(f.nakit.form.getRawValue().tutar).toBe('700.00');
    expect(f.nakit.kopya.kopya()?.anahtar).toBe(K2);
  });

  /** M-C: sunucunun sonucu (elle): 1. istek 500 yazıldı ama yanıt kayboldu; 2. istek aynı anahtar + 600 → 409 mevcut{500, farklı}. */
  const oncekiDenemeKaydedilmis = () =>
    apiHatasinaCevir(
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
    const { f, cagrilar, detayVer, toast, degisti } = await kur(() =>
      ++n === 1 ? throwError(() => agHatasi()) : throwError(() => oncekiDenemeKaydedilmis()),
    );
    detayVer(detay(tahsilat(K1)));
    f.nakit.form.controls.tutar.setValue('500.00');
    f.nakit.form.controls.tutar.markAsDirty();
    f.tahsilatYap(f.nakit); // yazıldı, yanıt kayboldu
    f.nakit.form.controls.tutar.setValue('600.00'); // kullanıcı tutarı düzeltti
    f.tahsilatYap(f.nakit);
    const t = tahsilatlar(cagrilar);
    expect(t.map((c) => govdesi(c)['tahsilatAnahtar'])).toEqual([K1, K1]); // donmuş anahtar
    expect(govdesi(t[1])['tutar']).toBe('600.00');
    t[1]?.secenek?.context?.get(MUKERRERDE_YENILE)?.();
    expect(degisti).toHaveBeenCalledTimes(1);

    expect(toast.uyari).toHaveBeenCalledTimes(1);
    const [mesaj, secenek] = toast.uyari.mock.calls[0] as unknown as [string, { baslik: string }];
    expect(mesaj).toContain('Önceki denemeniz kaydedilmiş (No T-42, 500,00');
    expect(mesaj).toContain('girdiğiniz 600,00');
    expect(mesaj).toContain('YAZILMADI');
    expect(mesaj).not.toContain('başka bir tahsilat');
    expect(secenek.baslik).toBe('Önceki denemeniz kaydedilmiş — yeni tutar yazılmadı');
    expect(f.nakit.form.getRawValue().tutar).toBeNull();

    // Tazelenen detay (yeni anahtar K2): tutar önerilmez (L-2 yalnız M-A'da); boş tutar → İSTEK YOK.
    detayVer(detay(tahsilat(K2, 2100)));
    expect(f.nakit.kopya.kopya()?.anahtar).toBe(K2);
    expect(f.nakit.form.getRawValue().tutar).toBeNull();
    f.tahsilatYap(f.nakit);
    expect(tahsilatlar(cagrilar)).toHaveLength(2);
  });

  it('5. tur MEDIUM-1: Nakit\'te kaybolan 500 → Kart/Havale\'de AYNI anahtarla 600 → "önceki denemeniz kaydedilmiş", Kart tutarı TEMİZLENİR, ikinci basış istek göndermez', async () => {
    let n = 0;
    const { f, cagrilar, detayVer, toast } = await kur(() =>
      ++n === 1 ? throwError(() => agHatasi()) : throwError(() => oncekiDenemeKaydedilmis()),
    );
    detayVer(detay(tahsilat(K1)));
    f.nakit.form.controls.tutar.setValue('500.00');
    f.nakit.form.controls.tutar.markAsDirty();
    f.tahsilatYap(f.nakit); // yazıldı, yanıt kayboldu
    f.kart.form.controls.tutar.setValue('600.00');
    f.kart.form.controls.tutar.markAsDirty();
    f.tahsilatYap(f.kart);
    const t = tahsilatlar(cagrilar);
    expect(t.map((c) => [govdesi(c)['tahsilatAnahtar'], govdesi(c)['hesap']])).toEqual([
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
    f.tahsilatYap(f.kart); // boş tutar → istek YOK
    expect(tahsilatlar(cagrilar)).toHaveLength(2);
  });

  it('M-C: aradaki kesin red (400) önceki bilinmeyen denemeyi KAPATMAZ; bilinmeyen deneme yoksa aynı 409 "başka tahsilat" (M-A) kalır', async () => {
    let n = 0;
    const { f, cagrilar, detayVer, toast } = await kur(() => {
      n++;
      if (n === 1) return throwError(() => sunucuHatasi(500, '', ''));
      if (n === 2) return throwError(() => sunucuHatasi(400, 'dogrulama', 'Kur girilemez.'));
      return throwError(() => oncekiDenemeKaydedilmis());
    });
    detayVer(detay(tahsilat(K1)));
    f.nakit.form.controls.tutar.setValue('500.00');
    f.tahsilatYap(f.nakit); // 5xx: sonucu bilinmiyor
    f.tahsilatYap(f.nakit); // 400: bu istek yazılmadı ama 1. deneme hâlâ belirsiz
    f.nakit.form.controls.tutar.setValue('600.00');
    f.tahsilatYap(f.nakit);
    expect(tahsilatlar(cagrilar)).toHaveLength(3);
    expect(toast.uyari).toHaveBeenLastCalledWith(
      expect.stringContaining('Önceki denemeniz kaydedilmiş'),
      expect.anything(),
    );
    expect(f.nakit.form.getRawValue().tutar).toBeNull();

    // Aynı yanıt, ÖNCESİNDE bilinmeyen deneme YOKKEN: başka sekmenin işlemi → form korunur (M-A).
    TestBed.resetTestingModule();
    const b = await kur(() => throwError(() => oncekiDenemeKaydedilmis()));
    b.detayVer(detay(tahsilat(K1)));
    b.f.nakit.form.controls.tutar.setValue('600.00');
    b.f.nakit.form.controls.tutar.markAsDirty();
    b.f.tahsilatYap(b.f.nakit);
    expect(b.toast.uyari).toHaveBeenCalledWith(expect.stringContaining('başka bir tahsilat'), {
      baslik: 'Başka bir tahsilat yazıldı — tutarınız kaydedilmedi',
    });
    expect(b.f.nakit.form.getRawValue().tutar).toBe('600.00');
  });

  it('L2: depozito alındıktan sonra kiranın depozitosuyla yeniden ÖN-DOLDURULMAZ (ikinci tık ikinci depozito değil)', async () => {
    const { f, cagrilar, detayVer } = await kur(() => of({ id: 'd' }));
    detayVer(detay(tahsilat(K1)));
    expect(f.depozitoAlFormu.getRawValue().tutar).toBe('500.00');
    f.depozitoAl();
    expect(cagrilar.filter((c) => c.yol.endsWith('/depozito/al'))).toHaveLength(1);
    detayVer(detay(tahsilat(K2)));
    expect(f.depozitoAlFormu.getRawValue().tutar).toBeNull();
    f.depozitoAl(); // boş tutar → istemci doğrulaması, istek YOK
    expect(cagrilar.filter((c) => c.yol.endsWith('/depozito/al'))).toHaveLength(1);
  });

  it('L3: kirli panel formu ya da sonuçlanmamış gönderim "kaydedilmemiş" sayılır', async () => {
    const { f, detayVer } = await kur(() => throwError(() => agHatasi()));
    detayVer(detay(tahsilat(K1)));
    expect(f.kirliMi()).toBe(false);
    f.tahsilatYap(f.nakit); // ağ hatası: form temiz ama donmuş anahtar bekliyor
    expect(f.nakit.form.dirty).toBe(false);
    expect(f.kirliMi()).toBe(true);
  });

  it('L3: başlık anahtarlı işlem sonuçlanmadıysa (bekleyen Idempotency-Key) kaydedilmemiş sayılır', async () => {
    const { f, detayVer } = await kur(() => throwError(() => agHatasi()));
    detayVer(detay(tahsilat(K1)));
    f.odemeFormu.patchValue({ tutar: '75.00' });
    f.odemeYap();
    f.odemeFormu.markAsPristine();
    expect(f.kirliMi()).toBe(true);
  });

  it('L6: dövizi değiştirince DOKUNULMAMIŞ ön-dolu tutar temizlenir; yazılmış tutar korunur', async () => {
    const { f, detayVer } = await kur(() => of({ id: 'c' }));
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
  it('L7: dönem kesiminde 400 → plan ve kira yeniden yüklenir (degisti)', async () => {
    const { f, detayVer, degisti, toast } = await kur(() =>
      throwError(() => sunucuHatasi(400, 'dogrulama', 'Dönem zaten kesildi.')),
    );
    detayVer(detay(tahsilat(K1)));
    f.donemKes({
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
    expect(f.gonderilenDonem()).toBeNull();
  });

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
