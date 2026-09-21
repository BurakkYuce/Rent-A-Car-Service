import { DestroyRef, inject, Injectable } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';

import { ToastServisi } from './toast-servisi';
import { UyariBandiServisi } from './uyari-bandi-servisi';

/** URL'den gelen mesajın en çok bu kadarı gösterilir (uzun sahte metinle ekran doldurulamasın). */
export const SORGU_MESAJI_SINIRI = 300;

/**
 * `?bilgi=` / `?hata=` sorgu parametreleri (Blazor `Sonuc` sözleşmesi, eski ekranlardan gelen
 * yönlendirmeler): `bilgi` → başarı toast'u, `hata` → hata bandı. BİR kez gösterilir, sonra URL'den
 * silinir (`replaceUrl` — geri tuşu mesajı yeniden göstermez); diğer parametreler ve `#sekme=` korunur.
 * Metin yalnız metin olarak basılır (Angular kaçışlar), kısaltılır.
 */
@Injectable({ providedIn: 'root' })
export class SorguMesajlari {
  private readonly router = inject(Router);
  private readonly toast = inject(ToastServisi);
  private readonly bant = inject(UyariBandiServisi);
  private readonly destroyRef = inject(DestroyRef);
  private basladi = false;

  baslat(): void {
    if (this.basladi) return;
    this.basladi = true;
    const abonelik = this.router.events.subscribe((olay) => {
      if (olay instanceof NavigationEnd) this.isle();
    });
    this.destroyRef.onDestroy(() => abonelik.unsubscribe());
  }

  /** Geçerli URL'deki mesajları gösterir ve siler; mesaj yoksa hiçbir şey yapmaz. */
  isle(): void {
    const parametreler = this.router.parseUrl(this.router.url).queryParamMap;
    const bilgi = kisalt(parametreler.get('bilgi'));
    const hata = kisalt(parametreler.get('hata'));
    if (!parametreler.has('bilgi') && !parametreler.has('hata')) return;

    // Önce URL temizlenir, SONRA gösterilir: temizleyen gezinme bandı "eski bant" sayıp kapatmasın.
    void this.router
      .navigate([], {
        queryParams: { bilgi: null, hata: null },
        queryParamsHandling: 'merge',
        preserveFragment: true,
        replaceUrl: true,
      })
      .finally(() => {
        if (bilgi) this.toast.basari(bilgi);
        if (hata) this.bant.goster({ tur: 'hata', mesaj: hata });
      });
  }
}

function kisalt(deger: string | null): string | null {
  const metin = deger?.trim();
  if (!metin) return null;
  return metin.length > SORGU_MESAJI_SINIRI ? `${metin.slice(0, SORGU_MESAJI_SINIRI)}…` : metin;
}
