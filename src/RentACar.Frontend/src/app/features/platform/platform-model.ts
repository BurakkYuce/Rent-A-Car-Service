import type { Sema } from '@core/api/ui-tipleri';

/**
 * F12.2 platform console — contract types (generated from OpenAPI) and PURE rules shared by the pages.
 * No tenant data beyond META (counts, status, contact fields, flags): the API never sends customer PII.
 */
export type PlatformSession = Sema<'PlatformSessionResponse'>;
export type PlatformSummary = Sema<'PlatformSummary'>;
export type PlatformTenantRow = Sema<'PlatformTenantRowDto'>;
export type PlatformTenantOption = Sema<'PlatformTenantOptionDto'>;
export type PlatformTenantDetail = Sema<'PlatformTenantDetailDto'>;
export type PlatformLogo = Sema<'PlatformLogoDto'>;
export type PlatformDocument = Sema<'PlatformConsoleDocumentDto'>;
export type PlatformTenantUpdateBody = Sema<'PlatformTenantUpdateRequest'>;

export const PLATFORM_API = '/api/ui/v1/platform';

/** Tenant status vocabulary (server `PlatformApi.StatusOf`; the closed stamp wins over IsActive). */
export type TenantStatus = 'Aktif' | 'Pasif' | 'Kapali';
export const TENANT_STATUSES: readonly TenantStatus[] = ['Aktif', 'Pasif', 'Kapali'];

/** Document status vocabulary (server `DocumentStatusName`). */
export type DocumentStatus = 'Taslak' | 'Yayinda' | 'Arsiv';

/** Unknown server value → `Pasif` badge (never shown as active). */
export function tenantStatus(value: string): TenantStatus {
  return (TENANT_STATUSES as readonly string[]).includes(value) ? (value as TenantStatus) : 'Pasif';
}

/** Badge modifier: Kapalı = error, Aktif = success, everything else = warning (Blazor parity). */
export function tenantStatusBadge(status: TenantStatus): string {
  switch (status) {
    case 'Kapali':
      return 'rc-rozet--hata';
    case 'Aktif':
      return 'rc-rozet--basari';
    default:
      return 'rc-rozet--uyari';
  }
}

export function documentStatus(value: string): DocumentStatus {
  return value === 'Yayinda' || value === 'Arsiv' ? value : 'Taslak';
}

/**
 * Suspend/resume toggle target for a NOT closed tenant (Aktif ↔ Pasif). A closed tenant has no toggle:
 * it is reopened (→ Aktif) or stays closed — `null`.
 */
export function toggleTarget(status: TenantStatus): TenantStatus | null {
  if (status === 'Kapali') return null;
  return status === 'Aktif' ? 'Pasif' : 'Aktif';
}

/** Generated int32/int64 fields are typed `number | string` — always read them through this. */
export function toNumber(value: number | string | null | undefined): number | null {
  if (value === null || value === undefined || value === '') return null;
  const n = typeof value === 'number' ? value : Number(value);
  return Number.isFinite(n) ? n : null;
}

/** Whole kilobytes, floored (Blazor `Boyut / 1024`). */
export function kilobytes(bytes: number | string | null | undefined): number {
  return Math.floor((toNumber(bytes) ?? 0) / 1024);
}

/**
 * Logo size line (Blazor TenantDetail): `"640×200 px · 12 KB"`; unreadable dimensions →
 * `"<bytes> bayt · ölçü okunamadı"`. `null` when no logo.
 */
export function logoInfo(logo: PlatformLogo): string | null {
  if (!logo.var) return null;
  const width = toNumber(logo.genislik);
  const height = toNumber(logo.yukseklik);
  const bytes = toNumber(logo.bayt) ?? 0;
  return width !== null && height !== null
    ? `${width}×${height} px · ${Math.floor(bytes / 1024)} KB`
    : `${bytes} bayt · ölçü okunamadı`;
}

/** Info-form value (every field a string; empty → null on the wire). */
export interface TenantInfoValue {
  readonly ad: string | null;
  readonly yetkiliAd: string | null;
  readonly eposta: string | null;
  readonly telefon: string | null;
  readonly plan: string | null;
  readonly notlar: string | null;
}

export function infoFromDetail(d: PlatformTenantDetail): TenantInfoValue {
  return {
    ad: d.ad,
    yetkiliAd: d.yetkiliAd,
    eposta: d.eposta,
    telefon: d.telefon,
    plan: d.plan,
    notlar: d.notlar,
  };
}

const trimOrNull = (v: string | null): string | null => {
  const t = (v ?? '').trim();
  return t === '' ? null : t;
};

/** Full-replace PUT body: all six fields + the concurrency token (`surum` is mandatory). */
export function updateBody(value: TenantInfoValue, surum: string): PlatformTenantUpdateBody {
  return {
    ad: trimOrNull(value.ad),
    yetkiliAd: trimOrNull(value.yetkiliAd),
    eposta: trimOrNull(value.eposta),
    telefon: trimOrNull(value.telefon),
    plan: trimOrNull(value.plan),
    notlar: trimOrNull(value.notlar),
    surum,
  };
}

/** Plan suggestions (Blazor datalist; free text is accepted). */
export const PLAN_SUGGESTIONS: readonly string[] = ['Deneme', 'Standart', 'Pro'];

/** Document upload multipart body. `hedef` repeated per tenant id; none = ALL tenants. */
export function documentUploadForm(input: {
  readonly baslik: string;
  readonly aciklama: string | null;
  readonly file: File;
  readonly onlyManagers: boolean;
  readonly targets: readonly string[];
}): FormData {
  const form = new FormData();
  form.append('baslik', input.baslik.trim());
  const description = (input.aciklama ?? '').trim();
  if (description !== '') form.append('aciklama', description);
  form.append('dosya', input.file, input.file.name);
  form.append('yalnizYoneticiler', input.onlyManagers ? 'true' : 'false');
  for (const id of input.targets) form.append('hedef', id);
  return form;
}
