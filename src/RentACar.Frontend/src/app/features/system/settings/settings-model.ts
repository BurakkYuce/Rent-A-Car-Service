import type { Sema } from '@core/api/ui-tipleri';

export type SettingsDto = Sema<'SettingsDto'>;
export type SettingsRequest = Sema<'SettingsRequest'>;
export type DomainDto = Sema<'DomainDto'>;
export type SendTestResult = Sema<'SendTestResult'>;

/**
 * Yalnız YAZILABİLİR sır alanları: hiçbir yanıtta dönmez (yalnız `*Tanimli`), formda boş = sunucudaki değer korunur
 * (`TenantSettingsService.Secret`). Tarayıcı deposuna yazılmaz; kayıttan sonra formdan silinir.
 */
export const SECRET_FIELDS = ['eFaturaSifre', 'smsApiKey', 'posApiKey', 'smtpSifre'] as const;
export type SecretField = (typeof SECRET_FIELDS)[number];

/** Sır → "tanımlı mı" bayrağı (GET yanıtı). */
export const SECRET_FLAGS: Readonly<Record<SecretField, keyof SettingsDto>> = {
  eFaturaSifre: 'eFaturaSifreTanimli',
  smsApiKey: 'smsApiKeyTanimli',
  posApiKey: 'posApiKeyTanimli',
  smtpSifre: 'smtpSifreTanimli',
};

/** Renk kodu alanları (boş = koddaki varsayılan renk; dolu ise `#rrggbb`, sunucu doğrular). */
export const COLOR_FIELDS = [
  'renkGecikenler',
  'renkBugunDonecekler',
  'renkBugunCikacaklar',
  'renkOpsiyonlu',
  'renkLimitBakiye',
  'renkAlacakli',
  'renkRezAtananPlaka',
  'renkKiralanmayan',
] as const;

/** Tam değiştirme PUT'unun alanları (`SettingsRequest`, `surum` hariç) — form bu listeden kurulur. */
export const SETTINGS_FIELDS = [
  'firmaUnvan',
  'firmaMarka',
  'firmaVergiDairesi',
  'firmaVergiNo',
  'firmaAdres',
  'firmaTel',
  'firmaMobilTel',
  'firmaEmail',
  'eFaturaKullanici',
  'eFaturaSifre',
  'smsBaslik',
  'smsApiKey',
  'posMerchantId',
  'posApiKey',
  'logoUrl',
  'varsayilanDoviz',
  'varsayilanKdvOrani',
  'varsayilanGrupId',
  'minKiraGun',
  'maxKiraGun',
  'rezOnayZorunlu',
  'donemselFaturalamaJob',
  'donemselOtomatikTahsilat',
  'varsayilanFiyatTuru',
  'varsayilanYakitSeviyesi',
  'kurElleGirisKilitli',
  'dropMesafeYokIseSifir',
  'saatFarkiToleransDk',
  'iadeIslemSaatSiniri',
  ...COLOR_FIELDS,
  'smtpHost',
  'smtpPort',
  'smtpKullanici',
  'smtpSifre',
  'smtpGonderenAdres',
  'smtpGonderenAd',
  'smtpSsl',
  'faturaSeriKodu',
  'whatsAppNumarasi',
  'whatsAppGunlukOzet',
] as const satisfies readonly (keyof SettingsRequest)[];

export type SettingsField = (typeof SETTINGS_FIELDS)[number];

/** Sunucunun zorunlu `bool` alanları (null gönderilemez). */
const REQUIRED_BOOLEANS: readonly SettingsField[] = [
  'kurElleGirisKilitli',
  'donemselFaturalamaJob',
  'donemselOtomatikTahsilat',
];

/** `FiyatTuruSecenek.Hepsi` (sunucu listesi; sıra canlı parite sırası). */
export const PRICE_TYPES = [
  'Otomatik',
  'KDV Dahil Günlük',
  'Günlük',
  'KDV Dahil Toplam',
  'Toplam',
] as const;

/** `SmtpEndpointGuard.AllowedPorts` (sunucu yalnız bunları kabul eder). */
export const SMTP_PORTS = [25, 465, 587, 2525] as const;

/**
 * Form değeri → PUT gövdesi. Sır alanı yalnız YAZILDIYSA gider (boş/boşluk → `null` = sunucuda korunur); diğer alanlar
 * aynen (tam değiştirme). `surum` ayar satırı varsa zorunludur, yoksa (ilk kayıt) `null`.
 */
export function settingsBody(
  value: Readonly<Record<string, unknown>>,
  version: string | null | undefined,
): Record<string, unknown> {
  const body: Record<string, unknown> = {};
  for (const name of SETTINGS_FIELDS) {
    const v = value[name] ?? null;
    if ((SECRET_FIELDS as readonly string[]).includes(name)) {
      body[name] = typeof v === 'string' && v.trim() !== '' ? v : null;
    } else if (REQUIRED_BOOLEANS.includes(name)) {
      body[name] = v === true;
    } else {
      body[name] = v;
    }
  }
  body['surum'] = version ?? null;
  return body;
}

/** GET yanıtı → form değerleri (sır alanları DAİMA boş). */
export function settingsFormValue(dto: SettingsDto): Record<SettingsField, unknown> {
  const source = dto as unknown as Readonly<Record<string, unknown>>;
  const out = {} as Record<SettingsField, unknown>;
  for (const name of SETTINGS_FIELDS) {
    out[name] = (SECRET_FIELDS as readonly string[]).includes(name) ? null : (source[name] ?? null);
  }
  return out;
}

/** 409/işlem sonrası birleştirme için sunucu değerleri: sır alanları HİÇ yok (yanıtta yoklar; yazılan sır korunur). */
export function settingsServerValues(dto: SettingsDto): Record<string, unknown> {
  const out: Record<string, unknown> = { ...settingsFormValue(dto) };
  for (const s of SECRET_FIELDS) delete out[s];
  return out;
}

/** Bekleyen özel alan adı: DNS'e eklenecek TXT kaydı var mı (yalnız kiracının kendi satırı için döner). */
export function needsTxtRecord(d: DomainDto): boolean {
  return !!d.dogrulamaKaydi && !!d.dogrulamaDegeri;
}
