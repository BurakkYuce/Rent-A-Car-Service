import { describe, expect, it } from 'vitest';

import {
  costFormToInput,
  costInputToForm,
  initialCostForm,
} from '@features/pricing/cost/cost-model';

import type { ServiceInfo } from '../service-insurance-model';
import { emptyInfoForm, infoRequest, infoToForm, lineRequest } from './service-form-model';

/** Elle kurulmuş bilgi bloğu: kaza günü İstanbul gece yarısı, 4 haneli eski fatura tutarı, 6 haneli kur. */
const INFO = {
  ...Object.fromEntries(Object.keys(emptyInfoForm()).map((k) => [k, null])),
  atolyeAdi: 'Usta Oto',
  kazaTarihi: '2026-09-09T21:00:00Z',
  faturaTutar: 1000.5,
  faturaKdv: 200.1,
  faturaGenelToplam: 1200.6,
  odemeKur: 36.123456,
  odemeTuru: 'Banka',
  cikisYakit: 8,
} as unknown as ServiceInfo;

describe('servis bilgi blokları (tam değiştirme PUT)', () => {
  it('sunucu → form: tarih İstanbul günü, tutar 2 hane, kur 6 hane invariant metin', () => {
    const f = infoToForm(INFO);
    expect(f).toMatchObject({
      atolyeAdi: 'Usta Oto',
      kazaTarihi: '2026-09-10',
      faturaTutar: '1000.50',
      faturaKdv: '200.10',
      odemeKur: '36.123456',
      odemeTuru: 'Banka',
      cikisYakit: 8,
    });
  });

  it('dokunmadan kaydet: tarih orijinal anıyla, surum zorunlu gövdede; türetilen genel toplam GÖNDERİLMEZ', () => {
    const body = infoRequest(infoToForm(INFO), INFO, 'v-7');
    expect(body.kazaTarihi).toBe('2026-09-09T21:00:00Z');
    expect(body.surum).toBe('v-7');
    expect(body.faturaTutar).toBe('1000.50');
    expect('faturaGenelToplam' in body).toBe(false);
  });

  it('boş metin null; değişen gün İstanbul gece yarısı', () => {
    const body = infoRequest(
      { ...emptyInfoForm(), atolyeAdi: '   ', planBitTarihi: '2026-10-05' },
      null,
      null,
    );
    expect(body.atolyeAdi).toBeNull();
    expect(body.planBitTarihi).toBe('2026-10-04T21:00:00.000Z');
  });

  it('kalem: tutar boşsa null gider (hesap SUNUCUDA); açıklama kırpılır', () => {
    expect(
      lineRequest({
        aciklama: ' Yağ ',
        birimFiyat: '100.00',
        miktar: 2,
        indirim: null,
        kdvOran: 0.2,
        tutar: null,
      }),
    ).toEqual({
      aciklama: 'Yağ',
      birimFiyat: '100.00',
      miktar: 2,
      indirim: null,
      kdvOran: 0.2,
      tutar: null,
    });
  });
});

describe('maliyet girdisi', () => {
  it('ilk açılış Blazor varsayılanları; alış bedeli boş', () => {
    expect(initialCostForm()).toMatchObject({
      alisBedeli: null,
      residualYuzde: 0.3,
      sureAy: 36,
      aracSayisi: 1,
      kkdfOran: 0.15,
      krediHesaplamaSekli: 'EsitTaksitli',
    });
  });

  it('kayıtlı girdi → form → istek: tutar 2 hane metin, oran sayı, kayma yok', () => {
    const saved = {
      ...costFormToInput(initialCostForm()),
      alisBedeli: 1500000,
      kaskoYillik: 24000.5,
      karMarji: 0.25,
      krediHesaplamaSekli: 'Rotatif',
    };
    const form = costInputToForm(saved);
    expect(form.alisBedeli).toBe('1500000.00');
    expect(form.kaskoYillik).toBe('24000.50');
    expect(form.karMarji).toBe(0.25);
    const again = costFormToInput(form);
    expect(again.alisBedeli).toBe('1500000.00');
    expect(again.karMarji).toBe(0.25);
    expect(again.krediHesaplamaSekli).toBe('Rotatif');
  });
});
