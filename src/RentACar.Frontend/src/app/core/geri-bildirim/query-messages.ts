import { DestroyRef, inject, Injectable } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';

import { translationFunction } from '@core/i18n/ceviri';

import { ToastService } from './toast-service';
import { WarningBannerService } from './warning-banner-service';

/** `?hata=` için bilinen kodlar (sunucu `Cutover.ErrorTarget` ile aynı küme). */
export const ERROR_CODES = [
  'yetki_yok',
  'bulunamadi',
  'oturum_yok',
  'dogrulama',
  'beklenmeyen',
] as const;

/** `?bilgi=` için bilinen kodlar. */
export const INFO_CODES = ['kaydedildi'] as const;

type ErrorCode = (typeof ERROR_CODES)[number];
type InfoCode = (typeof INFO_CODES)[number];

/** Destek kodu biçimi: trace id (16–32 hex). Başka hiçbir metin banda taşınmaz. */
const SUPPORT_CODE = /^[0-9a-f]{16,32}$/i;

/**
 * `?bilgi=` / `?hata=` sorgu parametreleri (sunucunun eski adres / hata yönlendirmeleri): `bilgi` → başarı toast'u,
 * `hata` → hata bandı. BİR kez gösterilir, sonra URL'den silinir (`replaceUrl` — geri tuşu mesajı yeniden göstermez);
 * diğer parametreler ve `#sekme=` korunur.
 *
 * **İçerik sahteciliğine kapalı (F13 sonrası güvenlik):** sorgu değeri ASLA metin olarak gösterilmez — yalnız bilinen
 * KODLAR (`yetki_yok`, `bulunamadi`…) kendi çeviri metnine çevrilir; bilinmeyen/serbest değer genel metne düşer.
 * Böylece bir saldırgan `/app/panel?hata=Hesabınız askıya alındı, şu numarayı arayın` gibi bir bağlantıyla bizim
 * bandımızda kendi metnini gösteremez. `destek` yalnız trace id biçimindeyse (hex) eklenir.
 */
@Injectable({ providedIn: 'root' })
export class QueryMessages {
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly bant = inject(WarningBannerService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = translationFunction();
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
    if (!parameters.has('bilgi') && !parameters.has('hata')) return;
    const info = parameters.has('bilgi') ? this.infoText(parameters.get('bilgi')) : null;
    const error = parameters.has('hata')
      ? this.errorText(parameters.get('hata'), parameters.get('destek'))
      : null;

    // Önce URL temizlenir, SONRA gösterilir: temizleyen gezinme bandı "eski bant" sayıp kapatmasın.
    void this.router
      .navigate([], {
        queryParams: { bilgi: null, hata: null, destek: null },
        queryParamsHandling: 'merge',
        preserveFragment: true,
        replaceUrl: true,
      })
      .finally(() => {
        if (info) this.toast.basari(info);
        if (error) this.bant.show({ tur: 'hata', mesaj: error });
      });
  }

  private infoText(value: string | null): string {
    const code = value?.trim() ?? '';
    return (INFO_CODES as readonly string[]).includes(code)
      ? this.t(`geriBildirim.sorgu.bilgi.${code as InfoCode}`)
      : this.t('geriBildirim.sorgu.bilgi.genel');
  }

  private errorText(value: string | null, support: string | null): string {
    const code = value?.trim() ?? '';
    const text = (ERROR_CODES as readonly string[]).includes(code)
      ? this.t(`geriBildirim.sorgu.hata.${code as ErrorCode}`)
      : this.t('geriBildirim.sorgu.hata.genel');
    const kod = support?.trim() ?? '';
    return code === 'beklenmeyen' && SUPPORT_CODE.test(kod)
      ? `${text} ${this.t('geriBildirim.sorgu.destekKodu', { kod })}`
      : text;
  }
}
