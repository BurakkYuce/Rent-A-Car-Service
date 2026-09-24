import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChildren,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';

import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';

import {
  AccountList,
  CustomerBalanceSource,
  FIN_COMMON,
  balanceSide,
  followCustomerQuery,
  labelFromData,
} from '../finance-shared';
import { CashOperationForm } from './cash-operation-form';

/**
 * Nakit İşlem (`/app/finans/nakit-islem`, Blazor `NakitIslem.razor`): cariye gitmeden bağımsız tahsilat / ödeme.
 * PARA MANTIĞI BURADA YOK — iki form F4.4 `finans/tahsilat|odeme` uçlarına gider (Blazor ile aynı servis). Cari
 * `?cariId=` ile de gelir (ekstreden/kasadan derin bağlantı). Bakiye sunucudan; işlem sonrası yenilenir.
 */
@Component({
  selector: 'rc-cash-operation-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...FIN_COMMON, CashOperationForm],
  providers: [FetchPolicy, AccountList, CustomerBalanceSource],
  templateUrl: './cash-operation-page.html',
  styleUrl: '../finance.scss',
})
export class CashOperationPage implements KaydedilmemisDegisiklikSahibi {
  protected readonly balance = inject(CustomerBalanceSource).store;
  private readonly t = ceviriFonksiyonu();
  protected readonly customers = sunucuSecimKaynagi('musteri');
  protected readonly customer = new FormControl<SecimSecenegi | null>(null);
  protected readonly cariId = signal<string | null>(
    inject(ActivatedRoute).snapshot.queryParamMap.get('cariId'),
  );
  protected readonly side = computed(() => balanceSide(this.balance.veri()?.bakiye));
  private readonly forms = viewChildren(CashOperationForm);

  constructor() {
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
    this.customer.valueChanges.pipe(takeUntilDestroyed()).subscribe((c) => {
      if (c && c.id !== this.cariId()) this.cariId.set(c.id);
    });
    // Sonucu bilinmeyen işlem varken cari değiştirilemez: donmuş kopya o cariye aittir, form yeniden kurulursa kaybolur.
    effect(() => {
      const locked = this.forms().some((f) => f.pending());
      untracked(() => (locked ? this.customer.disable() : this.customer.enable()));
    });
    // `?cariId=` izlenir (kalıcı sekmede başka cariyle açılış); seçici etiketi bakiye yanıtının (KVKK kurallı) adından.
    followCustomerQuery(this.cariId, this.customer, () => this.forms().some((f) => f.pending()));
    effect(() => {
      const b = this.balance.veri();
      const id = this.cariId();
      untracked(() => labelFromData(this.customer, id, b));
    });
    inject(FetchPolicy).baglan({
      parametre: this.cariId.asReadonly(),
      yukle: (id) => (id ? this.balance.yukle(id) : this.balance.sifirla()),
      sifirla: () => this.balance.sifirla(),
    });
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.forms().some((f) => f.pending() || f.dirty());
  }

  kaydedilmemisDegisiklikMesaji(): string | null {
    return this.forms().some((f) => f.pending()) ? this.t('finans.islem.terkMesaji') : null;
  }

  protected reload(): void {
    this.balance.yenile();
  }
}
