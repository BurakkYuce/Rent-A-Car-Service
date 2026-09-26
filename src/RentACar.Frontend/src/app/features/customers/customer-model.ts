import type { Schema } from '@core/api/ui-tipleri';
import type { Permission } from '@core/oturum/oturum-tipleri';
import { listDefinition } from '@core/veri/liste-sorgusu';

/**
 * #295 M1: cari ekstre (bakiye + firma geneli hareketler) yalnız detay ucunun bakiyeyi açtığı izinlerle görünür —
 * FinanceWrite ∨ ViewReports. Detay sekmesi ve listedeki bağlantı bu kuraldan geçer.
 */
export function canSeeStatement(has: (p: Permission) => boolean): boolean {
  return has('FinanceWrite') || has('ViewReports');
}

/** 11 haneli, tamamı rakam arama metni (TC olabilir) — KVKK: adres çubuğuna/geçmişe YAZILMAZ (#295 M2). */
export function looksLikeNationalId(value: string | null | undefined): boolean {
  // #295b L-B: yalnız rakamlar sayılır — "100 000 001 46" ve "100-00000146" de TC'dir (sunucu da öyle arar).
  return (value ?? '').replace(/\D/g, '').length === 11;
}

/**
 * Arama metnini URL'ye yazılacak ve yalnız bellekte tutulacak parçaya ayırır: TC benzeri değer URL'ye gitmez
 * (sorgu parametresi olarak yalnız API isteğinde, bellekten taşınır).
 */
export function splitSearch(q: string | null | undefined): {
  readonly url: string | undefined;
  readonly memory: string | null;
} {
  const v = q?.trim() ?? '';
  if (v === '') return { url: undefined, memory: null };
  return looksLikeNationalId(v) ? { url: undefined, memory: v } : { url: v, memory: null };
}

// ---- Cari (`/api/ui/v1/cariler`)
export type CustomerRow = Schema<'CustomerListRow'>;
export type CustomerCard = Schema<'CustomerCardDto'>;
export type CustomerRequest = Schema<'CustomerRequest'>;
export type CustomerUpdateRequest = Schema<'CustomerUpdateRequest'>;
export type CustomerContact = Schema<'CustomerContactDto'>;
export type CustomerDetail = Schema<'CustomerDetailView'>;
export type CustomerRentalSummary = Schema<'CustomerRentalSummary'>;
export type CustomerLedgerLine = Schema<'CustomerLedgerLine'>;
export type CustomerProfile = Schema<'KiraMusteriOzeti'>;

// ---- Cari ekstre (F8.1a `/api/ui/v1/finans/cariler/{id}/ekstre`)
export type CustomerStatement = Schema<'CustomerStatement'>;
export type CustomerStatementLine = Schema<'CustomerStatementLine'>;

export const CUSTOMERS = '/api/ui/v1/cariler';

export function customerPath(id: string, rest = ''): `/api/ui/v1/${string}` {
  return `${CUSTOMERS}/${encodeURIComponent(id)}${rest}`;
}

export function statementPath(id: string): `/api/ui/v1/${string}` {
  return `/api/ui/v1/finans/cariler/${encodeURIComponent(id)}/ekstre`;
}

/** Sunucu `CariType` enum ADLARI (tanımsız ad 400). */
export const CUSTOMER_TYPES = ['Bireysel', 'Kurumsal', 'Servis'] as const;
export type CustomerType = (typeof CUSTOMER_TYPES)[number];

/**
 * `/cariler` liste sorgusu — Blazor `CustomerList` süzgeçleri (arama, tür, İYS, uyarı, kara liste, durum, araç
 * verilmez). `q` ad/soyad/ünvan/vergi no içinde; TC yalnız TAM 11 hane (blind-index, kısmi TC araması YOK).
 */
export const CUSTOMER_LIST = listDefinition({
  filtreler: {
    q: { tur: 'metin', enFazla: 100 },
    tip: { tur: 'secim', degerler: CUSTOMER_TYPES },
    iysIzinli: { tur: 'bayrak' },
    uyari: { tur: 'bayrak' },
    karaListe: { tur: 'bayrak' },
    pasif: { tur: 'bayrak' },
    aracVerilmez: { tur: 'bayrak' },
  },
  siralanabilir: [
    'tip',
    'ad',
    'soyad',
    'unvan',
    'il',
    'ilce',
    'kaynak',
    'sinif',
    'vadeGun',
    'musteriTemsilcisi',
    'olusturma',
  ],
  varsayilanSirala: null,
  varsayilanBoyut: 50,
});

/** Liste satırının durum rozetleri (Blazor sırası): kara liste, uyarı, İYS, pasif, araç verilmez, not. */
export type BadgeKind = 'karaListe' | 'uyari' | 'iys' | 'pasif' | 'aracVerilmez' | 'not';

export interface Badge {
  readonly kind: BadgeKind;
  readonly tone: 'hata' | 'uyari' | 'notr';
  /** `not` rozetinin ipucu (uyarı nedeni). */
  readonly title?: string;
}

export function rowBadges(r: CustomerRow): readonly Badge[] {
  const list: Badge[] = [];
  if (r.karaListe) list.push({ kind: 'karaListe', tone: 'hata' });
  if (r.uyari) list.push({ kind: 'uyari', tone: 'uyari' });
  if (r.iysIzinli) list.push({ kind: 'iys', tone: 'notr' });
  if (r.pasif) list.push({ kind: 'pasif', tone: 'notr' });
  if (r.aracVerilmez) list.push({ kind: 'aracVerilmez', tone: 'hata' });
  const note = r.uyariNedeni?.trim() ?? '';
  if (note !== '') list.push({ kind: 'not', tone: 'uyari', title: note });
  return list;
}

/**
 * Bakiye işareti (Blazor "pozitif = borçlu"): müşteri borçluysa `borclu`, alacaklıysa `alacakli`, sıfırda `sifir`.
 */
export function balanceSide(value: number | null): 'borclu' | 'alacakli' | 'sifir' | null {
  if (value === null) return null;
  if (value > 0) return 'borclu';
  if (value < 0) return 'alacakli';
  return 'sifir';
}
