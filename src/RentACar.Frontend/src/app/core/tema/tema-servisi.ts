import { DOCUMENT } from '@angular/common';
import { computed, DestroyRef, inject, Injectable, signal } from '@angular/core';
import { WHITE, hexNormalize, mix, readableTone, BLACK, textOn } from './renk';

/** Kullanıcının seçtiği tema. `sistem` işletim sistemi tercihini izler. */
export type ThemeMode = 'sistem' | 'acik' | 'koyu';
export type ActiveTheme = 'acik' | 'koyu';

/** Tercih yalnız tema modunu tutar (kişisel veri değil). */
export const THEME_KEY = 'rc.tema';

/**
 * `_tokenlar.scss`'teki zemin renklerinin kopyası: kiracı vurgusunun metin tonu bunlara karşı
 * okunur yapılır. Eşleşmeyi `scripts/kontrast-denetimi.mjs` denetler (lint kapısı).
 */
export const TEMA_ZEMINLERI: Readonly<Record<ActiveTheme, readonly string[]>> = {
  acik: ['#f4f2ec', '#ffffff', '#ece9e1', '#e2ded3', '#e4e9f7'],
  koyu: ['#141310', '#1c1b17', '#24221d', '#2d2b25', '#1b2a55'],
};

/** Kiracı vurgusundan türetilen CSS değişkenleri (tokenlar bunları `var(--rc-kiraci-*, …)` ile okur). */
export interface KiraciVurgusu {
  '--rc-kiraci-vurgu': string;
  '--rc-kiraci-vurgu-hover': string;
  '--rc-kiraci-vurgu-uzeri': string;
  '--rc-kiraci-vurgu-metin-acik': string;
  '--rc-kiraci-vurgu-metin-koyu': string;
}

const TEXT_THRESHOLD = 4.5;

/** Tema değişirken tüm CSS geçişlerini bastıran sınıf (`_taban.scss`). */
export const NO_TRANSITION = 'rc-gecis-yok';

/**
 * Kiracı rengi → dolgu, üzerinde-durma tonu, üstündeki metin ve iki tema için okunur metin tonu.
 * Geçersiz renkte `null`. Saf: DOM'a dokunmaz, test edilir.
 */
export function deriveTenantAccent(renk: string | null | undefined): KiraciVurgusu | null {
  const accent = hexNormalize(renk);
  if (!accent) return null;

  // Önce metin rengi; üzerinde-durma tonu metinden UZAKLAŞAN yönde, böylece kontrast yalnız artar.
  const over = textOn(accent);
  const hover = mix(accent, over === WHITE ? BLACK : WHITE, 0.15);
  return {
    '--rc-kiraci-vurgu': accent,
    '--rc-kiraci-vurgu-hover': hover,
    '--rc-kiraci-vurgu-uzeri': over,
    '--rc-kiraci-vurgu-metin-acik': readableTone(accent, TEMA_ZEMINLERI.acik, TEXT_THRESHOLD),
    '--rc-kiraci-vurgu-metin-koyu': readableTone(accent, TEMA_ZEMINLERI.koyu, TEXT_THRESHOLD),
  };
}

const TENANT_VARIABLES: readonly (keyof KiraciVurgusu)[] = [
  '--rc-kiraci-vurgu',
  '--rc-kiraci-vurgu-hover',
  '--rc-kiraci-vurgu-uzeri',
  '--rc-kiraci-vurgu-metin-acik',
  '--rc-kiraci-vurgu-metin-koyu',
];

/**
 * Firma durum renkleri (`GET oturum/ben` → `renkler`; Blazor MainLayout'taki adlarla aynı). Her biri
 * `--rc-kiraci-renk-<ad>` değişkenine yazılır; tablo satır renklendirmesi (F4+) bunu
 * `var(--rc-kiraci-renk-gecikenler, <varsayılan>)` ile okur. Listede olmayan ad yok sayılır.
 */
export const TENANT_STATUS_COLORS = [
  'gecikenler',
  'bugun-donecekler',
  'bugun-cikacaklar',
  'opsiyonlu',
  'limit-bakiye',
  'alacakli',
  'rez-atanan-plaka',
  'kiralanmayan',
] as const;

export type TenantStatusColor = (typeof TENANT_STATUS_COLORS)[number];

function isThemeMode(value: unknown): value is ThemeMode {
  return value === 'sistem' || value === 'acik' || value === 'koyu';
}

/**
 * Çalışma zamanı teması (Revlo ThemeService fikrinden, zoneless + signal).
 * - `modAyarla`: `<html data-theme>` yazar (`sistem`'de kaldırır) ve tercihi saklar.
 * - `kiraciVurgusuUygula`: kiracı rengini `--rc-kiraci-*` değişkenlerine yazar; null → varsayılan.
 * CSP: stil CSSOM ile yazılır (inline `<style>` yok).
 */
@Injectable({ providedIn: 'root' })
export class TemaServisi {
  private readonly belge = inject(DOCUMENT);
  private readonly root = this.belge.documentElement;
  private readonly systemDark = signal(false);

  readonly mod = signal<ThemeMode>('sistem');
  readonly activeTheme = computed<ActiveTheme>(() => {
    const mod = this.mod();
    if (mod !== 'sistem') return mod;
    return this.systemDark() ? 'koyu' : 'acik';
  });

  constructor() {
    const window = this.belge.defaultView;
    const query = window?.matchMedia?.('(prefers-color-scheme: dark)');
    if (query) {
      this.systemDark.set(query.matches);
      const listen = (evt: MediaQueryListEvent) =>
        this.withoutTransition(() => this.systemDark.set(evt.matches));
      query.addEventListener('change', listen);
      inject(DestroyRef).onDestroy(() => query.removeEventListener('change', listen));
    }
    this.apply(this.readStoredMode());
  }

  setMode(mod: ThemeMode): void {
    this.apply(mod);
    try {
      this.belge.defaultView?.localStorage.setItem(THEME_KEY, mod);
    } catch {
      // Gizli pencere / engelli depolama: tercih yalnız bu oturumda geçerli kalır.
    }
  }

  applyTenantAccent(renk: string | null | undefined): void {
    const accent = deriveTenantAccent(renk);
    for (const variable of TENANT_VARIABLES) {
      if (accent) this.root.style.setProperty(variable, accent[variable]);
      else this.root.style.removeProperty(variable);
    }
  }

  /**
   * Firma durum renklerini uygular; geçerli `#rrggbb` olmayan ya da bilinmeyen ad atlanır, verilmeyen
   * renk kaldırılır (varsayılana döner). `null` → hepsi kaldırılır (çıkış/firma değişimi).
   */
  applyTenantColors(colors: Readonly<Record<string, string>> | null): void {
    for (const name of TENANT_STATUS_COLORS) {
      const renk = hexNormalize(colors?.[name]);
      const variable = `--rc-kiraci-renk-${name}`;
      if (renk) this.root.style.setProperty(variable, renk);
      else this.root.style.removeProperty(variable);
    }
  }

  private apply(mod: ThemeMode): void {
    this.mod.set(mod);
    this.withoutTransition(() => {
      if (mod === 'sistem') this.root.removeAttribute('data-theme');
      else this.root.setAttribute('data-theme', mod === 'koyu' ? 'dark' : 'light');
    });
  }

  /**
   * Tema değişimi CSS geçişleri kapalıyken yapılır: yoksa zemin 120 ms boyunca eski renkten
   * canlanırken metin anında döner ve ara karelerde kontrast düşer (e2e axe bunu yakaladı).
   * Sistem teması değişiminde de çağrılır; o an süren geçişler iptal olur, son renge atlar.
   */
  private withoutTransition(change: () => void): void {
    const window = this.belge.defaultView;
    this.root.classList.add(NO_TRANSITION);
    change();
    if (!window) {
      this.root.classList.remove(NO_TRANSITION);
      return;
    }
    void window.getComputedStyle(this.root).backgroundColor; // yeni stili geçişsiz hesaplat
    window.setTimeout(() => this.root.classList.remove(NO_TRANSITION), 1);
  }

  private readStoredMode(): ThemeMode {
    try {
      const value = this.belge.defaultView?.localStorage.getItem(THEME_KEY);
      return isThemeMode(value) ? value : 'sistem';
    } catch {
      return 'sistem';
    }
  }
}
