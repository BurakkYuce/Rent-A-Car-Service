import { inject, Injectable, InjectionToken } from '@angular/core';
import type { CanActivateFn, Route, Routes } from '@angular/router';
import { Translation, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { VARSAYILAN_DIL } from './ceviri';
import { CEVIRI_BLOKLARI, type CeviriBlogu } from './ceviri-bloklari';

/**
 * Rota bazlı tembel çeviri. Özellik metinleri ilk pakette DEĞİL: her blok (`src/i18n/bloklar/<blok>.json`)
 * kendi parçasıdır ve rotanın `canActivate`'inde, bileşen oluşmadan ÖNCE çekirdek sözlüğe birleştirilir →
 * ilk çizimde anahtar yanıp sönmez, TS `translate()` de hazır metni bulur. Eksik anahtar davranışı
 * değişmez (`EksikCeviriIsleyici`). Blok parçası yüklenemezse (yayından sonra eski sürüm) gezinme hatası
 * `withNavigationErrorHandler` → `parcaYuklemeHatasiMi` yoluna düşer: sayfa parçasıyla AYNI tek kontrollü yenileme.
 */
export const CEVIRI_BLOK_KAYNAKLARI = new InjectionToken<
  Readonly<Record<CeviriBlogu, () => Promise<Translation>>>
>('CEVIRI_BLOK_KAYNAKLARI', { factory: () => CEVIRI_BLOKLARI });

@Injectable({ providedIn: 'root' })
export class CeviriBloguYukleyici {
  private readonly transloco = inject(TranslocoService);
  private readonly kaynaklar = inject(CEVIRI_BLOK_KAYNAKLARI);
  private readonly yuklemeler = new Map<CeviriBlogu, Promise<void>>();

  /** Bloğu bir kez yükler ve birleştirir; eşzamanlı çağrılar aynı sözü paylaşır, hata sonrası yeniden denenir. */
  yukle(blok: CeviriBlogu): Promise<void> {
    const mevcut = this.yuklemeler.get(blok);
    if (mevcut) return mevcut;
    const yukleme = this.birlestir(blok);
    this.yuklemeler.set(blok, yukleme);
    yukleme.catch(() => this.yuklemeler.delete(blok));
    return yukleme;
  }

  private async birlestir(blok: CeviriBlogu): Promise<void> {
    // Çekirdek önce yüklenmiş olmalı: sonradan gelen `load` sözlüğü ezer (uygulamada başlatıcı zaten yükler).
    await firstValueFrom(this.transloco.load(VARSAYILAN_DIL));
    const ceviri = await this.kaynaklar[blok]();
    this.transloco.setTranslation(ceviri, VARSAYILAN_DIL, { merge: true, emitChange: false });
  }
}

/** Rota koruyucusu: verilen blokları yükler, sonra geçişe izin verir. `canActivate: [ceviriBlogu('panel')]`. */
export function ceviriBlogu(...bloklar: readonly CeviriBlogu[]): CanActivateFn {
  return async () => {
    const yukleyici = inject(CeviriBloguYukleyici);
    await Promise.all(bloklar.map((blok) => yukleyici.yukle(blok)));
    return true;
  };
}

/** Özelliğin rota dizisinin HER rotasına bloğu ekler (`X_ROTALARI = ceviriBloguyla('x', [...])`). */
export function ceviriBloguyla(blok: CeviriBlogu, rotalar: Routes): Routes {
  return rotalar.map((rota): Route => ({
    ...rota,
    canActivate: [ceviriBlogu(blok), ...(rota.canActivate ?? [])],
  }));
}
