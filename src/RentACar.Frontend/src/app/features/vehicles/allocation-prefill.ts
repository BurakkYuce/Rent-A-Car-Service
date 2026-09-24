/**
 * Durum panosundan "Tahsis" → BAF ekranının yeni tahsis formu (Blazor `FleetStatus` satır içi `/baf/create`
 * formunun SPA karşılığı, F6.3 parite). Araç, çıkış KM'si ve şube router `state` ile taşınır, URL'ye yazılmaz
 * (liste süzgeç senkronu bilinmeyen sorgu adlarını tanımaz). Personel BAF formunda seçilir; kayıt yine kullanıcıda.
 */
export const ALLOCATION_PREFILL_KEY = 'allocationPrefill';

export interface AllocationPrefill {
  readonly vehicleId: string;
  readonly plaka: string;
  readonly km: number | null;
  readonly sube: string | null;
}

/** Router state'inden güvenli okuma (şekli bozuk ya da yoksa `null`). */
export function readAllocationPrefill(state: unknown): AllocationPrefill | null {
  if (typeof state !== 'object' || state === null) return null;
  const raw = (state as Record<string, unknown>)[ALLOCATION_PREFILL_KEY];
  if (typeof raw !== 'object' || raw === null) return null;
  const p = raw as Record<string, unknown>;
  if (typeof p['vehicleId'] !== 'string' || p['vehicleId'] === '') return null;
  if (typeof p['plaka'] !== 'string') return null;
  const km =
    typeof p['km'] === 'number' && Number.isFinite(p['km']) && p['km'] >= 0 ? p['km'] : null;
  const sube = typeof p['sube'] === 'string' && p['sube'].trim() !== '' ? p['sube'] : null;
  return { vehicleId: p['vehicleId'], plaka: p['plaka'], km, sube };
}
