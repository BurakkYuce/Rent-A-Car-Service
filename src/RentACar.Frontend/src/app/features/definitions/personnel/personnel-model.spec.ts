import { personnelToBody, personnelToRow } from './personnel-model';

/** Personel KVKK kuralları: TC yazma-yalnız (boş = koru, sil = "", dolu = yeni), maaş silme bayrağı, gün ↔ an. */
describe('personel gövde/satır dönüşümü', () => {
  const base = { kod: 'P1', ad: 'Ali', soyad: 'Veli', aktif: true, maas: '45000.75' };

  it('TC boş ve sil işaretsiz: alan HİÇ gönderilmez (sunucu korur)', () => {
    const body = personnelToBody({ ...base, tcKimlik: null, tcTemizle: false });
    expect('tcKimlik' in body).toBe(false);
    expect('tcTemizle' in body).toBe(false);
    expect(body['maasTemizle']).toBe(false);
  });

  it('sil işaretli: tcKimlik "" gider; dolu değer silmeye üstün gelir', () => {
    expect(personnelToBody({ ...base, tcKimlik: '  ', tcTemizle: true })['tcKimlik']).toBe('');
    expect(personnelToBody({ ...base, tcKimlik: '10000000146', tcTemizle: true })['tcKimlik']).toBe(
      '10000000146',
    );
  });

  it('maaş boşaltılınca maasTemizle; gün alanları İstanbul gece yarısı UTC anı', () => {
    const body = personnelToBody({ ...base, maas: null, iseGiris: '2026-09-22', iseCikis: null });
    expect(body['maasTemizle']).toBe(true);
    expect(body['iseGiris']).toBe('2026-09-21T21:00:00.000Z');
    expect(body['iseCikis']).toBeNull();
  });

  it('satır: an → İstanbul günü, TC alanı daima boş', () => {
    const row = personnelToRow({
      id: '1',
      iseGiris: '2026-09-21T21:00:00+00:00',
      dogumTarihi: null,
      tcKimlikTanimli: true,
    });
    expect(row['iseGiris']).toBe('2026-09-22');
    expect(row['dogumTarihi']).toBeNull();
    expect(row['tcKimlik']).toBeNull();
    expect(row['tcTemizle']).toBe(false);
  });
});
