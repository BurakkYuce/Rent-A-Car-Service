import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { type UnsavedChangesOwner, pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { requestContext } from '@core/oturum/request-context';
import { TemelStore } from '@core/veri/temel-store';
import { serverSelectionSource } from '@shared/form/arama-secim/selection-source';
import { formSubmission } from '@shared/form/form-submission';

import { RF_SHARED } from '../../rezervasyonlar/ortak';
import {
  RESERVATION_ROOT,
  optionList,
  defaultDates,
  type ReservationFormOptions,
} from '../../rezervasyonlar/rezervasyon-modeli';
import type { DayText } from '@core/form/tarih-girdisi';
import {
  QUOTATION_ROOT,
  validityValidator,
  validityEarliest,
  createQuotationForm,
  quotationBody,
  type CreateQuotationResponse,
} from '../teklif-modeli';

/**
 * Yeni teklif (`/app/teklifler/yeni`) — Blazor QuotationList "+ Yeni Teklif" formu. Tutar/gün/KDV sunucuda (fiyat
 * motoru); hata formu silmez (`formGonderimi`). Fiyat türü seçenekleri rezervasyon formuyla ortak uçtan
 * (`/rezervasyonlar/form-secenekleri`); ön-seçim tenant varsayılanı, yoksa listenin ilki ("Otomatik") — Blazor
 * davranışı. Kayıttan sonra teklifin kaydına gidilir; sekme sonraki teklif için temiz forma döner.
 */
@Component({
  selector: 'rc-teklif-formu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...RF_SHARED, RouterLink],
  templateUrl: './teklif-formu.html',
  styleUrl: '../../rezervasyonlar/rezervasyon-formu/rezervasyon-formu.scss',
})
export class QuotationFormPage implements UnsavedChangesOwner {
  private readonly api = inject(ApiIstemcisi);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly t = translationFunction();

  readonly form = createQuotationForm();
  protected readonly record = formSubmission();

  private readonly options = new TemelStore(() =>
    this.api.get<ReservationFormOptions>(`${RESERVATION_ROOT}/form-secenekleri`, {
      context: requestContext({ sessiz: true }),
    }),
  );
  protected readonly priceTypes = computed(() =>
    optionList(this.options.veri()?.fiyatTurleri, null),
  );

  protected readonly customerDataSource = serverSelectionSource('musteri');
  protected readonly vehicleSource = serverSelectionSource('arac');
  protected readonly locationDataSource = serverSelectionSource('lokasyon');

  /** Takvimde seçilebilecek en erken geçerlilik günü (başlangıca göre; sunucu kuralıyla aynı). */
  protected readonly validityMin = signal<DayText | null>(null);

  constructor() {
    pageLeaveGuard(() => this.form.dirty);
    const { basTar: startDate, gecerlilik: validity } = this.form.controls;
    validity.addValidators(
      validityValidator((earliest) => this.t('teklif.alan.gecerlilikErken', { enErken: earliest })),
    );
    // Başlangıç değişince geçerlilik yeniden doğrulanır ve takvimin alt sınırı güncellenir.
    startDate.valueChanges.pipe(takeUntilDestroyed()).subscribe((b) => {
      this.validityMin.set(validityEarliest(b));
      validity.updateValueAndValidity({ emitEvent: false });
    });
    this.form.reset(defaultDates());
    this.options.yukle();
    effect(() => {
      const s = this.options.veri();
      untracked(() => {
        const k = this.form.controls.fiyatTuru;
        const first = s?.varsayilanFiyatTuru ?? s?.fiyatTurleri[0] ?? null;
        if (first && k.value === null && k.pristine) k.setValue(first);
      });
    });
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty;
  }

  protected kaydet(): void {
    this.record.gonder(
      this.form,
      () =>
        this.api.post<CreateQuotationResponse>(
          QUOTATION_ROOT,
          quotationBody(this.form.getRawValue()),
        ),
      {
        basarili: (y) => {
          this.toast.basari(this.t('teklif.bildirim.olusturuldu', { no: y.no }));
          const s = this.options.veri();
          this.form.reset({
            ...defaultDates(),
            fiyatTuru: s?.varsayilanFiyatTuru ?? s?.fiyatTurleri[0] ?? null,
          });
          this.record.kilit.yenile();
          void this.router.navigate(['/teklifler', y.id]);
        },
      },
    );
  }
}
