import {
  type UserDto,
  canManageAccount,
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
  it('Admin olmayan aktör Admin hesabına dokunamaz; Admin her hesaba', () => {
    expect(canManageAccount('Yonetici', ADMIN)).toBe(false);
    expect(canManageAccount('Yonetici', OPERATOR)).toBe(true);
    expect(canManageAccount('Admin', ADMIN)).toBe(true);
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
    expect(canManageAccount('Operator', OPERATOR)).toBe(true);
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

  it('istisna hedefleri: kendisi ve (Admin olmayan için) Admin hesapları hariç', () => {
    expect(exceptionTargets(ALL, 'mudur', 'Yonetici').map((u) => u.id)).toEqual(['op']);
    expect(exceptionTargets(ALL, 'admin', 'Admin').map((u) => u.id)).toEqual(['mudur', 'op']);
  });

  it('istisna tablosu: kendi satırı, Admin hesabı ve ManageUsers istisnası Yönetici için düzenlenemez', () => {
    const rows = exceptionRows(ALL, 'mudur', 'Yonetici');
    expect(rows.map((r) => [r.kullaniciAdi, r.izin, r.duzenlenebilir])).toEqual([
      ['admin', 'FinanceWrite', false],
      ['mudur', 'ManageUsers', false],
      ['op', 'FinanceWrite', true],
    ]);
    const asAdmin = exceptionRows(ALL, 'admin', 'Admin');
    expect(asAdmin.map((r) => r.duzenlenebilir)).toEqual([false, true, true]);
  });
});
