import type { Schema } from '@core/api/ui-tipleri';
import { trSearchKey } from '@core/metin/tr-normalize';

export type SettingsDto = Schema<'SettingsDto'>;
export type SettingsRequest = Schema<'SettingsRequest'>;
export type DomainDto = Schema<'DomainDto'>;
export type SendTestResult = Schema<'SendTestResult'>;

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

/**
 * Sır → "kayıtlı değeri sil" bayrağı (PUT). Boş sır alanı "koru" demektir; kayıtlı sırrı silmek yalnız bu açık bayrakla
 * olur (onay diyaloğundan sonra). Aynı istekte sır alanı doluysa sunucuda dolu değer kazanır.
 */
export const SECRET_CLEAR_FLAGS = {
  eFaturaSifre: 'eFaturaSifreTemizle',
  smsApiKey: 'smsApiKeyTemizle',
  posApiKey: 'posApiKeyTemizle',
  smtpSifre: 'smtpSifreTemizle',
} as const satisfies Readonly<Record<SecretField, keyof SettingsRequest>>;
export type SecretClearFlag = (typeof SECRET_CLEAR_FLAGS)[SecretField];
const CLEAR_FLAG_NAMES: readonly SecretClearFlag[] = Object.values(SECRET_CLEAR_FLAGS);

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

/**
 * Halka açık sitenin "kiralamadan önce" bilgileri (`SiteInfoRules`; boş alan sitede basılmaz). Google puanı elle girilir,
 * doluysa profil bağlantısı zorunlu (sunucu doğrular).
 */
export const SITE_INFO_FIELDS = [
  'siteKurulusYili',
  'siteDepozitoTutari',
  'siteDepozitoAciklama',
  'siteGunlukKmSiniri',
  'siteKmSinirsiz',
  'siteMinYas',
  'siteMinEhliyetYili',
  'siteSigortaOzeti',
  'siteIptalKosulu',
  'siteGooglePuani',
  'siteGoogleYorumSayisi',
  'siteGoogleProfilUrl',
] as const satisfies readonly (keyof SettingsRequest)[];

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
  'firmaIl',
  'firmaIlce',
  'eFaturaEntegrator',
  'eFaturaKullanici',
  'eFaturaSifre',
  'eFaturaGondericiEtiket',
  'eFaturaPostaKutusuEtiket',
  'eFaturaSenaryo',
  'eFaturaCanliOrtam',
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
  'siteVurguRengi',
  'siteTemasi',
  'sitePaleti',
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
  ...SITE_INFO_FIELDS,
] as const satisfies readonly (keyof SettingsRequest)[];

export type SettingsField = (typeof SETTINGS_FIELDS)[number];

/** Sunucunun zorunlu `bool` alanları (null gönderilemez). */
const REQUIRED_BOOLEANS: readonly SettingsField[] = [
  'kurElleGirisKilitli',
  'donemselFaturalamaJob',
  'donemselOtomatikTahsilat',
  'eFaturaCanliOrtam',
  'siteKmSinirsiz',
];

/** e-Fatura senaryoları (`EInvoiceReadiness.Scenarios`); boş = TEMELFATURA. e-Arşiv alıcıda senaryo otomatik. */
export const EINVOICE_SCENARIOS = ['TEMELFATURA', 'TICARIFATURA'] as const;

export type EInvoiceIntegratorOption = Schema<'EInvoiceIntegratorOptionDto'>;

/**
 * Entegratör seçenekleri: sunucuda kayıtlı adaptörler + (listede yoksa) kayıtlı değer — adaptör sonradan kaldırılmış
 * olsa da seçim formdan sessizce düşmesin; sunucu değişmeyen değeri geri kabul eder, durum satırı "bağlı değil" der.
 */
export function integratorOptions(
  dto: SettingsDto | null,
): readonly { deger: string; etiket: string }[] {
  const list = (dto?.eFaturaEntegratorSecenekleri ?? []).map((o) => ({
    deger: o.anahtar,
    etiket: o.ad,
  }));
  const stored = dto?.eFaturaEntegrator;
  if (stored && !list.some((o) => o.deger === stored)) list.push({ deger: stored, etiket: stored });
  return list;
}

/** `FiyatTuruSecenek.Hepsi` (sunucu listesi; sıra canlı parite sırası). */
export const PRICE_TYPES = [
  'Otomatik',
  'KDV Dahil Günlük',
  'Günlük',
  'KDV Dahil Toplam',
  'Toplam',
] as const;

/**
 * Halka açık site temaları (`SiteThemeKeys.All`; sunucu yalnız bunları saklar). Boş = varsayılan (anahtar, ilk
 * eleman); formda boş seçenek yok, yüklenen null değer "anahtar" gibi gösterilir.
 */
export const SITE_THEMES = ['anahtar', 'kiyas', 'paket'] as const;
export type SiteTheme = (typeof SITE_THEMES)[number];

export type SitePaletteOption = Schema<'SitePaletteOptionDto'>;

/** Kesin `#rrggbb` (sunucunun ve sitenin kuralı); başka her şey site önizlemesinde yok sayılır. */
const HEX_COLOR = /^#[0-9a-fA-F]{6}$/;

export function isHexColor(value: unknown): value is string {
  return typeof value === 'string' && HEX_COLOR.test(value);
}

/** Eski anahtar → yeni (`SiteThemeKeys.LegacyAliases`; sunucu da aynı eşlemeyle normalize eder). */
const LEGACY_SITE_THEMES: Readonly<Partial<Record<string, SiteTheme>>> = {
  pazar: 'anahtar',
  tarife: 'anahtar',
  rota: 'kiyas',
  kontuar: 'kiyas',
  vitrin: 'paket',
};

/**
 * Sunucudaki tema değeri → radyo seçimi: boş → varsayılan (site onu basıyor), eski anahtar → yeni karşılığı
 * (göç öncesi okunmuş ya da elle yazılmış değer de seçili görünsün). Tanınmayan değer aynen kalır.
 */
export function siteThemeFormValue(value: unknown): unknown {
  if (value === null || value === undefined || value === '') return SITE_THEMES[0];
  if (typeof value !== 'string') return value;
  // Anahtarlar ASCII: "TARIFE"/"VITRIN" Türkçe küçültmede ı'ya düşer; arama anahtarı onu i'ye çevirir
  // (sunucunun ToLowerInvariant'ıyla aynı sonuç).
  const key = trSearchKey(value);
  if ((SITE_THEMES as readonly string[]).includes(key)) return key;
  return LEGACY_SITE_THEMES[key] ?? value;
}

/**
 * Temanın palet setleri (sunucu kataloğu `sitePaletSecenekleri`; ilk = varsayılan). Boş tema = varsayılan tema
 * (`SITE_THEMES[0]`). Katalog henüz yüklenmediyse boş liste.
 */
export function palettesFor(
  dto: SettingsDto | null,
  theme: string | null | undefined,
): readonly SitePaletteOption[] {
  const key = theme || SITE_THEMES[0];
  return dto?.sitePaletSecenekleri?.find((t) => t.tema === key)?.paletler ?? [];
}

/**
 * Tema değişince palet seçimi: yeni temanın setlerinden biriyse korunur, değilse TEMİZLENİR (`null` = temanın
 * varsayılan seti) — sunucu temaya ait olmayan seti 400 `errors.sitePaleti` ile reddeder. Katalog yoksa dokunulmaz.
 */
export function paletteAfterThemeChange(
  current: unknown,
  palettes: readonly SitePaletteOption[],
): string | null {
  if (typeof current !== 'string' || current === '') return null;
  if (palettes.length === 0) return current;
  return palettes.some((p) => p.anahtar === current) ? current : null;
}

/**
 * Önizleme adresi: firmanın sitesinde `?tema=` + (seçiliyse) `&palet=` + (geçerliyse) `&renk=%23rrggbb` — kalıcı
 * değil, arama motoruna kapalı. Şema-göreli (`//host`): ERP ile site aynı şemayla (üretimde https) yayında; mutlak
 * http(s) adres SPA'da lint ile yasak. Biçimsiz renk gönderilmez (site zaten yok sayar).
 */
export function themePreviewUrl(
  host: string,
  theme: string,
  palette?: string | null,
  color?: unknown,
): string {
  let url = `//${host}/?tema=${encodeURIComponent(theme)}`;
  if (palette) url += `&palet=${encodeURIComponent(palette)}`;
  if (isHexColor(color)) url += `&renk=${encodeURIComponent(color)}`;
  return url;
}

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
  for (const flag of CLEAR_FLAG_NAMES) body[flag] = value[flag] === true;
  body['surum'] = version ?? null;
  return body;
}

/** GET yanıtı → form değerleri (sır alanları DAİMA boş, silme bayrakları DAİMA kapalı). */
export function settingsFormValue(
  dto: SettingsDto,
): Record<SettingsField | SecretClearFlag, unknown> {
  const source = dto as unknown as Readonly<Record<string, unknown>>;
  const out = {} as Record<SettingsField | SecretClearFlag, unknown>;
  for (const name of SETTINGS_FIELDS) {
    out[name] = (SECRET_FIELDS as readonly string[]).includes(name) ? null : (source[name] ?? null);
  }
  for (const flag of CLEAR_FLAG_NAMES) out[flag] = false;
  // Kayıtlı tema yoksa site varsayılanı (anahtar) basıyor: radyo da onu seçili göstersin; eski anahtar yeni karşılığı.
  out.siteTemasi = siteThemeFormValue(out.siteTemasi);
  return out;
}

/** 409/işlem sonrası birleştirme için sunucu değerleri: sır alanları ve silme bayrakları HİÇ yok (kullanıcının girdisi). */
export function settingsServerValues(dto: SettingsDto): Record<string, unknown> {
  const out: Record<string, unknown> = { ...settingsFormValue(dto) };
  for (const s of SECRET_FIELDS) delete out[s];
  for (const flag of CLEAR_FLAG_NAMES) delete out[flag];
  return out;
}

/** Silme bayrağı işaretli sırlar (onay diyaloğu için). */
export function secretsToClear(value: Readonly<Record<string, unknown>>): SecretField[] {
  return SECRET_FIELDS.filter((f) => value[SECRET_CLEAR_FLAGS[f]] === true);
}

/** Bekleyen özel alan adı: DNS'e eklenecek TXT kaydı var mı (yalnız kiracının kendi satırı için döner). */
export function needsTxtRecord(d: DomainDto): boolean {
  return !!d.dogrulamaKaydi && !!d.dogrulamaDegeri;
}
