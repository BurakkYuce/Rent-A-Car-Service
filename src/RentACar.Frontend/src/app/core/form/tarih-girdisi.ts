import { formatDate } from '@angular/common';
import { ISTANBUL_OFSETI, YEREL } from '../yerel/tr-yerel';

/**
 * Tarih girdisi (saf). İki ayrı değer türü, iki ayrı kural (repo dersi: "date LOCAL gün, datetime
 * UTC an" — Blazor kira formunda karıştırılınca her kayıtta hayalet +1 gün uzatma çıkmıştı):
 *
 * - **Takvim günü** (`DateOnly`): `"2026-09-22"` metni. Saat dilimine HİÇ girmez; `Date` +
 *   `toISOString()` yoluna sokulmaz (UTC'ye kayıp bir önceki güne düşer). Gün aritmetiği UTC
 *   bileşenleriyle yapılır, tarayıcının saat dilimi sonucu değiştirmez.
 * - **An** (`DateTimeOffset`): UTC ISO metni `"2026-09-22T11:30:00.000Z"`. Kullanıcı İstanbul saatiyle
 *   görür ve yazar (`tarihSaatBicimle` ile aynı ofset); gidip gelmede kayma yok.
 *
 * Revlo `date-picker.models.ts`'ten uyarlandı: dayjs, saat dilimi eklentileri ve çoklu mod atıldı;
 * ızgara (6×7, pazartesi başlangıç) ve katı `GG.AA.YYYY` ayrıştırma korundu.
 */

export type GunMetni = string;

export interface GunAraligi {
  readonly baslangic: GunMetni;
  readonly bitis: GunMetni;
}

/** Ayrıştırma sonucu: boş → `null`, biçimsiz → `'gecersiz'`. */
export type Cozum<T> = T | null | 'gecersiz';

const ISO_GUN = /^(\d{4})-(\d{2})-(\d{2})$/;
const TR_GUN = /^(\d{1,2})[./](\d{1,2})[./](\d{4})$/;
const SEKIZ_RAKAM = /^(\d{2})(\d{2})(\d{4})$/;
const GUN_MS = 86_400_000;

/** `+0300` → 180 dakika. Tek kaynak `ISTANBUL_OFSETI` (tr-yerel). */
export const ISTANBUL_OFSET_DAKIKA = ((): number => {
  const e = /^([+-])(\d{2})(\d{2})$/.exec(ISTANBUL_OFSETI);
  if (!e) return 180;
  const dakika = Number(e[2]) * 60 + Number(e[3]);
  return e[1] === '-' ? -dakika : dakika;
})();

function iki(n: number): string {
  return String(n).padStart(2, '0');
}

function gunUret(yil: number, ay: number, gun: number): GunMetni | null {
  if (yil < 1900 || yil > 2199 || ay < 1 || ay > 12 || gun < 1) return null;
  const t = new Date(Date.UTC(yil, ay - 1, gun));
  if (t.getUTCFullYear() !== yil || t.getUTCMonth() !== ay - 1 || t.getUTCDate() !== gun)
    return null;
  return `${yil}-${iki(ay)}-${iki(gun)}`;
}

/** Geçerli bir ISO takvim günü mü (`2026-02-30` değil). */
export function gunMu(deger: unknown): deger is GunMetni {
  if (typeof deger !== 'string') return false;
  const e = ISO_GUN.exec(deger);
  return !!e && gunUret(Number(e[1]), Number(e[2]), Number(e[3])) !== null;
}

/** Kullanıcı yazımı → takvim günü. `22.09.2026`, `2.9.2026`, `22/09/2026`, `22092026`. */
export function gunCoz(metin: string): Cozum<GunMetni> {
  const s = metin.trim();
  if (s === '') return null;
  const e = TR_GUN.exec(s) ?? SEKIZ_RAKAM.exec(s);
  if (!e) return 'gecersiz';
  return gunUret(Number(e[3]), Number(e[2]), Number(e[1])) ?? 'gecersiz';
}

/** Takvim günü → `dd.MM.yyyy`. */
export function gunBicimle(gun: GunMetni | null | undefined): string {
  if (!gun || !gunMu(gun)) return '';
  const [y, a, g] = gun.split('-');
  return `${g}.${a}.${y}`;
}

/** Sunucu değeri (takvim günü ya da `2026-09-22T00:00:00` gibi saatli yazım) → takvim günü. */
export function gunNormalize(deger: unknown): GunMetni | null {
  if (typeof deger !== 'string') return null;
  const gun = deger.slice(0, 10);
  return gunMu(gun) ? gun : null;
}

function gunUtcMs(gun: GunMetni): number {
  const [y, a, g] = gun.split('-').map(Number);
  return Date.UTC(y ?? 1970, (a ?? 1) - 1, g ?? 1);
}

function msGun(ms: number): GunMetni {
  const t = new Date(ms);
  return `${t.getUTCFullYear()}-${iki(t.getUTCMonth() + 1)}-${iki(t.getUTCDate())}`;
}

export function gunEkle(gun: GunMetni, adet: number): GunMetni {
  return msGun(gunUtcMs(gun) + adet * GUN_MS);
}

/** Ayın ilk günü (`yyyy-MM-01`), `adet` ay ileri/geri. */
export function ayBasi(gun: GunMetni, adet = 0): GunMetni {
  const [y, a] = gun.split('-').map(Number);
  const t = new Date(Date.UTC(y ?? 1970, (a ?? 1) - 1 + adet, 1));
  return msGun(t.getTime());
}

export function aySonu(gun: GunMetni): GunMetni {
  return gunEkle(ayBasi(gun, 1), -1);
}

/** Haftanın pazartesisi (Türkiye'de hafta pazartesi başlar). */
export function haftaBasi(gun: GunMetni): GunMetni {
  const haftaGunu = new Date(gunUtcMs(gun)).getUTCDay(); // 0 pazar
  return gunEkle(gun, -((haftaGunu + 6) % 7));
}

/** Aynı takvim günleri metin olarak kıyaslanabilir (`yyyy-MM-dd` sözlük sırası = zaman sırası). */
export function gunKiyasla(a: GunMetni, b: GunMetni): number {
  return a < b ? -1 : a > b ? 1 : 0;
}

/** İstanbul'da bugünün takvim günü (tarayıcı saat diliminden bağımsız). */
export function bugun(simdi: Date = new Date()): GunMetni {
  return msGun(simdi.getTime() + ISTANBUL_OFSET_DAKIKA * 60_000);
}

export interface TakvimHucresi {
  readonly gun: GunMetni;
  readonly ayDisi: boolean;
}

/** Sabit 6×7 (42 hücre) ay ızgarası, pazartesi başlangıç, komşu ay günleriyle. */
export function ayIzgarasi(ayCapasi: GunMetni): readonly TakvimHucresi[] {
  const bas = ayBasi(ayCapasi);
  const ilk = haftaBasi(bas);
  const ay = bas.slice(0, 7);
  return Array.from({ length: 42 }, (_, i) => {
    const gun = gunEkle(ilk, i);
    return { gun, ayDisi: gun.slice(0, 7) !== ay };
  });
}

/** `Eylül 2026`. */
export function ayBasligi(ayCapasi: GunMetni): string {
  return formatDate(gunUtcMs(ayBasi(ayCapasi)), 'LLLL yyyy', YEREL, '+0000');
}

/** Pazartesiden başlayan kısa gün adları (`Pt`, `Sa`, …). */
export function haftaGunuAdlari(): readonly string[] {
  const pazartesi = Date.UTC(2024, 0, 1); // 1 Ocak 2024 pazartesi
  return Array.from({ length: 7 }, (_, i) =>
    formatDate(pazartesi + i * GUN_MS, 'EEEEEE', YEREL, '+0000'),
  );
}

/** Uzun ad (ekran okuyucu için): `22 Eylül 2026 Salı`. */
export function gunUzunAdi(gun: GunMetni): string {
  return formatDate(gunUtcMs(gun), 'd MMMM y EEEE', YEREL, '+0000');
}

// ─── Saat ve an ──────────────────────────────────────────────────────────────────────────────

/** `14:30`, `1430`, `9` (→ 09:00), `9:5` (→ 09:05). Aralık dışı (`25:00`) geçersiz — sessiz kırpma yok. */
export function saatCoz(metin: string): Cozum<string> {
  const s = metin.trim();
  if (s === '') return null;
  let saat: number;
  let dakika: number;
  const ikiNokta = /^(\d{1,2})[:.](\d{1,2})$/.exec(s);
  if (ikiNokta) {
    saat = Number(ikiNokta[1]);
    dakika = Number(ikiNokta[2]);
  } else if (/^\d{1,4}$/.test(s)) {
    if (s.length <= 2) {
      saat = Number(s);
      dakika = 0;
    } else {
      saat = Number(s.slice(0, s.length - 2));
      dakika = Number(s.slice(-2));
    }
  } else {
    return 'gecersiz';
  }
  if (saat > 23 || dakika > 59) return 'gecersiz';
  return `${iki(saat)}:${iki(dakika)}`;
}

export interface AnParcalari {
  readonly gun: GunMetni;
  readonly saat: string;
}

/** An (ISO, ofsetli ya da `Z`) → İstanbul duvar saati parçaları. Biçimsiz → `null`. */
export function anParcala(deger: unknown): AnParcalari | null {
  if (typeof deger !== 'string' || !/^\d{4}-\d{2}-\d{2}T/.test(deger)) return null;
  // Ofsetsiz ISO yazımı (`2026-09-22T11:30:00`) tarayıcıda YEREL saat sayılır; sunucu anı daima
  // ofsetli gönderir, ofsetsizse UTC kabul edilir ki tarayıcı saat dilimi sonucu değiştirmesin.
  const ofsetli = /(Z|[+-]\d{2}:?\d{2})$/i.test(deger) ? deger : `${deger}Z`;
  const ms = Date.parse(ofsetli);
  if (Number.isNaN(ms)) return null;
  const t = new Date(ms + ISTANBUL_OFSET_DAKIKA * 60_000);
  return {
    gun: `${t.getUTCFullYear()}-${iki(t.getUTCMonth() + 1)}-${iki(t.getUTCDate())}`,
    saat: `${iki(t.getUTCHours())}:${iki(t.getUTCMinutes())}`,
  };
}

/** İstanbul duvar saati → UTC ISO anı. */
export function anBirlestir(gun: GunMetni, saat: string): string {
  const [s, d] = saat.split(':').map(Number);
  const ms = gunUtcMs(gun) + ((s ?? 0) * 60 + (d ?? 0) - ISTANBUL_OFSET_DAKIKA) * 60_000;
  return new Date(ms).toISOString();
}

// ─── Hazır aralıklar ─────────────────────────────────────────────────────────────────────────

export type HazirAralikKimligi =
  'bugun' | 'dun' | 'buHafta' | 'gecenHafta' | 'buAy' | 'gecenAy' | 'son7' | 'son30' | 'buYil';

export interface HazirAralik {
  readonly kimlik: HazirAralikKimligi;
  readonly aralik: GunAraligi;
}

/** Revlo `buildStandardDatePresets` kısaltılmış hali; bugün İstanbul günü. */
export function hazirAraliklar(bugunGun: GunMetni): readonly HazirAralik[] {
  const hafta = haftaBasi(bugunGun);
  const ay = ayBasi(bugunGun);
  const gecenAy = ayBasi(bugunGun, -1);
  const a = (kimlik: HazirAralikKimligi, baslangic: GunMetni, bitis: GunMetni): HazirAralik => ({
    kimlik,
    aralik: { baslangic, bitis },
  });
  return [
    a('bugun', bugunGun, bugunGun),
    a('dun', gunEkle(bugunGun, -1), gunEkle(bugunGun, -1)),
    a('buHafta', hafta, gunEkle(hafta, 6)),
    a('gecenHafta', gunEkle(hafta, -7), gunEkle(hafta, -1)),
    a('buAy', ay, aySonu(ay)),
    a('gecenAy', gecenAy, aySonu(gecenAy)),
    a('son7', gunEkle(bugunGun, -6), bugunGun),
    a('son30', gunEkle(bugunGun, -29), bugunGun),
    a('buYil', `${bugunGun.slice(0, 4)}-01-01`, `${bugunGun.slice(0, 4)}-12-31`),
  ];
}

/** Aralık metni: `22.09.2026 – 25.09.2026` (ayraç `–`, `—` ya da boşluklu `-`). */
export function aralikCoz(metin: string): Cozum<GunAraligi> {
  const s = metin.trim();
  if (s === '') return null;
  const parcalar = s.split(/\s*[–—]\s*|\s+-\s+/);
  if (parcalar.length !== 2) return 'gecersiz';
  const bas = gunCoz(parcalar[0] ?? '');
  const bit = gunCoz(parcalar[1] ?? '');
  if (bas === null || bit === null || bas === 'gecersiz' || bit === 'gecersiz') return 'gecersiz';
  return { baslangic: bas, bitis: bit };
}

export function aralikBicimle(aralik: GunAraligi | null | undefined): string {
  if (!aralik) return '';
  return `${gunBicimle(aralik.baslangic)} – ${gunBicimle(aralik.bitis)}`;
}
