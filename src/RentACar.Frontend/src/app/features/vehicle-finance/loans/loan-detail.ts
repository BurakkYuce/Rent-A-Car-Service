import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { type UnsavedChangesOwner, pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { SessionService } from '@core/oturum/session-service';
import { genelGosterilir } from '@core/oturum/session-interceptor';
import { tabContext } from '@core/sekme/tab-state';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { toNumber } from '@features/vehicles/vehicle-model';
import { MoneyPipe, NumberPipe, DatePipe } from '@shared/bicim/bicim-pipe';

import { LOANS, LoanDetailStore, recordPath } from '../finance.store';
import { InstallmentPaymentPanel } from './installment-payment-panel';
import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { PlateChipComponent } from '@shared/plaka/plaka';

/**
 * Araç kredisi kaydı (`/app/arac-kredi/:id`): künye, hesaplanan özet (deftere yazmaz), TAKSİT PLANI (kalan-yöntemi,
 * ödenenler işaretli), "Taksit Öde" (FinanceWrite, sunucunun `yetkiler.taksitOde` bayrağı) ve iptal
 * (OperationsDelete, `yetkiler.iptal`, onaylı). Düğmeler istemci izninden DEĞİL sunucunun bayraklarından.
 */
@Component({
  selector: 'rc-loan-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PlateChipComponent,
    PageBand,
    RouterLink,
    TranslocoPipe,
    InstallmentPaymentPanel,
    MoneyPipe,
    NumberPipe,
    DatePipe,
  ],
  providers: [FetchPolicy, LoanDetailStore],
  templateUrl: './loan-detail.html',
  styleUrl: '../vehicle-finance.scss',
})
export class LoanDetail implements UnsavedChangesOwner {
  protected readonly store = inject(LoanDetailStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly session = inject(SessionService);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly tab = tabContext();
  private readonly t = translationFunction();

  protected readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';
  protected readonly num = toNumber;
  protected readonly cancelling = signal(false);
  protected readonly accounts = computed(() => this.store.accounts.veri() ?? []);
  private readonly paymentPanel = viewChild(InstallmentPaymentPanel);

  constructor() {
    // Sonucu bilinmeyen ödeme varken sayfadan/sekmeden ayrılış sorulur (inceleme L1): donmuş kopya kaybolursa
    // kullanıcı aynı anahtarla tekrar şansını kaybeder.
    pageLeaveGuard(() => this.hasUnsavedChanges());
    const policy = inject(FetchPolicy);
    policy.connect({
      parametre: signal(this.id).asReadonly(),
      yukle: (id) => this.store.detail.yukle(id),
      sifirla: () => this.store.detail.reset(),
    });
    effect(() => {
      const d = this.store.detail.veri();
      if (!d) return;
      untracked(() => {
        this.tab.etiketAyarla(this.t('aracFinans.kredi.sekmeEtiketi', { no: d.no }));
        if (d.yetkiler.taksitOde && this.session.izinVar('FinanceWrite'))
          this.store.accounts.yukle();
      });
    });
  }

  hasUnsavedChanges(): boolean {
    return this.paymentPanel()?.hasPendingPayment() ?? false;
  }

  protected interestPercent(v: number | string): number | null {
    const n = toNumber(v);
    return n === null ? null : n * 100;
  }

  protected reload(): void {
    this.store.detail.yenile();
  }

  protected async cancel(): Promise<void> {
    const d = this.store.detail.veri();
    if (!d || this.cancelling()) return;
    const yes = await this.confirm.ask({
      baslik: this.t('aracFinans.kredi.iptalBaslik'),
      mesaj: this.t('aracFinans.kredi.iptalMesaj', { no: d.no }),
      onayEtiketi: this.t('aracFinans.kredi.iptal'),
      tehlikeli: true,
    });
    if (!yes || this.cancelling()) return;
    this.cancelling.set(true);
    this.api
      .post<null>(recordPath(LOANS, d.id, '/iptal'), null)
      .pipe(
        finalize(() => this.cancelling.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.toast.basari(this.t('aracFinans.kredi.iptalEdildi', { no: d.no }));
          this.reload();
        },
        error: (raw: unknown) => {
          const error = toApiError(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
          this.reload();
        },
      });
  }
}
