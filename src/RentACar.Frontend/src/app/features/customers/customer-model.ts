import type { Sema } from '@core/api/ui-tipleri';
import { listeTanimi } from '@core/veri/liste-sorgusu';

// ---- Cari (`/api/ui/v1/cariler`)
export type CustomerRow = Sema<'CustomerListRow'>;
export type CustomerCard = Sema<'CustomerCardDto'>;
export type CustomerRequest = Sema<'CustomerRequest'>;
export type CustomerUpdateRequest = Sema<'CustomerUpdateRequest'>;
export type CustomerContact = Sema<'CustomerContactDto'>;
export type CustomerDetail = Sema<'CustomerDetailView'>;
export type CustomerRentalSummary = Sema<'CustomerRentalSummary'>;
export type CustomerLedgerLine = Sema<'CustomerLedgerLine'>;
export type CustomerProfile = Sema<'KiraMusteriOzeti'>;

// ---- Cari ekstre (F8.1a `/api/ui/v1/finans/cariler/{id}/ekstre`)
export type CustomerStatement = Sema<'CustomerStatement'>;
export type CustomerStatementLine = Sema<'CustomerStatementLine'>;

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
export const CUSTOMER_LIST = listeTanimi({
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
