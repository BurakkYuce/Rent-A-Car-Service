import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, type Observable } from 'rxjs';

import { type ApiHatasi, apiHatasinaCevir } from '@core/api/api-hatasi';
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

/** #271 L3: kabul tekrarında (409 `cakisma`) sunucunun bildirdiği, ZATEN açılmış rezervasyon. */
export interface KabulMevcudu {
  readonly teklifId: string;
  readonly rezervasyonId: string;
  readonly rezervasyonNo: string;
}

/**
 * 409 `cakisma` gövdesindeki `mevcut{rezervasyonId, rezervasyonNo}` (TeklifApi — eklemeli uzantı). Çekirdek
 * `ApiHatasi` yalnız `mukerrer`'in `mevcut`'unu tiplediği için ham yanıt (`cause`) burada okunur; biçimsizse
 * `null` (uydurma rezervasyon gösterilmez).
 */
export function kabulMevcudu(hata: ApiHatasi, teklifId: string): KabulMevcudu | null {
  if (hata.kod !== 'cakisma' || !(hata.cause instanceof HttpErrorResponse)) return null;
  let govde: unknown = hata.cause.error;
  if (typeof govde === 'string') {
    try {
      govde = JSON.parse(govde);
    } catch {
      return null;
    }
  }
  if (typeof govde !== 'object' || govde === null) return null;
  const m = (govde as Record<string, unknown>)['mevcut'];
  if (typeof m !== 'object' || m === null) return null;
  const { rezervasyonId, rezervasyonNo } = m as Record<string, unknown>;
  if (typeof rezervasyonId !== 'string' || typeof rezervasyonNo !== 'string' || !rezervasyonNo) {
    return null;
  }
  return { teklifId, rezervasyonId, rezervasyonNo };
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
  /** #271 L3: son kabul tekrarında sunucunun bildirdiği rezervasyon (detay bağlantısında numarası görünür). */
  readonly kabulMevcudu = signal<KabulMevcudu | null>(null);

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
      (hata) => {
        // Tekrar: ikinci rezervasyon AÇILMADI; kullanıcıya açılmış olanın numarası söylenir.
        const m = kabulMevcudu(hata, h.id);
        if (!m) return false;
        this.kabulMevcudu.set(m);
        this.toast.bilgi(
          this.t('teklif.bildirim.zatenKabulEdildi', { no: h.no, rez: m.rezervasyonNo }),
        );
        return true;
      },
    );
  }

  /** `cakismada`: 409 `cakisma`'yı eyleme özgü bildirir; `true` dönerse genel "işlenmiş" bildirimi gösterilmez. */
  private calistir<T>(
    h: TeklifHedefi,
    istek: Observable<T>,
    basarili: (y: T) => void,
    sonra: () => void,
    cakismada?: (hata: ApiHatasi) => boolean,
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
            if (!cakismada?.(hata))
              this.toast.bilgi(this.t('teklif.bildirim.zatenIslendi', { no: h.no }));
          } else if (!genelGosterilir(hata)) {
            this.toast.hata(hata.detay);
          }
          sonra();
        },
      });
  }
}
