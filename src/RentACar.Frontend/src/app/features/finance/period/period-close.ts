import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, Validators } from '@angular/forms';
import { finalize } from 'rxjs';

import { ConfirmGate } from '@core/form/money-submission';
import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { clearServerErrors, applyServerErrors } from '@core/form/sunucu-hatalari';
import { formatDay } from '@core/form/tarih-girdisi';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/session-interceptor';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { TemelStore } from '@core/veri/temel-store';

import { type PeriodCloseState, financePath } from '../finance-model';
import { FIN_COMMON, toAmount } from '../finance-shared';

/**
 * Dönem Kapanışı (`/app/donem-kapanis`, Blazor `DonemKapanis.razor`): mevcut kilit, güncel mizan (sunucu; bakiye
 * toplamı 0 olmalı), kapanışın sıfırlayacağı Gelir/Gider önizlemesi (sunucu). "Dönemi Kapat" kapanış fişi + kilit
 * (E36 yapısal — anahtar yok; aynı/önceki tarihe ikinci kapanış 400), "Kilidi Kaldır" fişi GERİ ALMAZ. İkisi de onaylı,
 * çift tık tek istek. Hatalar alana (`kapanisTarihi`) ya da forma yazılır.
 */
@Component({
  selector: 'rc-period-close',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...FIN_COMMON],
  providers: [FetchPolicy],
  templateUrl: './period-close.html',
  styleUrl: '../finance.scss',
})
export class PeriodClose {
  private readonly api = inject(ApiIstemcisi);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = translationFunction();
  private readonly gate = new ConfirmGate();

  protected readonly state = new TemelStore(() =>
    this.api.get<PeriodCloseState>(financePath('/donem-kapanis')),
  );
  protected readonly busy = signal(false);
  protected readonly errors = signal<readonly string[]>([]);
  protected readonly form = new FormGroup({
    kapanisTarihi: new FormControl<string | null>(null, Validators.required),
  });
  protected readonly lockText = computed(() => {
    const d = this.state.veri()?.kapanisTarihi;
    return d ? formatDay(d) : null;
  });
  protected readonly profit = computed(() => (toAmount(this.state.veri()?.donemSonucu) ?? 0) >= 0);

  constructor() {
    inject(FetchPolicy).connect({ parametre: computed(() => 0), yukle: () => this.state.yukle() });
  }

  protected async close(): Promise<void> {
    if (this.busy()) return;
    clearServerErrors(this.form);
    this.errors.set([]);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const day = this.form.controls.kapanisTarihi.value;
    const yes = await this.gate.ask(() =>
      this.confirm.ask({
        baslik: this.t('finans.donem.kapatBaslik'),
        mesaj: this.t('finans.donem.kapatOnay'),
        onayEtiketi: this.t('finans.donem.kapat'),
        tehlikeli: true,
      }),
    );
    if (!yes) return;
    this.post('/donem-kapanis/kilitle', { kapanisTarihi: day }, 'finans.donem.kapatildi');
  }

  protected async unlock(): Promise<void> {
    if (this.busy()) return;
    const yes = await this.gate.ask(() =>
      this.confirm.ask({
        baslik: this.t('finans.donem.acBaslik'),
        mesaj: this.t('finans.donem.acOnay'),
        onayEtiketi: this.t('finans.donem.ac'),
        tehlikeli: true,
      }),
    );
    if (!yes) return;
    this.post('/donem-kapanis/ac', null, 'finans.donem.acildi');
  }

  private post(
    path: '/donem-kapanis/kilitle' | '/donem-kapanis/ac',
    body: object | null,
    done: 'finans.donem.kapatildi' | 'finans.donem.acildi',
  ): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.api
      .post<null>(financePath(path), body)
      .pipe(
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.toast.basari(this.t(done));
          this.form.reset();
          this.state.yenile();
        },
        error: (raw: unknown) => {
          const e = toApiError(raw);
          const rest = applyServerErrors(this.form, e.alanlar);
          if (e.alanlar === undefined && !genelGosterilir(e)) this.errors.set([e.detay]);
          else if (rest.length > 0) this.errors.set(rest);
          this.state.yenile();
        },
      });
  }
}
