import {
  type AbstractControl,
  FormArray,
  FormControl,
  FormGroup,
  type ValidatorFn,
  Validators,
} from '@angular/forms';
import type { Subscription } from 'rxjs';
import type { SorguParametreleri } from '@core/api/api-istemcisi';
import { SUNUCU_HATASI } from '@core/form/sunucu-hatalari';
import { type GunMetni, anBirlestir, gunCoz } from '@core/form/tarih-girdisi';
import type { SecimSecenegi } from '@shared/form/arama-secim/secim-kaynagi';
import type {
  AracSecenegi,
  KiraAraci,
  KiraDetayYaniti,
  KiraGuncelleIstegi,
  KiraOlusturIstegi,
  MusaitArac,
  SunucuSayisi,
} from './kira-tipleri';

/*
 * Kira formu (F4.3) SAF modeli: form yapısı, ön doldurma, gövde kurucuları, sorgu sözleşmesi, hash.
 * Burada PARA FORMÜLÜ YOK — tutarlar sunucu motorundan (`hesapla`, `donus-hesapla`, kayıtlı sözleşme)
 * gelir; UI yalnız gösterir. Para alanları invariant metin (`"1234.56"`) ya da sunucunun JSON sayısıdır,
 * sunucuya olduğu gibi gider (kayan noktaya çevrilip yeniden yazılmaz).
 */

// ─── Sekmeler + derin bağlantı ────────────────────────────────────────────────────────────────

/** Ana sekmeler — kimlikler Blazor mega-formuyla AYNI (`#sekme=donus` bağlantıları geçerli kalır). */
export const SEKMELER = [
  'hizli',
  'kira',
  'musteri',
  'arac',
  'fiyat',
  'ekhizmet',
  'ayrintilar',
  'donus',
] as const;
export type SekmeKimligi = (typeof SEKMELER)[number];

/** Ayrıntılar alt sekmeleri (`#sekme=ayrintilar&alt=aksesuar`). */
export const ALT_SEKMELER = [
  'aciklama',
  'finans',
  'suruculer',
  'diger',
  'hizmetalimi',
  'webapi',
  'aksesuar',
  'ekkosullar',
] as const;
export type AltSekmeKimligi = (typeof ALT_SEKMELER)[number];

/** `#sekme=x&alt=y` → `{ sekme: 'x', alt: 'y' }` (bilinmeyen anahtarlar da döner; doğrulama çağıranda). */
export function hashParcala(hash: string): Readonly<Record<string, string>> {
  const sonuc: Record<string, string> = {};
  for (const parca of hash.replace(/^#/, '').split('&')) {
    const i = parca.indexOf('=');
    if (i <= 0) continue;
    try {
      sonuc[parca.slice(0, i)] = decodeURIComponent(parca.slice(i + 1));
    } catch {
      // bozuk yüzde kodlaması: parça yok sayılır
    }
  }
  return sonuc;
}

export function sekmeMi(deger: string | undefined): deger is SekmeKimligi {
  return (SEKMELER as readonly string[]).includes(deger ?? '');
}

export function altSekmeMi(deger: string | undefined): deger is AltSekmeKimligi {
  return (ALT_SEKMELER as readonly string[]).includes(deger ?? '');
}

/**
 * Alt sekme adresi: TAM yol (`pathname + search + '#…'`). Çıplak `#` `<base href="/app/">` altında
 * köke çözülür (Blazor dersi) — yol ve sorgu açıkça korunur.
 */
export function altSekmeAdresi(pathname: string, search: string, alt: AltSekmeKimligi): string {
  return `${pathname}${search}#sekme=ayrintilar&alt=${encodeURIComponent(alt)}`;
}

// ─── Sorgu sözleşmesi (?varac, ?vfrom, ?vto, ?vgrup, ?musteriId) ─────────────────────────────

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const GUN = /^\d{4}-\d{2}-\d{2}$/;

/**
 * Blazor'da kalan müsaitlik (`MusaitlikArama` "Kirala") ve araç durumu (`FleetStatus` "Kirala") bu
 * bağlantıları üretir. Bozuk değer SESSİZCE yok sayılır (boş alan), form yine açılır.
 */
export interface KiraSorgusu {
  readonly varac: string | null;
  readonly vfrom: GunMetni | null;
  readonly vto: GunMetni | null;
  readonly vgrup: string | null;
  readonly musteriId: string | null;
}

export function kiraSorgusuCoz(oku: (ad: string) => string | null): KiraSorgusu {
  const kimlik = (ad: string): string | null => {
    const d = oku(ad)?.trim() ?? '';
    return UUID.test(d) ? d : null;
  };
  const gun = (ad: string): GunMetni | null => {
    const d = oku(ad)?.trim() ?? '';
    if (!GUN.test(d)) return null;
    const cozum = gunCoz(`${d.slice(8, 10)}.${d.slice(5, 7)}.${d.slice(0, 4)}`);
    return cozum === d ? d : null;
  };
  const grup = oku('vgrup')?.trim() ?? '';
  return {
    varac: kimlik('varac'),
    vfrom: gun('vfrom'),
    vto: gun('vto'),
    vgrup: grup === '' ? null : grup.slice(0, 64),
    musteriId: kimlik('musteriId'),
  };
}

export interface MusaitPencere {
  readonly vfrom: GunMetni;
  readonly vto: GunMetni;
  readonly vgrup: string | null;
}

/** Geçerli müsaitlik penceresi (bitiş > başlangıç) — Blazor `MusaitMod` koşulu. */
export function musaitPencere(
  s: Pick<KiraSorgusu, 'vfrom' | 'vto' | 'vgrup'>,
): MusaitPencere | null {
  return s.vfrom && s.vto && s.vto > s.vfrom
    ? { vfrom: s.vfrom, vto: s.vto, vgrup: s.vgrup }
    : null;
}

/** Pencere günlerinden kira tarihleri: İstanbul saatiyle 09:00 (Blazor `BasTarPrefill` ile aynı saat). */
export const VARSAYILAN_SAAT = '09:00';

export function penceredenTarihler(p: MusaitPencere): { basTar: string; bitTar: string } {
  return {
    basTar: anBirlestir(p.vfrom, VARSAYILAN_SAAT),
    bitTar: anBirlestir(p.vto, VARSAYILAN_SAAT),
  };
}

// ─── Form yapısı ──────────────────────────────────────────────────────────────────────────────

type K<T> = FormControl<T | null>;
type Para = string | number;

export interface EkHizmetSatiri {
  tanim: K<SecimSecenegi>;
  miktar: K<number>;
}

/** Hızlı Giriş ve Ayrıntılar'daki AYNA kontroller (kanonik alanın ikinci görünümü; gövdeye girmez). */
export const AYNALI_ALANLAR = [
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
export type AynaliAlan = (typeof AYNALI_ALANLAR)[number];

export interface KiraFormKontrolleri {
  // Yalnız yeni kira (düzenlemede pasif — tarih = Uzat, fiyat = fark faturası)
  musteri: K<SecimSecenegi>;
  arac: K<AracSecenegi>;
  basTar: K<string>;
  bitTar: K<string>;
  fiyatTuru: K<string>;
  gunlukUcret: K<Para>;
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
  provizyon: K<Para>;
  depozito: K<Para>;
  komisyonOran: K<number>;
  komisyonTutar: K<Para>;
  dropUcreti: K<Para>;
  sonraOdeOran: K<number>;
  uyariAciklama: K<string>;
  ozelFaturaAciklama: K<string>;
  faturaListesindeGizle: K<boolean>;
  ucusNo: K<string>;
  provizyonNo: K<string>;
  provizyonTarih: K<GunMetni>;
  onayKodu: K<string>;
  firmaKodu: K<string>;
  projeAdi: K<string>;
  ozelKod: K<string>;
  ozelKdvOran: K<number>;
  damgaVergisi: K<Para>;
  talepTuru: K<string>;
  geldigiBirim: K<string>;
  kefilBilgisi: K<string>;
  assistFirma: K<string>;
  ozelSoforBilgisi: K<string>;
  ekKosullar: K<string>;
  belgeSablonId: K<string>;
  manuelFindexPuan: K<number>;
  opsiyonNet: K<Para>;
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
  fazlaKmUcret: K<Para>;
  yakitBirimUcret: K<Para>;
  teslimEdenPersonel: K<SecimSecenegi>;
  aksYedekAnahtarDonus: K<boolean>;
  aksStepneDonus: K<boolean>;
  aksZincirDonus: K<boolean>;
  aksIlkYardimDonus: K<boolean>;
  aksLastikDonus: K<string>;
  ayna: FormGroup<{ [A in AynaliAlan]: KiraFormKontrolleri[A] }>;
}

export type KiraFormu = FormGroup<KiraFormKontrolleri>;
export type KiraFormDegeri = ReturnType<KiraFormu['getRawValue']>;

/** Yalnız yeni kirada düzenlenir (PUT whitelist'inde yok). */
export const YALNIZ_YENI: readonly (keyof KiraFormKontrolleri)[] = [
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
export const YALNIZ_DUZENLEME: readonly (keyof KiraFormKontrolleri)[] = [
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
export const TAMAMLANMISTA_DONUK: readonly (keyof KiraFormKontrolleri)[] = [
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
export const SUNUCU_ALAN_ESLEMESI: Readonly<Record<string, string>> = {
  musteriId: 'musteri',
  vehicleId: 'arac',
  ikinciSurucuId: 'ikinciSurucu',
  teslimEdenPersonelId: 'teslimEdenPersonel',
};

export type KiraFormModu = 'yeni' | 'duzenle';

export function kiraFormuOlustur(mod: KiraFormModu): KiraFormu {
  const yeni = mod === 'yeni';
  const k = <T>(deger: T | null = null, ...dogrulayicilar: ValidatorFn[]): K<T> =>
    new FormControl<T | null>(deger, dogrulayicilar);
  const metin = (azami: number): K<string> => k<string>(null, Validators.maxLength(azami));
  const zorunluYeni = yeni ? [Validators.required] : [];
  const zorunluDuzenle = yeni ? [] : [Validators.required];

  const kanonik = {
    musteri: k<SecimSecenegi>(null, ...zorunluYeni),
    arac: k<AracSecenegi>(null, ...zorunluYeni),
    basTar: k<string>(null, ...zorunluYeni),
    bitTar: k<string>(null, ...zorunluYeni),
    fiyatTuru: k<string>(),
    gunlukUcret: k<Para>(),
    doviz: k<string>(),
    kampanyaKodu: metin(64),
    riskOnay: k<boolean>(false),
    ekHizmetler: new FormArray<FormGroup<EkHizmetSatiri>>([]),
    cikisOfisi: k<SecimSecenegi>(),
    donusOfisi: k<SecimSecenegi>(),
    ikinciSurucu: k<SecimSecenegi>(),
    ikinciSurucuSerbestAd: metin(64),
    ikinciSurucuSerbestSoyad: metin(64),
    ikinciSurucuSerbestTel: metin(32),
    ikinciSurucuSerbestEhliyetSinifi: metin(16),
    odemeSekli: metin(64),
    aciklama: metin(1024),
    kaynak: metin(64),
    kiralamaTuru: k<string>(),
    donemselFaturalama: k<boolean>(false),
    faturalamaTipi: k<string>(),
    provizyon: k<Para>(),
    depozito: k<Para>(),
    komisyonOran: k<number>(null, Validators.min(0), Validators.max(100)),
    komisyonTutar: k<Para>(),
    dropUcreti: k<Para>(),
    sonraOdeOran: k<number>(null, Validators.min(0), Validators.max(100)),
    uyariAciklama: metin(512),
    ozelFaturaAciklama: metin(512),
    faturaListesindeGizle: k<boolean>(false),
    ucusNo: metin(32),
    provizyonNo: metin(64),
    provizyonTarih: k<GunMetni>(),
    onayKodu: metin(64),
    firmaKodu: metin(64),
    projeAdi: metin(128),
    ozelKod: metin(64),
    ozelKdvOran: k<number>(null, Validators.min(0), Validators.max(1)),
    damgaVergisi: k<Para>(),
    talepTuru: metin(64),
    geldigiBirim: metin(64),
    kefilBilgisi: metin(512),
    assistFirma: metin(128),
    ozelSoforBilgisi: metin(512),
    ekKosullar: metin(2048),
    belgeSablonId: k<string>(),
    manuelFindexPuan: k<number>(null, Validators.min(0)),
    opsiyonNet: k<Para>(),
    opsiyonGun: k<number>(null, Validators.min(0)),
    kabisCikis: k<boolean>(false),
    kabisDonus: k<boolean>(false),
    otomatikUzat: k<boolean>(false),
    aksYedekAnahtarCikis: k<boolean>(false),
    aksStepneCikis: k<boolean>(false),
    aksZincirCikis: k<boolean>(false),
    aksIlkYardimCikis: k<boolean>(false),
    aksLastikCikis: metin(64),
    kmLimit: k<number>(null, ...zorunluDuzenle, Validators.min(0)),
    fazlaKmUcret: k<Para>(null, ...zorunluDuzenle),
    yakitBirimUcret: k<Para>(null, ...zorunluDuzenle),
    teslimEdenPersonel: k<SecimSecenegi>(),
    aksYedekAnahtarDonus: k<boolean>(false),
    aksStepneDonus: k<boolean>(false),
    aksZincirDonus: k<boolean>(false),
    aksIlkYardimDonus: k<boolean>(false),
    aksLastikDonus: metin(64),
  };

  // Ayna aynı doğrulayıcıları taşır: Hızlı Giriş'te de "zorunlu" işareti ve hata görünür.
  const ayna = <A extends AynaliAlan>(ad: A): KiraFormKontrolleri[A] =>
    new FormControl(null, kanonik[ad].validator) as KiraFormKontrolleri[A];
  const aynalar = Object.fromEntries(AYNALI_ALANLAR.map((ad) => [ad, ayna(ad)])) as {
    [A in AynaliAlan]: KiraFormKontrolleri[A];
  };

  const form: KiraFormu = new FormGroup<KiraFormKontrolleri>({
    ...kanonik,
    ayna: new FormGroup(aynalar),
  });
  for (const ad of yeni ? YALNIZ_DUZENLEME : YALNIZ_YENI) form.controls[ad].disable();
  aynaDurumlariniEsitle(form);
  return form;
}

export function ekHizmetSatiri(tanim: SecimSecenegi, miktar = 1): FormGroup<EkHizmetSatiri> {
  return new FormGroup<EkHizmetSatiri>({
    tanim: new FormControl<SecimSecenegi | null>(tanim, Validators.required),
    miktar: new FormControl<number | null>(miktar, [Validators.required, Validators.min(0.01)]),
  });
}

// ─── Aynalar ──────────────────────────────────────────────────────────────────────────────────

/**
 * Kanonik ↔ ayna iki yönlü senkron. Aynı `FormControl`'ü iki girdiye bağlamak Angular'da çalışmaz
 * (görünümden gelen değer öbür erişimciye yazılmaz); ayrı kontrol + `emitEvent: false` döngüsüz eşitler.
 * Gövdeye YALNIZ kanonik girer.
 */
export function aynalariBagla(form: KiraFormu): Subscription[] {
  return AYNALI_ALANLAR.flatMap((ad) => {
    const kanonik = form.controls[ad] as AbstractControl<unknown>;
    const ayna = form.controls.ayna.controls[ad] as AbstractControl<unknown>;
    return [
      kanonik.valueChanges.subscribe((v) => {
        if (!Object.is(ayna.value, v)) ayna.setValue(v, { emitEvent: false });
      }),
      ayna.valueChanges.subscribe((v) => {
        if (!Object.is(kanonik.value, v)) {
          kanonik.setValue(v);
          kanonik.markAsDirty();
        }
      }),
    ];
  });
}

/** Aynaların değer + etkinlik durumunu kanoniğe eşitler (ön doldurma, mod/durum değişimi sonrası). */
export function aynaDurumlariniEsitle(form: KiraFormu): void {
  for (const ad of AYNALI_ALANLAR) {
    const kanonik = form.controls[ad] as AbstractControl<unknown>;
    const ayna = form.controls.ayna.controls[ad] as AbstractControl<unknown>;
    ayna.setValue(kanonik.value, { emitEvent: false });
    if (kanonik.disabled && ayna.enabled) ayna.disable({ emitEvent: false });
    if (kanonik.enabled && ayna.disabled) ayna.enable({ emitEvent: false });
  }
}

/** Sunucu alan hatası kanoniğe yazıldıktan sonra aynasına da (Hızlı Giriş'te de görünsün). */
export function aynalaraHataKopyala(form: KiraFormu): void {
  for (const ad of AYNALI_ALANLAR) {
    const mesajlar: unknown = form.controls[ad].errors?.[SUNUCU_HATASI];
    if (mesajlar === undefined) continue;
    const ayna = form.controls.ayna.controls[ad] as AbstractControl<unknown>;
    ayna.setErrors({ ...(ayna.errors ?? {}), [SUNUCU_HATASI]: mesajlar });
    ayna.markAsTouched();
  }
}

// ─── Ön doldurma ──────────────────────────────────────────────────────────────────────────────

/** Sunucu sayısı → sayı (yalnız gösterim/tam sayı alanları; para alanı olduğu gibi kalır). */
export function sayiya(v: SunucuSayisi): number | null {
  if (v === null || v === undefined || v === '') return null;
  const n = typeof v === 'number' ? v : Number(v);
  return Number.isFinite(n) ? n : null;
}

/** Ofis adı (Location.Ad — sözleşmede METİN saklanır) → seçim öğesi. */
export function ofisSecenegi(ad: string | null | undefined): SecimSecenegi | null {
  const d = ad?.trim() ?? '';
  return d === '' ? null : { id: `ofis:${d}`, etiket: d };
}

export function aracSecenegi(a: KiraAraci | MusaitArac): AracSecenegi {
  const ek = [a.marka, a.tip].filter((x) => x?.trim()).join(' ');
  return { ...a, etiket: ek === '' ? a.plaka : `${a.plaka} — ${ek}` };
}

/** An → UTC takvim günü (Blazor `provizyonTarih` date alanı sunucuda UTC gece yarısı olarak saklanır). */
export function utcGunu(an: string | null | undefined): GunMetni | null {
  if (!an) return null;
  const ms = Date.parse(an);
  return Number.isNaN(ms) ? null : new Date(ms).toISOString().slice(0, 10);
}

/** Takvim günü → UTC gece yarısı anı (tur-döngüsünde gün kaymaz: kaydet → aç → kaydet aynı gün). */
export function gunAnina(gun: GunMetni | null | undefined): string | null {
  return gun ? `${gun}T00:00:00Z` : null;
}

/** Kayıtlı sözleşmeden form değerleri (düzenleme). `ayna` ve `ekHizmetler` ayrı ele alınır. */
export function detaydanDegerler(d: KiraDetayYaniti): Omit<KiraFormDegeri, 'ayna' | 'ekHizmetler'> {
  const k = d.kira;
  return {
    musteri: { id: k.musteriId, etiket: d.musteri.ad },
    arac: d.arac ? aracSecenegi(d.arac) : { id: k.vehicleId, etiket: '—' },
    basTar: k.basTar,
    bitTar: k.bitTar,
    fiyatTuru: k.fiyatTuru,
    gunlukUcret: k.gunlukUcret,
    doviz: k.doviz,
    kampanyaKodu: k.kampanyaKodu,
    riskOnay: k.riskOnay,
    cikisOfisi: ofisSecenegi(k.cikisOfisi),
    donusOfisi: ofisSecenegi(k.donusOfisi),
    ikinciSurucu: d.ikinciSurucu ? { id: d.ikinciSurucu.id, etiket: d.ikinciSurucu.ad } : null,
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
    komisyonOran: sayiya(k.komisyonOran),
    komisyonTutar: k.komisyonTutar ?? null,
    dropUcreti: k.dropUcreti ?? null,
    sonraOdeOran: sayiya(k.sonraOdeOran),
    uyariAciklama: k.uyariAciklama,
    ozelFaturaAciklama: k.ozelFaturaAciklama,
    faturaListesindeGizle: k.faturaListesindeGizle ?? false,
    ucusNo: k.ucusNo,
    provizyonNo: k.provizyonNo,
    provizyonTarih: utcGunu(k.provizyonTarih),
    onayKodu: k.onayKodu,
    firmaKodu: k.firmaKodu,
    projeAdi: k.projeAdi,
    ozelKod: k.ozelKod,
    ozelKdvOran: sayiya(k.ozelKdvOran),
    damgaVergisi: k.damgaVergisi ?? null,
    talepTuru: k.talepTuru,
    geldigiBirim: k.geldigiBirim,
    kefilBilgisi: k.kefilBilgisi,
    assistFirma: k.assistFirma,
    ozelSoforBilgisi: k.ozelSoforBilgisi,
    ekKosullar: k.ekKosullar,
    belgeSablonId: k.belgeSablonId,
    manuelFindexPuan: sayiya(k.manuelFindexPuan),
    opsiyonNet: k.opsiyonNet ?? null,
    opsiyonGun: sayiya(k.opsiyonGun),
    kabisCikis: k.kabisCikis ?? false,
    kabisDonus: k.kabisDonus ?? false,
    otomatikUzat: k.otomatikUzat ?? false,
    aksYedekAnahtarCikis: k.aksYedekAnahtarCikis ?? false,
    aksStepneCikis: k.aksStepneCikis ?? false,
    aksZincirCikis: k.aksZincirCikis ?? false,
    aksIlkYardimCikis: k.aksIlkYardimCikis ?? false,
    aksLastikCikis: k.aksLastikCikis,
    kmLimit: sayiya(k.kmLimit),
    fazlaKmUcret: k.fazlaKmUcret,
    yakitBirimUcret: k.yakitBirimUcret,
    teslimEdenPersonel: k.teslimEdenPersonelId
      ? { id: k.teslimEdenPersonelId, etiket: d.teslimEdenPersonelAd ?? '—' }
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
export function formuSifirla(
  form: KiraFormu,
  degerler: Partial<Omit<KiraFormDegeri, 'ayna' | 'ekHizmetler'>>,
): void {
  const aynalar: Record<string, unknown> = {};
  for (const ad of AYNALI_ALANLAR) aynalar[ad] = degerler[ad] ?? null;
  form.controls.ekHizmetler.clear({ emitEvent: false });
  form.reset({ ...degerler, ayna: aynalar } as Parameters<KiraFormu['reset']>[0]);
  aynaDurumlariniEsitle(form);
  form.markAsPristine();
  form.markAsUntouched();
}

// ─── Gövdeler ─────────────────────────────────────────────────────────────────────────────────

const bos = (v: string | null | undefined): string | null => {
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
function ekSecimleri(d: KiraFormDegeri): { tanimId: string; miktar: number | null }[] {
  return d.ekHizmetler
    .filter((s): s is { tanim: SecimSecenegi; miktar: number | null } => s.tanim !== null)
    .map((s) => ({ tanimId: s.tanim.id, miktar: s.miktar }));
}

/** `POST /kiralar` gövdesi (Blazor `/kiralar/create` whitelist'i; müşteri önce `POST /kiralar/musteri`). */
export function olusturGovdesi(d: KiraFormDegeri): KiraOlusturIstegi {
  return {
    musteriId: zorunlu(d.musteri, 'musteri').id,
    vehicleId: zorunlu(d.arac, 'arac').id,
    basTar: zorunlu(d.basTar, 'basTar'),
    bitTar: zorunlu(d.bitTar, 'bitTar'),
    gunlukUcret: d.gunlukUcret,
    ikinciSurucuId: d.ikinciSurucu?.id ?? null,
    cikisOfisi: d.cikisOfisi?.etiket ?? null,
    donusOfisi: d.donusOfisi?.etiket ?? null,
    aciklama: bos(d.aciklama),
    provizyon: d.provizyon,
    depozito: d.depozito,
    komisyonOran: d.komisyonOran,
    komisyonTutar: d.komisyonTutar,
    dropUcreti: d.dropUcreti,
    sonraOdeOran: d.sonraOdeOran,
    kiralamaTuru: bos(d.kiralamaTuru),
    donemselFaturalama: d.donemselFaturalama ?? false,
    faturalamaTipi: bos(d.faturalamaTipi),
    fiyatTuru: bos(d.fiyatTuru),
    doviz: bos(d.doviz),
    kaynak: bos(d.kaynak),
    kampanyaKodu: bos(d.kampanyaKodu),
    uyariAciklama: bos(d.uyariAciklama),
    ozelFaturaAciklama: bos(d.ozelFaturaAciklama),
    faturaListesindeGizle: d.faturaListesindeGizle ?? false,
    ucusNo: bos(d.ucusNo),
    provizyonNo: bos(d.provizyonNo),
    provizyonTarih: gunAnina(d.provizyonTarih),
    onayKodu: bos(d.onayKodu),
    firmaKodu: bos(d.firmaKodu),
    projeAdi: bos(d.projeAdi),
    ozelKod: bos(d.ozelKod),
    ozelKdvOran: d.ozelKdvOran,
    damgaVergisi: d.damgaVergisi,
    talepTuru: bos(d.talepTuru),
    geldigiBirim: bos(d.geldigiBirim),
    kefilBilgisi: bos(d.kefilBilgisi),
    assistFirma: bos(d.assistFirma),
    ozelSoforBilgisi: bos(d.ozelSoforBilgisi),
    ekKosullar: bos(d.ekKosullar),
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
    aksLastikCikis: bos(d.aksLastikCikis),
    odemeSekli: bos(d.odemeSekli),
    ikinciSurucuSerbestAd: bos(d.ikinciSurucuSerbestAd),
    ikinciSurucuSerbestSoyad: bos(d.ikinciSurucuSerbestSoyad),
    ikinciSurucuSerbestTel: bos(d.ikinciSurucuSerbestTel),
    ikinciSurucuSerbestEhliyetSinifi: bos(d.ikinciSurucuSerbestEhliyetSinifi),
    ekHizmetler: ekSecimleri(d),
  };
}

/**
 * `PUT /kiralar/{id}` gövdesi — TAM DEĞİŞTİRME: 58 alanın HEPSİ (sunucuda `required`; eksik alan 400).
 * Tip `KiraGuncelleIstegi` fazla/eksik anahtara izin vermez. Pasif (donuk) alanlar da kayıtlı değeriyle
 * gider (`getRawValue`), sunucu değişmediğini doğrular.
 */
export function guncelleGovdesi(d: KiraFormDegeri): KiraGuncelleIstegi {
  return {
    cikisOfisi: d.cikisOfisi?.etiket ?? null,
    donusOfisi: d.donusOfisi?.etiket ?? null,
    ikinciSurucuId: d.ikinciSurucu?.id ?? null,
    teslimEdenPersonelId: d.teslimEdenPersonel?.id ?? null,
    odemeSekli: bos(d.odemeSekli),
    ikinciSurucuSerbestAd: bos(d.ikinciSurucuSerbestAd),
    ikinciSurucuSerbestSoyad: bos(d.ikinciSurucuSerbestSoyad),
    ikinciSurucuSerbestTel: bos(d.ikinciSurucuSerbestTel),
    ikinciSurucuSerbestEhliyetSinifi: bos(d.ikinciSurucuSerbestEhliyetSinifi),
    aciklama: bos(d.aciklama),
    kaynak: bos(d.kaynak),
    kiralamaTuru: bos(d.kiralamaTuru),
    donemselFaturalama: d.donemselFaturalama ?? false,
    faturalamaTipi: bos(d.faturalamaTipi),
    kmLimit: zorunlu(d.kmLimit, 'kmLimit'),
    fazlaKmUcret: zorunlu(d.fazlaKmUcret, 'fazlaKmUcret'),
    yakitBirimUcret: zorunlu(d.yakitBirimUcret, 'yakitBirimUcret'),
    provizyon: d.provizyon,
    depozito: d.depozito,
    komisyonOran: d.komisyonOran,
    komisyonTutar: d.komisyonTutar,
    dropUcreti: d.dropUcreti,
    sonraOdeOran: d.sonraOdeOran,
    uyariAciklama: bos(d.uyariAciklama),
    ozelFaturaAciklama: bos(d.ozelFaturaAciklama),
    faturaListesindeGizle: d.faturaListesindeGizle ?? false,
    ucusNo: bos(d.ucusNo),
    provizyonNo: bos(d.provizyonNo),
    provizyonTarih: gunAnina(d.provizyonTarih),
    onayKodu: bos(d.onayKodu),
    firmaKodu: bos(d.firmaKodu),
    projeAdi: bos(d.projeAdi),
    ozelKod: bos(d.ozelKod),
    ozelKdvOran: d.ozelKdvOran,
    damgaVergisi: d.damgaVergisi,
    talepTuru: bos(d.talepTuru),
    geldigiBirim: bos(d.geldigiBirim),
    kefilBilgisi: bos(d.kefilBilgisi),
    assistFirma: bos(d.assistFirma),
    ozelSoforBilgisi: bos(d.ozelSoforBilgisi),
    ekKosullar: bos(d.ekKosullar),
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
    aksLastikCikis: bos(d.aksLastikCikis),
    aksLastikDonus: bos(d.aksLastikDonus),
  };
}

/** Para/sayı sorgu parametresi: invariant metin (sayı `String` ile — JSON sayısını birebir verir). */
function paramDeger(v: Para | null | undefined): string | null {
  if (v === null || v === undefined) return null;
  const d = String(v).trim();
  return d === '' ? null : d;
}

/**
 * Canlı hesap (`GET /kiralar/hesapla`) parametreleri — Blazor `rc-kira-fiyat.js` ile aynı alan kümesi;
 * boş değer gönderilmez. Tarih yoksa `null` (istek atılmaz). `ek` = `tanimId:miktar,…` (nokta ondalık).
 */
export function hesaplaParametreleri(
  d: KiraFormDegeri,
  rentalId: string | null = null,
): SorguParametreleri | null {
  if (!d.basTar || !d.bitTar) return null;
  const ek = ekSecimleri(d)
    .map((s) => `${s.tanimId}:${paramDeger(s.miktar) ?? '1'}`)
    .join(',');
  return {
    basTar: d.basTar,
    bitTar: d.bitTar,
    vehicleId: d.arac?.id ?? null,
    gunlukUcret: paramDeger(d.gunlukUcret),
    fiyatTuru: bos(d.fiyatTuru),
    doviz: bos(d.doviz),
    cikisOfisi: d.cikisOfisi?.etiket ?? null,
    donusOfisi: d.donusOfisi?.etiket ?? null,
    dropUcreti: paramDeger(d.dropUcreti),
    ek: ek === '' ? null : ek,
    musteriId: d.musteri?.id ?? null,
    kampanyaKodu: bos(d.kampanyaKodu),
    ikinciSurucuId: d.ikinciSurucu?.id ?? null,
    rentalId,
  };
}

// ─── Gösterim yardımcıları ────────────────────────────────────────────────────────────────────

/** Kira dövizi (`TL`/`EURO`/`USD`, sunucu `TRY`/`EUR`) → ISO kodu (yalnız simge gösterimi). */
export function isoParaBirimi(doviz: string | null | undefined): string {
  switch ((doviz ?? '').trim()) {
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
export function secenekListesi(
  liste: readonly string[] | undefined,
  mevcut: string | null | undefined,
): { deger: string; etiket: string }[] {
  const sonuc = (liste ?? []).map((x) => ({ deger: x, etiket: x }));
  const m = mevcut?.trim();
  if (m && !sonuc.some((s) => s.deger === m)) sonuc.push({ deger: m, etiket: m });
  return sonuc;
}

/** Sistem ücret kalemi (SYS-*) manuel seçilemez — sunucu da reddeder. */
export function sistemKalemiMi(kod: string | null | undefined): boolean {
  return /^sys-/i.test(kod?.trim() ?? '');
}
