import { DestroyRef, inject, Injectable } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';

import { ToastService } from './toast-service';
import { WarningBannerService } from './warning-banner-service';

/** URL'den gelen mesajın en çok bu kadarı gösterilir (uzun sahte metinle ekran doldurulamasın). */
export const QUERY_MESSAGE_LIMIT = 300;

/**
 * `?bilgi=` / `?hata=` sorgu parametreleri (Blazor `Sonuc` sözleşmesi, eski ekranlardan gelen
 * yönlendirmeler): `bilgi` → başarı toast'u, `hata` → hata bandı. BİR kez gösterilir, sonra URL'den
 * silinir (`replaceUrl` — geri tuşu mesajı yeniden göstermez); diğer parametreler ve `#sekme=` korunur.
 * Metin yalnız metin olarak basılır (Angular kaçışlar), kısaltılır.
 */
@Injectable({ providedIn: 'root' })
export class QueryMessages {
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly bant = inject(WarningBannerService);
  private readonly destroyRef = inject(DestroyRef);
  private started = false;

  start(): void {
    if (this.started) return;
    this.started = true;
    const subscription = this.router.events.subscribe((evt) => {
      if (evt instanceof NavigationEnd) this.isle();
    });
    this.destroyRef.onDestroy(() => subscription.unsubscribe());
  }

  /** Geçerli URL'deki mesajları gösterir ve siler; mesaj yoksa hiçbir şey yapmaz. */
  isle(): void {
    const parameters = this.router.parseUrl(this.router.url).queryParamMap;
    const info = shorten(parameters.get('bilgi'));
    const error = shorten(parameters.get('hata'));
    if (!parameters.has('bilgi') && !parameters.has('hata')) return;

    // Önce URL temizlenir, SONRA gösterilir: temizleyen gezinme bandı "eski bant" sayıp kapatmasın.
    void this.router
      .navigate([], {
        queryParams: { bilgi: null, hata: null },
        queryParamsHandling: 'merge',
        preserveFragment: true,
        replaceUrl: true,
      })
      .finally(() => {
        if (info) this.toast.basari(info);
        if (error) this.bant.show({ tur: 'hata', mesaj: error });
      });
  }
}

function shorten(value: string | null): string | null {
  const text = value?.trim();
  if (!text) return null;
  return text.length > QUERY_MESSAGE_LIMIT ? `${text.slice(0, QUERY_MESSAGE_LIMIT)}…` : text;
}
