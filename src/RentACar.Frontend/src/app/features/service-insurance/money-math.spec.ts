import { describe, expect, it } from 'vitest';

import { lineNetAmount, round2 } from './money-math';

/** Beklenen değerler elle hesaplandı (bağımsız oracle); `mukerrer` bildirimindeki "girdiğiniz" tutarı (kanca). */
describe('servis kalemi net tutarı (mükerrer bildirimi — inceleme M3)', () => {
  it('elle kurulmuş: 100 × 2 − 0 = 200; 12,345 × 3 → 37,04 − 1,005 → 1,01 = 36,03; açık tutar önceliklidir', () => {
    expect(lineNetAmount({ tutar: null, birimFiyat: '100.00', miktar: 2, indirim: null })).toBe(
      200,
    );
    expect(lineNetAmount({ tutar: null, birimFiyat: 12.345, miktar: 3, indirim: '1.005' })).toBe(
      36.03,
    );
    expect(lineNetAmount({ tutar: '150.00', birimFiyat: '100.00', miktar: 2, indirim: null })).toBe(
      150,
    );
    expect(lineNetAmount({ tutar: null, birimFiyat: '99.99', miktar: null, indirim: null })).toBe(
      99.99,
    );
    expect(lineNetAmount({ tutar: null, birimFiyat: null, miktar: 2, indirim: null })).toBeNull();
  });

  it('yarım kuruş sıfırdan uzağa (kayan nokta tuzağı: 1,005 → 1,01; −2,675 → −2,68)', () => {
    expect(round2(1.005)).toBe(1.01);
    expect(round2(-2.675)).toBe(-2.68);
    expect(round2(0)).toBe(0);
  });
});
