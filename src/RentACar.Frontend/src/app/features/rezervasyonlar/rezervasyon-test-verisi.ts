import type { RezervasyonDetayYaniti, RezervasyonDto } from './rezervasyon-modeli';

/**
 * Birim testlerinin elle kurulmuş sunucu kaydı (bağımsız oracle: beklenen değerler buradan, model kodundan
 * türetilmez). Uygulama kodu bunu içe aktarmaz.
 */
export const REZ_ID = '0b0e7c1a-7777-4aaa-8bbb-000000000007';
export const MUSTERI_ID = '0b0e7c1a-2222-4aaa-8bbb-000000000002';
export const ARAC_ID = '0b0e7c1a-3333-4aaa-8bbb-000000000003';

export function rezervasyonDto(ek: Partial<RezervasyonDto> = {}): RezervasyonDto {
  return {
    id: REZ_ID,
    no: 'RZ-000042',
    durum: 'Rezerv',
    surum: '812',
    musteriId: MUSTERI_ID,
    vehicleId: ARAC_ID,
    basTar: '2026-10-01T06:00:00+00:00',
    bitTar: '2026-10-04T06:00:00+00:00',
    cikisOfisi: 'Merkez',
    donusOfisi: 'Havalimanı',
    gun: 3,
    gunlukUcret: 1250.5,
    tutar: 3751.5,
    hediyeGun: null,
    faturalananGun: 3,
    iskontoTutar: null,
    haftaSonuFark: null,
    fiyatTuru: 'Günlük',
    kampanyaKodu: null,
    kdvOranSnapshot: 0.2,
    kmLimit: 300,
    fazlaKmUcret: 2.5,
    yakitBirimUcret: 45,
    provizyon: 5000,
    depozito: null,
    komisyonOran: 10,
    komisyonTutar: 125.05,
    dropUcreti: null,
    sonraOdeOran: null,
    kaynak: 'Web',
    aciklama: 'Havalimanı teslim',
    otaKiraBedeli: 3000,
    otaDropBedeli: null,
    otaBebekKoltugu: 150,
    otaNavigasyon: null,
    otaLcf: null,
    otaCdw: 99.9,
    otaScdw: null,
    otaEkSurucu: null,
    talepTuru: 'Kurumsal',
    geldigiBirim: 'Satış',
    onayKodu: 'ONY-1',
    projeAdi: 'Fuar',
    kiraId: null,
    olusturmaUtc: '2026-09-20T08:00:00+00:00',
    ...ek,
  };
}

export function rezervasyonDetayi(ek: Partial<RezervasyonDto> = {}): RezervasyonDetayYaniti {
  return {
    rezervasyon: rezervasyonDto(ek),
    musteriAd: 'Ayşe Yılmaz',
    plaka: '34 ABC 123',
    yetkiler: { duzenle: true, onayla: true, kirayaCevir: true, iptal: true },
  };
}
