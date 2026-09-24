import { describe, expect, it } from 'vitest';

import type { CustomerCard } from '../customer-model';
import {
  cardToForm,
  clearFlag,
  formToRequest,
  liftedPrivacyFlags,
  newCustomerValue,
  secretValue,
} from './customer-form-model';

/**
 * Elle kurulmuş kartlar (beklenen değerler buradan; dönüşüm kodundan türetilmez). Kart sözleşmesi: TC hiç dönmez
 * (`tcKimlikVar`), ehliyet/pasaport maskeli, bireysel vergi no maskeli, `Anonim*` grubu `null`.
 */
const PERSON = {
  id: '11111111-1111-4111-8111-111111111111',
  surum: 'surum-7',
  tip: 'Bireysel',
  ad: 'Ayşe',
  soyad: 'Yılmaz',
  tcKimlikVar: true,
  ehliyetNoMaske: '****5678',
  pasaportNoMaske: null,
  vergiNo: null,
  vergiNoMaske: '******4321',
  cepTel: '05321112233',
  riskLimiti: 15000.5,
  vadeGun: 30,
  // 2000-05-01 İstanbul gece yarısı = 2000-04-30T21:00:00Z (o tarihte +03:00 yaz saati)
  dogumTarihi: '2000-04-30T21:00:00Z',
  mailIzin: false,
  smsIzin: null,
  tevkifatOrani: null,
  bayiKomisyon: 0.125,
  islemSubeId: '33333333-3333-4333-8333-333333333333',
  firmaId: null,
  kisiler: [{ adSoyad: 'Mehmet Kaya', telefon: '0212', mail: null, gorev: 'Müdür' }],
  anonimAd: false,
  anonimTc: false,
  anonimTelefon: false,
  anonimMail: false,
  anonimAdres: false,
  anonimBelge: false,
  aracVerilmez: true,
  createdAtUtc: '2026-01-01T00:00:00Z',
} as unknown as CustomerCard;

/** Adres grubu KVKK ile anonim: kart il/adres alanlarını `null` döndü. */
const ANON_ADDRESS = {
  ...PERSON,
  tip: 'Kurumsal',
  unvan: 'Acme AŞ',
  vergiNo: '1234567890',
  vergiNoMaske: null,
  il: null,
  adres: null,
  anonimAdres: true,
} as unknown as CustomerCard;

const body = (v: Record<string, unknown>, card: CustomerCard | null) =>
  formToRequest(v, card?.kisiler ?? [], card) as Record<string, unknown>;

describe('cari kartı gizli alan kuralı (KVKK)', () => {
  it('yazılan değer > temizle ("") > değiştirme (null)', () => {
    expect(secretValue(' 12345678901 ', false)).toBe('12345678901');
    expect(secretValue('', true)).toBe('');
    expect(secretValue(null, true)).toBe('');
    expect(secretValue('', false)).toBeNull();
    expect(secretValue(null, false)).toBeNull();
    expect(secretValue('   ', false)).toBeNull();
  });

  it('kart → form: TC/ehliyet/pasaport BOŞ açılır (kart taşımaz), bireysel vergi no boş', () => {
    const v = cardToForm(PERSON);
    expect(v['tcKimlik']).toBeNull();
    expect(v['ehliyetNo']).toBeNull();
    expect(v['pasaportNo']).toBeNull();
    expect(v['vergiNo']).toBeNull();
    expect(v[clearFlag('tcKimlik')]).toBe(false);
    expect(v['sifre']).toBeNull();
  });

  it('PUT gidiş-dönüşü: dokunulmayan gizli alanlar null (korunur), vergi no null, surum kartınki', () => {
    const b = body(cardToForm(PERSON), PERSON);
    expect(b['tcKimlik']).toBeNull();
    expect(b['ehliyetNo']).toBeNull();
    expect(b['pasaportNo']).toBeNull();
    expect(b['vergiNo']).toBeNull();
    expect(b['sifre']).toBeNull();
    expect(b['surum']).toBe('surum-7');
    expect(b).not.toHaveProperty('tcKimlikTemizle');
  });

  it('temizle kutusu "" gönderir; yeni değer yazılırsa o gider', () => {
    const v = {
      ...cardToForm(PERSON),
      [clearFlag('tcKimlik')]: true,
      ehliyetNo: 'B-99887766',
      [clearFlag('vergiNo')]: true,
    };
    const b = body(v, PERSON);
    expect(b['tcKimlik']).toBe('');
    expect(b['ehliyetNo']).toBe('B-99887766');
    expect(b['pasaportNo']).toBeNull();
    expect(b['vergiNo']).toBe('');
  });

  it('yeni cari: gizli alan boşsa null, yazılırsa değer; surum YOK', () => {
    const b = body({ ...newCustomerValue(), ad: 'Ali', tcKimlik: '10000000146' }, null);
    expect(b['tcKimlik']).toBe('10000000146');
    expect(b['ehliyetNo']).toBeNull();
    expect(b).not.toHaveProperty('surum');
    expect(b['tip']).toBe('Bireysel');
    expect(b['riskLimiti']).toBe('0');
    expect(b['vadeGun']).toBe(0);
  });

  it('anonim adres grubu: kilitli alanlar null gider (sunucu korur); kurumsal vergi no düz', () => {
    const b = body(cardToForm(ANON_ADDRESS), ANON_ADDRESS);
    expect(b['il']).toBeNull();
    expect(b['adres']).toBeNull();
    expect(b['anonimAdres']).toBe(true);
    expect(b['vergiNo']).toBe('1234567890');
  });
});

describe('cari kartı gidiş-dönüş', () => {
  it('tarih sunucunun anıyla aynen; oran/tutar invariant metin; üç durumlu izinler', () => {
    const v = cardToForm(PERSON);
    expect(v['dogumTarihi']).toBe('2000-05-01');
    expect(v['riskLimiti']).toBe('15000.5');
    expect(v['bayiKomisyon']).toBe('0.125');
    expect(v['mailIzin']).toBe('false');
    expect(v['smsIzin']).toBeNull();
    const b = body(v, PERSON);
    expect(b['dogumTarihi']).toBe('2000-04-30T21:00:00Z');
    expect(b['riskLimiti']).toBe('15000.5');
    expect(b['vadeGun']).toBe(30);
    expect(b['mailIzin']).toBe(false);
    expect(b['smsIzin']).toBeNull();
    expect(b['tevkifatOrani']).toBeNull();
    expect(b['aracVerilmez']).toBe(true);
    expect(b['islemSubeId']).toBe('33333333-3333-4333-8333-333333333333');
    expect(b['firmaId']).toBeNull();
  });

  it('yeni gün İstanbul gece yarısı anı olur', () => {
    // 2020'den beri İstanbul sabit +03:00: 2020-01-15 00:00 = 2020-01-14T21:00Z.
    const b = body({ ...cardToForm(PERSON), dogumTarihi: '2020-01-15' }, PERSON);
    expect(b['dogumTarihi']).toBe('2020-01-14T21:00:00.000Z');
  });

  it('yetkili kişiler: ad soyadı boş satır gönderilmez, metinler kırpılır', () => {
    const b = formToRequest(
      cardToForm(PERSON),
      [
        { adSoyad: ' Mehmet Kaya ', telefon: '0212', mail: null, gorev: 'Müdür' },
        { adSoyad: '  ', telefon: '0555', mail: 'x@y.z', gorev: null },
      ],
      PERSON,
    ) as Record<string, unknown>;
    expect(b['kisiler']).toEqual([
      { adSoyad: 'Mehmet Kaya', telefon: '0212', mail: null, gorev: 'Müdür' },
    ]);
  });

  it('kaldırılan anonimleştirme bayrakları listelenir (sunucu ManageUsers ister)', () => {
    const card = { ...PERSON, anonimAd: true, anonimMail: true } as CustomerCard;
    const v = { ...cardToForm(card), anonimAd: false };
    expect(liftedPrivacyFlags(v, card)).toEqual(['anonimAd']);
    expect(liftedPrivacyFlags(cardToForm(card), card)).toEqual([]);
    expect(liftedPrivacyFlags(v, null)).toEqual([]);
  });
});
