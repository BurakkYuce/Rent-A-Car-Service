import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, Validators } from '@angular/forms';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import { moneySubmission } from '@core/form/money-submission';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { TemelStore } from '@core/veri/temel-store';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';

import {
  type AccountKind,
  type DepositBalanceRow,
  type DepositOperation,
  depositRequest,
  financePath,
} from '../finance-model';
import { AccountList, FIN_COMMON, clearAccountOnKindChange, kindOptions } from '../finance-shared';

/**
 * Depozito (emanet) işlemleri (`/app/depozito`, Blazor `Depozito.razor`): Al (F4.4 `depozito/al`, E09), İade (E10),
 * Mahsup (cari borcuna, E11), İrat (gelire al, F4.4 `depozito/irat`, E12 — ONAYLI, geri alınamaz; bu ekrandan kiraya
 * atfedilmez). Tek form, dört işlem; her işlem kendi `Idempotency-Key`'iyle. Sonucu bilinmeyen işlem varken yalnız O
 * işlem aynı anahtarla tekrar gönderilebilir. İade/mahsup/irat tutulanı aşamaz (sunucu, kilit altında).
 */
@Component({
  selector: 'rc-deposit-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...FIN_COMMON],
  providers: [FetchPolicy, AccountList],
  templateUrl: './deposit-page.html',
  styleUrl: '../finance.scss',
})
export class DepositPage implements KaydedilmemisDegisiklikSahibi {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly confirm = inject(OnayServisi);
  private readonly t = ceviriFonksiyonu();
  protected readonly accounts = inject(AccountList);
  protected readonly customers = sunucuSecimKaynagi('musteri');
  protected readonly kindOptions = kindOptions(this.t);
  protected readonly action = moneySubmission<object>({ scope: () => 'depozito' });
  protected readonly operations: readonly DepositOperation[] = ['al', 'iade', 'mahsup', 'irat'];

  protected readonly balances = new TemelStore(() =>
    this.api.get<readonly DepositBalanceRow[]>(financePath('/depozito')),
  );

  protected readonly form = new FormGroup({
    cari: new FormControl<SecimSecenegi | null>(null, Validators.required),
    tutar: new FormControl<string | null>(null, Validators.required),
    hesap: new FormControl<AccountKind | null>('Kasa', Validators.required),
    hesapId: new FormControl<string | null>(null),
  });
  private readonly accountKind = toSignal(this.form.controls.hesap.valueChanges, {
    initialValue: this.form.controls.hesap.value,
  });
  protected readonly accountOptions = computed(() => this.accounts.options(this.accountKind()));

  constructor() {
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
    // Sonucu bilinmeyen işlem (sayfa kapanıp açıldıysa) aynı gövde + anahtarla KİLİTLİ geri gelir.
    this.action.restore(this.form);
    this.accounts.load();
    clearAccountOnKindChange(this.accounts, this.form.controls.hesap, this.form.controls.hesapId);
    inject(FetchPolicy).baglan({
      parametre: computed(() => 0),
      yukle: () => this.balances.yukle(),
    });
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.action.pending() || this.form.dirty;
  }

  kaydedilmemisDegisiklikMesaji(): string | null {
    return this.action.pending() ? this.t('finans.islem.terkMesaji') : null;
  }

  /** Donmuş işlemin adı (bant ve "tekrar" düğmesi için). */
  protected readonly frozenOperation = computed(() => {
    const f = this.action.frozen();
    return f ? (this.operations.find((o) => f.path.endsWith(`/depozito/${o}`)) ?? null) : null;
  });

  protected run(op: DepositOperation): void {
    void this.action.run<{ id: string }>({
      form: this.form,
      fieldMap: () => ({ cariId: 'cari' }),
      build: () => {
        const v = this.form.getRawValue();
        const body = depositRequest(op, v.cari?.id ?? '', v);
        return {
          path: financePath(`/depozito/${op}`),
          body,
          content: { tutar: body.tutar, doviz: 'TRY' },
        };
      },
      confirm:
        op === 'irat'
          ? () =>
              this.confirm.sor({
                baslik: this.t('finans.depozito.iratBaslik'),
                mesaj: this.t('finans.depozito.iratOnay'),
                tehlikeli: true,
              })
          : undefined,
      success: () => {
        this.toast.basari(this.t(`finans.depozito.tamam.${op}`));
        const v = this.form.getRawValue();
        this.form.reset({ cari: v.cari, hesap: v.hesap, hesapId: v.hesapId });
      },
      afterDuplicate: () => this.form.controls.tutar.reset(),
      settled: () => this.balances.yenile(),
    });
  }

  /** Donmuş kopyanın tekrarı: kopya hangi işlemse O gider (form ve düğme yok sayılır). */
  protected retry(): void {
    const op = this.frozenOperation();
    if (op) this.run(op);
  }

  protected async abandon(): Promise<void> {
    if (await this.action.abandon()) this.balances.yenile();
  }
}
