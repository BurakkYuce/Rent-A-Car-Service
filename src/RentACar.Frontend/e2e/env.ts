/**
 * Koşum ortamı (e2e tsconfig'i Node tiplerini içermez; yalnız gereken iki alan bildirilir).
 * `ci`: GitHub Actions `CI=true` verir → eksik görsel taban yazılmaz, kırmızı olur.
 * `linux`: görsel tabanlar yalnız Linux imajında karşılaştırılır.
 */
declare const process: {
  readonly env: Readonly<Record<string, string | undefined>>;
  readonly platform: string;
};

export const ORTAM = {
  ci: Boolean(process.env['CI']),
  linux: process.platform === 'linux',
  /**
   * Gerçek backend e2e'si (`*-gercek.spec.ts`, F5.3): çalışan Web sunucusunun kökü (ör. `http://localhost:5351`,
   * SPA `Spa__Dizin` ile o sunucudan). Yoksa bu spec'ler ATLANIR (CI backend kaldırmaz).
   * Kimlik ORTAMDAN — repoda sabit parola yok: firma + üç kullanıcı (Admin, başka şubeye atanmış Operatör,
   * Muhasebe) aynı rastgele parolayla koşum öncesi üretilir, koşum sonrası silinir.
   */
  gercekKok: process.env['RACAR_E2E_KOK'] ?? '',
  gercekFirma: process.env['RACAR_E2E_FIRMA'] ?? 'yucerent',
  gercekSifre: process.env['RACAR_E2E_SIFRE'] ?? '',
  gercekAdmin: process.env['RACAR_E2E_ADMIN'] ?? 'e2e-f53-admin',
  gercekOperator: process.env['RACAR_E2E_OPERATOR'] ?? 'e2e-f53-op',
  gercekMuhasebe: process.env['RACAR_E2E_MUHASEBE'] ?? 'e2e-f53-muh',
} as const;
