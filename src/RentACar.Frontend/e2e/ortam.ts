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
} as const;
