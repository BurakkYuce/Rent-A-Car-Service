import type { RentalListRow } from '@core/api/ui-tipleri';

import {
  viewFilters,
  isViewCode,
  badgeClass,
  rowView,
  rowClass,
  filtersEqual,
  allFilters,
} from './kira-gorunumleri';

const TODAY = '2026-09-25';

function satir(extra: Partial<RentalListRow>): RentalListRow {
  return {
    id: '00000001-0000-4000-8000-000000000000',
    sozlesmeNo: '2026250901001',
    musteriId: 'c0000000-0000-4000-8000-000000000001',
    musteriAd: 'Ayşe Yılmaz',
    plaka: '34 ABC 123',
    basTar: '2026-09-20T07:00:00Z',
    bitTar: '2026-09-28T07:00:00Z',
    vadeTar: null,
    gun: 8,
    hediyeGun: null,
    faturalananGun: null,
    tutar: 800,
    bakiye: 0,
    doviz: 'TRY',
    kaynak: null,
    cikisOfisi: 'Merkez',
    donusOfisi: 'Merkez',
    provizyon: null,
    depozito: null,
    komisyonOran: null,
    komisyonTutar: null,
    onayKodu: null,
    projeAdi: null,
    assistFirma: null,
    ozelSoforBilgisi: null,
    durum: 'Kirada',
    faturali: false,
    tahsilat: null,
    ...extra,
  };
}

describe('kayıtlı görünüm ön ayarları', () => {
  it('her görünüm yalnız mevcut sunucu süzgeçlerine çevrilir (İstanbul günü)', () => {
    expect(viewFilters('kirada', TODAY)).toEqual({ durum: 'Kirada' });
    expect(viewFilters('geciken', TODAY)).toEqual({
      durum: 'Kirada',
      tarihTuru: 'Bitis',
      basMax: '2026-09-24',
    });
    expect(viewFilters('bugun-cikan', TODAY)).toEqual({
      tarihTuru: 'Baslangic',
      basMin: '2026-09-25',
      basMax: '2026-09-25',
    });
    expect(viewFilters('bugun-donecek', TODAY)).toEqual({
      durum: 'Kirada',
      tarihTuru: 'Bitis',
      basMin: '2026-09-25',
      basMax: '2026-09-25',
    });
    expect(viewFilters('faturasiz', TODAY)).toEqual({ fatura: false });
    expect(viewFilters('kapali', TODAY)).toEqual({ durum: 'Tamamlandi' });
    // Ay başı: dün önceki ayın son günü.
    expect(viewFilters('geciken', '2026-10-01').basMax).toBe('2026-09-30');
  });

  it('kod doğrulama: yalnız bilinen kodlar', () => {
    expect(isViewCode('kirada')).toBe(true);
    expect(isViewCode('tum')).toBe(false);
    expect(isViewCode(null)).toBe(false);
  });

  it('tumSuzgecler katalogdaki her adı verir (ön ayar önceki süzgeçleri siler)', () => {
    const f = allFilters({ durum: 'Kirada' });
    expect(Object.keys(f)).toContain('q');
    expect(Object.keys(f)).toContain('personelId');
    expect(f.durum).toBe('Kirada');
    expect(f.q).toBeUndefined();
  });

  it('suzgeclerAyni undefined alanları yok sayar, sıraya bakmaz', () => {
    expect(filtersEqual({ durum: 'Kirada', q: undefined }, { durum: 'Kirada' })).toBe(true);
    expect(
      filtersEqual(
        { tarihTuru: 'Bitis', durum: 'Kirada' },
        { durum: 'Kirada', tarihTuru: 'Bitis' },
      ),
    ).toBe(true);
    expect(filtersEqual({ durum: 'Kirada' }, { durum: 'Kirada', q: 'x' })).toBe(false);
  });
});

describe('satır görünümü (yalnız gösterim)', () => {
  it('kirada + bitiş geçmiş gün → n gün gecikti (kırmızı), satır vurgusu yok', () => {
    // 2026-09-17 10:00 İstanbul (07:00Z) → 25'ine 8 gün.
    const g = rowView(satir({ bitTar: '2026-09-17T07:00:00Z' }), TODAY);
    expect(g).toEqual({ tur: 'gecikmis', gun: 8 });
    expect(badgeClass(g)).toBe('rc-rozet rc-rozet--hata');
    expect(rowClass(satir({ bitTar: '2026-09-17T07:00:00Z' }), TODAY)).toBeNull();
  });

  it('bitiş bugün (İstanbul) → bugün dönüyor (sarı) + satır vurgusu; UTC günü farklı olsa da', () => {
    // 2026-09-24T22:30Z = 25.09.2026 01:30 İstanbul.
    const s = satir({ bitTar: '2026-09-24T22:30:00Z' });
    expect(rowView(s, TODAY)).toEqual({ tur: 'bugunDonuyor' });
    expect(badgeClass(rowView(s, TODAY))).toBe('rc-rozet rc-rozet--uyari');
    expect(rowClass(s, TODAY)).toBe('rc-satir-bugun');
  });

  it('bugün çıkan satır vurgulanır; durum rozeti değişmez', () => {
    const s = satir({ basTar: '2026-09-25T06:00:00Z' });
    expect(rowClass(s, TODAY)).toBe('rc-satir-bugun');
    expect(badgeClass(rowView(s, TODAY))).toBe('rc-rozet rc-rozet--basari');
  });

  it('kapalı/iptal sözleşmede gecikme hesaplanmaz', () => {
    const closed = satir({ durum: 'Tamamlandi', bitTar: '2026-09-17T07:00:00Z' });
    expect(rowView(closed, TODAY)).toEqual({ tur: 'durum', durum: 'Tamamlandi' });
    expect(badgeClass(rowView(closed, TODAY))).toBe('rc-rozet rc-rozet--notr');
    const cancel = satir({ durum: 'Iptal' });
    expect(badgeClass(rowView(cancel, TODAY))).toBe('rc-rozet rc-rozet--hata');
  });
});
