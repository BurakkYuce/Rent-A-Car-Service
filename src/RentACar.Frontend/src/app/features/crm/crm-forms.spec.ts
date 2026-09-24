import { of } from 'rxjs';
import { describe, expect, it } from 'vitest';

import type { ApiIstemcisi, SorguParametreleri } from '@core/api/api-istemcisi';
import { balanceSide, rowBadges, type CustomerRow } from '@features/customers/customer-model';
import { currencyCode } from '@features/customers/customer-detail/customer-detail';

import { viewFilters, viewOf } from './assistance/assistance-list';
import { answerRows, legalRequest, surveyRequest, emptyLegal, complaintRequest } from './crm-forms';
import { legalExportParameters, rentalOption, type SurveyAnswer } from './crm-model';
import { countRequests } from './crm.store';

/** Beklenen değerler elle kurulmuş senaryolardan (dönüşüm kodundan türetilmez). */
describe('anket soru satırları', () => {
  const defaults = ['Araç temiz miydi?', 'Personel ilgili miydi?', 'Tekrar kiralar mısınız?'];

  it('yeni anket: varsayılan soru sayısı kadar boş cevaplı satır', () => {
    expect(answerRows(defaults, [])).toEqual([
      { soru: 'Araç temiz miydi?', cevap: null, aciklama: null },
      { soru: 'Personel ilgili miydi?', cevap: null, aciklama: null },
      { soru: 'Tekrar kiralar mısınız?', cevap: null, aciklama: null },
    ]);
  });

  it('kayıtlı cevap SORU NO ile eşleşir, soru metni kayıttaki (snapshot); fazla soru satırı uzatır', () => {
    const answers = [
      { soruNo: 2, soru: 'Eski metin: personel?', cevap: 'Evet', aciklama: null },
      { soruNo: 5, soru: 'Ek soru', cevap: '10', aciklama: 'not' },
    ] as SurveyAnswer[];
    const rows = answerRows(defaults, answers);
    expect(rows).toHaveLength(5);
    expect(rows[1]).toEqual({ soru: 'Eski metin: personel?', cevap: 'Evet', aciklama: null });
    expect(rows[3]).toEqual({ soru: null, cevap: null, aciklama: null });
    expect(rows[4]).toEqual({ soru: 'Ek soru', cevap: '10', aciklama: 'not' });
  });

  it('gövde: sorusu boş satır atlanır, soru no satır sırası; surum yalnız düzenlemede', () => {
    const body = surveyRequest(
      {
        cari: { id: 'c1', etiket: 'Ayşe' },
        kira: null,
        anketTuru: 'Donus',
        durum: 'Yapilmadi',
        cikisOfisi: '  ',
        tarih: null,
        puan: 7,
        kaynak: 'Telefon',
        yorum: null,
      },
      [
        { soru: 'S1', cevap: 'C1', aciklama: null },
        { soru: '  ', cevap: 'yok sayılır', aciklama: null },
        { soru: 'S3', cevap: null, aciklama: 'A3' },
      ],
      { tarih: '2026-09-01T09:00:00Z', surum: 'v3' },
    );
    expect(body.cevaplar).toEqual([
      { soruNo: 1, soru: 'S1', cevap: 'C1', aciklama: null },
      { soruNo: 3, soru: 'S3', cevap: null, aciklama: 'A3' },
    ]);
    expect(body.cariId).toBe('c1');
    expect(body.rentalId).toBeNull();
    expect(body.cikisOfisi).toBeNull();
    expect(body.puan).toBe(7);
    expect(body.surum).toBe('v3');
    expect(body.tarih).toBeNull();
  });
});

describe('hukuk ve şikayet gövdeleri', () => {
  it('hukuk: boş tutar "0" (zorunlu decimal), boş tahsilat null; dokunulmayan tarih aynen', () => {
    const body = legalRequest(
      { ...emptyLegal(), dosyaNo: ' 2026/15 ', tarih: '2026-09-02' },
      { tarih: '2026-09-01T21:00:00Z', surum: 's1' },
    );
    expect(body.dosyaNo).toBe('2026/15');
    expect(body.tutar).toBe('0');
    expect(body.tahsilat).toBeNull();
    expect(body.tarih).toBe('2026-09-01T21:00:00Z');
    expect(body.tur).toBe('Dava');
    expect(body.durum).toBe('Acik');
    expect(body.aktif).toBe(true);
  });

  it('şikayet: puan boşsa null; personel seçimleri kimlikle', () => {
    const body = complaintRequest(
      {
        cari: null,
        kira: { id: 'r1', etiket: 'KS-1' },
        sikayetYeri: 'Kira',
        sikayetKanali: 'Web',
        cikisOfisi: null,
        teslimAlan: { id: 'p1', etiket: 'Ali' },
        teslimEden: null,
        puan: null,
        tarih: null,
        konu: 'Araç kirli',
        detay: null,
        durum: 'Cozuldu',
        cozum: 'Özür dilendi',
      },
      null,
    );
    expect(body).toMatchObject({
      rentalId: 'r1',
      teslimAlanPersonelId: 'p1',
      teslimEdenPersonelId: null,
      puan: null,
      durum: 'Cozuldu',
      surum: null,
    });
  });

  it('hukuk dışa aktarma: Blazor sorgu adları (tarihBas → bas), boş değer taşınmaz', () => {
    expect(
      legalExportParameters({
        tarihBas: '2026-01-01',
        tarihBit: undefined,
        durum: 'Acik',
        ara: '',
      }),
    ).toEqual({ bas: '2026-01-01', durum: 'Acik' });
  });
});

describe('assistans durum görünümü', () => {
  it('tek seçim ↔ uç bayrakları', () => {
    expect(viewFilters('acik')).toEqual({ kapandi: false });
    expect(viewFilters('kapali')).toEqual({ kapandi: true });
    expect(viewFilters('cekici')).toEqual({ hareketEdemiyor: true });
    expect(viewFilters('lastik')).toEqual({ yedekLastik: true });
    expect(viewFilters(null)).toEqual({});
    expect(viewOf({ kapandi: false })).toBe('acik');
    expect(viewOf({ hareketEdemiyor: true })).toBe('cekici');
    expect(viewOf({})).toBeNull();
  });
});

describe('özet sayaçları (sunucu sayar)', () => {
  it('her kalem aynı süzgeç + ek koşulla boyut=1 isteğinin toplamı; çelişen süzgeçte istek yok, 0', () => {
    const calls: SorguParametreleri[] = [];
    const api = {
      get: (_path: string, o: { parametreler: SorguParametreleri }) => {
        calls.push(o.parametreler);
        return of({ kayitlar: [], toplam: o.parametreler['durum'] === 'Yapildi' ? 12 : 99 });
      },
    } as unknown as ApiIstemcisi;
    let result: Readonly<Record<string, number>> = {};
    countRequests(
      api,
      '/api/ui/v1/anketler',
      { durum: 'Yapildi', sayfa: 3, boyut: 50, sirala: 'tarih' },
      {
        yapildi: { key: 'durum', value: 'Yapildi' },
        yapilmadi: { key: 'durum', value: 'Yapilmadi' },
      },
    ).subscribe((r) => (result = r));
    expect(result).toEqual({ yapildi: 12, yapilmadi: 0 });
    expect(calls).toEqual([{ durum: 'Yapildi', sayfa: 1, boyut: 1 }]);
  });
});

describe('cari yardımcıları', () => {
  it('liste rozetleri Blazor sırasıyla; not rozeti uyarı nedenini taşır', () => {
    const row = {
      karaListe: true,
      uyari: true,
      iysIzinli: false,
      pasif: true,
      aracVerilmez: true,
      uyariNedeni: ' Ödeme gecikti ',
    } as unknown as CustomerRow;
    expect(rowBadges(row).map((b) => b.kind)).toEqual([
      'karaListe',
      'uyari',
      'pasif',
      'aracVerilmez',
      'not',
    ]);
    expect(rowBadges(row).at(-1)?.title).toBe('Ödeme gecikti');
  });

  it('bakiye işareti: pozitif = müşteri borçlu', () => {
    expect(balanceSide(250)).toBe('borclu');
    expect(balanceSide(-0.01)).toBe('alacakli');
    expect(balanceSide(0)).toBe('sifir');
    expect(balanceSide(null)).toBeNull();
  });

  it('eski döviz adları ISO koduna', () => {
    expect(currencyCode('TL')).toBe('TRY');
    expect(currencyCode('EURO')).toBe('EUR');
    expect(currencyCode('USD')).toBe('USD');
    expect(currencyCode(null)).toBe('TRY');
    expect(currencyCode('dolar')).toBe('TRY');
  });

  it('kira seçim etiketi: sözleşme — plaka — müşteri (TC/telefon yok)', () => {
    expect(
      rentalOption({
        id: 'r1',
        sozlesmeNo: '2026260801001',
        plaka: null,
        musteriAd: 'Anonim müşteri',
        musteriId: 'c1',
        cikisOfisi: null,
        basTar: '2026-08-26T09:00:00Z',
      }),
    ).toEqual({ id: 'r1', etiket: '2026260801001 — Anonim müşteri' });
  });
});
