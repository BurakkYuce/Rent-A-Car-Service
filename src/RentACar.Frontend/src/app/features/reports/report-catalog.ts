import { JOB_RUNS, MILEAGE, RESERVATION_SOURCE, VEHICLE_TRACKING } from './catalog-activity';
import { CUSTOMER_REPORTS } from './catalog-customer';
import { LEDGER_REPORTS } from './catalog-finance';
import { SCORECARD } from './catalog-fleet';
import { OCCUPANCY, SERVICE_COST, VEHICLE_DAILY } from './catalog-fleet-detail';
import { FLEET_ANALYSIS, FLEET_STATUS } from './catalog-fleet-status';
import {
  COMPARATIVE,
  INSURANCE_INSPECTION,
  PERIODIC_SERVICE,
  STAFF_SHIFTS,
} from './catalog-operations';
import { SALES_REPORTS } from './catalog-sales';
import type { ReportDefinition } from './report-model';

/** F10.2 — rapor ekranlarının TEK listesi (26; rota tablosu `reports.routes.ts` bununla testte birebir). */
export const REPORTS: readonly ReportDefinition[] = [
  ...LEDGER_REPORTS,
  ...CUSTOMER_REPORTS,
  ...SALES_REPORTS,
  SCORECARD,
  FLEET_ANALYSIS,
  FLEET_STATUS,
  OCCUPANCY,
  VEHICLE_DAILY,
  SERVICE_COST,
  RESERVATION_SOURCE,
  JOB_RUNS,
  VEHICLE_TRACKING,
  MILEAGE,
  PERIODIC_SERVICE,
  INSURANCE_INSPECTION,
  COMPARATIVE,
  STAFF_SHIFTS,
];

/** Rota koduyla rapor; bilinmeyen kod programlama hatasıdır (rota tablosu katalogla testte eşlenir). */
export function findReport(code: string): ReportDefinition {
  const def = REPORTS.find((r) => r.kod === code);
  if (!def) throw new Error(`Bilinmeyen rapor: "${code}".`);
  return def;
}
