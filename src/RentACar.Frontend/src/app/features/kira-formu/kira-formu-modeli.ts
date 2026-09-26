import {
  type AbstractControl,
  FormArray,
  FormControl,
  FormGroup,
  type ValidatorFn,
  Validators,
} from '@angular/forms';
import type { Subscription } from 'rxjs';
import type { QueryParameters } from '@core/api/api-istemcisi';
import { SERVER_ERROR } from '@core/form/sunucu-hatalari';
import { type DayText, mergeMoment, parseMoment, parseDay } from '@core/form/tarih-girdisi';
import type { SecimSecenegi } from '@shared/form/arama-secim/selection-source';
import type {
  AracSecenegi,
  RentalVehicle,
  RentalDetailResponse,
  UpdateRentalRequest,
  CreateRentalRequest,
  AvailableVehicle,
  ServerNumber,
} from './kira-tipleri';

/*
 * Kira formu (F4.3) SAF modeli: form yapısı, ön doldurma, gövde kurucuları, sorgu sözleşmesi, hash.
 * Burada PARA FORMÜLÜ YOK — tutarlar sunucu motorundan (`hesapla`, `donus-hesapla`, kayıtlı sözleşme)
 * gelir; UI yalnız gösterir. Para alanları invariant metin (`"1234.56"`) ya da sunucunun JSON sayısıdır,
 * sunucuya olduğu gibi gider (kayan noktaya çevrilip yeniden yazılmaz).
 */

// ─── Sekmeler + derin bağlantı ────────────────────────────────────────────────────────────────

/** Ana sekmeler — kimlikler Blazor mega-formuyla AYNI (`#sekme=donus` bağlantıları geçerli kalır). */
export const TABS = [
  'hizli',
  'kira',
  'musteri',
  'arac',
  'fiyat',
  'ekhizmet',
  'ayrintilar',
  'donus',
] as const;
export type TabId = (typeof TABS)[number];

/** Ayrıntılar alt sekmeleri (`#sekme=ayrintilar&alt=aksesuar`). */
export const SUB_TABS = [
  'aciklama',
  'finans',
  'suruculer',
  'diger',
  'hizmetalimi',
  'webapi',
  'aksesuar',
  'ekkosullar',
] as const;
export type SubTabId = (typeof SUB_TABS)[number];

/** `#sekme=x&alt=y` → `{ sekme: 'x', alt: 'y' }` (bilinmeyen anahtarlar da döner; doğrulama çağıranda). */
export function parseHash(hash: string): Readonly<Record<string, string>> {
  const result: Record<string, string> = {};
  for (const part of hash.replace(/^#/, '').split('&')) {
    const i = part.indexOf('=');
    if (i <= 0) continue;
    try {
      result[part.slice(0, i)] = decodeURIComponent(part.slice(i + 1));
    } catch {
      // bozuk yüzde kodlaması: parça yok sayılır
    }
  }
  return result;
}

export function isTab(value: string | undefined): value is TabId {
  return (TABS as readonly string[]).includes(value ?? '');
}

export function isSubTab(value: string | undefined): value is SubTabId {
  return (SUB_TABS as readonly string[]).includes(value ?? '');
}

/**
 * Alt sekme adresi: TAM yol (`pathname + search + '#…'`). Çıplak `#` `<base href="/app/">` altında
 * köke çözülür (Blazor dersi) — yol ve sorgu açıkça korunur.
 */
export function subTabUrl(pathname: string, search: string, sub: SubTabId): string {
  return `${pathname}${search}#sekme=ayrintilar&alt=${encodeURIComponent(sub)}`;
}

// ─── Sorgu sözleşmesi (?varac, ?vfrom, ?vto, ?vgrup, ?musteriId) ─────────────────────────────

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DAY = /^\d{4}-\d{2}-\d{2}$/;

/**
 * Blazor'da kalan müsaitlik (`MusaitlikArama` "Kirala") ve araç durumu (`FleetStatus` "Kirala") bu
 * bağlantıları üretir. Bozuk değer SESSİZCE yok sayılır (boş alan), form yine açılır.
 */
export interface KiraSorgusu {
  readonly varac: string | null;
  readonly vfrom: DayText | null;
  readonly vto: DayText | null;
  readonly vgrup: string | null;
  readonly musteriId: string | null;
}

export function resolveRentalQuery(read: (name: string) => string | null): KiraSorgusu {
  const identity = (name: string): string | null => {
    const d = read(name)?.trim() ?? '';
    return UUID.test(d) ? d : null;
  };
  const day = (name: string): DayText | null => {
    const d = read(name)?.trim() ?? '';
    if (!DAY.test(d)) return null;
    const resolution = parseDay(`${d.slice(8, 10)}.${d.slice(5, 7)}.${d.slice(0, 4)}`);
    return resolution === d ? d : null;
  };
  const group = read('vgrup')?.trim() ?? '';
  return {
    varac: identity('varac'),
    vfrom: day('vfrom'),
    vto: day('vto'),
    vgrup: group === '' ? null : group.slice(0, 64),
    musteriId: identity('musteriId'),
  };
}

export interface MusaitPencere {
  readonly vfrom: DayText;
  readonly vto: DayText;
  readonly vgrup: string | null;
}

/** Geçerli müsaitlik penceresi (bitiş > başlangıç) — Blazor `MusaitMod` koşulu. */
export function availabilityWindow(
  s: Pick<KiraSorgusu, 'vfrom' | 'vto' | 'vgrup'>,
): MusaitPencere | null {
  return s.vfrom && s.vto && s.vto > s.vfrom
    ? { vfrom: s.vfrom, vto: s.vto, vgrup: s.vgrup }
    : null;
}

/** Pencere günlerinden kira tarihleri: İstanbul saatiyle 09:00 (Blazor `BasTarPrefill` ile aynı saat). */
export const DEFAULT_HOUR = '09:00';

export function datesFromWindow(p: MusaitPencere): { basTar: string; bitTar: string } {
  return {
    basTar: mergeMoment(p.vfrom, DEFAULT_HOUR),
    bitTar: mergeMoment(p.vto, DEFAULT_HOUR),
  };
}

// ─── Form yapısı ──────────────────────────────────────────────────────────────────────────────

type K<T> = FormControl<T | null>;
type Money = string | number;

export interface EkHizmetSatiri {
  tanim: K<SecimSecenegi>;
  miktar: K<number>;
}

/** Hızlı Giriş ve Ayrıntılar'daki AYNA kontroller (kanonik alanın ikinci görünümü; gövdeye girmez). */
export const MIRRORED_FIELDS = [
  'musteri',
  'basTar',
  'bitTar',
  'cikisOfisi',
  'donusOfisi',
  'arac',
  'kiralamaTuru',
  'fiyatTuru',
  'doviz',
  'gunlukUcret',
  'kaynak',
  'ikinciSurucu',
  'talepTuru',
  'geldigiBirim',
  'manuelFindexPuan',
  'kefilBilgisi',
] as const;
export type MirroredField = (typeof MIRRORED_FIELDS)[number];

export interface KiraFormKontrolleri {
  // Yalnız yeni kira (düzenlemede pasif — tarih = Uzat, fiyat = fark faturası)
  musteri: K<SecimSecenegi>;
  arac: K<AracSecenegi>;
  basTar: K<string>;
  bitTar: K<string>;
  fiyatTuru: K<string>;
  gunlukUcret: K<Money>;
  doviz: K<string>;
  kampanyaKodu: K<string>;
  riskOnay: K<boolean>;
  ekHizmetler: FormArray<FormGroup<EkHizmetSatiri>>;
  // Ortak (oluştur + PUT)
  cikisOfisi: K<SecimSecenegi>;
  donusOfisi: K<SecimSecenegi>;
  ikinciSurucu: K<SecimSecenegi>;
  ikinciSurucuSerbestAd: K<string>;
  ikinciSurucuSerbestSoyad: K<string>;
  ikinciSurucuSerbestTel: K<string>;
  ikinciSurucuSerbestEhliyetSinifi: K<string>;
  odemeSekli: K<string>;
  aciklama: K<string>;
  kaynak: K<string>;
  kiralamaTuru: K<string>;
  donemselFaturalama: K<boolean>;
  faturalamaTipi: K<string>;
  provizyon: K<Money>;
  depozito: K<Money>;
  komisyonOran: K<number>;
  komisyonTutar: K<Money>;
  dropUcreti: K<Money>;
  sonraOdeOran: K<number>;
  uyariAciklama: K<string>;
  ozelFaturaAciklama: K<string>;
  faturaListesindeGizle: K<boolean>;
  ucusNo: K<string>;
  provizyonNo: K<string>;
  provizyonTarih: K<DayText>;
  onayKodu: K<string>;
  firmaKodu: K<string>;
  projeAdi: K<string>;
  ozelKod: K<string>;
  ozelKdvOran: K<number>;
  damgaVergisi: K<Money>;
  talepTuru: K<string>;
  geldigiBirim: K<string>;
  kefilBilgisi: K<string>;
  assistFirma: K<string>;
  ozelSoforBilgisi: K<string>;
  ekKosullar: K<string>;
  belgeSablonId: K<string>;
  manuelFindexPuan: K<number>;
  opsiyonNet: K<Money>;
  opsiyonGun: K<number>;
  kabisCikis: K<boolean>;
  kabisDonus: K<boolean>;
  otomatikUzat: K<boolean>;
  aksYedekAnahtarCikis: K<boolean>;
  aksStepneCikis: K<boolean>;
  aksZincirCikis: K<boolean>;
  aksIlkYardimCikis: K<boolean>;
  aksLastikCikis: K<string>;
  // Yalnız düzenleme (Blazor create ucu bunları BAĞLAMIYOR — yeni kirada sessizce kaybolmasın diye pasif)
  kmLimit: K<number>;
  fazlaKmUcret: K<Money>;
  yakitBirimUcret: K<Money>;
  teslimEdenPersonel: K<SecimSecenegi>;
  aksYedekAnahtarDonus: K<boolean>;
  aksStepneDonus: K<boolean>;
  aksZincirDonus: K<boolean>;
  aksIlkYardimDonus: K<boolean>;
  aksLastikDonus: K<string>;
  ayna: FormGroup<{ [A in MirroredField]: KiraFormKontrolleri[A] }>;
}

export type RentalForm = FormGroup<KiraFormKontrolleri>;
export type RentalFormValue = ReturnType<RentalForm['getRawValue']>;

/** Yalnız yeni kirada düzenlenir (PUT whitelist'inde yok). */
export const NEW_ONLY: readonly (keyof KiraFormKontrolleri)[] = [
  'musteri',
  'arac',
  'basTar',
  'bitTar',
  'fiyatTuru',
  'gunlukUcret',
  'doviz',
  'kampanyaKodu',
  'riskOnay',
  'ekHizmetler',
];

/** Yalnız kayıtlı kirada düzenlenir (oluşturma gövdesinde yok). */
export const EDIT_ONLY: readonly (keyof KiraFormKontrolleri)[] = [
  'kmLimit',
  'fazlaKmUcret',
  'yakitBirimUcret',
  'teslimEdenPersonel',
  'aksYedekAnahtarDonus',
  'aksStepneDonus',
  'aksZincirDonus',
  'aksIlkYardimDonus',
  'aksLastikDonus',
];

/** Tamamlanmış kirada sunucunun reddettiği değişiklikler (aşım parametreleri, 2. sürücü, ofisler, drop). */
export const FROZEN_WHEN_COMPLETED: readonly (keyof KiraFormKontrolleri)[] = [
  'kmLimit',
  'fazlaKmUcret',
  'yakitBirimUcret',
  'ikinciSurucu',
  'ikinciSurucuSerbestAd',
  'ikinciSurucuSerbestSoyad',
  'ikinciSurucuSerbestTel',
  'ikinciSurucuSerbestEhliyetSinifi',
  'cikisOfisi',
  'donusOfisi',
  'dropUcreti',
];

/** Sunucu alan adı → form yolu (adı farklı olanlar; gerisi büyük/küçük harf duyarsız aynı ad). */
export const SERVER_FIELD_MAPPING: Readonly<Record<string, string>> = {
  musteriId: 'musteri',
  vehicleId: 'arac',
  ikinciSurucuId: 'ikinciSurucu',
  teslimEdenPersonelId: 'teslimEdenPersonel',
};

export type RentalFormMode = 'yeni' | 'duzenle';

export function createRentalForm(mod: RentalFormMode): RentalForm {
  const newItem = mod === 'yeni';
  const k = <T>(value: T | null = null, ...validators: ValidatorFn[]): K<T> =>
    new FormControl<T | null>(value, validators);
  const text = (maximum: number): K<string> => k<string>(null, Validators.maxLength(maximum));
  const requiredNew = newItem ? [Validators.required] : [];
  const requiredEdit = newItem ? [] : [Validators.required];

  const canonical = {
    musteri: k<SecimSecenegi>(null, ...requiredNew),
    arac: k<AracSecenegi>(null, ...requiredNew),
    basTar: k<string>(null, ...requiredNew),
    bitTar: k<string>(null, ...requiredNew),
    fiyatTuru: k<string>(),
    gunlukUcret: k<Money>(),
    doviz: k<string>(),
    kampanyaKodu: text(64),
    riskOnay: k<boolean>(false),
    ekHizmetler: new FormArray<FormGroup<EkHizmetSatiri>>([]),
    cikisOfisi: k<SecimSecenegi>(),
    donusOfisi: k<SecimSecenegi>(),
    ikinciSurucu: k<SecimSecenegi>(),
    ikinciSurucuSerbestAd: text(64),
    ikinciSurucuSerbestSoyad: text(64),
    ikinciSurucuSerbestTel: text(32),
    ikinciSurucuSerbestEhliyetSinifi: text(16),
    odemeSekli: text(64),
    aciklama: text(1024),
    kaynak: text(64),
    kiralamaTuru: k<string>(),
    donemselFaturalama: k<boolean>(false),
    faturalamaTipi: k<string>(),
    provizyon: k<Money>(),
    depozito: k<Money>(),
    komisyonOran: k<number>(null, Validators.min(0), Validators.max(100)),
    komisyonTutar: k<Money>(),
    dropUcreti: k<Money>(),
    sonraOdeOran: k<number>(null, Validators.min(0), Validators.max(100)),
    uyariAciklama: text(512),
    ozelFaturaAciklama: text(512),
    faturaListesindeGizle: k<boolean>(false),
    ucusNo: text(32),
    provizyonNo: text(64),
    provizyonTarih: k<DayText>(),
    onayKodu: text(64),
    firmaKodu: text(64),
    projeAdi: text(128),
    ozelKod: text(64),
    ozelKdvOran: k<number>(null, Validators.min(0), Validators.max(1)),
    damgaVergisi: k<Money>(),
    talepTuru: text(64),
    geldigiBirim: text(64),
    kefilBilgisi: text(512),
    assistFirma: text(128),
    ozelSoforBilgisi: text(512),
    ekKosullar: text(2048),
    belgeSablonId: k<string>(),
    manuelFindexPuan: k<number>(null, Validators.min(0)),
    opsiyonNet: k<Money>(),
    opsiyonGun: k<number>(null, Validators.min(0)),
    kabisCikis: k<boolean>(false),
    kabisDonus: k<boolean>(false),
    otomatikUzat: k<boolean>(false),
    aksYedekAnahtarCikis: k<boolean>(false),
    aksStepneCikis: k<boolean>(false),
    aksZincirCikis: k<boolean>(false),
    aksIlkYardimCikis: k<boolean>(false),
    aksLastikCikis: text(64),
    kmLimit: k<number>(null, ...requiredEdit, Validators.min(0)),
    fazlaKmUcret: k<Money>(null, ...requiredEdit),
    yakitBirimUcret: k<Money>(null, ...requiredEdit),
    teslimEdenPersonel: k<SecimSecenegi>(),
    aksYedekAnahtarDonus: k<boolean>(false),
    aksStepneDonus: k<boolean>(false),
    aksZincirDonus: k<boolean>(false),
    aksIlkYardimDonus: k<boolean>(false),
    aksLastikDonus: text(64),
  };

  // Ayna aynı doğrulayıcıları taşır: Hızlı Giriş'te de "zorunlu" işareti ve hata görünür.
  const mirror = <A extends MirroredField>(name: A): KiraFormKontrolleri[A] =>
    new FormControl(null, canonical[name].validator) as KiraFormKontrolleri[A];
  const mirrors = Object.fromEntries(MIRRORED_FIELDS.map((name) => [name, mirror(name)])) as {
    [A in MirroredField]: KiraFormKontrolleri[A];
  };

  const form: RentalForm = new FormGroup<KiraFormKontrolleri>({
    ...canonical,
    ayna: new FormGroup(mirrors),
  });
  for (const name of newItem ? EDIT_ONLY : NEW_ONLY) form.controls[name].disable();
  syncMirrorStates(form);
  return form;
}

export function addOnRow(definition: SecimSecenegi, quantity = 1): FormGroup<EkHizmetSatiri> {
  return new FormGroup<EkHizmetSatiri>({
    tanim: new FormControl<SecimSecenegi | null>(definition, Validators.required),
    miktar: new FormControl<number | null>(quantity, [Validators.required, Validators.min(0.01)]),
  });
}

// ─── Aynalar ──────────────────────────────────────────────────────────────────────────────────

/**
 * Kanonik ↔ ayna iki yönlü senkron. Aynı `FormControl`'ü iki girdiye bağlamak Angular'da çalışmaz
 * (görünümden gelen değer öbür erişimciye yazılmaz); ayrı kontrol + `emitEvent: false` döngüsüz eşitler.
 * Gövdeye YALNIZ kanonik girer.
 */
export function bindMirrors(form: RentalForm): Subscription[] {
  return MIRRORED_FIELDS.flatMap((name) => {
    const canonical = form.controls[name] as AbstractControl<unknown>;
    const mirror = form.controls.ayna.controls[name] as AbstractControl<unknown>;
    return [
      canonical.valueChanges.subscribe((v) => {
        if (!Object.is(mirror.value, v)) mirror.setValue(v, { emitEvent: false });
      }),
      mirror.valueChanges.subscribe((v) => {
        if (!Object.is(canonical.value, v)) {
          canonical.setValue(v);
          canonical.markAsDirty();
        }
      }),
    ];
  });
}

/** Aynaların değer + etkinlik durumunu kanoniğe eşitler (ön doldurma, mod/durum değişimi sonrası). */
export function syncMirrorStates(form: RentalForm): void {
  for (const name of MIRRORED_FIELDS) {
    const canonical = form.controls[name] as AbstractControl<unknown>;
    const mirror = form.controls.ayna.controls[name] as AbstractControl<unknown>;
    mirror.setValue(canonical.value, { emitEvent: false });
    if (canonical.disabled && mirror.enabled) mirror.disable({ emitEvent: false });
    if (canonical.enabled && mirror.disabled) mirror.enable({ emitEvent: false });
  }
}

/** Sunucu alan hatası kanoniğe yazıldıktan sonra aynasına da (Hızlı Giriş'te de görünsün). */
export function copyErrorToMirrors(form: RentalForm): void {
  for (const name of MIRRORED_FIELDS) {
    const messages: unknown = form.controls[name].errors?.[SERVER_ERROR];
    if (messages === undefined) continue;
    const mirror = form.controls.ayna.controls[name] as AbstractControl<unknown>;
    mirror.setErrors({ ...(mirror.errors ?? {}), [SERVER_ERROR]: messages });
    mirror.markAsTouched();
  }
}

// ─── Ön doldurma ──────────────────────────────────────────────────────────────────────────────

/** Sunucu sayısı → sayı (yalnız gösterim/tam sayı alanları; para alanı olduğu gibi kalır). */
export function toNumber(v: ServerNumber): number | null {
  if (v === null || v === undefined || v === '') return null;
  const n = typeof v === 'number' ? v : Number(v);
  return Number.isFinite(n) ? n : null;
}

/** Ofis adı (Location.Ad — sözleşmede METİN saklanır) → seçim öğesi. */
export function officeOption(name: string | null | undefined): SecimSecenegi | null {
  const d = name?.trim() ?? '';
  return d === '' ? null : { id: `ofis:${d}`, etiket: d };
}

export function vehicleOption(a: RentalVehicle | AvailableVehicle): AracSecenegi {
  const extra = [a.marka, a.tip].filter((x) => x?.trim()).join(' ');
  return { ...a, etiket: extra === '' ? a.plaka : `${a.plaka} — ${extra}` };
}

/**
 * An → İSTANBUL takvim günü (kullanıcının gördüğü gün). Sunucunun yazdığı an (ör. provizyon al: 22:30Z =
 * İstanbul 01:30, ertesi gün) UTC gününe indirilseydi bir gün geri görünürdü (F4.3 adversarial F6).
 */
export function istanbulDay(an: string | null | undefined): DayText | null {
  return parseMoment(an)?.gun ?? null;
}

/**
 * Kullanıcının SEÇTİĞİ takvim günü → UTC gece yarısı anı (Blazor date alanının saklama biçimi; İstanbul günü
 * olarak geri okunduğunda aynı gün: 00:00Z = 03:00 +03 → kaydet → aç → kaydet kayma yok).
 */
export function toDayMoment(day: DayText | null | undefined): string | null {
  return day ? `${day}T00:00:00Z` : null;
}

export type RentalServerValues = Omit<RentalFormValue, 'ayna' | 'ekHizmetler'>;

/**
 * Kayıtlı sözleşmeden form değerleri (düzenleme). `ayna` ve `ekHizmetler` ayrı ele alınır.
 * Kimlikler SÖZLEŞMEDEN okunur, etiket yan tablodan: kayıtlı 2. sürücü carisi silinmiş olsa da kimlik
 * korunur (`kayitYokEtiketi`) — aksi hâlde PUT kimliği null gönderip ek sürücü ücret satırını düşürürdü
 * (F4.3 adversarial F4).
 */
export function valuesFromDetail(d: RentalDetailResponse, noRecordLabel = '—'): RentalServerValues {
  const k = d.kira;
  return {
    musteri: { id: k.musteriId, etiket: d.musteri.ad },
    arac: d.arac ? vehicleOption(d.arac) : { id: k.vehicleId, etiket: '—' },
    basTar: k.basTar,
    bitTar: k.bitTar,
    fiyatTuru: k.fiyatTuru,
    gunlukUcret: k.gunlukUcret,
    doviz: k.doviz,
    kampanyaKodu: k.kampanyaKodu,
    riskOnay: k.riskOnay,
    cikisOfisi: officeOption(k.cikisOfisi),
    donusOfisi: officeOption(k.donusOfisi),
    ikinciSurucu: k.ikinciSurucuId
      ? { id: k.ikinciSurucuId, etiket: d.ikinciSurucu?.ad ?? noRecordLabel }
      : null,
    ikinciSurucuSerbestAd: k.ikinciSurucuSerbestAd,
    ikinciSurucuSerbestSoyad: k.ikinciSurucuSerbestSoyad,
    ikinciSurucuSerbestTel: k.ikinciSurucuSerbestTel,
    ikinciSurucuSerbestEhliyetSinifi: k.ikinciSurucuSerbestEhliyetSinifi,
    odemeSekli: k.odemeSekli,
    aciklama: k.aciklama,
    kaynak: k.kaynak,
    kiralamaTuru: k.kiralamaTuru,
    donemselFaturalama: k.donemselFaturalama,
    faturalamaTipi: k.faturalamaTipi,
    provizyon: k.provizyon ?? null,
    depozito: k.depozito ?? null,
    komisyonOran: toNumber(k.komisyonOran),
    komisyonTutar: k.komisyonTutar ?? null,
    dropUcreti: k.dropUcreti ?? null,
    sonraOdeOran: toNumber(k.sonraOdeOran),
    uyariAciklama: k.uyariAciklama,
    ozelFaturaAciklama: k.ozelFaturaAciklama,
    faturaListesindeGizle: k.faturaListesindeGizle ?? false,
    ucusNo: k.ucusNo,
    provizyonNo: k.provizyonNo,
    provizyonTarih: istanbulDay(k.provizyonTarih),
    onayKodu: k.onayKodu,
    firmaKodu: k.firmaKodu,
    projeAdi: k.projeAdi,
    ozelKod: k.ozelKod,
    ozelKdvOran: toNumber(k.ozelKdvOran),
    damgaVergisi: k.damgaVergisi ?? null,
    talepTuru: k.talepTuru,
    geldigiBirim: k.geldigiBirim,
    kefilBilgisi: k.kefilBilgisi,
    assistFirma: k.assistFirma,
    ozelSoforBilgisi: k.ozelSoforBilgisi,
    ekKosullar: k.ekKosullar,
    belgeSablonId: k.belgeSablonId,
    manuelFindexPuan: toNumber(k.manuelFindexPuan),
    opsiyonNet: k.opsiyonNet ?? null,
    opsiyonGun: toNumber(k.opsiyonGun),
    kabisCikis: k.kabisCikis ?? false,
    kabisDonus: k.kabisDonus ?? false,
    otomatikUzat: k.otomatikUzat ?? false,
    aksYedekAnahtarCikis: k.aksYedekAnahtarCikis ?? false,
    aksStepneCikis: k.aksStepneCikis ?? false,
    aksZincirCikis: k.aksZincirCikis ?? false,
    aksIlkYardimCikis: k.aksIlkYardimCikis ?? false,
    aksLastikCikis: k.aksLastikCikis,
    kmLimit: toNumber(k.kmLimit),
    fazlaKmUcret: k.fazlaKmUcret,
    yakitBirimUcret: k.yakitBirimUcret,
    teslimEdenPersonel: k.teslimEdenPersonelId
      ? { id: k.teslimEdenPersonelId, etiket: d.teslimEdenPersonelAd ?? noRecordLabel }
      : null,
    aksYedekAnahtarDonus: k.aksYedekAnahtarDonus ?? false,
    aksStepneDonus: k.aksStepneDonus ?? false,
    aksZincirDonus: k.aksZincirDonus ?? false,
    aksIlkYardimDonus: k.aksIlkYardimDonus ?? false,
    aksLastikDonus: k.aksLastikDonus,
  };
}

/**
 * Formu verilen değerlere sıfırlar (pristine). Aynalar AYNI değerlerle birlikte sıfırlanır: `reset`
 * değer verilmeyen alt grubu (`ayna`) null'a çeker ve bağlı aynalar bu null'u kanoniğe yazardı —
 * düzenlemede çıkış/dönüş ofisi, kaynak, 2. sürücü… PUT'ta sessizce silinirdi (e2e ile yakalandı).
 */
export function resetForm(
  form: RentalForm,
  values: Partial<Omit<RentalFormValue, 'ayna' | 'ekHizmetler'>>,
): void {
  const mirrors: Record<string, unknown> = {};
  for (const name of MIRRORED_FIELDS) mirrors[name] = values[name] ?? null;
  form.controls.ekHizmetler.clear({ emitEvent: false });
  form.reset({ ...values, ayna: mirrors } as Parameters<RentalForm['reset']>[0]);
  syncMirrorStates(form);
  form.markAsPristine();
  form.markAsUntouched();
}

/** Karşılaştırma anahtarı: seçim öğesi kimliğiyle, gerisi değeriyle (boş = null). */
function valueKey(v: unknown): string {
  if (v === undefined || v === null || v === '') return 'null';
  if (typeof v === 'object' && 'id' in v) return `id:${String((v as { id: unknown }).id)}`;
  return JSON.stringify(v);
}

/**
 * Sunucunun yeni değerlerini KİRLİ formla birleştirir (F4.3 adversarial F2): kullanıcının DOKUNMADIĞI
 * alanlar sunucu değerine çekilir (başka oturumun ya da bir işlemin — provizyon al, teslim — yazdığı
 * değer bayat gövdeyle geri alınmasın); dokunduğu alanlar KORUNUR. Dönüş: kullanıcının da dokunduğu ve
 * sunucuda ÖNCEKİ okumadan bu yana değişmiş alanlar (çakışma — çağıran işaretler).
 */
export function mergeServerValues(
  form: RentalForm,
  newItem: RentalServerValues,
  previous: RentalServerValues | null,
): (keyof RentalServerValues)[] {
  const conflicting: (keyof RentalServerValues)[] = [];
  for (const name of Object.keys(newItem) as (keyof RentalServerValues)[]) {
    const check = form.controls[name] as AbstractControl<unknown>;
    if (check.dirty) {
      if (previous && valueKey(newItem[name]) !== valueKey(previous[name])) conflicting.push(name);
    } else if (valueKey(newItem[name]) !== valueKey(check.value)) {
      check.setValue(newItem[name]);
    }
  }
  syncMirrorStates(form);
  return conflicting;
}

// ─── Gövdeler ─────────────────────────────────────────────────────────────────────────────────

const empty = (v: string | null | undefined): string | null => {
  const d = v?.trim() ?? '';
  return d === '' ? null : d;
};

/** Doğrulayıcının garanti ettiği zorunlu değer; yoksa programlama hatası (sessiz 0 YOK). */
function zorunlu<T>(v: T | null | undefined, alan: string): T {
  if (v === null || v === undefined || v === '') {
    throw new Error(`Kira formu: zorunlu alan boş gönderilemez (${alan}).`);
  }
  return v;
}

/** Ek hizmet seçimi: miktar sunucuya olduğu gibi (≤ 0 → sunucu 1 sayar). */
function extraSelections(d: RentalFormValue): { tanimId: string; miktar: number | null }[] {
  return d.ekHizmetler
    .filter((s): s is { tanim: SecimSecenegi; miktar: number | null } => s.tanim !== null)
    .map((s) => ({ tanimId: s.tanim.id, miktar: s.miktar }));
}

/** `POST /kiralar` gövdesi (Blazor `/kiralar/create` whitelist'i; müşteri önce `POST /kiralar/musteri`). */
export function createBody(d: RentalFormValue): CreateRentalRequest {
  return {
    musteriId: zorunlu(d.musteri, 'musteri').id,
    vehicleId: zorunlu(d.arac, 'arac').id,
    basTar: zorunlu(d.basTar, 'basTar'),
    bitTar: zorunlu(d.bitTar, 'bitTar'),
    gunlukUcret: d.gunlukUcret,
    ikinciSurucuId: d.ikinciSurucu?.id ?? null,
    cikisOfisi: d.cikisOfisi?.etiket ?? null,
    donusOfisi: d.donusOfisi?.etiket ?? null,
    aciklama: empty(d.aciklama),
    provizyon: d.provizyon,
    depozito: d.depozito,
    komisyonOran: d.komisyonOran,
    komisyonTutar: d.komisyonTutar,
    dropUcreti: d.dropUcreti,
    sonraOdeOran: d.sonraOdeOran,
    kiralamaTuru: empty(d.kiralamaTuru),
    donemselFaturalama: d.donemselFaturalama ?? false,
    faturalamaTipi: empty(d.faturalamaTipi),
    fiyatTuru: empty(d.fiyatTuru),
    doviz: empty(d.doviz),
    kaynak: empty(d.kaynak),
    kampanyaKodu: empty(d.kampanyaKodu),
    uyariAciklama: empty(d.uyariAciklama),
    ozelFaturaAciklama: empty(d.ozelFaturaAciklama),
    faturaListesindeGizle: d.faturaListesindeGizle ?? false,
    ucusNo: empty(d.ucusNo),
    provizyonNo: empty(d.provizyonNo),
    provizyonTarih: toDayMoment(d.provizyonTarih),
    onayKodu: empty(d.onayKodu),
    firmaKodu: empty(d.firmaKodu),
    projeAdi: empty(d.projeAdi),
    ozelKod: empty(d.ozelKod),
    ozelKdvOran: d.ozelKdvOran,
    damgaVergisi: d.damgaVergisi,
    talepTuru: empty(d.talepTuru),
    geldigiBirim: empty(d.geldigiBirim),
    kefilBilgisi: empty(d.kefilBilgisi),
    assistFirma: empty(d.assistFirma),
    ozelSoforBilgisi: empty(d.ozelSoforBilgisi),
    ekKosullar: empty(d.ekKosullar),
    belgeSablonId: d.belgeSablonId,
    manuelFindexPuan: d.manuelFindexPuan,
    opsiyonNet: d.opsiyonNet,
    opsiyonGun: d.opsiyonGun,
    riskOnay: d.riskOnay ?? false,
    kabisCikis: d.kabisCikis ?? false,
    kabisDonus: d.kabisDonus ?? false,
    otomatikUzat: d.otomatikUzat ?? false,
    aksYedekAnahtarCikis: d.aksYedekAnahtarCikis ?? false,
    aksStepneCikis: d.aksStepneCikis ?? false,
    aksZincirCikis: d.aksZincirCikis ?? false,
    aksIlkYardimCikis: d.aksIlkYardimCikis ?? false,
    aksLastikCikis: empty(d.aksLastikCikis),
    odemeSekli: empty(d.odemeSekli),
    ikinciSurucuSerbestAd: empty(d.ikinciSurucuSerbestAd),
    ikinciSurucuSerbestSoyad: empty(d.ikinciSurucuSerbestSoyad),
    ikinciSurucuSerbestTel: empty(d.ikinciSurucuSerbestTel),
    ikinciSurucuSerbestEhliyetSinifi: empty(d.ikinciSurucuSerbestEhliyetSinifi),
    ekHizmetler: extraSelections(d),
  };
}

export interface GuncelleBaglami {
  /** Okunan kayıt sürümü (`kira.surum`) — sunucu satır kilidi altında karşılaştırır (F4.3 adversarial F2). */
  readonly surum: string;
  /** Sunucunun kayıtlı provizyon ANI; alana dokunulmadıysa aynen geri gider (gün yuvarlaması yok — F6). */
  readonly provizyonTarihAni: string | null;
  readonly provizyonTarihDegisti: boolean;
}

/**
 * `PUT /kiralar/{id}` gövdesi — TAM DEĞİŞTİRME: 58 alanın HEPSİ + okunan sürüm (sunucuda `required`; eksik
 * alan 400, bayat sürüm 409 `cakisma`). Tip `KiraGuncelleIstegi` fazla/eksik anahtara izin vermez. Pasif
 * (donuk) alanlar da kayıtlı değeriyle gider (`getRawValue`), sunucu değişmediğini doğrular.
 */
export function updateBody(d: RentalFormValue, context: GuncelleBaglami): UpdateRentalRequest {
  return {
    surum: context.surum,
    cikisOfisi: d.cikisOfisi?.etiket ?? null,
    donusOfisi: d.donusOfisi?.etiket ?? null,
    ikinciSurucuId: d.ikinciSurucu?.id ?? null,
    teslimEdenPersonelId: d.teslimEdenPersonel?.id ?? null,
    odemeSekli: empty(d.odemeSekli),
    ikinciSurucuSerbestAd: empty(d.ikinciSurucuSerbestAd),
    ikinciSurucuSerbestSoyad: empty(d.ikinciSurucuSerbestSoyad),
    ikinciSurucuSerbestTel: empty(d.ikinciSurucuSerbestTel),
    ikinciSurucuSerbestEhliyetSinifi: empty(d.ikinciSurucuSerbestEhliyetSinifi),
    aciklama: empty(d.aciklama),
    kaynak: empty(d.kaynak),
    kiralamaTuru: empty(d.kiralamaTuru),
    donemselFaturalama: d.donemselFaturalama ?? false,
    faturalamaTipi: empty(d.faturalamaTipi),
    kmLimit: zorunlu(d.kmLimit, 'kmLimit'),
    fazlaKmUcret: zorunlu(d.fazlaKmUcret, 'fazlaKmUcret'),
    yakitBirimUcret: zorunlu(d.yakitBirimUcret, 'yakitBirimUcret'),
    provizyon: d.provizyon,
    depozito: d.depozito,
    komisyonOran: d.komisyonOran,
    komisyonTutar: d.komisyonTutar,
    dropUcreti: d.dropUcreti,
    sonraOdeOran: d.sonraOdeOran,
    uyariAciklama: empty(d.uyariAciklama),
    ozelFaturaAciklama: empty(d.ozelFaturaAciklama),
    faturaListesindeGizle: d.faturaListesindeGizle ?? false,
    ucusNo: empty(d.ucusNo),
    provizyonNo: empty(d.provizyonNo),
    provizyonTarih: context.provizyonTarihDegisti
      ? toDayMoment(d.provizyonTarih)
      : context.provizyonTarihAni,
    onayKodu: empty(d.onayKodu),
    firmaKodu: empty(d.firmaKodu),
    projeAdi: empty(d.projeAdi),
    ozelKod: empty(d.ozelKod),
    ozelKdvOran: d.ozelKdvOran,
    damgaVergisi: d.damgaVergisi,
    talepTuru: empty(d.talepTuru),
    geldigiBirim: empty(d.geldigiBirim),
    kefilBilgisi: empty(d.kefilBilgisi),
    assistFirma: empty(d.assistFirma),
    ozelSoforBilgisi: empty(d.ozelSoforBilgisi),
    ekKosullar: empty(d.ekKosullar),
    belgeSablonId: d.belgeSablonId,
    manuelFindexPuan: d.manuelFindexPuan,
    opsiyonNet: d.opsiyonNet,
    opsiyonGun: d.opsiyonGun,
    kabisCikis: d.kabisCikis ?? false,
    kabisDonus: d.kabisDonus ?? false,
    otomatikUzat: d.otomatikUzat ?? false,
    aksYedekAnahtarCikis: d.aksYedekAnahtarCikis ?? false,
    aksYedekAnahtarDonus: d.aksYedekAnahtarDonus ?? false,
    aksStepneCikis: d.aksStepneCikis ?? false,
    aksStepneDonus: d.aksStepneDonus ?? false,
    aksZincirCikis: d.aksZincirCikis ?? false,
    aksZincirDonus: d.aksZincirDonus ?? false,
    aksIlkYardimCikis: d.aksIlkYardimCikis ?? false,
    aksIlkYardimDonus: d.aksIlkYardimDonus ?? false,
    aksLastikCikis: empty(d.aksLastikCikis),
    aksLastikDonus: empty(d.aksLastikDonus),
  };
}

/** Para/sayı sorgu parametresi: invariant metin (sayı `String` ile — JSON sayısını birebir verir). */
function paramValue(v: Money | null | undefined): string | null {
  if (v === null || v === undefined) return null;
  const d = String(v).trim();
  return d === '' ? null : d;
}

/**
 * Canlı hesap (`GET /kiralar/hesapla`) parametreleri — Blazor `rc-kira-fiyat.js` ile aynı alan kümesi;
 * boş değer gönderilmez. Tarih yoksa `null` (istek atılmaz). `ek` = `tanimId:miktar,…` (nokta ondalık).
 */
export function calculateParams(
  d: RentalFormValue,
  rentalId: string | null = null,
): QueryParameters | null {
  if (!d.basTar || !d.bitTar) return null;
  const extra = extraSelections(d)
    .map((s) => `${s.tanimId}:${paramValue(s.miktar) ?? '1'}`)
    .join(',');
  return {
    basTar: d.basTar,
    bitTar: d.bitTar,
    vehicleId: d.arac?.id ?? null,
    gunlukUcret: paramValue(d.gunlukUcret),
    fiyatTuru: empty(d.fiyatTuru),
    doviz: empty(d.doviz),
    cikisOfisi: d.cikisOfisi?.etiket ?? null,
    donusOfisi: d.donusOfisi?.etiket ?? null,
    dropUcreti: paramValue(d.dropUcreti),
    ek: extra === '' ? null : extra,
    musteriId: d.musteri?.id ?? null,
    kampanyaKodu: empty(d.kampanyaKodu),
    ikinciSurucuId: d.ikinciSurucu?.id ?? null,
    rentalId,
  };
}

// ─── Gösterim yardımcıları ────────────────────────────────────────────────────────────────────

/** Kira dövizi (`TL`/`EURO`/`USD`, sunucu `TRY`/`EUR`) → ISO kodu (yalnız simge gösterimi). */
export function isoCurrency(currency: string | null | undefined): string {
  switch ((currency ?? '').trim()) {
    case 'EURO':
    case 'EUR':
      return 'EUR';
    case 'USD':
      return 'USD';
    default:
      return 'TRY';
  }
}

/** Sabit liste + kayıtlı değer: listede olmayan eski değer kaybolmasın diye seçeneklere eklenir. */
export function optionList(
  list: readonly string[] | undefined,
  existing: string | null | undefined,
): { deger: string; etiket: string }[] {
  const result = (list ?? []).map((x) => ({ deger: x, etiket: x }));
  const m = existing?.trim();
  if (m && !result.some((s) => s.deger === m)) result.push({ deger: m, etiket: m });
  return result;
}

/** Sistem ücret kalemi (SYS-*) manuel seçilemez — sunucu da reddeder. */
export function isSystemItem(code: string | null | undefined): boolean {
  return /^sys-/i.test(code?.trim() ?? '');
}
