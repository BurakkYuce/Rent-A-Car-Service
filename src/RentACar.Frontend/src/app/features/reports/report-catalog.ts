import { CUSTOMER_REPORTS } from './catalog-customer';
import { LEDGER_REPORTS } from './catalog-finance';
import { SALES_REPORTS } from './catalog-sales';
import type { ReportDefinition } from './report-model';

/** F10.2 — rapor ekranlarının TEK listesi (rota tablosu `reports.routes.ts` bununla testte birebir). */
export const REPORTS: readonly ReportDefinition[] = [
  ...LEDGER_REPORTS,
  ...CUSTOMER_REPORTS,
  ...SALES_REPORTS,
];

/** Rota koduyla rapor; bilinmeyen kod programlama hatasıdır (rota tablosu katalogla testte eşlenir). */
export function findReport(code: string): ReportDefinition {
  const def = REPORTS.find((r) => r.kod === code);
  if (!def) throw new Error(`Bilinmeyen rapor: "${code}".`);
  return def;
}
