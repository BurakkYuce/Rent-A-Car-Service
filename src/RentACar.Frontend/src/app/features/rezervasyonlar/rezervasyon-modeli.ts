import { FormControl, FormGroup, Validators, type AbstractControl } from '@angular/forms';
import type { Sema } from '@core/api/ui-tipleri';
import { anBirlestir, bugun, gunEkle } from '@core/form/tarih-girdisi';
import type { SecimSecenegi } from '@shared/form/arama-secim/secim-kaynagi';

/** F5.1 rezervasyon uçlarının sözleşme tipleri (`docs/api/ui-v1.json`'dan üretilen şemaların takma adları). */
export type RezervasyonListeSatiri = Sema<'RezervasyonListeSatiri'>;
export type RezervasyonDto = Sema<'RezervasyonDto'>;
export type RezervasyonDetayYaniti = Sema<'RezervasyonDetayYaniti'>;
export type RezervasyonIstegi = Sema<'RezervasyonIstegi'>;
export type RezervasyonGuncelleIstegi = Sema<'RezervasyonGuncelleIstegi'>;
export type RezervasyonOlusturYaniti = Sema<'RezervasyonOlusturYaniti'>;
export type KirayaCevirYaniti = Sema<'KirayaCevirYaniti'>;
export type RezervasyonFormSecenekleri = Sema<'RezervasyonFormSecenekleri'>;

export const REZERVASYON_KOKU = '/api/ui/v1/rezervasyonlar';

/** Sunucu enum ADLARI (`ReservationStatus`) — API tanımsız adı 400'ler. */
export const REZERVASYON_DURUMLARI = ['Rezerv', 'Onayli', 'KirayaCevrildi', 'Iptal'] as const;
export type RezervasyonDurumu = (typeof REZERVASYON_DURUMLARI)[number];

export function rezervasyonDurumuMu(d: string): d is RezervasyonDurumu {
  return (REZERVASYON_DURUMLARI as readonly string[]).includes(d);
}

const DURUM_ROZETI: Readonly<Record<RezervasyonDurumu, string>> = {
  Rezerv: 'rc-rozet--uyari',
  Onayli: 'rc-rozet--bilgi',
  KirayaCevrildi: 'rc-rozet--basari',
  Iptal: 'rc-rozet--hata',
};

export function durumRozeti(durum: string): string {
  return `rc-rozet ${rezervasyonDurumuMu(durum) ? DURUM_ROZETI[durum] : ''}`.trim();
}

/** Para alanı değeri: `rc-para-girdisi` invariant metin (`"1234.56"`) ya da sunucudan gelen JSON sayısı. */
type Para = string | number | null;

/** Brokerden gelen bilgi (FAZ 4.5; fiyata/deftere GİRMEZ) — Blazor `BrokerBilgisiPanel` sırası. */
export const OTA_ALANLARI = [
  'otaKiraBedeli',
  'otaDropBedeli',
  'otaBebekKoltugu',
  'otaNavigasyon',
  'otaLcf',
  'otaCdw',
  'otaScdw',
  'otaEkSurucu',
] as const;

/** Ödeme/komisyon (bilgi; deftere/bakiyeye yansımaz) — Blazor oluştur formu sırası. */
export const ODEME_PARA_ALANLARI = [
  'provizyon',
  'depozito',
  'komisyonTutar',
  'dropUcreti',
] as const;

const para = () => new FormControl<Para>(null);
const metin = (azami: number) => new FormControl<string | null>(null, Validators.maxLength(azami));
const secim = (zorunlu = false) =>
  new FormControl<SecimSecenegi | null>(null, zorunlu ? Validators.required : []);
const oran = () => new FormControl<number | null>(null, [Validators.min(0), Validators.max(100)]);

/**
 * Rezervasyon formu — alan kümesi `RezervasyonIstegi` ile BİREBİR (sunucu whitelist'i). Oluştur ve düzenle
 * aynı formu kullanır: PUT tam değiştirmedir, gövdede olmayan alan boş yazılır; bu yüzden düzenlemede de
 * TÜM alanlar formda ve sunucu değeriyle dolu gider (Blazor düzenle formunda olmayan ofis/ödeme alanları dahil).
 */
export function rezervasyonFormuOlustur() {
  return new FormGroup({
    musteri: secim(true),
    arac: secim(true),
    basTar: new FormControl<string | null>(null, Validators.required),
    bitTar: new FormControl<string | null>(null, Validators.required),
    gunlukUcret: para(),
    fiyatTuru: new FormControl<string | null>(null),
    kampanyaKodu: metin(64),
    cikisOfisi: secim(),
    donusOfisi: secim(),
    kaynak: secim(),
    talepTuru: metin(64),
    geldigiBirim: metin(64),
    onayKodu: metin(64),
    projeAdi: metin(128),
    kmLimit: new FormControl<number | null>(null, [Validators.min(0), Validators.max(10_000_000)]),
    fazlaKmUcret: para(),
    yakitBirimUcret: para(),
    provizyon: para(),
    depozito: para(),
    komisyonOran: oran(),
    komisyonTutar: para(),
    dropUcreti: para(),
    sonraOdeOran: oran(),
    otaKiraBedeli: para(),
    otaDropBedeli: para(),
    otaBebekKoltugu: para(),
    otaNavigasyon: para(),
    otaLcf: para(),
    otaCdw: para(),
    otaScdw: para(),
    otaEkSurucu: para(),
    aciklama: metin(1024),
  });
}

export type RezervasyonFormu = ReturnType<typeof rezervasyonFormuOlustur>;
export type RezervasyonFormDegeri = ReturnType<RezervasyonFormu['getRawValue']>;
export type RezervasyonAlani = keyof RezervasyonFormDegeri;

export const VARSAYILAN_SAAT = '09:00';

/**
 * Yeni rezervasyonun ön tarihleri (Blazor: bugün 09:00 → +3 gün 09:00). Bugünün 09:00'u geçtiyse yarın 09:00:
 * sunucu geçmiş başlangıcı reddeder ("Rezervasyon geçmiş tarihe…"), dolu gelen formun ilk kaydı hata vermesin.
 */
export function varsayilanTarihler(simdi: Date = new Date()): { basTar: string; bitTar: string } {
  let gun = bugun(simdi);
  if (Date.parse(anBirlestir(gun, VARSAYILAN_SAAT)) <= simdi.getTime()) gun = gunEkle(gun, 1);
  return {
    basTar: anBirlestir(gun, VARSAYILAN_SAAT),
    bitTar: anBirlestir(gunEkle(gun, 3), VARSAYILAN_SAAT),
  };
}

/** Sunucu sayısı (`number | string`) → form sayısı (yalnız tamsayı/oran alanları; para metin kalır). */
export function sayiya(v: number | string | null | undefined): number | null {
  if (typeof v === 'number') return Number.isFinite(v) ? v : null;
  if (typeof v === 'string' && v.trim() !== '') {
    const n = Number(v);
    return Number.isFinite(n) ? n : null;
  }
  return null;
}

/** Ad → seçim değeri (ofis/kaynak sunucuda METİN tutulur; kimlik = ad). */
export function adSecenegi(ad: string | null | undefined): SecimSecenegi | null {
  const d = ad?.trim() ?? '';
  return d === '' ? null : { id: d, etiket: d };
}

/** Fiyat türü ön-seçimi (Blazor A5-B2): kayıt boş ama kampanya kodu doluysa "Otomatik" (kod yalnız Otomatik'te). */
function fiyatTuruDegeri(r: RezervasyonDto): string | null {
  if (r.fiyatTuru) return r.fiyatTuru;
  return r.kampanyaKodu ? 'Otomatik' : null;
}

/** Detay yanıtı → form değerleri (düzenleme; temiz forma `reset`, kirli forma `sunucuDegerleriniBirlestir`). */
export function detaydanDegerler(d: RezervasyonDetayYaniti): RezervasyonFormDegeri {
  const r = d.rezervasyon;
  return {
    musteri: { id: r.musteriId, etiket: d.musteriAd },
    arac: { id: r.vehicleId, etiket: d.plaka },
    basTar: r.basTar,
    bitTar: r.bitTar,
    gunlukUcret: r.gunlukUcret,
    fiyatTuru: fiyatTuruDegeri(r),
    kampanyaKodu: r.kampanyaKodu,
    cikisOfisi: adSecenegi(r.cikisOfisi),
    donusOfisi: adSecenegi(r.donusOfisi),
    kaynak: adSecenegi(r.kaynak),
    talepTuru: r.talepTuru,
    geldigiBirim: r.geldigiBirim,
    onayKodu: r.onayKodu,
    projeAdi: r.projeAdi,
    kmLimit: sayiya(r.kmLimit),
    fazlaKmUcret: r.fazlaKmUcret,
    yakitBirimUcret: r.yakitBirimUcret,
    provizyon: r.provizyon,
    depozito: r.depozito,
    komisyonOran: sayiya(r.komisyonOran),
    komisyonTutar: r.komisyonTutar,
    dropUcreti: r.dropUcreti,
    sonraOdeOran: sayiya(r.sonraOdeOran),
    otaKiraBedeli: r.otaKiraBedeli,
    otaDropBedeli: r.otaDropBedeli,
    otaBebekKoltugu: r.otaBebekKoltugu,
    otaNavigasyon: r.otaNavigasyon,
    otaLcf: r.otaLcf,
    otaCdw: r.otaCdw,
    otaScdw: r.otaScdw,
    otaEkSurucu: r.otaEkSurucu,
    aciklama: r.aciklama,
  };
}

const bosMu = (v: unknown) => v === null || v === undefined || v === '';
const metinDegeri = (v: string | null): string | null => {
  const d = v?.trim() ?? '';
  return d === '' ? null : d;
};
const paraDegeri = (v: Para): Para => (bosMu(v) ? null : v);

/** Doğrulayıcının garanti ettiği zorunlu değer; yoksa programlama hatası (sessiz boş gövde YOK). */
function zorunlu<T>(v: T | null | undefined, alan: string): T {
  if (v === null || v === undefined || v === '') {
    throw new Error(`Rezervasyon formu: zorunlu alan boş gönderilemez (${alan}).`);
  }
  return v;
}

/**
 * `POST /rezervasyonlar` ve `PUT /rezervasyonlar/{id}` gövdesi — sunucu whitelist'inin TAMAMI (PUT tam
 * değiştirme). Para alanları `rc-para-girdisi`'nin invariant metni (ya da dokunulmamış sunucu sayısı) olarak
 * AYNEN gider; kayan noktaya çevrilmez. Ofis/kaynak sunucuda ad olarak tutulur (seçimin etiketi).
 */
export function rezervasyonGovdesi(d: RezervasyonFormDegeri): RezervasyonIstegi {
  return {
    musteriId: zorunlu(d.musteri?.id, 'musteri'),
    vehicleId: zorunlu(d.arac?.id, 'arac'),
    basTar: zorunlu(d.basTar, 'basTar'),
    bitTar: zorunlu(d.bitTar, 'bitTar'),
    gunlukUcret: paraDegeri(d.gunlukUcret),
    fiyatTuru: metinDegeri(d.fiyatTuru),
    kampanyaKodu: metinDegeri(d.kampanyaKodu),
    cikisOfisi: d.cikisOfisi?.etiket ?? null,
    donusOfisi: d.donusOfisi?.etiket ?? null,
    kaynak: d.kaynak?.etiket ?? null,
    aciklama: metinDegeri(d.aciklama),
    kmLimit: d.kmLimit,
    fazlaKmUcret: paraDegeri(d.fazlaKmUcret),
    yakitBirimUcret: paraDegeri(d.yakitBirimUcret),
    provizyon: paraDegeri(d.provizyon),
    depozito: paraDegeri(d.depozito),
    komisyonOran: d.komisyonOran,
    komisyonTutar: paraDegeri(d.komisyonTutar),
    dropUcreti: paraDegeri(d.dropUcreti),
    sonraOdeOran: d.sonraOdeOran,
    otaKiraBedeli: paraDegeri(d.otaKiraBedeli),
    otaDropBedeli: paraDegeri(d.otaDropBedeli),
    otaBebekKoltugu: paraDegeri(d.otaBebekKoltugu),
    otaNavigasyon: paraDegeri(d.otaNavigasyon),
    otaLcf: paraDegeri(d.otaLcf),
    otaCdw: paraDegeri(d.otaCdw),
    otaScdw: paraDegeri(d.otaScdw),
    otaEkSurucu: paraDegeri(d.otaEkSurucu),
    talepTuru: metinDegeri(d.talepTuru),
    geldigiBirim: metinDegeri(d.geldigiBirim),
    onayKodu: metinDegeri(d.onayKodu),
    projeAdi: metinDegeri(d.projeAdi),
  };
}

/** Karşılaştırma anahtarı: müşteri/araç kimlikle, ofis/kaynak adla, para sayısal değerle ("1200" = 1200.00). */
function degerAnahtari(v: unknown): string {
  if (bosMu(v)) return 'null';
  if (typeof v === 'object' && v !== null && 'id' in v) {
    const s = v as SecimSecenegi;
    return `s:${s.id}|${s.etiket}`;
  }
  if (typeof v === 'number' || (typeof v === 'string' && /^-?\d+(\.\d+)?$/.test(v))) {
    return `n:${Number(v)}`;
  }
  return JSON.stringify(v);
}

/** Ofis ve kaynak için kimlik değil ad önemlidir (seçim ucu kimlik verir, sunucu adı saklar). */
const AD_ILE_KARSILASTIR: ReadonlySet<RezervasyonAlani> = new Set([
  'cikisOfisi',
  'donusOfisi',
  'kaynak',
]);

function anahtar(ad: RezervasyonAlani, v: unknown): string {
  if (AD_ILE_KARSILASTIR.has(ad) && typeof v === 'object' && v !== null && 'etiket' in v) {
    return `a:${(v as SecimSecenegi).etiket}`;
  }
  if ((ad === 'musteri' || ad === 'arac') && typeof v === 'object' && v !== null && 'id' in v) {
    return `k:${(v as SecimSecenegi).id}`;
  }
  return degerAnahtari(v);
}

/**
 * Sunucunun güncel değerlerini KİRLİ formla birleştirir (409 `cakisma` / sekmeye dönüş — F4.3 dersi): kullanıcının
 * DOKUNMADIĞI alan sunucu değerine çekilir (başka oturumun yazdığı değer bayat tam değiştirmeyle geri alınmasın),
 * dokunduğu alan KORUNUR. Dönüş: kullanıcının dokunduğu VE sunucuda önceki okumadan beri değişmiş alanlar.
 */
export function sunucuDegerleriniBirlestir(
  form: RezervasyonFormu,
  yeni: RezervasyonFormDegeri,
  onceki: RezervasyonFormDegeri | null,
): RezervasyonAlani[] {
  const cakisan: RezervasyonAlani[] = [];
  for (const ad of Object.keys(yeni) as RezervasyonAlani[]) {
    const kontrol = form.controls[ad] as AbstractControl<unknown>;
    if (kontrol.dirty) {
      if (onceki && anahtar(ad, yeni[ad]) !== anahtar(ad, onceki[ad])) cakisan.push(ad);
    } else if (anahtar(ad, yeni[ad]) !== anahtar(ad, kontrol.value)) {
      kontrol.setValue(yeni[ad]);
    }
  }
  return cakisan;
}

/** Sunucu seçenek listesi + kayıttaki değer (listede yoksa eklenir — eski değer kaybolmaz). */
export function secenekListesi(
  liste: readonly string[] | undefined,
  mevcut: string | null | undefined,
): { deger: string; etiket: string }[] {
  const degerler = [...(liste ?? [])];
  if (mevcut && !degerler.includes(mevcut)) degerler.push(mevcut);
  return degerler.map((d) => ({ deger: d, etiket: d }));
}
