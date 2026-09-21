import { DOCUMENT } from '@angular/common';
import { computed, DestroyRef, inject, Injectable, signal } from '@angular/core';
import { BEYAZ, hexNormalize, karistir, okunurTon, SIYAH, uzerindekiMetin } from './renk';

/** Kullanıcının seçtiği tema. `sistem` işletim sistemi tercihini izler. */
export type TemaModu = 'sistem' | 'acik' | 'koyu';
export type EtkinTema = 'acik' | 'koyu';

/** Tercih yalnız tema modunu tutar (kişisel veri değil). */
export const TEMA_ANAHTARI = 'rc.tema';

/**
 * `_tokenlar.scss`'teki zemin renklerinin kopyası: kiracı vurgusunun metin tonu bunlara karşı
 * okunur yapılır. Eşleşmeyi `scripts/kontrast-denetimi.mjs` denetler (lint kapısı).
 */
export const TEMA_ZEMINLERI: Readonly<Record<EtkinTema, readonly string[]>> = {
  acik: ['#f5f7fa', '#ffffff', '#eef2f6', '#e4eaf2', '#e3ebfd'],
  koyu: ['#0d131c', '#151d29', '#1b2533', '#233044', '#1a2a4a'],
};

/** Kiracı vurgusundan türetilen CSS değişkenleri (tokenlar bunları `var(--rc-kiraci-*, …)` ile okur). */
export interface KiraciVurgusu {
  '--rc-kiraci-vurgu': string;
  '--rc-kiraci-vurgu-hover': string;
  '--rc-kiraci-vurgu-uzeri': string;
  '--rc-kiraci-vurgu-metin-acik': string;
  '--rc-kiraci-vurgu-metin-koyu': string;
}

const METIN_ESIGI = 4.5;

/** Tema değişirken tüm CSS geçişlerini bastıran sınıf (`_taban.scss`). */
export const GECIS_YOK = 'rc-gecis-yok';

/**
 * Kiracı rengi → dolgu, üzerinde-durma tonu, üstündeki metin ve iki tema için okunur metin tonu.
 * Geçersiz renkte `null`. Saf: DOM'a dokunmaz, test edilir.
 */
export function kiraciVurgusuTuret(renk: string | null | undefined): KiraciVurgusu | null {
  const vurgu = hexNormalize(renk);
  if (!vurgu) return null;

  // Önce metin rengi; üzerinde-durma tonu metinden UZAKLAŞAN yönde, böylece kontrast yalnız artar.
  const uzeri = uzerindekiMetin(vurgu);
  const hover = karistir(vurgu, uzeri === BEYAZ ? SIYAH : BEYAZ, 0.15);
  return {
    '--rc-kiraci-vurgu': vurgu,
    '--rc-kiraci-vurgu-hover': hover,
    '--rc-kiraci-vurgu-uzeri': uzeri,
    '--rc-kiraci-vurgu-metin-acik': okunurTon(vurgu, TEMA_ZEMINLERI.acik, METIN_ESIGI),
    '--rc-kiraci-vurgu-metin-koyu': okunurTon(vurgu, TEMA_ZEMINLERI.koyu, METIN_ESIGI),
  };
}

const KIRACI_DEGISKENLERI: readonly (keyof KiraciVurgusu)[] = [
  '--rc-kiraci-vurgu',
  '--rc-kiraci-vurgu-hover',
  '--rc-kiraci-vurgu-uzeri',
  '--rc-kiraci-vurgu-metin-acik',
  '--rc-kiraci-vurgu-metin-koyu',
];

function temaModuMu(deger: unknown): deger is TemaModu {
  return deger === 'sistem' || deger === 'acik' || deger === 'koyu';
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
  private readonly kok = this.belge.documentElement;
  private readonly sistemKoyu = signal(false);

  readonly mod = signal<TemaModu>('sistem');
  readonly etkinTema = computed<EtkinTema>(() => {
    const mod = this.mod();
    if (mod !== 'sistem') return mod;
    return this.sistemKoyu() ? 'koyu' : 'acik';
  });

  constructor() {
    const pencere = this.belge.defaultView;
    const sorgu = pencere?.matchMedia?.('(prefers-color-scheme: dark)');
    if (sorgu) {
      this.sistemKoyu.set(sorgu.matches);
      const dinle = (olay: MediaQueryListEvent) =>
        this.gecissiz(() => this.sistemKoyu.set(olay.matches));
      sorgu.addEventListener('change', dinle);
      inject(DestroyRef).onDestroy(() => sorgu.removeEventListener('change', dinle));
    }
    this.uygula(this.sakliModuOku());
  }

  modAyarla(mod: TemaModu): void {
    this.uygula(mod);
    try {
      this.belge.defaultView?.localStorage.setItem(TEMA_ANAHTARI, mod);
    } catch {
      // Gizli pencere / engelli depolama: tercih yalnız bu oturumda geçerli kalır.
    }
  }

  kiraciVurgusuUygula(renk: string | null | undefined): void {
    const vurgu = kiraciVurgusuTuret(renk);
    for (const degisken of KIRACI_DEGISKENLERI) {
      if (vurgu) this.kok.style.setProperty(degisken, vurgu[degisken]);
      else this.kok.style.removeProperty(degisken);
    }
  }

  private uygula(mod: TemaModu): void {
    this.mod.set(mod);
    this.gecissiz(() => {
      if (mod === 'sistem') this.kok.removeAttribute('data-theme');
      else this.kok.setAttribute('data-theme', mod === 'koyu' ? 'dark' : 'light');
    });
  }

  /**
   * Tema değişimi CSS geçişleri kapalıyken yapılır: yoksa zemin 120 ms boyunca eski renkten
   * canlanırken metin anında döner ve ara karelerde kontrast düşer (e2e axe bunu yakaladı).
   * Sistem teması değişiminde de çağrılır; o an süren geçişler iptal olur, son renge atlar.
   */
  private gecissiz(degistir: () => void): void {
    const pencere = this.belge.defaultView;
    this.kok.classList.add(GECIS_YOK);
    degistir();
    if (!pencere) {
      this.kok.classList.remove(GECIS_YOK);
      return;
    }
    void pencere.getComputedStyle(this.kok).backgroundColor; // yeni stili geçişsiz hesaplat
    pencere.setTimeout(() => this.kok.classList.remove(GECIS_YOK), 1);
  }

  private sakliModuOku(): TemaModu {
    try {
      const deger = this.belge.defaultView?.localStorage.getItem(TEMA_ANAHTARI);
      return temaModuMu(deger) ? deger : 'sistem';
    } catch {
      return 'sistem';
    }
  }
}
