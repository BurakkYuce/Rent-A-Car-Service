import { metinDegeri } from '@features/planlama-ortak/form-yardimcilari';
import { toNumber } from '@features/vehicles/vehicle-model';

import type { FleetPlan, FleetPlanRequest } from '../finance-model';

export interface FleetPlanFormValue {
  readonly aracGrupAdi: string | null;
  readonly sipp: string | null;
  readonly donem: string | null;
  readonly hedefAdet: number | null;
  readonly aciklama: string | null;
}

export function emptyFleetPlan(): FleetPlanFormValue {
  return { aracGrupAdi: null, sipp: null, donem: null, hedefAdet: 0, aciklama: null };
}

export function fleetPlanToForm(p: FleetPlan): FleetPlanFormValue {
  return {
    aracGrupAdi: p.aracGrupAdi,
    sipp: p.sipp,
    donem: p.donem,
    hedefAdet: toNumber(p.hedefAdet),
    aciklama: p.aciklama,
  };
}

/** Form → `POST` / tam değiştirme `PUT` gövdesi (PUT'ta `surum`). */
export function fleetPlanRequest(v: FleetPlanFormValue, base: FleetPlan | null): FleetPlanRequest {
  return {
    aracGrupAdi: metinDegeri(v.aracGrupAdi),
    sipp: metinDegeri(v.sipp),
    donem: metinDegeri(v.donem),
    hedefAdet: v.hedefAdet ?? 0,
    aciklama: metinDegeri(v.aciklama),
    surum: base?.surum ?? null,
  };
}

export interface FleetPlanTotals {
  readonly hedef: number;
  readonly gerceklesen: number;
  readonly kayitli: number;
  /** Toplam hedef − toplam gerçekleşen (satır farklarının toplamı DEĞİL; Blazor toplam satırı). */
  readonly fark: number;
}

/** Toplam satırı: hedef ve gerçekleşen AYRI toplanır. */
export function fleetPlanTotals(rows: readonly FleetPlan[]): FleetPlanTotals {
  let hedef = 0;
  let gerceklesen = 0;
  let kayitli = 0;
  for (const r of rows) {
    hedef += toNumber(r.hedefAdet) ?? 0;
    gerceklesen += toNumber(r.gerceklesen) ?? 0;
    kayitli += toNumber(r.toplamKayitli) ?? 0;
  }
  return { hedef, gerceklesen, kayitli, fark: hedef - gerceklesen };
}
