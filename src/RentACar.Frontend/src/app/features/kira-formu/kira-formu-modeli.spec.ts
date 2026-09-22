import { sunucuHatalariniUygula } from '@core/form/sunucu-hatalari';
import {
  SUNUCU_ALAN_ESLEMESI,
  altSekmeAdresi,
  altSekmeMi,
  aynalaraHataKopyala,
  aynalariBagla,
  detaydanDegerler,
  ekHizmetSatiri,
  formuSifirla,
  guncelleGovdesi,
  hashParcala,
  hesaplaParametreleri,
  kiraFormuOlustur,
  kiraSorgusuCoz,
  musaitPencere,
  olusturGovdesi,
  penceredenTarihler,
  secenekListesi,
  sekmeMi,
  sistemKalemiMi,
  sunucuDegerleriniBirlestir,
} from './kira-formu-modeli';
import type { KiraDetayYaniti, KiraSozlesmesi } from './kira-tipleri';
import { sozlesmePdfAdresi } from './kira-yazdir';

const KIRA_ID = '0b0e7c1a-1111-4aaa-8bbb-000000000001';
const MUSTERI_ID = '0b0e7c1a-2222-4aaa-8bbb-000000000002';
const ARAC_ID = '0b0e7c1a-3333-4aaa-8bbb-000000000003';
const SURUCU_ID = '0b0e7c1a-4444-4aaa-8bbb-000000000004';
const PERSONEL_ID = '0b0e7c1a-5555-4aaa-8bbb-000000000005';

/** Sunucunun `KiraGuncelleIstegi` alanları — C# sınıfından elle kopyalandı (bağımsız oracle; 58 alan + sürüm). */
const PUT_ALANLARI = [
  'surum',
  'cikisOfisi',
  'donusOfisi',
  'ikinciSurucuId',
  'teslimEdenPersonelId',
  'odemeSekli',
  'ikinciSurucuSerbestAd',
  'ikinciSurucuSerbestSoyad',
  'ikinciSurucuSerbestTel',
  'ikinciSurucuSerbestEhliyetSinifi',
  'aciklama',
  'kaynak',
  'kiralamaTuru',
  'donemselFaturalama',
  'faturalamaTipi',
  'kmLimit',
  'fazlaKmUcret',
  'yakitBirimUcret',
  'provizyon',
  'depozito',
  'komisyonOran',
  'komisyonTutar',
  'dropUcreti',
  'sonraOdeOran',
  'uyariAciklama',
  'ozelFaturaAciklama',
  'faturaListesindeGizle',
  'ucusNo',
  'provizyonNo',
  'provizyonTarih',
  'onayKodu',
  'firmaKodu',
  'projeAdi',
  'ozelKod',
  'ozelKdvOran',
  'damgaVergisi',
  'talepTuru',
  'geldigiBirim',
  'kefilBilgisi',
  'assistFirma',
  'ozelSoforBilgisi',
  'ekKosullar',
  'belgeSablonId',
  'manuelFindexPuan',
  'opsiyonNet',
  'opsiyonGun',
  'kabisCikis',
  'kabisDonus',
  'otomatikUzat',
  'aksYedekAnahtarCikis',
  'aksYedekAnahtarDonus',
  'aksStepneCikis',
  'aksStepneDonus',
  'aksZincirCikis',
  'aksZincirDonus',
  'aksIlkYardimCikis',
  'aksIlkYardimDonus',
  'aksLastikCikis',
  'aksLastikDonus',
];

function kira(ek: Partial<KiraSozlesmesi> = {}): KiraSozlesmesi {
  return {
    id: KIRA_ID,
    sozlesmeNo: '2026220901001',
    durum: 'Kirada',
    reservationId: null,
    musteriId: MUSTERI_ID,
    vehicleId: ARAC_ID,
    basTar: '2026-09-22T06:00:00+00:00',
    bitTar: '2026-09-25T06:00:00+00:00',
    cikisOfisi: 'Merkez',
    cikisSubeId: null,
    donusOfisi: 'Havalimanı',
    kmLimit: 300,
    fazlaKmUcret: 2.5,
    yakitBirimUcret: 45.75,
    cikisKm: null,
    cikisYakit: null,
    donusKm: null,
    donusYakit: null,
    gercekDonusTar: null,
    fazlaKm: 0,
    fazlaKmBedeli: 0,
    eksikYakit: 0,
    yakitBedeli: 0,
    uzatmaGun: 0,
    uzatmaBedeli: 0,
    kmHediye: null,
    bitisSebebi: null,
    teslimAlanPersonelId: null,
    teslimEdenPersonelId: PERSONEL_ID,
    odemeSekli: 'Nakit',
    ikinciSurucuId: SURUCU_ID,
    ikinciSurucuSerbestAd: null,
    ikinciSurucuSerbestSoyad: null,
    ikinciSurucuSerbestTel: null,
    ikinciSurucuSerbestEhliyetSinifi: null,
    hediyeGun: null,
    faturalananGun: null,
    vadeTar: null,
    iskontoTutar: null,
    haftaSonuFark: null,
    gun: 3,
    gunlukUcret: 1000,
    tutar: 3000,
    genelToplam: 3000,
    tahsilat: 0,
    bakiye: 3000,
    provizyon: 5000,
    depozito: null,
    komisyonOran: 10,
    komisyonTutar: null,
    dropUcreti: 250.5,
    sonraOdeOran: null,
    aciklama: 'Açıklama',
    kaynak: 'Web',
    kampanyaKodu: null,
    uyariAciklama: null,
    ozelFaturaAciklama: null,
    faturaListesindeGizle: null,
    ucusNo: 'TK1923',
    provizyonNo: 'P-1',
    provizyonTarih: '2026-09-10T00:00:00+00:00',
    provizyonDurum: 'Yok',
    provizyonKapamaTarih: null,
    provizyonKapamaTutar: null,
    onayKodu: null,
    firmaKodu: null,
    projeAdi: null,
    ozelKod: null,
    talepTuru: null,
    geldigiBirim: null,
    kefilBilgisi: null,
    assistFirma: null,
    ozelSoforBilgisi: null,
    ekKosullar: 'Sigara içilmez',
    belgeSablonId: null,
    opsiyonNet: null,
    opsiyonGun: 2,
    riskOnay: false,
    manuelFindexPuan: 1450,
    kabisCikis: true,
    kabisDonus: null,
    otomatikUzat: false,
    aksYedekAnahtarCikis: true,
    aksYedekAnahtarDonus: null,
    aksStepneCikis: null,
    aksStepneDonus: null,
    aksZincirCikis: null,
    aksZincirDonus: null,
    aksIlkYardimCikis: null,
    aksIlkYardimDonus: null,
    aksLastikCikis: 'iyi',
    aksLastikDonus: null,
    kiralamaTuru: 'Kısa Kiralama',
    faturalamaTipi: null,
    fiyatTuru: 'KDV Dahil Günlük',
    doviz: 'TL',
    kurSnapshot: 1,
    donemselFaturalama: false,
    kdvOranSnapshot: 0.2,
    ozelKdvOran: 0.1,
    damgaVergisi: null,
    createdAtUtc: '2026-09-22T06:00:00+00:00',
    updatedAtUtc: null,
    surum: '4711',
    ...ek,
  };
}

function detay(ek: Partial<KiraSozlesmesi> = {}): KiraDetayYaniti {
  return {
    kira: kira(ek),
    musteri: { id: MUSTERI_ID, ad: 'Ayşe Yılmaz' },
    ikinciSurucu: { id: SURUCU_ID, ad: 'Mehmet Kaya' },
    arac: {
      id: ARAC_ID,
      plaka: '34 ABC 123',
      marka: 'Fiat',
      tip: 'Egea',
      modelYili: 2024,
      vites: 'Manuel',
      yakit: 'Dizel',
      grup: 'C',
      segment: 'Orta',
      km: 12000,
      sube: 'Merkez',
      konum: 'Otopark',
    },
    islemSubeAdi: 'Merkez Şube',
    teslimAlanPersonelAd: null,
    teslimEdenPersonelAd: 'Ali Veli',
    ekHizmetler: [],
    doviz: null,
    paylasim: null,
    yetkiler: { operasyon: true, silme: true, finans: true },
    tahsilat: null,
  };
}

describe('kira sorgu sözleşmesi (?varac, ?vfrom, ?vto, ?vgrup, ?musteriId)', () => {
  const oku = (q: Record<string, string>) => (ad: string) => q[ad] ?? null;

  it('geçerli parametreler okunur', () => {
    expect(
      kiraSorgusuCoz(
        oku({
          varac: ARAC_ID,
          vfrom: '2026-10-01',
          vto: '2026-10-04',
          vgrup: ' C ',
          musteriId: MUSTERI_ID,
        }),
      ),
    ).toEqual({
      varac: ARAC_ID,
      vfrom: '2026-10-01',
      vto: '2026-10-04',
      vgrup: 'C',
      musteriId: MUSTERI_ID,
    });
  });

  it('bozuk değer sessizce yok sayılır (form yine açılır)', () => {
    expect(
      kiraSorgusuCoz(oku({ varac: 'abc', vfrom: '2026-02-30', vto: '01.10.2026', musteriId: '1' })),
    ).toEqual({ varac: null, vfrom: null, vto: null, vgrup: null, musteriId: null });
  });

  it('müsaitlik penceresi yalnız bitiş > başlangıçta', () => {
    expect(musaitPencere({ vfrom: '2026-10-01', vto: '2026-10-01', vgrup: null })).toBeNull();
    expect(musaitPencere({ vfrom: '2026-10-02', vto: '2026-10-01', vgrup: null })).toBeNull();
    expect(musaitPencere({ vfrom: '2026-10-01', vto: '2026-10-04', vgrup: 'C' })).toEqual({
      vfrom: '2026-10-01',
      vto: '2026-10-04',
      vgrup: 'C',
    });
  });

  it('pencere günleri İstanbul 09:00 anına çevrilir (UTC+3 → 06:00Z)', () => {
    expect(penceredenTarihler({ vfrom: '2026-10-01', vto: '2026-10-04', vgrup: null })).toEqual({
      basTar: '2026-10-01T06:00:00.000Z',
      bitTar: '2026-10-04T06:00:00.000Z',
    });
  });
});

describe('hash sekme (#sekme=…&alt=…)', () => {
  it('ayrıştırır ve kimlikleri doğrular', () => {
    const h = hashParcala('#sekme=ayrintilar&alt=aksesuar&fin=nakit');
    expect(h).toEqual({ sekme: 'ayrintilar', alt: 'aksesuar', fin: 'nakit' });
    expect(sekmeMi(h['sekme'])).toBe(true);
    expect(altSekmeMi(h['alt'])).toBe(true);
    expect(sekmeMi('odeme')).toBe(false);
    expect(altSekmeMi(undefined)).toBe(false);
    expect(hashParcala('#sekme=%E0%A4%A')).toEqual({});
  });

  it('alt sekme adresi yolu ve sorguyu korur (çıplak # köke çözülmez)', () => {
    expect(altSekmeAdresi('/app/kiralar/yeni', '?varac=1', 'aksesuar')).toBe(
      '/app/kiralar/yeni?varac=1#sekme=ayrintilar&alt=aksesuar',
    );
  });
});

describe('form durumu', () => {
  it('yeni kirada düzenleme-alanları, düzenlemede oluşturma-alanları pasif', () => {
    const yeni = kiraFormuOlustur('yeni');
    expect(yeni.controls.kmLimit.disabled).toBe(true);
    expect(yeni.controls.teslimEdenPersonel.disabled).toBe(true);
    expect(yeni.controls.musteri.enabled).toBe(true);
    expect(yeni.controls.ayna.controls.musteri.enabled).toBe(true);

    const duzenle = kiraFormuOlustur('duzenle');
    expect(duzenle.controls.musteri.disabled).toBe(true);
    expect(duzenle.controls.gunlukUcret.disabled).toBe(true);
    expect(duzenle.controls.ayna.controls.basTar.disabled).toBe(true);
    expect(duzenle.controls.kmLimit.enabled).toBe(true);
  });

  it('yeni kirada müşteri/araç/tarih zorunlu — boş form istek üretmez', () => {
    const form = kiraFormuOlustur('yeni');
    expect(form.invalid).toBe(true);
    expect(form.controls.musteri.hasError('required')).toBe(true);
    expect(form.controls.ayna.controls.arac.hasError('required')).toBe(true);
  });

  it('ayna ↔ kanonik iki yönlü eşitlenir; aynadan yazım formu kirletir', () => {
    const form = kiraFormuOlustur('yeni');
    aynalariBagla(form);
    form.controls.kaynak.setValue('Web');
    expect(form.controls.ayna.controls.kaynak.value).toBe('Web');

    form.controls.ayna.controls.gunlukUcret.setValue('1250.50');
    expect(form.controls.gunlukUcret.value).toBe('1250.50');
    expect(form.controls.gunlukUcret.dirty).toBe(true);
  });

  /** Sayfanın PUT bağlamı: okunan sürüm + dokunulmadıysa sunucunun orijinal provizyon anı. */
  const baglam = (form: ReturnType<typeof kiraFormuOlustur>, k: KiraSozlesmesi = kira()) => ({
    surum: k.surum ?? '',
    provizyonTarihAni: k.provizyonTarih,
    provizyonTarihDegisti: form.controls.provizyonTarih.dirty,
  });

  it('düzenleme ön doldurması + PUT gövdesi: 58 alanın HEPSİ + sürüm, kayıtlı değerlerle (tur-döngüsü sabit)', () => {
    const form = kiraFormuOlustur('duzenle');
    aynalariBagla(form); // sayfadaki gibi bağlı: ayna sıfırlaması kanoniği SİLMEMELİ
    formuSifirla(form, detaydanDegerler(detay()));
    expect(form.pristine).toBe(true);
    expect(form.controls.ayna.controls.cikisOfisi.value?.etiket).toBe('Merkez');

    const govde = guncelleGovdesi(form.getRawValue(), baglam(form));
    expect(Object.keys(govde).sort()).toEqual([...PUT_ALANLARI].sort());
    expect(govde).toMatchObject({
      surum: '4711',
      cikisOfisi: 'Merkez',
      donusOfisi: 'Havalimanı',
      kaynak: 'Web',
      kiralamaTuru: 'Kısa Kiralama',
      ikinciSurucuId: SURUCU_ID,
      teslimEdenPersonelId: PERSONEL_ID,
      kmLimit: 300,
      fazlaKmUcret: 2.5,
      yakitBirimUcret: 45.75,
      provizyon: 5000,
      dropUcreti: 250.5,
      komisyonOran: 10,
      ozelKdvOran: 0.1,
      provizyonTarih: '2026-09-10T00:00:00+00:00', // dokunulmadı → sunucunun anı AYNEN
      kabisCikis: true,
      kabisDonus: false,
      faturaListesindeGizle: false,
      aksYedekAnahtarCikis: true,
      ekKosullar: 'Sigara içilmez',
      opsiyonGun: 2,
      manuelFindexPuan: 1450,
    });

    // Kaydet → aç → kaydet: gün kayması / değer kayması yok.
    const k2 = kira({ provizyonTarih: govde.provizyonTarih ?? null });
    const ikinci = kiraFormuOlustur('duzenle');
    formuSifirla(ikinci, detaydanDegerler({ ...detay(), kira: k2 }));
    expect(guncelleGovdesi(ikinci.getRawValue(), baglam(ikinci, k2))).toEqual(govde);
  });

  it('F6: gece yarısından sonraki provizyon anı İstanbul gününde görünür, dokunulmadan kayıtta KAYMAZ', () => {
    // Provizyon al 01:30 İstanbul'da (sunucu UtcNow yazar): 22.09 22:30Z = 23.09 01:30 +03.
    const k = kira({ provizyonTarih: '2026-09-22T22:30:00+00:00' });
    const form = kiraFormuOlustur('duzenle');
    formuSifirla(form, detaydanDegerler({ ...detay(), kira: k }));
    expect(form.controls.provizyonTarih.value).toBe('2026-09-23');
    expect(guncelleGovdesi(form.getRawValue(), baglam(form, k)).provizyonTarih).toBe(
      '2026-09-22T22:30:00+00:00',
    );
    // Kullanıcı günü seçerse: seçilen günün UTC gece yarısı (İstanbul'da aynı gün okunur).
    form.controls.provizyonTarih.setValue('2026-09-25');
    form.controls.provizyonTarih.markAsDirty();
    const yazilan = guncelleGovdesi(form.getRawValue(), baglam(form, k)).provizyonTarih;
    expect(yazilan).toBe('2026-09-25T00:00:00Z');
    const geri = kiraFormuOlustur('duzenle');
    formuSifirla(
      geri,
      detaydanDegerler({ ...detay(), kira: kira({ provizyonTarih: yazilan ?? null }) }),
    );
    expect(geri.controls.provizyonTarih.value).toBe('2026-09-25');
  });

  it('F4: 2. sürücü carisi silinmişse kimlik KORUNUR (PUT null göndermez; ücret satırı düşmez)', () => {
    const form = kiraFormuOlustur('duzenle');
    formuSifirla(form, detaydanDegerler({ ...detay(), ikinciSurucu: null }, '(kayıt bulunamadı)'));
    expect(form.controls.ikinciSurucu.value).toEqual({
      id: SURUCU_ID,
      etiket: '(kayıt bulunamadı)',
    });
    expect(guncelleGovdesi(form.getRawValue(), baglam(form)).ikinciSurucuId).toBe(SURUCU_ID);
  });

  it('F2: kirli forma sunucu değerleri birleşir — dokunulmayan güncellenir, dokunulan korunur, çakışan döner', () => {
    const form = kiraFormuOlustur('duzenle');
    aynalariBagla(form);
    const ilk = detaydanDegerler(detay());
    formuSifirla(form, ilk);
    // Kullanıcı açıklamayı ve komisyon oranını değiştirir.
    form.controls.aciklama.setValue('benim notum');
    form.controls.aciklama.markAsDirty();
    form.controls.ayna.controls.kaynak.setValue('Telefon'); // ayna → kanonik kirlenir
    // Başka oturum: drop 300, kaynak 'Acente', provizyon tarihi yazıldı.
    const sunucu = detaydanDegerler({
      ...detay(),
      kira: kira({
        dropUcreti: 300,
        kaynak: 'Acente',
        provizyonTarih: '2026-09-22T08:15:00+00:00',
      }),
    });
    const cakisan = sunucuDegerleriniBirlestir(form, sunucu, ilk);
    expect(cakisan).toEqual(['kaynak']);
    expect(form.controls.dropUcreti.value).toBe(300);
    expect(form.controls.dropUcreti.dirty).toBe(false);
    expect(form.controls.provizyonTarih.value).toBe('2026-09-22');
    expect(form.controls.aciklama.value).toBe('benim notum');
    expect(form.controls.kaynak.value).toBe('Telefon');
    expect(form.controls.ayna.controls.kaynak.value).toBe('Telefon');
    const govde = guncelleGovdesi(form.getRawValue(), baglam(form));
    expect(govde).toMatchObject({ dropUcreti: 300, aciklama: 'benim notum', kaynak: 'Telefon' });
  });

  it('boş metin null gider; zorunlu PUT alanı boşsa gövde KURULMAZ (sessiz 0 yok)', () => {
    const form = kiraFormuOlustur('duzenle');
    formuSifirla(form, detaydanDegerler(detay({ aciklama: '   ' })));
    expect(guncelleGovdesi(form.getRawValue(), baglam(form)).aciklama).toBeNull();

    form.controls.kmLimit.setValue(null);
    expect(form.invalid).toBe(true);
    expect(() => guncelleGovdesi(form.getRawValue(), baglam(form))).toThrow(/kmLimit/);
  });

  it('oluşturma gövdesi: kimlikler, ofis adı, ek hizmet seçimi; müşteri PII taşımaz', () => {
    const form = kiraFormuOlustur('yeni');
    form.patchValue({
      musteri: { id: MUSTERI_ID, etiket: 'Ayşe Yılmaz' },
      arac: { id: ARAC_ID, etiket: '34 ABC 123' },
      basTar: '2026-10-01T06:00:00.000Z',
      bitTar: '2026-10-04T06:00:00.000Z',
      gunlukUcret: '1250.50',
      cikisOfisi: { id: 'x', etiket: 'Merkez' },
      kaynak: '  ',
    });
    form.controls.ekHizmetler.push(ekHizmetSatiri({ id: 'e1', etiket: 'Bebek koltuğu' }, 2));
    const g = olusturGovdesi(form.getRawValue());
    expect(g).toMatchObject({
      musteriId: MUSTERI_ID,
      vehicleId: ARAC_ID,
      basTar: '2026-10-01T06:00:00.000Z',
      bitTar: '2026-10-04T06:00:00.000Z',
      gunlukUcret: '1250.50',
      cikisOfisi: 'Merkez',
      donusOfisi: null,
      kaynak: null,
      riskOnay: false,
      ekHizmetler: [{ tanimId: 'e1', miktar: 2 }],
    });
    expect(Object.keys(g)).not.toContain('kmLimit');
    expect(Object.keys(g)).not.toContain('tcKimlik');
  });

  it('canlı hesap parametreleri: tarih yoksa istek yok; ek = "tanimId:miktar"', () => {
    const form = kiraFormuOlustur('yeni');
    expect(hesaplaParametreleri(form.getRawValue())).toBeNull();
    form.patchValue({
      basTar: '2026-10-01T06:00:00.000Z',
      bitTar: '2026-10-04T06:00:00.000Z',
      gunlukUcret: 1000,
      dropUcreti: '250.50',
      donusOfisi: { id: 'x', etiket: 'Havalimanı' },
    });
    form.controls.ekHizmetler.push(ekHizmetSatiri({ id: 'e1', etiket: 'A' }, 1.5));
    form.controls.ekHizmetler.push(ekHizmetSatiri({ id: 'e2', etiket: 'B' }));
    expect(hesaplaParametreleri(form.getRawValue(), KIRA_ID)).toEqual({
      basTar: '2026-10-01T06:00:00.000Z',
      bitTar: '2026-10-04T06:00:00.000Z',
      vehicleId: null,
      gunlukUcret: '1000',
      fiyatTuru: null,
      doviz: null,
      cikisOfisi: null,
      donusOfisi: 'Havalimanı',
      dropUcreti: '250.50',
      ek: 'e1:1.5,e2:1',
      musteriId: null,
      kampanyaKodu: null,
      ikinciSurucuId: null,
      rentalId: KIRA_ID,
    });
  });
});

describe('hata eşlemesi', () => {
  it('sunucu alan adı forma eşlenir, aynasına kopyalanır; değerlere dokunulmaz', () => {
    const form = kiraFormuOlustur('yeni');
    aynalariBagla(form);
    form.controls.gunlukUcret.setValue('1250.50');
    const eslesmeyen = sunucuHatalariniUygula(
      form,
      {
        musteriId: ['Müşteri seçilmelidir.'],
        gunlukUcret: ['Günlük ücret negatif olamaz.'],
        bilinmeyen: ['Genel mesaj'],
      },
      SUNUCU_ALAN_ESLEMESI,
    );
    aynalaraHataKopyala(form);
    expect(eslesmeyen).toEqual(['Genel mesaj']);
    expect(form.controls.musteri.errors?.['sunucu']).toEqual(['Müşteri seçilmelidir.']);
    expect(form.controls.ayna.controls.musteri.errors?.['sunucu']).toEqual([
      'Müşteri seçilmelidir.',
    ]);
    expect(form.controls.ayna.controls.gunlukUcret.touched).toBe(true);
    expect(form.controls.gunlukUcret.value).toBe('1250.50');
  });
});

describe('yardımcılar', () => {
  it('kayıtlı eski değer seçenek listesinden düşmez', () => {
    expect(secenekListesi(['A', 'B'], 'Eski')).toEqual([
      { deger: 'A', etiket: 'A' },
      { deger: 'B', etiket: 'B' },
      { deger: 'Eski', etiket: 'Eski' },
    ]);
    expect(secenekListesi(['A'], 'A')).toHaveLength(1);
  });

  it('sistem ücret kalemi (SYS-*) tanınır', () => {
    expect(sistemKalemiMi('SYS-GENC')).toBe(true);
    expect(sistemKalemiMi('sys-drop')).toBe(true);
    expect(sistemKalemiMi('BEBEK')).toBe(false);
  });

  it('yazdırma: sunucu PDF ucu; kimlik değilse adres yok', () => {
    expect(sozlesmePdfAdresi(KIRA_ID)).toBe(`/kiralar/${KIRA_ID}/pdf`);
    expect(sozlesmePdfAdresi('../../x')).toBeNull();
  });
});
