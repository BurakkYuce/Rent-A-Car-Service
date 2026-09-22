/** OTOMATİK ÜRETİLDİ: scripts/i18n-tipleri.mjs (BLOK_HARITASI + src/i18n/bloklar/*.json). Elle düzenlemeyin. */
import type { Translation } from '@jsverse/transloco';

/** Özellik çeviri blokları: her biri ayrı tembel parça; rota `ceviriBlogu('<blok>')` ile yükler. */
export const CEVIRI_BLOKLARI = {
  'kira-formu': (): Promise<Translation> =>
    import('../../../i18n/bloklar/kira-formu.json').then((m) => m.default),
  kiralar: (): Promise<Translation> =>
    import('../../../i18n/bloklar/kiralar.json').then((m) => m.default),
  panel: (): Promise<Translation> =>
    import('../../../i18n/bloklar/panel.json').then((m) => m.default),
  vitrin: (): Promise<Translation> =>
    import('../../../i18n/bloklar/vitrin.json').then((m) => m.default),
} as const;

export type CeviriBlogu = keyof typeof CEVIRI_BLOKLARI;
