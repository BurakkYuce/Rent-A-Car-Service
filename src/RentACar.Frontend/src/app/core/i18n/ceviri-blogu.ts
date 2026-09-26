import { inject, Injectable, InjectionToken } from '@angular/core';
import type { CanActivateFn, Route, Routes } from '@angular/router';
import { Translation, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { DEFAULT_LANGUAGE } from './ceviri';
import { CEVIRI_BLOKLARI, type CeviriBlogu } from './ceviri-bloklari';

/**
 * Rota bazlı tembel çeviri. Özellik metinleri ilk pakette DEĞİL: her blok (`src/i18n/bloklar/<blok>.json`)
 * kendi parçasıdır ve rotanın `canActivate`'inde, bileşen oluşmadan ÖNCE çekirdek sözlüğe birleştirilir →
 * ilk çizimde anahtar yanıp sönmez, TS `translate()` de hazır metni bulur. Eksik anahtar davranışı
 * değişmez (`EksikCeviriIsleyici`). Blok parçası yüklenemezse (yayından sonra eski sürüm) gezinme hatası
 * `withNavigationErrorHandler` → `parcaYuklemeHatasiMi` yoluna düşer: sayfa parçasıyla AYNI tek kontrollü yenileme.
 */
export const TRANSLATION_BLOCK_SOURCES = new InjectionToken<
  Readonly<Record<CeviriBlogu, () => Promise<Translation>>>
>('CEVIRI_BLOK_KAYNAKLARI', { factory: () => CEVIRI_BLOKLARI });

@Injectable({ providedIn: 'root' })
export class TranslationBlockLoader {
  private readonly transloco = inject(TranslocoService);
  private readonly sources = inject(TRANSLATION_BLOCK_SOURCES);
  private readonly loads = new Map<CeviriBlogu, Promise<void>>();

  /** Bloğu bir kez yükler ve birleştirir; eşzamanlı çağrılar aynı sözü paylaşır, hata sonrası yeniden denenir. */
  yukle(block: CeviriBlogu): Promise<void> {
    const existing = this.loads.get(block);
    if (existing) return existing;
    const loading = this.birlestir(block);
    this.loads.set(block, loading);
    loading.catch(() => this.loads.delete(block));
    return loading;
  }

  private async birlestir(block: CeviriBlogu): Promise<void> {
    // Çekirdek önce yüklenmiş olmalı: sonradan gelen `load` sözlüğü ezer (uygulamada başlatıcı zaten yükler).
    await firstValueFrom(this.transloco.load(DEFAULT_LANGUAGE));
    const translation = await this.sources[block]();
    this.transloco.setTranslation(translation, DEFAULT_LANGUAGE, {
      merge: true,
      emitChange: false,
    });
  }
}

/** Rota koruyucusu: verilen blokları yükler, sonra geçişe izin verir. `canActivate: [ceviriBlogu('panel')]`. */
export function ceviriBlogu(...blocks: readonly CeviriBlogu[]): CanActivateFn {
  return async () => {
    const loader = inject(TranslationBlockLoader);
    await Promise.all(blocks.map((block) => loader.yukle(block)));
    return true;
  };
}

/** Özelliğin rota dizisinin HER rotasına bloğu ekler (`X_ROTALARI = ceviriBloguyla('x', [...])`). */
export function withTranslationBlock(block: CeviriBlogu, routes: Routes): Routes {
  return routes.map((route): Route => ({
    ...route,
    canActivate: [ceviriBlogu(block), ...(route.canActivate ?? [])],
  }));
}
