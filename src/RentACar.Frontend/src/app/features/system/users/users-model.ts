import type { Schema } from '@core/api/ui-tipleri';
import { PERMISSIONS, type Permission } from '@core/oturum/oturum-tipleri';

export type UserDto = Schema<'UserDto'>;
export type PermissionExceptionDto = Schema<'PermissionExceptionDto'>;

/** Backend `UserRole` (ad sırası enum sırası). */
export const ROLES = ['Admin', 'Yonetici', 'Operator', 'Muhasebe'] as const;
export type Role = (typeof ROLES)[number];

/**
 * F11.1b güvenlik M2 (sunucu kuralının ekrandaki yansıması; asıl kapı `UserService`/`KullaniciIzinService`):
 * Admin hesabı oluşturma, Admin hesabının parola/aktif işlemleri ve istisnaları yalnız Admin rolüne açıktır.
 * ManageUsers istisnasıyla kullanıcı yöneten bir Yönetici bu düğmeleri görmez (görse de sunucu 403 verir).
 */
export function canManageAccount(actorRole: string | null | undefined, target: UserDto): boolean {
  return actorRole === 'Admin' || target.rol !== 'Admin';
}

/** Oluşturma formunda seçilebilecek roller (Admin yalnız Admin'e). */
export function creatableRoles(actorRole: string | null | undefined): readonly Role[] {
  return actorRole === 'Admin' ? ROLES : ROLES.filter((r) => r !== 'Admin');
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
