import { sorguyuCoz } from '@core/veri/liste-sorgusu';

import {
  REZERVASYON_LISTESI,
  disaAktarmaParametreleri,
} from './rezervasyon-listesi/rezervasyon-listesi.store';
import {
  detaydanDegerler,
  rezervasyonFormuOlustur,
  rezervasyonGovdesi,
  sunucuDegerleriniBirlestir,
  varsayilanTarihler,
} from './rezervasyon-modeli';
import { ARAC_ID, MUSTERI_ID, rezervasyonDetayi } from './rezervasyon-test-verisi';

describe('rezervasyon modeli', () => {
  it('varsayılan tarihler: bugün 09:00 (İstanbul) geçmediyse bugün, geçtiyse yarın; bitiş +3 gün', () => {
    // 2026-09-23 05:30Z = 08:30 İstanbul → bugün 09:00 (06:00Z).
    expect(varsayilanTarihler(new Date('2026-09-23T05:30:00Z'))).toEqual({
      basTar: '2026-09-23T06:00:00.000Z',
      bitTar: '2026-09-26T06:00:00.000Z',
    });
    // 10:00 İstanbul → yarın 09:00.
    expect(varsayilanTarihler(new Date('2026-09-23T07:00:00Z'))).toEqual({
      basTar: '2026-09-24T06:00:00.000Z',
      bitTar: '2026-09-27T06:00:00.000Z',
    });
  });

  it('dokunulmamış düzenleme gövdesi sunucunun değerlerini AYNEN geri gönderir (PUT tam değiştirme)', () => {
    const form = rezervasyonFormuOlustur();
    form.reset(detaydanDegerler(rezervasyonDetayi()));
    expect(rezervasyonGovdesi(form.getRawValue())).toEqual({
      musteriId: MUSTERI_ID,
      vehicleId: ARAC_ID,
      basTar: '2026-10-01T06:00:00+00:00',
      bitTar: '2026-10-04T06:00:00+00:00',
      gunlukUcret: 1250.5,
      fiyatTuru: 'Günlük',
      kampanyaKodu: null,
      cikisOfisi: 'Merkez',
      donusOfisi: 'Havalimanı',
      kaynak: 'Web',
      aciklama: 'Havalimanı teslim',
      kmLimit: 300,
      fazlaKmUcret: 2.5,
      yakitBirimUcret: 45,
      provizyon: 5000,
      depozito: null,
      komisyonOran: 10,
      komisyonTutar: 125.05,
      dropUcreti: null,
      sonraOdeOran: null,
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
    });
  });

  it('A5-B2: fiyat türü boş + kampanya kodu dolu → "Otomatik" ön-seçili (kodlu kayıt düzenlenebilsin)', () => {
    const d = detaydanDegerler(rezervasyonDetayi({ fiyatTuru: null, kampanyaKodu: 'YAZ10' }));
    expect(d.fiyatTuru).toBe('Otomatik');
    expect(detaydanDegerler(rezervasyonDetayi({ fiyatTuru: null })).fiyatTuru).toBeNull();
  });

  it('gövde: metin kırpılır (boş → null), para metni kayan noktaya girmez, ofis/kaynak ADLA gider', () => {
    const form = rezervasyonFormuOlustur();
    form.reset({
      musteri: { id: MUSTERI_ID, etiket: 'Ayşe' },
      arac: { id: ARAC_ID, etiket: '34 ABC 123' },
      basTar: '2026-10-01T06:00:00.000Z',
      bitTar: '2026-10-04T06:00:00.000Z',
      gunlukUcret: '1250.50',
      kampanyaKodu: '   ',
      projeAdi: '  Fuar  ',
      cikisOfisi: { id: '0b0e7c1a-9999-4aaa-8bbb-000000000009', etiket: 'Merkez' },
      provizyon: '',
    });
    const g = rezervasyonGovdesi(form.getRawValue());
    expect(g.gunlukUcret).toBe('1250.50');
    expect(g.kampanyaKodu).toBeNull();
    expect(g.projeAdi).toBe('Fuar');
    expect(g.cikisOfisi).toBe('Merkez');
    expect(g.provizyon).toBeNull();
    expect(g.donusOfisi).toBeNull();
  });

  it('birleştirme: dokunulmamış alan sunucuya çekilir, dokunulan korunur, ikisi de değiştiyse çakışma', () => {
    const form = rezervasyonFormuOlustur();
    const v1 = detaydanDegerler(rezervasyonDetayi());
    form.reset(v1);
    // Kullanıcı proje adını ve günlük ücreti değiştirdi.
    form.controls.projeAdi.setValue('Kongre');
    form.controls.projeAdi.markAsDirty();
    form.controls.gunlukUcret.setValue('1300.00');
    form.controls.gunlukUcret.markAsDirty();
    // Başka oturum: onay kodu ve günlük ücret değişti (proje adı aynı).
    const v2 = detaydanDegerler(rezervasyonDetayi({ onayKodu: 'ONY-2', gunlukUcret: 1400 }));
    const cakisan = sunucuDegerleriniBirlestir(form, v2, v1);
    expect(cakisan).toEqual(['gunlukUcret']);
    expect(form.controls.onayKodu.value).toBe('ONY-2'); // dokunulmamış → sunucu
    expect(form.controls.projeAdi.value).toBe('Kongre'); // dokunulan → korunur
    expect(form.controls.gunlukUcret.value).toBe('1300.00'); // çakışan da SİLİNMEZ
  });

  it('birleştirme: aynı sayı farklı yazım ("1250.50" = 1250.5) değişiklik sayılmaz', () => {
    const form = rezervasyonFormuOlustur();
    const v1 = detaydanDegerler(rezervasyonDetayi());
    form.reset(v1);
    form.controls.gunlukUcret.setValue('1250.50');
    form.controls.gunlukUcret.markAsDirty();
    const v2 = detaydanDegerler(rezervasyonDetayi({ gunlukUcret: '1250.5000' }));
    expect(sunucuDegerleriniBirlestir(form, v2, v1)).toEqual([]);
  });
});

describe('rezervasyon listesi sorgusu', () => {
  it('URL = API adları; bozuk durum/tarih ve beyaz liste dışı sıralama düşer', () => {
    const s = sorguyuCoz(REZERVASYON_LISTESI, {
      q: ' RZ-1 ',
      durum: 'Kiralik',
      basMin: '2026-02-30',
      basMax: '2026-10-31',
      kaynak: 'Web',
      sirala: 'musteriAd',
    });
    expect(s.filtreler).toEqual({ q: 'RZ-1', basMax: '2026-10-31', kaynak: 'Web' });
    expect(s.sirala).toBeNull();
  });

  it('dışa aktarma Blazor export adlarını kullanır (ara/durum/bas/bit/kaynak); sayfa ve sıralama taşınmaz', () => {
    expect(
      disaAktarmaParametreleri({
        q: 'Yılmaz',
        durum: 'Onayli',
        basMin: '2026-10-01',
        basMax: '2026-10-31',
        kaynak: 'Web',
        sayfa: 2,
        boyut: 25,
        sirala: '-tutar',
      }),
    ).toEqual({
      ara: 'Yılmaz',
      durum: 'Onayli',
      bas: '2026-10-01',
      bit: '2026-10-31',
      kaynak: 'Web',
    });
  });
});
