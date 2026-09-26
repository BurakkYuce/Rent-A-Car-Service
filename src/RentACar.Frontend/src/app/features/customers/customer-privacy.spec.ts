import { describe, expect, it } from 'vitest';

import type { Permission } from '@core/oturum/oturum-tipleri';

import {
  cardToForm,
  formToRequest,
  taxNumberIsSecret,
  taxNumberRequiredOnTypeChange,
} from './customer-form/customer-form-model';
import {
  canSeeStatement,
  looksLikeNationalId,
  splitSearch,
  type CustomerCard,
} from './customer-model';

/** PR #295 KVKK incelemesi çiti (H1 SPA, M1, M2). Beklenenler elle kurulmuş senaryodan. */
const individual = {
  id: '11111111-1111-4111-8111-111111111111',
  surum: 's-1',
  tip: 'Bireysel',
  ad: 'Vedat',
  vergiNo: null,
  vergiNoMaske: '******6780',
  kisiler: [],
  riskLimiti: 0,
  vadeGun: 0,
} as unknown as CustomerCard;

describe('H1: tür değişikliğinde gizli vergi no', () => {
  it('bireysel + maskeli vergi no + Kurumsal/Servis → zorunlu; temizle ya da Bireysel kalırsa değil', () => {
    expect(taxNumberRequiredOnTypeChange(individual, 'Kurumsal', false)).toBe(true);
    expect(taxNumberRequiredOnTypeChange(individual, 'Servis', false)).toBe(true);
    expect(taxNumberRequiredOnTypeChange(individual, 'Bireysel', false)).toBe(false);
    expect(taxNumberRequiredOnTypeChange(individual, 'Kurumsal', true)).toBe(false);
    expect(
      taxNumberRequiredOnTypeChange({ ...individual, vergiNoMaske: null }, 'Kurumsal', false),
    ).toBe(false);
    expect(taxNumberRequiredOnTypeChange(null, 'Kurumsal', false)).toBe(false);
  });

  it('yazılan yeni değer gövdede gider; temizle "" gönderir', () => {
    const typed = formToRequest(
      { ...cardToForm(individual), tip: 'Kurumsal', vergiNo: '1234567890' },
      [],
      individual,
    );
    expect(typed).toMatchObject({ tip: 'Kurumsal', vergiNo: '1234567890' });
    const cleared = formToRequest(
      { ...cardToForm(individual), tip: 'Kurumsal', vergiNoTemizle: true },
      [],
      individual,
    );
    expect(cleared).toMatchObject({ vergiNo: '' });
  });

  it('kurumsal kayıtta maskeli (11 hane eski TC) vergi no da gizli alan kuralına girer', () => {
    const corporate = {
      ...individual,
      tip: 'Kurumsal',
      vergiNoMaske: '*******0146',
    } as CustomerCard;
    expect(taxNumberIsSecret(corporate)).toBe(true);
    expect(formToRequest(cardToForm(corporate), [], corporate)).toMatchObject({ vergiNo: null });
    expect(taxNumberIsSecret({ ...corporate, vergiNoMaske: null, vergiNo: '1234567890' })).toBe(
      false,
    );
  });
});

describe('M1: ekstre yalnız FinanceWrite ∨ ViewReports', () => {
  const has = (list: Permission[]) => (p: Permission) => list.includes(p);
  it('izin tablosu', () => {
    expect(canSeeStatement(has(['FinanceWrite']))).toBe(true);
    expect(canSeeStatement(has(['ViewReports']))).toBe(true);
    expect(canSeeStatement(has(['OperationsWrite']))).toBe(false);
    expect(canSeeStatement(has(['OperationsWrite', 'OperationsDelete', 'ManageUsers']))).toBe(
      false,
    );
  });
});

describe('M2: TC benzeri arama URL’ye yazılmaz', () => {
  it('11 hane rakam bellekte, diğer her şey URL’de', () => {
    expect(looksLikeNationalId('10000000146')).toBe(true);
    expect(looksLikeNationalId(' 10000000146 ')).toBe(true);
    expect(looksLikeNationalId('1000000014')).toBe(false);
    expect(looksLikeNationalId('1000000014a')).toBe(false);
    expect(splitSearch('10000000146')).toEqual({ url: undefined, memory: '10000000146' });
    expect(splitSearch(' Ayşe ')).toEqual({ url: 'Ayşe', memory: null });
    expect(splitSearch('1234567890')).toEqual({ url: '1234567890', memory: null });
    expect(splitSearch('  ')).toEqual({ url: undefined, memory: null });
    expect(splitSearch(null)).toEqual({ url: undefined, memory: null });
  });

  it('#295b L-B: boşluklu, tireli ve karışık biçimli TC de URL’ye yazılmaz (yalnız rakamlar sayılır)', () => {
    expect(looksLikeNationalId('100 000 001 46')).toBe(true);
    expect(looksLikeNationalId('100-00000146')).toBe(true);
    expect(looksLikeNationalId('100.000.001.46 ')).toBe(true);
    expect(looksLikeNationalId('100 000 001 4')).toBe(false);
    expect(looksLikeNationalId('100 000 001 467')).toBe(false);
    expect(splitSearch(' 100 000 001 46 ')).toEqual({ url: undefined, memory: '100 000 001 46' });
    expect(splitSearch('100-00000146')).toEqual({ url: undefined, memory: '100-00000146' });
    expect(splitSearch('Ayşe 2024')).toEqual({ url: 'Ayşe 2024', memory: null });
  });
});
