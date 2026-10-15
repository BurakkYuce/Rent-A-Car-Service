import {
  type KabisStatus,
  attemptCount,
  kabisActionable,
  kabisHasProblem,
  kabisTone,
  knownStatus,
  knownType,
} from './kabis-model';

function notice(durum: string): KabisStatus['bildirimler'][number] {
  return {
    tur: 'Kiralama',
    durum,
    kurum: 'Emniyet',
    olayUtc: '2026-10-15T09:00:00Z',
    denemeSayisi: 1,
    sonHata: null,
    disReferans: null,
    elle: false,
    gerekce: null,
    sonDenemeUtc: null,
    sonrakiDenemeUtc: null,
    bildirimUtc: null,
  };
}

describe('KABİS kartı modeli', () => {
  it('rozet tonu durumdan: bildirildi yeşil, bekliyor sarı, hata kırmızı, gerekli değil nötr', () => {
    expect(kabisTone('Bildirildi')).toBe('basari');
    expect(kabisTone('Bekliyor')).toBe('uyari');
    expect(kabisTone('Hata')).toBe('hata');
    expect(kabisTone('GerekliDegil')).toBe('notr');
    expect(kabisTone('BilinmeyenYeniDurum')).toBe('notr');
  });

  it('operatör işlemi yalnız açık (bekleyen/hatalı) kayıtta', () => {
    expect(kabisActionable({ durum: 'Bekliyor' })).toBe(true);
    expect(kabisActionable({ durum: 'Hata' })).toBe(true);
    expect(kabisActionable({ durum: 'Bildirildi' })).toBe(false);
    expect(kabisActionable({ durum: 'GerekliDegil' })).toBe(false);
  });

  it('sorun yalnız otomatik bildirim açıkken ve açık kayıt varsa', () => {
    const base: KabisStatus = {
      etkin: true,
      teslimiEngelle: false,
      kurum: 'Emniyet',
      bildirimler: [notice('Bildirildi')],
      uyari: null,
    };
    expect(kabisHasProblem(base)).toBe(false);
    expect(kabisHasProblem({ ...base, bildirimler: [notice('Hata')] })).toBe(true);
    expect(kabisHasProblem({ ...base, etkin: false, bildirimler: [notice('Hata')] })).toBe(false);
    expect(kabisHasProblem(null)).toBe(false);
  });

  it('bilinmeyen tür/durum çeviri anahtarına dönüşmez', () => {
    expect(knownStatus('Bekliyor')).toBe(true);
    expect(knownStatus('Yeni')).toBe(false);
    expect(knownType('Iade')).toBe(true);
    expect(knownType('Sorgu')).toBe(false);
  });

  it('deneme sayısı metin ya da sayı gelebilir', () => {
    expect(attemptCount(3)).toBe(3);
    expect(attemptCount('4')).toBe(4);
    expect(attemptCount('x')).toBe(0);
  });
});
