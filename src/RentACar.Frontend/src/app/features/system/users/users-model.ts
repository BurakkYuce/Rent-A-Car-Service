import type { Schema } from '@core/api/ui-tipleri';
import { PERMISSIONS, type Permission } from '@core/oturum/oturum-tipleri';

export type UserDto = Schema<'UserDto'>;
export type PermissionExceptionDto = Schema<'PermissionExceptionDto'>;

/** Backend `UserRole` (ad sırası enum sırası). */
export const ROLES = ['Admin', 'Yonetici', 'Operator', 'Muhasebe'] as const;
export type Role = (typeof ROLES)[number];

/**
 * Güvenlik tur 2 M1 (sunucu kuralının ekrandaki yansıması; asıl kapı `UserService`/`UserPermissionService` ve uç):
 * kullanıcı yönetimi YAZMALARI (oluştur, düzenle, aktiflik, parola sıfırla, istisna) yalnız Admin rolüne açıktır.
 * ManageUsers istisnası almış Admin olmayan kullanıcı ekranı salt okunur görür (görse de sunucu 403 verir).
 */
export function canManageAccount(actorRole: string | null | undefined, target: UserDto): boolean {
  return actorRole === 'Admin' && !outranks(actorRole, target);
}

/** Kullanıcı yönetimi yazma yetkisi (oluşturma formu, istisna formu): yalnız Admin rolü. */
export function canWriteUsers(actorRole: string | null | undefined): boolean {
  return actorRole === 'Admin';
}

/** Rol kıdemi (sunucu `UserService.Rank` ile aynı): Admin > Yönetici > Operatör = Muhasebe. */
export function roleRank(role: string | null | undefined): number {
  return role === 'Admin' ? 3 : role === 'Yonetici' ? 2 : role ? 1 : 0;
}

/**
 * Oluşturma formunda seçilebilecek roller: aktörün kıdeminden yüksek olmayanlar (güvenlik F2 — Admin yalnız Admin'e,
 * ManageUsers istisnalı Operatör Yönetici veremez).
 */
export function creatableRoles(actorRole: string | null | undefined): readonly Role[] {
  return ROLES.filter((r) => roleRank(r) <= roleRank(actorRole));
}

/** Kıdemli (aktörden yüksek roldeki) hesaba dokunulmaz (sunucu 403). */
export function outranks(actorRole: string | null | undefined, target: UserDto): boolean {
  return roleRank(target.rol) > roleRank(actorRole);
}

/**
 * Rol/şube düzenlemesinde seçilebilecek roller (sunucu kuralının yansıması — asıl kapı `UserService.UpdateAsync`):
 * kendi kaydı düzenlenmez (ekranda düğme yok; sunucu rol ve şube değişimini reddeder); aktör kendi kıdeminden yüksek
 * rol veremez; Admin hesabının kendisi zaten yalnız Admin'e düzenlenebilir (`canManageAccount`).
 */
export function editableRoles(
  actorRole: string | null | undefined,
  actorId: string | null | undefined,
  target: UserDto,
): readonly string[] {
  if (target.id === actorId) return [target.rol];
  const roles: readonly string[] = creatableRoles(actorRole);
  return roles.includes(target.rol) ? roles : [target.rol, ...roles];
}

/** İstisnası verilebilecek izinler: ManageUsers'ı vermek/kaldırmak yalnız Admin'e (M2). */
export function grantablePermissions(actorRole: string | null | undefined): readonly Permission[] {
  return actorRole === 'Admin' ? PERMISSIONS : PERMISSIONS.filter((p) => p !== 'ManageUsers');
}

/**
 * İstisnası düzenlenebilecek kullanıcılar: kendisi hariç (sunucu "kendi izninizi değiştiremezsiniz" der) ve Admin
 * olmayan aktör için Admin hesapları hariç.
 */
export function exceptionTargets(
  users: readonly UserDto[],
  actorId: string | null | undefined,
  actorRole: string | null | undefined,
): readonly UserDto[] {
  return users.filter((u) => u.id !== actorId && canManageAccount(actorRole, u));
}

/** Tüm kullanıcıların istisnaları tek tabloda (kullanıcı adıyla). */
export interface ExceptionRow extends PermissionExceptionDto {
  readonly kullaniciId: string;
  readonly kullaniciAdi: string;
  readonly duzenlenebilir: boolean;
}

export function exceptionRows(
  users: readonly UserDto[],
  actorId: string | null | undefined,
  actorRole: string | null | undefined,
): readonly ExceptionRow[] {
  return users.flatMap((u) =>
    u.istisnalar.map((x) => ({
      ...x,
      kullaniciId: u.id,
      kullaniciAdi: u.kullaniciAdi,
      duzenlenebilir:
        u.id !== actorId &&
        canManageAccount(actorRole, u) &&
        (x.izin !== 'ManageUsers' || actorRole === 'Admin'),
    })),
  );
}
