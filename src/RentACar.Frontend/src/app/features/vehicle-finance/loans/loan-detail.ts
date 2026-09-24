import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { sekmeBaglami } from '@core/sekme/sekme-durumu';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { toNumber } from '@features/vehicles/vehicle-model';
import { ParaPipe, SayiPipe, TarihPipe } from '@shared/bicim/bicim-pipe';

import { LOANS, LoanDetailStore, recordPath } from '../finance.store';
import { InstallmentPaymentPanel } from './installment-payment-panel';

/**
 * Araç kredisi kaydı (`/app/arac-kredi/:id`): künye, hesaplanan özet (deftere yazmaz), TAKSİT PLANI (kalan-yöntemi,
 * ödenenler işaretli), "Taksit Öde" (FinanceWrite, sunucunun `yetkiler.taksitOde` bayrağı) ve iptal
 * (OperationsDelete, `yetkiler.iptal`, onaylı). Düğmeler istemci izninden DEĞİL sunucunun bayraklarından.
 */
@Component({
  selector: 'rc-loan-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslocoPipe, InstallmentPaymentPanel, ParaPipe, SayiPipe, TarihPipe],
  providers: [FetchPolicy, LoanDetailStore],
  templateUrl: './loan-detail.html',
  styleUrl: '../vehicle-finance.scss',
})
export class LoanDetail {
  protected readonly store = inject(LoanDetailStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly session = inject(OturumServisi);
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly tab = sekmeBaglami();
  private readonly t = ceviriFonksiyonu();

  protected readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';
  protected readonly num = toNumber;
  protected readonly cancelling = signal(false);
  protected readonly accounts = computed(() => this.store.accounts.veri() ?? []);

  constructor() {
    const policy = inject(FetchPolicy);
    policy.baglan({
      parametre: signal(this.id).asReadonly(),
      yukle: (id) => this.store.detail.yukle(id),
      sifirla: () => this.store.detail.sifirla(),
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

  protected faizYuzde(v: number | string): number | null {
    const n = toNumber(v);
    return n === null ? null : n * 100;
  }

  protected reload(): void {
    this.store.detail.yenile();
  }

  protected async cancel(): Promise<void> {
    const d = this.store.detail.veri();
    if (!d || this.cancelling()) return;
    const yes = await this.confirm.sor({
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
          const error = apiHatasinaCevir(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
          this.reload();
        },
      });
  }
}
