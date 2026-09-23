import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, type Observable } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';

import { TEKLIF_KOKU, type TeklifDetayYaniti, type TeklifKabulYaniti } from './teklif-modeli';

export interface TeklifHedefi {
  readonly id: string;
  readonly no: string;
}

/**
 * Teklif durum eylemleri (gönder, reddet, kabul → rezervasyon) — liste ve detay aynı kuralla çağırır.
 * Uçlar anahtarsızdır; durum makinesi yapısal korur, SPA istek boyunca tüm eylemleri kilitler (`suruyor`).
 *
 * **Kabul tekrarı (F5.1 adversarial H1):** ikinci kabul (eşzamanlı, başka sekme ya da yanıtı kaybolan tekrar)
 * sunucuda 409 `cakisma` alır ve ikinci rezervasyon AÇILMAZ. SPA otomatik yeniden göndermez; teklifi yeniden
 * yükler — güncel kayıt Kabul durumunu ve OLUŞAN rezervasyonun bağlantısını gösterir. Her sonuçta `sonra`
 * çağrılır (kayıt/liste yeniden okunur).
 */
@Injectable()
export class TeklifIslemleri {
  private readonly api = inject(ApiIstemcisi);
  private readonly onay = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly yikim = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();

  readonly suruyor = signal<string | null>(null);

  gonder(h: TeklifHedefi, sonra: () => void): void {
    this.calistir(
      h,
      this.api.post<TeklifDetayYaniti>(`${TEKLIF_KOKU}/${h.id}/gonder`, null),
      () => this.toast.basari(this.t('teklif.bildirim.gonderildi', { no: h.no })),
      sonra,
    );
  }

  async reddet(h: TeklifHedefi, sonra: () => void): Promise<void> {
    if (this.suruyor() !== null) return;
    const evet = await this.onay.sor({
      baslik: this.t('teklif.onay.reddetBaslik'),
      mesaj: this.t('teklif.onay.reddetMesaj', { no: h.no }),
      onayEtiketi: this.t('teklif.onay.reddetOnay'),
      tehlikeli: true,
    });
    if (!evet) return;
    this.calistir(
      h,
      this.api.post<TeklifDetayYaniti>(`${TEKLIF_KOKU}/${h.id}/reddet`, null),
      () => this.toast.basari(this.t('teklif.bildirim.reddedildi', { no: h.no })),
      sonra,
    );
  }

  async kabul(h: TeklifHedefi, sonra: () => void): Promise<void> {
    if (this.suruyor() !== null) return;
    const evet = await this.onay.sor({
      baslik: this.t('teklif.onay.kabulBaslik'),
      mesaj: this.t('teklif.onay.kabulMesaj', { no: h.no }),
      onayEtiketi: this.t('teklif.onay.kabulOnay'),
    });
    if (!evet) return;
    this.calistir(
      h,
      this.api.post<TeklifKabulYaniti>(`${TEKLIF_KOKU}/${h.id}/kabul`, null),
      (y) =>
        this.toast.basari(
          this.t('teklif.bildirim.kabulEdildi', { no: h.no, rez: y.rezervasyonNo }),
        ),
      sonra,
    );
  }

  private calistir<T>(
    h: TeklifHedefi,
    istek: Observable<T>,
    basarili: (y: T) => void,
    sonra: () => void,
  ): void {
    if (this.suruyor() !== null) return;
    this.suruyor.set(h.id);
    istek
      .pipe(
        finalize(() => this.suruyor.set(null)),
        takeUntilDestroyed(this.yikim),
      )
      .subscribe({
        next: (y) => {
          basarili(y);
          sonra();
        },
        error: (ham: unknown) => {
          const hata = apiHatasinaCevir(ham);
          // Alansız `cakisma` bandı interceptor'da (sunucu metni); burada kaydın güncel hâline dikkat çekilir.
          if (hata.kod === 'cakisma') {
            this.toast.bilgi(this.t('teklif.bildirim.zatenIslendi', { no: h.no }));
          } else if (!genelGosterilir(hata)) {
            this.toast.hata(hata.detay);
          }
          sonra();
        },
      });
  }
}
