import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, type Observable } from 'rxjs';

import { type ApiHatasi, toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/session-interceptor';

import {
  QUOTATION_ROOT,
  type QuotationDetailResponse,
  type QuotationAcceptResponse,
} from './teklif-modeli';

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
export function acceptExisting(error: ApiHatasi, quotationId: string): KabulMevcudu | null {
  if (error.kod !== 'cakisma' || !(error.cause instanceof HttpErrorResponse)) return null;
  let body: unknown = error.cause.error;
  if (typeof body === 'string') {
    try {
      body = JSON.parse(body);
    } catch {
      return null;
    }
  }
  if (typeof body !== 'object' || body === null) return null;
  const m = (body as Record<string, unknown>)['mevcut'];
  if (typeof m !== 'object' || m === null) return null;
  const { rezervasyonId: reservationId, rezervasyonNo: reservationNo } = m as Record<
    string,
    unknown
  >;
  if (typeof reservationId !== 'string' || typeof reservationNo !== 'string' || !reservationNo) {
    return null;
  }
  return { teklifId: quotationId, rezervasyonId: reservationId, rezervasyonNo: reservationNo };
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
export class QuotationActions {
  private readonly api = inject(ApiIstemcisi);
  private readonly approval = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly teardown = inject(DestroyRef);
  private readonly t = translationFunction();

  readonly inProgress = signal<string | null>(null);
  /** #271 L3: son kabul tekrarında sunucunun bildirdiği rezervasyon (detay bağlantısında numarası görünür). */
  readonly acceptExisting = signal<KabulMevcudu | null>(null);

  gonder(h: TeklifHedefi, after: () => void): void {
    this.calistir(
      h,
      this.api.post<QuotationDetailResponse>(`${QUOTATION_ROOT}/${h.id}/gonder`, null),
      () => this.toast.basari(this.t('teklif.bildirim.gonderildi', { no: h.no })),
      after,
    );
  }

  async reddet(h: TeklifHedefi, after: () => void): Promise<void> {
    if (this.inProgress() !== null) return;
    const yes = await this.approval.ask({
      baslik: this.t('teklif.onay.reddetBaslik'),
      mesaj: this.t('teklif.onay.reddetMesaj', { no: h.no }),
      onayEtiketi: this.t('teklif.onay.reddetOnay'),
      tehlikeli: true,
    });
    if (!yes) return;
    this.calistir(
      h,
      this.api.post<QuotationDetailResponse>(`${QUOTATION_ROOT}/${h.id}/reddet`, null),
      () => this.toast.basari(this.t('teklif.bildirim.reddedildi', { no: h.no })),
      after,
    );
  }

  async kabul(h: TeklifHedefi, after: () => void): Promise<void> {
    if (this.inProgress() !== null) return;
    const yes = await this.approval.ask({
      baslik: this.t('teklif.onay.kabulBaslik'),
      mesaj: this.t('teklif.onay.kabulMesaj', { no: h.no }),
      onayEtiketi: this.t('teklif.onay.kabulOnay'),
    });
    if (!yes) return;
    this.calistir(
      h,
      this.api.post<QuotationAcceptResponse>(`${QUOTATION_ROOT}/${h.id}/kabul`, null),
      (y) =>
        this.toast.basari(
          this.t('teklif.bildirim.kabulEdildi', { no: h.no, rez: y.rezervasyonNo }),
        ),
      after,
      (error) => {
        // Tekrar: ikinci rezervasyon AÇILMADI; kullanıcıya açılmış olanın numarası söylenir.
        const m = acceptExisting(error, h.id);
        if (!m) return false;
        this.acceptExisting.set(m);
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
    request: Observable<T>,
    successful: (y: T) => void,
    after: () => void,
    onConflict?: (error: ApiHatasi) => boolean,
  ): void {
    if (this.inProgress() !== null) return;
    this.inProgress.set(h.id);
    request
      .pipe(
        finalize(() => this.inProgress.set(null)),
        takeUntilDestroyed(this.teardown),
      )
      .subscribe({
        next: (y) => {
          successful(y);
          after();
        },
        error: (raw: unknown) => {
          const error = toApiError(raw);
          // Alansız `cakisma` bandı interceptor'da (sunucu metni); burada kaydın güncel hâline dikkat çekilir.
          if (error.kod === 'cakisma') {
            if (!onConflict?.(error))
              this.toast.bilgi(this.t('teklif.bildirim.zatenIslendi', { no: h.no }));
          } else if (!genelGosterilir(error)) {
            this.toast.hata(error.detay);
          }
          after();
        },
      });
  }
}
