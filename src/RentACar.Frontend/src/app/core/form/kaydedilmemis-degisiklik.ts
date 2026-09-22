import { DOCUMENT } from '@angular/common';
import { DestroyRef, Injectable, InjectionToken, inject } from '@angular/core';
import type { CanDeactivateFn } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';

import { SekmeRotaStratejisi } from '@core/sekme/sekme-stratejisi';

/** Kaydedilmemiş değişikliği olan sayfa bileşeni (rota `canDeactivate`'i bunu sorar). */
export interface KaydedilmemisDegisiklikSahibi {
  kaydedilmemisDegisiklikVar(): boolean;
  /**
   * İsteğe bağlı özel onay metni (ör. sonucu bilinmeyen para işlemi: "kasa hareketlerini kontrol edin");
   * `null`/yoksa genel "kaydedilmemiş değişiklik" metni.
   */
  kaydedilmemisDegisiklikMesaji?(): string | null;
}

/** Bileşenin özel terk metni (varsa). Tip güvenli yoklama — sekme servisi de kullanır. */
export function terkMesaji(bilesen: unknown): string | null {
  if (typeof bilesen !== 'object' || bilesen === null) return null;
  const f = (bilesen as Partial<KaydedilmemisDegisiklikSahibi>).kaydedilmemisDegisiklikMesaji;
  return typeof f === 'function' ? (f.call(bilesen) ?? null) : null;
}

export type OnayIstemi = (mesaj: string) => boolean | Promise<boolean>;

/**
 * Onay penceresi. Varsayılan tarayıcının `confirm`'ü (bağımlılıksız, erişilebilir, Esc = vazgeç);
 * F3.3'ün CDK onay diyaloğu bu token'ı değiştirir, guard değişmez.
 */
export const ONAY_ISTEMI = new InjectionToken<OnayIstemi>('ONAY_ISTEMI', {
  providedIn: 'root',
  factory: () => {
    const pencere = inject(DOCUMENT).defaultView;
    return (mesaj) => pencere?.confirm(mesaj) ?? true;
  },
});

/** Tam sayfa gezinme (Blazor ekranı). Testlerde değiştirilir (jsdom gezinemez). */
export const TAM_SAYFA_GEZINMESI = new InjectionToken<(adres: string) => void>(
  'TAM_SAYFA_GEZINMESI',
  {
    providedIn: 'root',
    factory: () => {
      const pencere = inject(DOCUMENT).defaultView;
      return (adres) => pencere?.location.assign(adres);
    },
  },
);

/**
 * Kullanıcı ayrılmayı TOPLU onayladığında (F3.2 kabuk: Blazor ekranına geçiş, çıkış) guard ve
 * `beforeunload` aynı soruyu ikinci kez sormasın diye bayrak. Onay kabukta tüm sekmeler için bir kez
 * alınır (`SekmeServisi.ayrilmaOnayi`).
 */
@Injectable({ providedIn: 'root' })
export class SayfaTerki {
  private readonly pencere = inject(DOCUMENT).defaultView;
  private readonly gezin = inject(TAM_SAYFA_GEZINMESI);
  private onay = false;

  /** Onay alındı: şu an ayrılma soruları atlanır. */
  get onaylandi(): boolean {
    return this.onay;
  }

  /** Onaylanmış uygulama içi işlem (çıkış): süresince guard sormaz, bitince bayrak iner. */
  async onayliCalistir<T>(is: () => Promise<T>): Promise<T> {
    this.onay = true;
    try {
      return await is();
    } finally {
      this.onay = false;
    }
  }

  /**
   * Onaylanmış tam sayfa terk (SPA dışı adres): `beforeunload` ikinci kez sormaz. Sayfa geri/ileri
   * önbelleğinden dönerse (`pageshow`) bayrak iner, koruma yeniden devrededir.
   */
  tamSayfayaGit(adres: string): void {
    this.onay = true;
    this.pencere?.addEventListener('pageshow', () => (this.onay = false), { once: true });
    this.gezin(adres);
  }
}

/**
 * Uygulama içi gezinmede (rota değişimi) kirli formu korur. Rotaya `canDeactivate: [kaydedilmemisDegisiklikGuard]`.
 * Başarılı kayıttan sonra form `markAsPristine` edildiği için soru gelmez.
 *
 * Sekmeli çalışma alanı (F3.2): sayfanın sekmesi AÇIK kalıyorsa (başka sekmeye geçiliyor) bileşen
 * arka planda yaşamaya devam eder, değişiklik kaybolmaz → sorulmaz. Sekme kapatılırken, sekme dışı
 * sayfada ve oturum/sekme temizliğinde sorulur.
 */
export const kaydedilmemisDegisiklikGuard: CanDeactivateFn<
  Partial<KaydedilmemisDegisiklikSahibi> | null
> = (bilesen, mevcutRota) => {
  if (!bilesen?.kaydedilmemisDegisiklikVar?.()) return true;
  if (inject(SekmeRotaStratejisi).saklanacakMi(mevcutRota)) return true;
  if (inject(SayfaTerki).onaylandi) return true;
  const mesaj =
    terkMesaji(bilesen) ?? inject(TranslocoService).translate<string>('form.kaydedilmemis.onay');
  return inject(ONAY_ISTEMI)(mesaj);
};

/** Bileşen `KaydedilmemisDegisiklikSahibi` ve şu an kirli mi (tip güvenli yoklama). */
export function kirliBilesenMi(bilesen: unknown): boolean {
  if (typeof bilesen !== 'object' || bilesen === null) return false;
  const yoklama = (bilesen as Partial<KaydedilmemisDegisiklikSahibi>).kaydedilmemisDegisiklikVar;
  return typeof yoklama === 'function' && yoklama.call(bilesen) === true;
}

/**
 * Sekme kapatma / yenileme / Blazor ekranına tam sayfa geçiş için `beforeunload`. Bileşen
 * kurucusunda (enjeksiyon bağlamı) çağrılır; bileşen yok olunca dinleyici kalkar. Tarayıcı kendi
 * sabit metnini gösterir (özel metin 2016'dan beri yok sayılıyor).
 */
export function sayfaTerkKorumasi(kirliMi: () => boolean): void {
  const pencere = inject(DOCUMENT).defaultView;
  const terk = inject(SayfaTerki);
  if (!pencere) return;
  const dinleyici = (olay: BeforeUnloadEvent): void => {
    if (terk.onaylandi || !kirliMi()) return;
    olay.preventDefault();
    // Eski tarayıcılar yalnız returnValue'ya bakar.
    olay.returnValue = '';
  };
  pencere.addEventListener('beforeunload', dinleyici);
  inject(DestroyRef).onDestroy(() => pencere.removeEventListener('beforeunload', dinleyici));
}
