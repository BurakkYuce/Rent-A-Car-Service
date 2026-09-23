import { ondalikCoz } from '@core/form/ondalik';
import {
  TahsilatKopyasi,
  depozitoAlGovdesi,
  disHizmetGovdesi,
  dovizSecenekleri,
  faturaGovdesi,
  kurAlani,
  odemeGovdesi,
  onDoldurmaTutari,
  tahsilatGovdesi,
} from './finans-modeli';
import type { TahsilatBilgisi } from './finans-tipleri';

const KIRA = '0b0e7c1a-1111-4aaa-8bbb-000000000001';
const CARI = '0b0e7c1a-2222-4aaa-8bbb-000000000002';
const K1 = 'aaaaaaaa-0000-4000-8000-000000000001';
const K2 = 'aaaaaaaa-0000-4000-8000-000000000002';

const bilgi = (anahtar: string, varsayilanTutar: number | string = 2600): TahsilatBilgisi => ({
  anahtar,
  cariId: CARI,
  rentalId: KIRA,
  doviz: 'TRY',
  varsayilanTutar,
});

const bos = { kur: null, hesapId: null, kanal: 'Masaüstü', aciklama: null };

describe('tutar girişi (tr → invariant)', () => {
  it('"1.500,50" sunucuya 1500.5 olarak gider (metin "1500.50", kayan nokta yok)', () => {
    const cozum = ondalikCoz('1.500,50', { kesir: 2 });
    expect(cozum).toEqual({ gecerli: true, deger: '1500.50' });
    const govde = tahsilatGovdesi(bilgi(K1), 'Kasa', {
      ...bos,
      tutar: cozum.gecerli ? cozum.deger : null,
      doviz: 'TRY',
    });
    const tel = JSON.parse(JSON.stringify(govde)) as { tutar: string };
    expect(tel.tutar).toBe('1500.50');
    expect(Number(tel.tutar)).toBe(1500.5);
  });

  it.each([
    ['1500,5', '1500.50'],
    ['1.500', '1500.00'], // Türkçe binlik
    ['1500.50', '1500.50'], // İngilizce yapıştırma
    ['0,005', '0.01'], // yarım kuruş sıfırdan uzağa
  ])('%s → %s', (yazilan, beklenen) => {
    expect(ondalikCoz(yazilan, { kesir: 2 })).toEqual({ gecerli: true, deger: beklenen });
  });

  it('ön-doldurma yalnız pozitif bakiye (sunucu ölçeği "300.0000" → "300.00")', () => {
    expect(onDoldurmaTutari('300.0000')).toBe('300.00');
    expect(onDoldurmaTutari(2600)).toBe('2600.00');
    expect(onDoldurmaTutari(0)).toBeNull();
    expect(onDoldurmaTutari('-50')).toBeNull();
    expect(onDoldurmaTutari(null)).toBeNull();
  });
});

describe('kur alanı', () => {
  it('TRY: kur GÖNDERİLMEZ (yazılmış olsa bile)', () => {
    expect(kurAlani('TRY', '5')).toEqual({});
    expect(kurAlani('TL', '5')).toEqual({});
    expect(kurAlani(null, '5')).toEqual({});
    const govde = tahsilatGovdesi(bilgi(K1), 'Kasa', {
      ...bos,
      tutar: '100.00',
      doviz: 'TRY',
      kur: '5',
    });
    expect('kur' in govde).toBe(false);
  });

  it('dövizde boş kur gönderilmez (sunucu çözer), dolu kur invariant gider', () => {
    expect(kurAlani('USD', null)).toEqual({});
    expect(kurAlani('USD', '')).toEqual({});
    expect(kurAlani('EUR', '35.1234')).toEqual({ kur: '35.123400' });
    const govde = disHizmetGovdesi(KIRA, {
      cariId: CARI,
      alinanHizmet: 'Çekici',
      hizmetAlinanFirma: null,
      hizmetBedeli: '1000.00',
      komisyonOran: 10,
      doviz: 'USD',
      kur: '32.5',
      komisyonFaturaNo: null,
      aciklama: '  ',
    });
    expect(govde).toMatchObject({ doviz: 'USD', kur: '32.500000', komisyonOran: '10.00' });
    expect(govde.aciklama).toBeNull();
  });

  it('döviz seçenekleri: sabit liste + kiranın dövizi', () => {
    expect(dovizSecenekleri('TRY')).toEqual(['TRY', 'USD', 'EUR']);
    expect(dovizSecenekleri('GBP')).toEqual(['TRY', 'USD', 'EUR', 'GBP']);
  });
});

describe('istek gövdeleri', () => {
  it('tahsilat: cari/kira/anahtar SATIR KOPYASINDAN (formdan değil); başlık alanı yok', () => {
    const govde = tahsilatGovdesi(bilgi(K1), 'Banka', {
      tutar: '250.00',
      doviz: 'TRY',
      kur: null,
      hesapId: 'h1',
      kanal: 'Mobil',
      aciklama: ' Ali ',
    });
    expect(govde).toEqual({
      cariId: CARI,
      kiraId: KIRA,
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
    const govde = odemeGovdesi(CARI, {
      tutar: '75.00',
      hesapId: null,
      kanal: 'Masaüstü',
      aciklama: null,
    });
    expect(govde).toEqual({
      cariId: CARI,
      tutar: '75.00',
      hesap: 'Banka',
      hesapId: null,
      kanal: 'Masaüstü',
      aciklama: null,
    });
    expect('kiraId' in govde).toBe(false);
  });

  it('depozito al ve fatura', () => {
    expect(depozitoAlGovdesi(CARI, { tutar: 500, hesap: 'Banka', hesapId: null })).toEqual({
      cariId: CARI,
      tutar: '500.00',
      hesap: 'Banka',
      hesapId: null,
    });
    expect(
      faturaGovdesi(KIRA, {
        otv: null,
        tevkifatOran: null,
        tevkifatTutar: null,
        damgaVergisi: '0',
        iadeMi: null,
        manuelMi: true,
      }),
    ).toEqual({
      kiraId: KIRA,
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
    const k = new TahsilatKopyasi();
    expect(k.gonderilebilir()).toBe(false);
    expect(k.detayGeldi(bilgi(K1), false)).toBe('ondoldur');
    expect(k.kopya()?.anahtar).toBe(K1);
    expect(k.gonderilebilir()).toBe(true);
    expect(k.detayGeldi(bilgi(K2), false)).toBe('ondoldur');
    expect(k.kopya()?.anahtar).toBe(K2);
  });

  it('AÇIK form (kullanıcı yazdı) eski kopyayı KORUR — bayatsa sunucu 409 verir', () => {
    const k = new TahsilatKopyasi();
    k.detayGeldi(bilgi(K1), false);
    expect(k.detayGeldi(bilgi(K2), true)).toBe('korundu');
    expect(k.kopya()?.anahtar).toBe(K1);
  });

  it('sonuçlanmamış gönderim (ağ/5xx/oturum) kopyayı DONDURUR: yeniden deneme aynı anahtarla', () => {
    const k = new TahsilatKopyasi();
    k.detayGeldi(bilgi(K1), false);
    expect(k.gonderiliyor()?.anahtar).toBe(K1);
    // Hata sonrası kira başka bir nedenle tazelendi (form temiz olsa da): anahtar DEĞİŞMEZ.
    expect(k.detayGeldi(bilgi(K2), false)).toBe('korundu');
    expect(k.gonderiliyor()?.anahtar).toBe(K1);
  });

  it('2xx ya da mukerrer sonrası gönderim kapalı; yeni detayın anahtarı alınır', () => {
    const k = new TahsilatKopyasi();
    k.detayGeldi(bilgi(K1), false);
    k.gonderiliyor();
    k.sonuclandi();
    expect(k.gonderilebilir()).toBe(false);
    expect(k.tazelemeBekleniyor()).toBe(true);
    expect(k.gonderiliyor()).toBeNull(); // tazeleme gelmeden ikinci gönderim yok
    // mukerrer sonrası kullanıcının yazdıkları korunur ('anahtar'), anahtar yenilenir.
    expect(k.detayGeldi(bilgi(K2), true)).toBe('anahtar');
    expect(k.gonderilebilir()).toBe(true);
    expect(k.gonderiliyor()?.anahtar).toBe(K2);
  });

  it('iptal/izinsiz kira (satır yok) → gönderilemez', () => {
    const k = new TahsilatKopyasi();
    k.detayGeldi(null, false);
    expect(k.gonderilebilir()).toBe(false);
    expect(k.gonderiliyor()).toBeNull();
  });
});
