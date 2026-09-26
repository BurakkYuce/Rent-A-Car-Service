import type { KiraListeSatiri } from '@core/api/ui-tipleri';

import {
  gorunumFiltreleri,
  gorunumKoduMu,
  rozetSinifi,
  satirGorunumu,
  satirSinifi,
  suzgeclerAyni,
  tumSuzgecler,
} from './kira-gorunumleri';

const BUGUN = '2026-09-25';

function satir(ek: Partial<KiraListeSatiri>): KiraListeSatiri {
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
    ...ek,
  };
}

describe('kayıtlı görünüm ön ayarları', () => {
  it('her görünüm yalnız mevcut sunucu süzgeçlerine çevrilir (İstanbul günü)', () => {
    expect(gorunumFiltreleri('kirada', BUGUN)).toEqual({ durum: 'Kirada' });
    expect(gorunumFiltreleri('geciken', BUGUN)).toEqual({
      durum: 'Kirada',
      tarihTuru: 'Bitis',
      basMax: '2026-09-24',
    });
    expect(gorunumFiltreleri('bugun-cikan', BUGUN)).toEqual({
      tarihTuru: 'Baslangic',
      basMin: '2026-09-25',
      basMax: '2026-09-25',
    });
    expect(gorunumFiltreleri('bugun-donecek', BUGUN)).toEqual({
      durum: 'Kirada',
      tarihTuru: 'Bitis',
      basMin: '2026-09-25',
      basMax: '2026-09-25',
    });
    expect(gorunumFiltreleri('faturasiz', BUGUN)).toEqual({ fatura: false });
    expect(gorunumFiltreleri('kapali', BUGUN)).toEqual({ durum: 'Tamamlandi' });
    // Ay başı: dün önceki ayın son günü.
    expect(gorunumFiltreleri('geciken', '2026-10-01').basMax).toBe('2026-09-30');
  });

  it('kod doğrulama: yalnız bilinen kodlar', () => {
    expect(gorunumKoduMu('kirada')).toBe(true);
    expect(gorunumKoduMu('tum')).toBe(false);
    expect(gorunumKoduMu(null)).toBe(false);
  });

  it('tumSuzgecler katalogdaki her adı verir (ön ayar önceki süzgeçleri siler)', () => {
    const f = tumSuzgecler({ durum: 'Kirada' });
    expect(Object.keys(f)).toContain('q');
    expect(Object.keys(f)).toContain('personelId');
    expect(f.durum).toBe('Kirada');
    expect(f.q).toBeUndefined();
  });

  it('suzgeclerAyni undefined alanları yok sayar, sıraya bakmaz', () => {
    expect(suzgeclerAyni({ durum: 'Kirada', q: undefined }, { durum: 'Kirada' })).toBe(true);
    expect(
      suzgeclerAyni(
        { tarihTuru: 'Bitis', durum: 'Kirada' },
        { durum: 'Kirada', tarihTuru: 'Bitis' },
      ),
    ).toBe(true);
    expect(suzgeclerAyni({ durum: 'Kirada' }, { durum: 'Kirada', q: 'x' })).toBe(false);
  });
});

describe('satır görünümü (yalnız gösterim)', () => {
  it('kirada + bitiş geçmiş gün → n gün gecikti (kırmızı), satır vurgusu yok', () => {
    // 2026-09-17 10:00 İstanbul (07:00Z) → 25'ine 8 gün.
    const g = satirGorunumu(satir({ bitTar: '2026-09-17T07:00:00Z' }), BUGUN);
    expect(g).toEqual({ tur: 'gecikmis', gun: 8 });
    expect(rozetSinifi(g)).toBe('rc-rozet rc-rozet--hata');
    expect(satirSinifi(satir({ bitTar: '2026-09-17T07:00:00Z' }), BUGUN)).toBeNull();
  });

  it('bitiş bugün (İstanbul) → bugün dönüyor (sarı) + satır vurgusu; UTC günü farklı olsa da', () => {
    // 2026-09-24T22:30Z = 25.09.2026 01:30 İstanbul.
    const s = satir({ bitTar: '2026-09-24T22:30:00Z' });
    expect(satirGorunumu(s, BUGUN)).toEqual({ tur: 'bugunDonuyor' });
    expect(rozetSinifi(satirGorunumu(s, BUGUN))).toBe('rc-rozet rc-rozet--uyari');
    expect(satirSinifi(s, BUGUN)).toBe('rc-satir-bugun');
  });

  it('bugün çıkan satır vurgulanır; durum rozeti değişmez', () => {
    const s = satir({ basTar: '2026-09-25T06:00:00Z' });
    expect(satirSinifi(s, BUGUN)).toBe('rc-satir-bugun');
    expect(rozetSinifi(satirGorunumu(s, BUGUN))).toBe('rc-rozet rc-rozet--basari');
  });

  it('kapalı/iptal sözleşmede gecikme hesaplanmaz', () => {
    const kapali = satir({ durum: 'Tamamlandi', bitTar: '2026-09-17T07:00:00Z' });
    expect(satirGorunumu(kapali, BUGUN)).toEqual({ tur: 'durum', durum: 'Tamamlandi' });
    expect(rozetSinifi(satirGorunumu(kapali, BUGUN))).toBe('rc-rozet rc-rozet--notr');
    const iptal = satir({ durum: 'Iptal' });
    expect(rozetSinifi(satirGorunumu(iptal, BUGUN))).toBe('rc-rozet rc-rozet--hata');
  });
});
