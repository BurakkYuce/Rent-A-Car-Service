import { DOCUMENT } from '@angular/common';
import { inject, Injectable } from '@angular/core';

import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';

import { SAYFA_YENILEYICI } from './surum-servisi';

/** Yenileme koruması (sessionStorage; `rc.` önekli → çıkışta silinir). */
export const PARCA_YENILEME_ANAHTARI = 'rc.parcaYenileme';
/** Bu süre içinde ikinci kez yenilenmez (döngü koruması). */
export const PARCA_YENILEME_ARALIGI = 60_000;

const PARCA_HATASI_KALIPLARI = [
  /Failed to fetch dynamically imported module/i, // Chromium
  /error loading dynamically imported module/i, // Firefox
  /Importing a module script failed/i, // Safari
  /Loading chunk [\w-]+ failed/i, // webpack biçimi (ChunkLoadError)
];

/** Tembel parça (lazy chunk) yüklenemedi mi? Yayından sonra eski parça silinmişse olur. */
export function parcaYuklemeHatasiMi(hata: unknown): boolean {
  if (hata instanceof Error && hata.name === 'ChunkLoadError') return true;
  const mesaj = hata instanceof Error ? hata.message : typeof hata === 'string' ? hata : '';
  return PARCA_HATASI_KALIPLARI.some((kalip) => kalip.test(mesaj));
}

/**
 * ChunkLoadError → KONTROLLÜ tek yenileme: gidilmek istenen adrese tam sayfa yükleme (yeni sürümün
 * kabuğu yeni parçaları bilir). Son {@link PARCA_YENILEME_ARALIGI} içinde zaten yenilendiyse döngüye
 * girmez; kalıcı hata toast'u gösterir.
 */
@Injectable({ providedIn: 'root' })
export class ParcaHatasiServisi {
  private readonly pencere = inject(DOCUMENT).defaultView;
  private readonly sayfa = inject(SAYFA_YENILEYICI);
  private readonly toast = inject(ToastServisi);
  private readonly t = ceviriFonksiyonu();
  /** Yenileme başlatıldı: aynı hatanın ikinci bildirimi (router + ErrorHandler) yok sayılır. */
  private yenileniyor = false;

  /** @returns yenileme başlatıldıysa (ya da zaten sürüyorsa) `true`. */
  isle(hedefAdres?: string): boolean {
    if (this.yenileniyor) return true;
    const simdi = Date.now();
    const son = Number(this.oku() ?? 0);
    const yakinda =
      Number.isFinite(son) && simdi - son >= 0 && simdi - son < PARCA_YENILEME_ARALIGI;
    // Koruma yazılamıyorsa (engelli depolama) otomatik yenileme de yok: döngü riski.
    if (yakinda || !this.yaz(String(simdi))) {
      this.toast.hata(this.t('surum.yenilemeBasarisiz'), {
        sure: 0,
        eylem: { etiket: this.t('surum.yenile'), calistir: () => this.sayfa.yenile() },
      });
      return false;
    }
    this.yenileniyor = true;
    if (hedefAdres) this.sayfa.git(hedefAdres);
    else this.sayfa.yenile();
    return true;
  }

  private oku(): string | null {
    try {
      return this.pencere?.sessionStorage.getItem(PARCA_YENILEME_ANAHTARI) ?? null;
    } catch {
      return null;
    }
  }

  private yaz(deger: string): boolean {
    try {
      const depo = this.pencere?.sessionStorage;
      if (!depo) return false;
      depo.setItem(PARCA_YENILEME_ANAHTARI, deger);
      return depo.getItem(PARCA_YENILEME_ANAHTARI) === deger;
    } catch {
      return false;
    }
  }
}
