import { describe, expect, it } from 'vitest';

import {
  allocationRequest,
  allocationReturnRequest,
  timeValue,
} from './allocations/allocation-model';
import {
  installmentRequest,
  installmentToForm,
  planRequest,
} from './customer-installments/installment-form-model';
import { signed, vehicleText } from './finance-columns';
import type { CustomerInstallment, FleetPlan, OrderDetail } from './finance-model';
import { LOAN_EXPORT_NAMES, exportParameters } from './finance-model';
import { fleetPlanTotals } from './fleet-plan/fleet-plan-model';
import { activeSelection, loanRequest } from './loans/loan-form-model';
import { orderRequest, orderToForm } from './orders/order-form-model';

/** Beklenen değerler elle kurulmuş senaryodan (bağımsız oracle); dönüşüm kodundan türetilmez. */
const CARI = { id: 'c0c0c0c0-0000-4000-8000-000000000001', etiket: 'Ayşe Yılmaz' };
const ARAC = { id: 'a1a1a1a1-0000-4000-8000-000000000001', etiket: '34ABC123' };

const INSTALLMENT = {
  id: 'd0d0d0d0-0000-4000-8000-000000000001',
  sira: 3,
  cariId: CARI.id,
  cariAd: CARI.etiket,
  vehicleId: ARAC.id,
  plaka: ARAC.etiket,
  vehicleSaleId: 'e0e0e0e0-0000-4000-8000-000000000001',
  // 2026-10-15 İstanbul gece yarısı (+03:00) = 2026-10-14T21:00:00Z
  vade: '2026-10-14T21:00:00Z',
  taksitTutari: 1250.5,
  doviz: 'EUR',
  kur: 35.1234,
  tutarBaz: 43921.8,
  durum: 'Odendi',
  gecikti: false,
  odemeTarihi: '2026-10-16T08:30:00Z',
  aciklama: null,
  surum: 's-7',
} as unknown as CustomerInstallment;

describe('kredi formu', () => {
  it('tutar invariant METİN olarak aynen gider; kesir faiz; gün İstanbul gece yarısı', () => {
    expect(
      loanRequest({
        bankaAdi: '  Ziraat ',
        cari: CARI,
        dosyaNo: null,
        arac: null,
        krediTutari: '1500000.5',
        faizOran: 0.2,
        taksitSayisi: 36,
        baslangic: '2026-10-01',
        aciklama: '',
      }),
    ).toEqual({
      bankaAdi: 'Ziraat',
      cariId: CARI.id,
      dosyaNo: null,
      vehicleId: null,
      krediTutari: '1500000.5',
      faizOran: 0.2,
      taksitSayisi: 36,
      baslangicTarihi: '2026-09-30T21:00:00.000Z',
      aciklama: null,
    });
  });

  it('toplu iptal: bu sayfada aktif olmadığı görülen seçim düşer, başka sayfadaki kimlik kalır', () => {
    const rows = [
      { id: 'k1', durum: 'Aktif' },
      { id: 'k2', durum: 'Kapandi' },
      { id: 'k3', durum: 'Iptal' },
    ];
    expect(activeSelection(['k1', 'k2', 'k3', 'baska-sayfa'], rows)).toEqual(['k1', 'baska-sayfa']);
  });
});

describe('müşteri taksiti', () => {
  it('kayıt → form: tutar invariant metin, gün İstanbul günü, kur korunur', () => {
    const v = installmentToForm(INSTALLMENT);
    expect(v.vade).toBe('2026-10-15');
    expect(v.taksitTutari).toBe('1250.50');
    expect(v.kur).toBe(35.1234);
    expect(v.arac).toEqual(ARAC);
  });

  it('dokunmadan kaydet: tarih sunucu anıyla AYNEN gider, surum + araç satış bağı tabandan', () => {
    const body = installmentRequest(installmentToForm(INSTALLMENT), INSTALLMENT);
    expect(body).toEqual({
      cariId: CARI.id,
      vehicleId: ARAC.id,
      vehicleSaleId: 'e0e0e0e0-0000-4000-8000-000000000001',
      vade: '2026-10-14T21:00:00Z',
      taksitTutari: '1250.50',
      doviz: 'EUR',
      kur: 35.1234,
      durum: 'Odendi',
      odemeTarihi: '2026-10-16T08:30:00Z',
      aciklama: null,
      surum: 's-7',
    });
  });

  it('Bekliyor: ödeme tarihi gönderilmez; yeni kayıtta surum yok', () => {
    const body = installmentRequest(
      { ...installmentToForm(INSTALLMENT), durum: 'Bekliyor', vade: '2026-11-01' },
      null,
    );
    expect(body.odemeTarihi).toBeNull();
    expect(body.vade).toBe('2026-10-31T21:00:00.000Z');
    expect('surum' in body).toBe(false);
    expect(body.vehicleSaleId).toBeNull();
  });

  it('plan: toplam metin aynen, kur boş → sunucu çözer', () => {
    expect(
      planRequest({
        cari: CARI,
        arac: null,
        toplamTutar: '12000.01',
        taksitSayisi: 12,
        ilkVade: '2026-11-01',
        doviz: 'TRY',
        kur: null,
        aciklama: null,
      }),
    ).toEqual({
      cariId: CARI.id,
      vehicleId: null,
      toplamTutar: '12000.01',
      taksitSayisi: 12,
      ilkVade: '2026-10-31T21:00:00.000Z',
      doviz: 'TRY',
      kur: null,
      aciklama: null,
    });
  });
});

describe('sipariş', () => {
  const ORDER = {
    id: 'o1',
    no: 'SP-1',
    durum: 'Bekliyor',
    surum: 's-1',
    tedarikci: 'Bayi A',
    tedarikciCariId: null,
    tedarikciCariAd: null,
    siparisTarihi: '2026-09-01T21:00:00Z',
    beklenenTeslim: null,
    imzaTarih: null,
    adet: 2,
    birimFiyat: 750000,
    piyasaFiyat: 800000.25,
    opsFiyat: null,
    filoFiyat: null,
    doviz: 'TRY',
    kur: 1,
    krediId: null,
    aciklama: null,
  } as unknown as OrderDetail;

  it('dokunmadan kaydet: tarih anı aynen, bilgi fiyatı metin, surum', () => {
    const body = orderRequest(orderToForm(ORDER), ORDER);
    expect(body.siparisTarihi).toBe('2026-09-01T21:00:00Z');
    expect(body.birimFiyat).toBe('750000.00');
    expect(body.piyasaFiyat).toBe('800000.25');
    expect(body.adet).toBe(2);
    expect(body.surum).toBe('s-1');
  });

  it('araç metni boşlukları tekilleştirir', () => {
    expect(vehicleText({ marka: 'Fiat', tip: ' Egea ', grup: null, versiyon: '1.4' })).toBe(
      'Fiat Egea 1.4',
    );
  });
});

describe('filo plan toplam satırı', () => {
  it('hedef ve gerçekleşen AYRI toplanır; fark = toplam hedef − toplam gerçekleşen', () => {
    const rows = [
      { hedefAdet: 10, gerceklesen: 7, toplamKayitli: 9, fark: 3 },
      { hedefAdet: 5, gerceklesen: 8, toplamKayitli: 8, fark: -3 },
      { hedefAdet: '4', gerceklesen: '4', toplamKayitli: '6', fark: '0' },
    ] as unknown as FleetPlan[];
    expect(fleetPlanTotals(rows)).toEqual({ hedef: 19, gerceklesen: 19, kayitli: 23, fark: 0 });
    expect(signed(3)).toBe('+3');
    expect(signed(-2)).toBe('-2');
    expect(signed(0)).toBe('0');
  });
});

describe('BAF', () => {
  it('saat "14:05" → "14:05:00"; biçimsiz → null; teslim tarihi boş → sunucu şimdi', () => {
    expect(timeValue('14:05')).toBe('14:05:00');
    expect(timeValue('25:00')).toBeNull();
    expect(
      allocationReturnRequest({
        donusKm: 15000,
        donusYakit: 80,
        donusTarihi: null,
        donusSube: ' Havalimanı ',
        donusSaat: '09:30',
      }),
    ).toEqual({
      donusKm: 15000,
      donusYakit: 80,
      donusTarihi: null,
      donusSube: 'Havalimanı',
      donusSaat: '09:30:00',
    });
    const req = allocationRequest({
      personel: { id: 'p1', etiket: 'Ali' },
      arac: ARAC,
      cikisTarihi: null,
      cikisSaat: null,
      cikisKm: 12000,
      cikisYakit: null,
      sube: null,
      kullanimAmaci: 'Yikama',
      onaylayan: null,
      kirayaVer: true,
      aciklama: null,
    });
    expect(req).toMatchObject({
      personelId: 'p1',
      vehicleId: ARAC.id,
      cikisKm: 12000,
      kirayaVer: true,
    });
  });
});

describe('dışa aktarma', () => {
  it('ekrandaki süzgeç Blazor ucunun eski adlarıyla taşınır', () => {
    expect(
      exportParameters(
        { cariId: CARI.id, durum: 'Aktif', plaka: undefined, bas: '2026-01-01' },
        LOAN_EXPORT_NAMES,
      ),
    ).toEqual({ cariF: CARI.id, durumF: 'Aktif', bas: '2026-01-01' });
  });
});

describe('döviz değişince eski kur taşınmaz (inceleme M2)', () => {
  const TRY_ROW = { ...INSTALLMENT, doviz: 'TRY', kur: 1 } as unknown as CustomerInstallment;

  it('taksit: TRY → EUR, kur dokunulmadı (1) → kur null (sunucu çözer)', () => {
    const v = { ...installmentToForm(TRY_ROW), doviz: 'EUR' };
    expect(installmentRequest(v, TRY_ROW).kur).toBeNull();
  });

  it('taksit: EUR (35,1234) → TRY, kur dokunulmadı → null (TRY = 1)', () => {
    const v = { ...installmentToForm(INSTALLMENT), doviz: 'TRY' };
    expect(installmentRequest(v, INSTALLMENT).kur).toBeNull();
  });

  it('taksit: döviz değişti ve kullanıcı kuru AÇIKÇA yazdı → yazılan kur gider', () => {
    const v = { ...installmentToForm(TRY_ROW), doviz: 'EUR', kur: 36.5 };
    expect(installmentRequest(v, TRY_ROW).kur).toBe(36.5);
  });

  it('taksit: döviz aynı (küçük harf yazımı dahil) → kaydın kuru aynen', () => {
    const v = { ...installmentToForm(INSTALLMENT), doviz: 'eur' };
    expect(installmentRequest(v, INSTALLMENT).kur).toBe(35.1234);
  });

  it('sipariş: TRY → USD, kur dokunulmadı → null; yeni kayıtta formdaki kur aynen', () => {
    const order = {
      doviz: 'TRY',
      kur: 1,
      siparisTarihi: '2026-09-01T21:00:00Z',
      adet: 1,
      birimFiyat: 10,
      surum: 's',
    } as unknown as OrderDetail;
    expect(orderRequest({ ...orderToForm(order), doviz: 'USD' }, order).kur).toBeNull();
    expect(orderRequest({ ...orderToForm(order), doviz: 'USD', kur: 41.2 }, null).kur).toBe(41.2);
  });

  it('sipariş: boş birim fiyat 0 olarak GİTMEZ (inceleme L3)', () => {
    const order = { doviz: 'TRY', kur: 1, adet: 1, birimFiyat: 10 } as unknown as OrderDetail;
    expect(orderRequest({ ...orderToForm(order), birimFiyat: null }, null).birimFiyat).toBe('');
  });
});
