import {
  type UserDto,
  canManageAccount,
  canWriteUsers,
  creatableRoles,
  editableRoles,
  exceptionRows,
  exceptionTargets,
  grantablePermissions,
} from './users-model';

/** Elle kurulmuş kullanıcılar (F11.1b M2 kuralının ekran yansıması). */
const user = (id: string, rol: string, exceptions: UserDto['istisnalar'] = []): UserDto => ({
  id,
  kullaniciAdi: id,
  gorunenAd: id,
  rol,
  aktif: true,
  atanmisSube: null,
  istisnalar: exceptions,
  etkinIzinler: [],
  surum: '1',
});
const ex = (permission: string, give: boolean) => ({
  izin: permission,
  ver: give,
  tanimlayan: 'x',
  tarihUtc: '2026-01-01T00:00:00Z',
});

const ADMIN = user('admin', 'Admin', [ex('FinanceWrite', false)]);
const MANAGER = user('mudur', 'Yonetici', [ex('ManageUsers', true)]);
const OPERATOR = user('op', 'Operator', [ex('FinanceWrite', true)]);
const ALL = [ADMIN, MANAGER, OPERATOR];

describe('kullanıcı yönetimi kuralları (M2)', () => {
  it('güvenlik tur 2 M1: kullanıcı yazmaları yalnız Admin rolü; Admin olmayan hiçbir hesaba dokunamaz', () => {
    expect(canManageAccount('Yonetici', ADMIN)).toBe(false);
    expect(canManageAccount('Yonetici', OPERATOR)).toBe(false);
    expect(canManageAccount('Admin', ADMIN)).toBe(true);
    expect(canManageAccount('Admin', OPERATOR)).toBe(true);
    expect(canWriteUsers('Admin')).toBe(true);
    expect(canWriteUsers('Yonetici')).toBe(false);
    expect(canWriteUsers('Operator')).toBe(false);
  });

  it('Admin rolü ve ManageUsers izni yalnız Admin tarafından verilir', () => {
    expect(creatableRoles('Yonetici')).toEqual(['Yonetici', 'Operator', 'Muhasebe']);
    expect(creatableRoles('Admin')).toContain('Admin');
    expect(grantablePermissions('Yonetici')).not.toContain('ManageUsers');
    expect(grantablePermissions('Admin')).toContain('ManageUsers');
  });

  it('güvenlik F2: aktör kıdeminden yüksek rol veremez ve kıdemlisine dokunamaz', () => {
    expect(creatableRoles('Operator')).toEqual(['Operator', 'Muhasebe']);
    expect(canManageAccount('Operator', MANAGER)).toBe(false);
    expect(canManageAccount('Operator', OPERATOR)).toBe(false);
    expect(editableRoles('Operator', 'baska', OPERATOR)).toEqual(['Operator', 'Muhasebe']);
  });

  it('rol düzenleme: kendi rolü sabit; Admin rolünü yalnız Admin verir (kabul d-sistem-kullanici-09)', () => {
    expect(editableRoles('Admin', 'admin', ADMIN)).toEqual(['Admin']);
    expect(editableRoles('Admin', 'admin', OPERATOR)).toEqual([
      'Admin',
      'Yonetici',
      'Operator',
      'Muhasebe',
    ]);
    expect(editableRoles('Yonetici', 'mudur', OPERATOR)).toEqual([
      'Yonetici',
      'Operator',
      'Muhasebe',
    ]);
    expect(editableRoles('Yonetici', 'mudur', MANAGER)).toEqual(['Yonetici']);
  });

  it('istisna hedefleri: Admin için kendisi hariç; Admin olmayan için hiç (M1)', () => {
    expect(exceptionTargets(ALL, 'mudur', 'Yonetici').map((u) => u.id)).toEqual([]);
    expect(exceptionTargets(ALL, 'admin', 'Admin').map((u) => u.id)).toEqual(['mudur', 'op']);
  });

  it('istisna tablosu: Yönetici hiçbir satırı düzenleyemez (M1); Admin kendi satırı dışında', () => {
    const rows = exceptionRows(ALL, 'mudur', 'Yonetici');
    expect(rows.map((r) => [r.kullaniciAdi, r.izin, r.duzenlenebilir])).toEqual([
      ['admin', 'FinanceWrite', false],
      ['mudur', 'ManageUsers', false],
      ['op', 'FinanceWrite', false],
    ]);
    const asAdmin = exceptionRows(ALL, 'admin', 'Admin');
    expect(asAdmin.map((r) => r.duzenlenebilir)).toEqual([false, true, true]);
  });
});
