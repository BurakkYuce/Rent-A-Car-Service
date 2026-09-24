import { describe, expect, it } from 'vitest';

import {
  adjustmentBody,
  bulkCollectionBody,
  bulkExpenseBody,
  candidateKey,
  cashTransferBody,
  closeItemsBody,
  collectionBody,
  customerTransferBody,
  dayToInstant,
  depositRequest,
  queryParams,
  rowErrorMap,
} from './finance-model';
import { fixedRateToForm, fixedRateUpdateBody } from './rates/rates-model';

const CARI = 'c0c0c0c0-0000-4000-8000-000000000001';

// Beklenen değerler elle yazılmıştır (bağımsız oracle): İstanbul UTC+3 → 22.09.2026 00:00 = 21.09.2026 21:00Z.
describe('finans gövdeleri', () => {
  it('gün → İstanbul gece yarısı UTC; boş → null', () => {
    expect(dayToInstant('2026-09-22')).toBe('2026-09-21T21:00:00.000Z');
    expect(dayToInstant(null)).toBeNull();
  });

  it('tahsilat: TRY iken kur GÖNDERİLMEZ; tutar 2 ondalık invariant; boş metin null', () => {
    const b = collectionBody(CARI, {
      tutar: '1500.5',
      doviz: 'TRY',
      kur: 1,
      hesap: 'Banka',
      hesapId: null,
      kanal: '',
      tarih: '2026-09-22',
      aciklama: '  ',
    });
    expect(b).toEqual({
      cariId: CARI,
      tutar: '1500.50',
      hesap: 'Banka',
      hesapId: null,
      doviz: 'TRY',
      kanal: null,
      aciklama: null,
      tarih: '2026-09-21T21:00:00.000Z',
    });
    expect('kur' in b).toBe(false);
  });

  it('dövizde boş kur gönderilmez (sunucu çözer), dolu kur 6 ondalık metin', () => {
    const base = {
      tutar: '100',
      hesap: 'Kasa' as const,
      hesapId: null,
      kanal: null,
      tarih: null,
      aciklama: null,
    };
    expect('kur' in collectionBody(CARI, { ...base, doviz: 'EUR', kur: null })).toBe(false);
    expect(collectionBody(CARI, { ...base, doviz: 'EUR', kur: 35.1234565 }).kur).toBe('35.123457');
  });

  it('kasa virmanı: künye alanları kırpılır, işlemi yapan gövdede YOK', () => {
    const b = cashTransferBody({
      kaynak: 'Kasa',
      kaynakHesapId: null,
      hedef: 'Banka',
      hedefHesapId: 'h-2',
      tutar: '250',
      doviz: 'TRY',
      kur: null,
      makbuzNo: ' MK-7 ',
      sube: '',
      aciklama: null,
    });
    expect(b).toEqual({
      kaynak: 'Kasa',
      hedef: 'Banka',
      tutar: '250.00',
      kaynakHesapId: null,
      hedefHesapId: 'h-2',
      doviz: 'TRY',
      makbuzNo: 'MK-7',
      sube: null,
      aciklama: null,
    });
    expect('islemYapan' in b).toBe(false);
  });

  it('bakiye düzeltme ve cari virman: tarih/vade günleri UTC anına', () => {
    expect(
      adjustmentBody(CARI, {
        yon: 'Borclandir',
        tutar: '10',
        doviz: 'TRY',
        kur: null,
        tarih: null,
        vade: '2026-10-01',
        makbuzNo: null,
        aciklama: 'yuvarlama',
      }),
    ).toEqual({
      cariId: CARI,
      yon: 'Borclandir',
      tutar: '10.00',
      doviz: 'TRY',
      tarih: null,
      vade: '2026-09-30T21:00:00.000Z',
      makbuzNo: null,
      aciklama: 'yuvarlama',
    });
    expect(
      customerTransferBody({
        kaynakCariId: 'a',
        hedefCariId: 'b',
        tutar: '99.99',
        doviz: 'USD',
        kur: 34.5,
        tarih: '2026-09-22',
        vade: null,
        makbuzNo: null,
        sube: 'Merkez',
        aciklama: null,
      }),
    ).toMatchObject({
      kaynakCariId: 'a',
      hedefCariId: 'b',
      tutar: '99.99',
      doviz: 'USD',
      kur: '34.500000',
      sube: 'Merkez',
    });
  });

  it('toplu kapatma: boş tutar null (= kalanın tamamı), kısmi tutar metin', () => {
    expect(
      closeItemsBody(
        [
          { kalemId: 'k1', tutar: null },
          { kalemId: 'k2', tutar: '40' },
        ],
        { hesap: 'Kasa', kanal: 'Mobil', tarih: null, aciklama: null },
      ),
    ).toEqual({
      secim: [
        { kalemId: 'k1', tutar: null },
        { kalemId: 'k2', tutar: '40.00' },
      ],
      hesap: 'Kasa',
      kanal: 'Mobil',
      tarih: null,
      aciklama: null,
    });
  });

  it('toplu tahsilat / gider: satır SIRASI korunur (sunucu satır anahtarı sıra indeksiyle)', () => {
    const c = bulkCollectionBody(
      [
        { cariId: 'x', tutar: '1500', aciklama: 'Mart' },
        { cariId: 'y', tutar: '2000', aciklama: null },
      ],
      { hesap: 'Banka', hesapId: null, kanal: 'Masaüstü' },
    );
    expect(c.satirlar).toEqual([
      { cariId: 'x', tutar: '1500.00', aciklama: 'Mart' },
      { cariId: 'y', tutar: '2000.00', aciklama: null },
    ]);
    const e = bulkExpenseBody([{ netTutar: '1000', aciklama: 'Yakıt', aracId: 'v1' }], {
      tip: 'Arac',
      odemeYontemi: 'AcikHesap',
      kdvOrani: 0.2,
      cariId: 'sup',
      vade: null,
      finansalHesapId: null,
    });
    expect(e).toEqual({
      satirlar: [{ netTutar: '1000.00', aciklama: 'Yakıt', aracId: 'v1' }],
      tip: 'Arac',
      odemeYontemi: 'AcikHesap',
      kdvOrani: 0.2,
      cariId: 'sup',
      vade: null,
      finansalHesapId: null,
    });
  });

  it('depozito: al/iade hesap taşır, mahsup/irat taşımaz', () => {
    const v = { tutar: '300', hesap: 'Banka' as const, hesapId: 'h1' };
    expect(depositRequest('al', CARI, v)).toEqual({
      cariId: CARI,
      tutar: '300.00',
      hesap: 'Banka',
      hesapId: 'h1',
    });
    expect(depositRequest('iade', CARI, v)).toEqual({
      cariId: CARI,
      tutar: '300.00',
      hesap: 'Banka',
      hesapId: 'h1',
    });
    expect(depositRequest('mahsup', CARI, v)).toEqual({ cariId: CARI, tutar: '300.00' });
    expect(depositRequest('irat', CARI, v)).toEqual({ cariId: CARI, tutar: '300.00' });
  });

  it('sabit kur PUT: kod gövdede yok, surum zorunlu alan', () => {
    const f = fixedRateToForm({
      id: 'r',
      kod: 'EUR',
      kur: '36.500000',
      basTar: null,
      bitTar: '2026-12-31',
      aktif: true,
      surum: 's-1',
    });
    expect(f).toEqual({ kod: 'EUR', kur: 36.5, basTar: null, bitTar: '2026-12-31', aktif: true });
    expect(fixedRateUpdateBody(f, 's-1')).toEqual({
      kur: 36.5,
      basTar: null,
      bitTar: '2026-12-31',
      aktif: true,
      surum: 's-1',
    });
  });

  it('yardımcılar: aday anahtarı ve boş parametre ayıklama', () => {
    expect(candidateKey({ kiraId: 'k', donemSira: 3 })).toBe('k:3');
    expect(queryParams({ a: 'x', b: '', c: null, d: false, e: 'true' })).toEqual({
      a: 'x',
      e: 'true',
    });
  });
});

describe('toplu satır hata eşlemesi (r299 MEDIUM-1)', () => {
  it('satır hataları GÖNDERİLEN satırın kimliğine göre güncel sıraya eşlenir; silinen satır eşlenmez', () => {
    // Gönderilen: [A, B]; ekranda [B, C] olsaydı: B hatası 0. satıra gider, A'nınki hiçbir satıra gitmez.
    expect(rowErrorMap(['A', 'B'], ['B', 'C'], { tutar: 'tutar', cariId: 'cari' })).toEqual({
      'satirlar[1].tutar': 'satirlar.0.tutar',
      'satirlar[1].cariId': 'satirlar.0.cari',
    });
  });
});
