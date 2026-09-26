import { parseDecimal } from '@core/form/ondalik';
import {
  CollectionCopy,
  takeDepositBody,
  outsourcedServiceBody,
  currencyOptions,
  invoiceBody,
  exchangeRateField,
  paymentBody,
  prefillAmount,
  collectionBody,
} from './finans-modeli';
import type { CollectionInfo } from './finans-tipleri';

const RENTAL = '0b0e7c1a-1111-4aaa-8bbb-000000000001';
const ACCOUNT = '0b0e7c1a-2222-4aaa-8bbb-000000000002';
const K1 = 'aaaaaaaa-0000-4000-8000-000000000001';
const K2 = 'aaaaaaaa-0000-4000-8000-000000000002';

const bilgi = (key: string, defaultAmount: number | string = 2600): CollectionInfo => ({
  anahtar: key,
  cariId: ACCOUNT,
  rentalId: RENTAL,
  doviz: 'TRY',
  varsayilanTutar: defaultAmount,
});

const empty = { kur: null, hesapId: null, kanal: 'Masaüstü', aciklama: null };

describe('tutar girişi (tr → invariant)', () => {
  it('"1.500,50" sunucuya 1500.5 olarak gider (metin "1500.50", kayan nokta yok)', () => {
    const resolution = parseDecimal('1.500,50', { kesir: 2 });
    expect(resolution).toEqual({ gecerli: true, deger: '1500.50' });
    const body = collectionBody(bilgi(K1), 'Kasa', {
      ...empty,
      tutar: resolution.gecerli ? resolution.deger : null,
      doviz: 'TRY',
    });
    const tel = JSON.parse(JSON.stringify(body)) as { tutar: string };
    expect(tel.tutar).toBe('1500.50');
    expect(Number(tel.tutar)).toBe(1500.5);
  });

  it.each([
    ['1500,5', '1500.50'],
    ['1.500', '1500.00'], // Türkçe binlik
    ['1500.50', '1500.50'], // İngilizce yapıştırma
    ['0,005', '0.01'], // yarım kuruş sıfırdan uzağa
  ])('%s → %s', (written, expected) => {
    expect(parseDecimal(written, { kesir: 2 })).toEqual({ gecerli: true, deger: expected });
  });

  it('ön-doldurma yalnız pozitif bakiye (sunucu ölçeği "300.0000" → "300.00")', () => {
    expect(prefillAmount('300.0000')).toBe('300.00');
    expect(prefillAmount(2600)).toBe('2600.00');
    expect(prefillAmount(0)).toBeNull();
    expect(prefillAmount('-50')).toBeNull();
    expect(prefillAmount(null)).toBeNull();
  });
});

describe('kur alanı', () => {
  it('TRY: kur GÖNDERİLMEZ (yazılmış olsa bile)', () => {
    expect(exchangeRateField('TRY', '5')).toEqual({});
    expect(exchangeRateField('TL', '5')).toEqual({});
    expect(exchangeRateField(null, '5')).toEqual({});
    const body = collectionBody(bilgi(K1), 'Kasa', {
      ...empty,
      tutar: '100.00',
      doviz: 'TRY',
      kur: '5',
    });
    expect('kur' in body).toBe(false);
  });

  it('dövizde boş kur gönderilmez (sunucu çözer), dolu kur invariant gider', () => {
    expect(exchangeRateField('USD', null)).toEqual({});
    expect(exchangeRateField('USD', '')).toEqual({});
    expect(exchangeRateField('EUR', '35.1234')).toEqual({ kur: '35.123400' });
    const body = outsourcedServiceBody(RENTAL, {
      cariId: ACCOUNT,
      alinanHizmet: 'Çekici',
      hizmetAlinanFirma: null,
      hizmetBedeli: '1000.00',
      komisyonOran: 10,
      doviz: 'USD',
      kur: '32.5',
      komisyonFaturaNo: null,
      aciklama: '  ',
    });
    expect(body).toMatchObject({ doviz: 'USD', kur: '32.500000', komisyonOran: '10.00' });
    expect(body.aciklama).toBeNull();
  });

  it('döviz seçenekleri: sabit liste + kiranın dövizi', () => {
    expect(currencyOptions('TRY')).toEqual(['TRY', 'USD', 'EUR']);
    expect(currencyOptions('GBP')).toEqual(['TRY', 'USD', 'EUR', 'GBP']);
  });
});

describe('istek gövdeleri', () => {
  it('tahsilat: cari/kira/anahtar SATIR KOPYASINDAN (formdan değil); başlık alanı yok', () => {
    const body = collectionBody(bilgi(K1), 'Banka', {
      tutar: '250.00',
      doviz: 'TRY',
      kur: null,
      hesapId: 'h1',
      kanal: 'Mobil',
      aciklama: ' Ali ',
    });
    expect(body).toEqual({
      cariId: ACCOUNT,
      kiraId: RENTAL,
      tahsilatAnahtar: K1,
      tutar: '250.00',
      hesap: 'Banka',
      hesapId: 'h1',
      doviz: 'TRY',
      kanal: 'Mobil',
      aciklama: 'Ali',
    });
  });

  it('giden havale: Banka, kiraya BAĞLANMAZ (Blazor paritesi)', () => {
    const body = paymentBody(ACCOUNT, {
      tutar: '75.00',
      hesapId: null,
      kanal: 'Masaüstü',
      aciklama: null,
    });
    expect(body).toEqual({
      cariId: ACCOUNT,
      tutar: '75.00',
      hesap: 'Banka',
      hesapId: null,
      kanal: 'Masaüstü',
      aciklama: null,
    });
    expect('kiraId' in body).toBe(false);
  });

  it('depozito al ve fatura', () => {
    expect(takeDepositBody(ACCOUNT, { tutar: 500, hesap: 'Banka', hesapId: null })).toEqual({
      cariId: ACCOUNT,
      tutar: '500.00',
      hesap: 'Banka',
      hesapId: null,
    });
    expect(
      invoiceBody(RENTAL, {
        otv: null,
        tevkifatOran: null,
        tevkifatTutar: null,
        damgaVergisi: '0',
        iadeMi: null,
        manuelMi: true,
      }),
    ).toEqual({
      kiraId: RENTAL,
      otv: null,
      tevkifatOran: null,
      tevkifatTutar: null,
      damgaVergisi: '0.00', // 0 = bu faturada damgasız (boş ≠ 0)
      iadeMi: false,
      manuelMi: true,
    });
  });
});

describe('TahsilatKopyasi (deterministik anahtar satır kopyası)', () => {
  it('boştaki form her yeni detayda kopyayı tazeler ve ön-doldurur', () => {
    const k = new CollectionCopy();
    expect(k.canSubmit()).toBe(false);
    expect(k.detailLoaded(bilgi(K1), false)).toBe('ondoldur');
    expect(k.kopya()?.anahtar).toBe(K1);
    expect(k.canSubmit()).toBe(true);
    expect(k.detailLoaded(bilgi(K2), false)).toBe('ondoldur');
    expect(k.kopya()?.anahtar).toBe(K2);
  });

  it('AÇIK form (kullanıcı yazdı) eski kopyayı KORUR — bayatsa sunucu 409 verir', () => {
    const k = new CollectionCopy();
    k.detailLoaded(bilgi(K1), false);
    expect(k.detailLoaded(bilgi(K2), true)).toBe('korundu');
    expect(k.kopya()?.anahtar).toBe(K1);
  });

  it('sonuçlanmamış gönderim (ağ/5xx/oturum) kopyayı DONDURUR: yeniden deneme aynı anahtarla', () => {
    const k = new CollectionCopy();
    k.detailLoaded(bilgi(K1), false);
    expect(k.submitting()?.anahtar).toBe(K1);
    // Hata sonrası kira başka bir nedenle tazelendi (form temiz olsa da): anahtar DEĞİŞMEZ.
    expect(k.detailLoaded(bilgi(K2), false)).toBe('korundu');
    expect(k.submitting()?.anahtar).toBe(K1);
  });

  it('2xx ya da mukerrer sonrası gönderim kapalı; yeni detayın anahtarı alınır', () => {
    const k = new CollectionCopy();
    k.detailLoaded(bilgi(K1), false);
    k.submitting();
    k.settled();
    expect(k.canSubmit()).toBe(false);
    expect(k.refreshPending()).toBe(true);
    expect(k.submitting()).toBeNull(); // tazeleme gelmeden ikinci gönderim yok
    // mukerrer sonrası kullanıcının yazdıkları korunur ('anahtar'), anahtar yenilenir.
    expect(k.detailLoaded(bilgi(K2), true)).toBe('anahtar');
    expect(k.canSubmit()).toBe(true);
    expect(k.submitting()?.anahtar).toBe(K2);
  });

  it('#318 L2: kardeş formun işlemi sonuçlandı (anahtar kesin bayat) → KİRLİ form da yeni anahtarı alır, değerlere dokunulmaz', () => {
    const k = new CollectionCopy();
    k.detailLoaded(bilgi(K1), false);
    expect(k.detailLoaded(bilgi(K2), true, true)).toBe('anahtar');
    expect(k.kopya()?.anahtar).toBe(K2);
  });

  it('#318 L2: donmuş (sonucu bilinmeyen) deneme varken kardeş sonuçlansa da anahtar DEĞİŞMEZ', () => {
    const k = new CollectionCopy();
    k.detailLoaded(bilgi(K1), false);
    k.submitting(); // ağ hatası: sonuç bilinmiyor
    expect(k.detailLoaded(bilgi(K2), true, true)).toBe('korundu');
    expect(k.submitting()?.anahtar).toBe(K1);
  });

  it('iptal/izinsiz kira (satır yok) → gönderilemez', () => {
    const k = new CollectionCopy();
    k.detailLoaded(null, false);
    expect(k.canSubmit()).toBe(false);
    expect(k.submitting()).toBeNull();
  });
});
