/**
 * Cari kartının alan tanımları — Blazor `CustomerEdit` + liste içi "Yeni Cari" formunun TÜM alanları (`CustomerFields`
 * ile alan alan aynı). Gizli numaralar (TC, ehliyet no, pasaport no, bireysel caride vergi no) ve portal şifresi bu
 * listede DEĞİL: yalnız yazılır, ayrı kontrollerle yönetilir (`customer-form-model.ts`).
 */

export type FieldKind = 'text' | 'int' | 'money' | 'rate' | 'date' | 'flag' | 'select' | 'tri';

/** KVKK anonimleştirme grubu (kayıtlı `Anonim*` bayrağı işaretliyse kart bu alanları `null` döner). */
export type PrivacyGroup =
  'anonimAd' | 'anonimTelefon' | 'anonimMail' | 'anonimAdres' | 'anonimBelge';

export type SuggestionKind = 'il' | 'ilce' | 'kaynak' | 'sinif';

export interface FieldSpec {
  /** Form kontrolü = API alanı (camelCase). */
  readonly name: string;
  readonly kind: FieldKind;
  /** Kolon sınırı (sunucu `CustomerInputMapper.Texts` ile aynı). */
  readonly maxLength?: number;
  readonly required?: boolean;
  readonly suggest?: SuggestionKind;
  /** `select` sabit seçenekleri (değer = görünen metin; Blazor ile aynı). */
  readonly options?: readonly string[];
  /** `rate` ondalık hane. */
  readonly fraction?: number;
  readonly privacy?: PrivacyGroup;
  readonly wide?: boolean;
  readonly inputType?: 'email' | 'tel';
}

export type SectionId = 'genel' | 'adres' | 'kimlik' | 'finans' | 'tercihler';

export interface SectionSpec {
  readonly id: SectionId;
  readonly fields: readonly FieldSpec[];
}

const t = (name: string, maxLength: number, extra: Partial<FieldSpec> = {}): FieldSpec => ({
  name,
  kind: 'text',
  maxLength,
  ...extra,
});
const d = (name: string, privacy?: PrivacyGroup): FieldSpec => ({ name, kind: 'date', privacy });
const f = (name: string): FieldSpec => ({ name, kind: 'flag' });
const s = (name: string, options: readonly string[]): FieldSpec => ({
  name,
  kind: 'select',
  options,
});
const tri = (name: string): FieldSpec => ({ name, kind: 'tri' });

export const CLASS_SUGGESTIONS = ['Düşük', 'Orta', 'Yüksek', 'VIP', 'Personel', 'Problemli'];

export const SECTIONS: readonly SectionSpec[] = [
  {
    id: 'genel',
    fields: [
      t('ad', 128, { privacy: 'anonimAd' }),
      t('soyad', 128, { privacy: 'anonimAd' }),
      t('unvan', 256, { privacy: 'anonimAd' }),
      t('vergiDairesi', 128),
      t('cepTel', 32, { privacy: 'anonimTelefon', inputType: 'tel' }),
      t('gsm2', 32, { privacy: 'anonimTelefon', inputType: 'tel' }),
      t('tel2', 32, { privacy: 'anonimTelefon', inputType: 'tel' }),
      t('isTelefonu', 32, { privacy: 'anonimTelefon', inputType: 'tel' }),
      t('email', 256, { privacy: 'anonimMail', inputType: 'email' }),
      t('kaynak', 64, { suggest: 'kaynak' }),
      t('musteriTemsilcisi', 128),
      t('sinif', 32, { suggest: 'sinif' }),
      s('ozelCariTip', ['Standart', 'Yurtiçi', 'Yurtdışı', '2.El', 'Grup İçi']),
      s('musteriTipi', ['Türk Ehliyetli', 'Yabancı Ehliyetli', 'Türk-Yabancı Ehliyetli']),
      s('dil', ['TR', 'EN', 'Diğer']),
      s('doviz', ['TL', 'EURO', 'USD']),
      t('ulke', 64),
      t('ozelKod', 64),
      t('entegrasyonKodu', 64),
      t('kurumsalNo', 64),
      t('aciklama', 1024, { wide: true }),
      t('uyariSerbest', 512, { wide: true }),
    ],
  },
  {
    id: 'adres',
    fields: [
      t('il', 64, { suggest: 'il', privacy: 'anonimAdres' }),
      t('ilce', 64, { suggest: 'ilce', privacy: 'anonimAdres' }),
      t('adres', 512, { privacy: 'anonimAdres', wide: true }),
      t('ekAdres', 512, { privacy: 'anonimAdres' }),
      t('isAdresi', 512, { privacy: 'anonimAdres', wide: true }),
      t('faturaAdresi', 512, { privacy: 'anonimAdres' }),
      t('faturaUnvan', 256, { privacy: 'anonimAd' }),
      t('faturaKiralayanIsim', 256, { privacy: 'anonimAd' }),
      t('kayitliIl', 64, { privacy: 'anonimAdres' }),
      t('kayitliIlce', 64, { privacy: 'anonimAdres' }),
      t('mahalleKoy', 128, { privacy: 'anonimAdres' }),
    ],
  },
  {
    id: 'kimlik',
    fields: [
      t('ehliyetSinifi', 16, { privacy: 'anonimBelge' }),
      d('ehliyetTarihi', 'anonimBelge'),
      t('ehliyetYeri', 64, { privacy: 'anonimBelge' }),
      t('ehliyetUlke', 64, { privacy: 'anonimBelge' }),
      t('pasaportYeri', 128, { privacy: 'anonimBelge' }),
      d('pasaportTarihi', 'anonimBelge'),
      d('dogumTarihi'),
      t('dogumYeri', 128, { privacy: 'anonimBelge' }),
      t('babaAdi', 128, { privacy: 'anonimAd' }),
      t('anaAdi', 128, { privacy: 'anonimAd' }),
      t('seriNo', 32, { privacy: 'anonimBelge' }),
      t('ciltNo', 32, { privacy: 'anonimBelge' }),
      t('aileSira', 32, { privacy: 'anonimBelge' }),
      t('siraNo', 32, { privacy: 'anonimBelge' }),
      f('tcDogrulama'),
    ],
  },
  {
    id: 'finans',
    fields: [
      t('tarife', 64),
      { name: 'vadeGun', kind: 'int' },
      { name: 'riskLimiti', kind: 'money' },
      t('riskMesaji', 256),
      d('riskTarihi'),
      t('riskIzin', 64),
      t('hgsYansitmaTuru', 32),
      t('faturaDonemi', 32),
      { name: 'tevkifatOrani', kind: 'rate', fraction: 2 },
      s('tevkifatDurum', ['Serbest', 'Sadece Tevkifatsız', 'Sadece Tevkifatlı']),
      t('tevkifatKodu', 32),
      t('bankaIban', 34),
      t('bankaAdi', 128),
      { name: 'bayiKomisyon', kind: 'rate', fraction: 4 },
      { name: 'webIndirim', kind: 'rate', fraction: 4 },
    ],
  },
  {
    id: 'tercihler',
    fields: [
      f('iysIzinli'),
      f('uyari'),
      t('uyariNedeni', 256),
      f('karaListe'),
      d('karaZamani'),
      f('pasif'),
      tri('mailIzin'),
      tri('smsIzin'),
      tri('telefonIzin'),
      tri('kvkkOnay'),
      d('kvkkOnayTarih'),
      f('faturaAdresFarkli'),
      f('faturaTekSatir'),
      f('dogumGunuTakip'),
      f('bakiyeGor'),
      f('aracVerilmez'),
      f('yasEhliyetSerbest'),
      f('merkezKurumsal'),
      f('broker'),
      f('findexZorunlu'),
    ],
  },
];

export const ALL_FIELDS: readonly FieldSpec[] = SECTIONS.flatMap((x) => x.fields);

/** KVKK anonimleştirme bayrakları (Blazor sırası). Kaldırmak yalnız ManageUsers ile (sunucu 403). */
export const PRIVACY_FLAGS = [
  'anonimAd',
  'anonimTc',
  'anonimTelefon',
  'anonimMail',
  'anonimAdres',
  'anonimBelge',
] as const;
export type PrivacyFlag = (typeof PRIVACY_FLAGS)[number];

/** Kurumsal yetkili kişi sınırı (sunucu `MaxContacts`). */
export const MAX_CONTACTS = 30;
