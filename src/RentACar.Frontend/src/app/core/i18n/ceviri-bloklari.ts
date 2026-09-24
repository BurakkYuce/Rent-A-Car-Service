/** OTOMATİK ÜRETİLDİ: scripts/i18n-tipleri.mjs (BLOK_HARITASI + src/i18n/bloklar/*.json). Elle düzenlemeyin. */
import type { Translation } from '@jsverse/transloco';

/** Özellik çeviri blokları: her biri ayrı tembel parça; rota `ceviriBlogu('<blok>')` ile yükler. */
export const CEVIRI_BLOKLARI = {
  arac: (): Promise<Translation> =>
    import('../../../i18n/bloklar/arac.json').then((m) => m.default),
  'arac-finans': (): Promise<Translation> =>
    import('../../../i18n/bloklar/arac-finans.json').then((m) => m.default),
  'kira-formu': (): Promise<Translation> =>
    import('../../../i18n/bloklar/kira-formu.json').then((m) => m.default),
  kiralar: (): Promise<Translation> =>
    import('../../../i18n/bloklar/kiralar.json').then((m) => m.default),
  panel: (): Promise<Translation> =>
    import('../../../i18n/bloklar/panel.json').then((m) => m.default),
  planlama: (): Promise<Translation> =>
    import('../../../i18n/bloklar/planlama.json').then((m) => m.default),
  platform: (): Promise<Translation> =>
    import('../../../i18n/bloklar/platform.json').then((m) => m.default),
  rezervasyon: (): Promise<Translation> =>
    import('../../../i18n/bloklar/rezervasyon.json').then((m) => m.default),
  tanimlar: (): Promise<Translation> =>
    import('../../../i18n/bloklar/tanimlar.json').then((m) => m.default),
  vitrin: (): Promise<Translation> =>
    import('../../../i18n/bloklar/vitrin.json').then((m) => m.default),
} as const;

export type CeviriBlogu = keyof typeof CEVIRI_BLOKLARI;
