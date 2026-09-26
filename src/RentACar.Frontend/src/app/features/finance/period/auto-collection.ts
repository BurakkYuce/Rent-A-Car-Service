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
import { FormControl, FormGroup, FormRecord, Validators } from '@angular/forms';
import { finalize } from 'rxjs';

import { ConfirmGate } from '@core/form/money-submission';
import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi, type QueryParameters } from '@core/api/api-istemcisi';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/session-interceptor';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { TemelStore } from '@core/veri/temel-store';

import {
  type AccountKind,
  type AutoCollectionList,
  type AutoCollectionRequest,
  type AutoCollectionResult,
  candidateKey,
  financePath,
  queryParams,
} from '../finance-model';
import { FIN_COMMON, kindOptions, toAmount } from '../finance-shared';

/**
 * Otomatik Tahsilat — elle çalıştır (`/app/otomatik-tahsilat`, Blazor `OtomatikTahsilat.razor`): vadesi gelmiş dönem
 * faturalarını seçip keser, isteğe bağlı tahsilatı yazar (`POST finans/otomatik-tahsilat/calistir`, E20 YAPISAL —
 * anahtar yok; aynı dönem ikinci kez çalışmaz, atlananlarda döner). Gece işinden bağımsızdır. Toplamlar döviz kırılımlı
 * (sunucu). Onaylı, çift tık tek istek. Seçim satır kimliğiyle (kira + dönem) tutulur.
 */
@Component({
  selector: 'rc-auto-collection',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...FIN_COMMON],
  providers: [FetchPolicy],
  templateUrl: './auto-collection.html',
  styleUrl: '../finance.scss',
})
export class AutoCollection {
  private readonly api = inject(ApiIstemcisi);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = translationFunction();
  private readonly gate = new ConfirmGate();
  protected readonly key = candidateKey;
  protected readonly kindOptions = kindOptions(this.t);

  protected readonly list = new TemelStore(
    (p: QueryParameters) =>
      this.api.get<AutoCollectionList>(financePath('/otomatik-tahsilat'), { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );
  private readonly params = signal<QueryParameters>({});
  protected readonly filtered = computed(() => Object.keys(this.params()).length > 0);
  protected readonly debtorCount = computed(
    () => (this.list.veri()?.adaylar ?? []).filter((a) => (toAmount(a.cariBakiye) ?? 0) > 0).length,
  );
  protected readonly isPositive = (v: number | string) => (toAmount(v) ?? 0) > 0;

  protected readonly filter = new FormGroup({
    sozlesmeNo: new FormControl<string | null>(null, Validators.maxLength(64)),
    vadeMin: new FormControl<string | null>(null),
    vadeMax: new FormControl<string | null>(null),
    bakiyeli: new FormControl<boolean | null>(false),
  });
  protected readonly form = new FormGroup({
    secim: new FormRecord<FormControl<boolean | null>>({}),
    tahsilat: new FormControl<boolean | null>(true),
    hesap: new FormControl<AccountKind | null>('Kasa', Validators.required),
  });
  protected readonly busy = signal(false);
  protected readonly errors = signal<readonly string[]>([]);
  protected readonly skipped = signal<readonly string[]>([]);

  constructor() {
    inject(FetchPolicy).connect({ parametre: this.params, yukle: (p) => this.list.yukle(p) });
    effect(() => {
      const rows = this.list.veri()?.adaylar ?? [];
      untracked(() => {
        const rec = this.form.controls.secim;
        const old = rec.getRawValue();
        for (const k of Object.keys(rec.controls)) rec.removeControl(k, { emitEvent: false });
        for (const a of rows) {
          const k = candidateKey(a);
          rec.addControl(k, new FormControl<boolean | null>(old[k] ?? false), { emitEvent: false });
        }
      });
    });
  }

  protected check(k: string): FormControl<boolean | null> | null {
    return this.form.controls.secim.controls[k] ?? null;
  }

  protected applyFilter(): void {
    const v = this.filter.getRawValue();
    this.params.set(queryParams({ ...v, bakiyeli: v.bakiyeli === true ? 'true' : null }));
  }

  protected clearFilter(): void {
    this.filter.reset({ bakiyeli: false });
    this.params.set({});
  }

  protected async run(): Promise<void> {
    if (this.busy()) return;
    this.errors.set([]);
    const rows = this.list.veri()?.adaylar ?? [];
    const sel = rows.filter((a) => this.check(candidateKey(a))?.value === true);
    if (sel.length === 0) {
      this.errors.set([this.t('finans.otomatik.secimYok')]);
      return;
    }
    const v = this.form.getRawValue();
    const body: AutoCollectionRequest = {
      secim: sel.map((a) => ({ kiraId: a.kiraId, donemSira: a.donemSira })),
      tahsilat: v.tahsilat === true,
      hesap: v.hesap,
    };
    const yes = await this.gate.ask(() =>
      this.confirm.ask({
        baslik: this.t('finans.otomatik.onayBaslik'),
        mesaj: this.t('finans.otomatik.onayMesaj', { adet: sel.length }),
      }),
    );
    if (!yes || this.busy()) return;
    this.busy.set(true);
    this.api
      .post<AutoCollectionResult>(financePath('/otomatik-tahsilat/calistir'), body)
      .pipe(
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (r) => {
          this.toast.basari(
            this.t('finans.otomatik.sonuc', { kesilen: r.kesilen, tahsilat: r.tahsilat }),
          );
          this.skipped.set(r.atlananlar);
          this.form.controls.secim.reset();
          this.list.yenile();
        },
        error: (raw: unknown) => {
          const e = toApiError(raw);
          const msgs = e.alanlar ? Object.values(e.alanlar).flat() : [];
          if (msgs.length > 0) this.errors.set(msgs);
          else if (!genelGosterilir(e)) this.errors.set([e.detay]);
          this.list.yenile();
        },
      });
  }
}
