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
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { type UnsavedChangesOwner, pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import type { DayText } from '@core/form/tarih-girdisi';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/session-interceptor';
import { tabContext } from '@core/sekme/tab-state';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { momentValue, textValue } from '@features/planlama-ortak/form-yardimcilari';
import { MoneyPipe, DatePipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { TextInput } from '@shared/form/kontroller/text-input';
import { MoneyInput } from '@shared/form/kontroller/money-input';
import { DatePicker } from '@shared/form/tarih/date-picker';
import { Icon } from '@shared/ikon/icon';

import {
  type Endorsement,
  type EndorsementRequest,
  REGULATION,
  num,
  recordPath,
} from '../service-insurance-model';
import { PolicyDetailStore, RegulationOptionsStore } from '../service-insurance.store';
import { PolicyPayPanel } from './policy-pay-panel';
import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';

/**
 * Sigorta poliçesi kaydı (`/app/regulasyon/sigortalar/:id`) — Blazor `/regulasyon` poliçe satırı + "Öde" formu +
 * "Zeyil (Poliçe Ekleri)" bölümü (Blazor tüm zeyilleri tek tabloda gösteriyordu; burada poliçenin kendi zeyilleri).
 * Ödeme FinanceWrite, zeyil OperationsWrite (sunucu `yetkiler`). Zeyiller BİLGİdir: deftere yazmaz, kalanı değiştirmez.
 */
@Component({
  selector: 'rc-policy-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PageBand,
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    FormErrors,
    Icon,
    TextInput,
    MoneyInput,
    MoneyPipe,
    PolicyPayPanel,
    DatePipe,
    DatePicker,
  ],
  providers: [FetchPolicy, PolicyDetailStore, RegulationOptionsStore],
  templateUrl: './policy-detail.html',
  styleUrl: '../service-insurance.scss',
})
export class PolicyDetail implements UnsavedChangesOwner {
  protected readonly store = inject(PolicyDetailStore);
  private readonly optionsStore = inject(RegulationOptionsStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly tab = tabContext();
  private readonly t = translationFunction();
  private readonly panel = viewChild(PolicyPayPanel);

  protected readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';
  protected readonly num = num;
  protected readonly deleting = signal<string | null>(null);
  protected readonly endorsementTypes = computed(
    () => this.optionsStore.options.veri()?.zeyilTipleri ?? [],
  );

  protected readonly form = new FormGroup({
    zeyilNo: new FormControl<string | null>(null, [Validators.required, Validators.maxLength(32)]),
    tarih: new FormControl<DayText | null>(null, Validators.required),
    tanzim: new FormControl<DayText | null>(null),
    deger: new FormControl<string | null>(null),
    brut: new FormControl<string | null>(null),
    net: new FormControl<string | null>(null),
    fonVergi: new FormControl<string | null>(null),
    tipi: new FormControl<string | null>(null, Validators.maxLength(64)),
    neden: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  protected readonly submission = formSubmission();

  constructor() {
    inject(FetchPolicy).connect({
      parametre: signal(this.id).asReadonly(),
      yukle: (id) => this.store.detail.yukle(id),
      sifirla: () => this.store.detail.reset(),
    });
    effect(() => {
      const d = this.store.detail.veri();
      if (!d) return;
      untracked(() => {
        this.tab.etiketAyarla(`${d.police.plaka} ${d.police.policeNo ?? d.police.tip}`);
        if (d.yetkiler.odeyebilir && this.store.accounts.tur() === 'bos')
          this.store.accounts.yukle();
        if (d.yetkiler.duzenleyebilir && this.optionsStore.options.tur() === 'bos')
          this.optionsStore.options.yukle();
      });
    });
    pageLeaveGuard(() => this.hasUnsavedChanges());
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty || (this.panel()?.hasPendingWork() ?? false);
  }

  /** Uçuştaki / sonucu bilinmeyen ödeme varken özel terk metni (inceleme L2). */
  unsavedChangesMessage(): string | null {
    return this.panel()?.hasPendingPayment() ? this.t('servisSigorta.para.terkMesaji') : null;
  }

  protected reload(): void {
    this.store.detail.yenile();
  }

  protected addEndorsement(): void {
    const v = this.form.getRawValue();
    const body: EndorsementRequest = {
      zeyilNo: textValue(v.zeyilNo),
      tarih: momentValue(v.tarih, null),
      tanzim: momentValue(v.tanzim, null),
      deger: v.deger,
      brut: v.brut,
      net: v.net,
      fonVergi: v.fonVergi,
      tipi: textValue(v.tipi),
      neden: textValue(v.neden),
    };
    this.submission.gonder(
      this.form,
      (key) =>
        this.api.post<Endorsement>(
          recordPath(`${REGULATION}/sigortalar`, this.id, '/zeyiller'),
          body,
          { islemAnahtari: key },
        ),
      {
        basarili: (z) => {
          this.toast.basari(this.t('servisSigorta.zeyil.eklendi', { no: z.zeyilNo }));
          this.form.reset();
          this.reload();
        },
      },
    );
  }

  protected async deleteEndorsement(z: Endorsement): Promise<void> {
    if (this.deleting() !== null) return;
    const yes = await this.confirm.ask({
      baslik: this.t('servisSigorta.zeyil.silBaslik'),
      mesaj: this.t('servisSigorta.zeyil.silMesaj', { no: z.zeyilNo }),
      onayEtiketi: this.t('servisSigorta.sil'),
      tehlikeli: true,
    });
    if (!yes || this.deleting() !== null) return;
    this.deleting.set(z.id);
    this.api
      .delete<unknown>(recordPath(`${REGULATION}/zeyiller`, z.id))
      .pipe(
        finalize(() => this.deleting.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.toast.basari(this.t('servisSigorta.zeyil.silindi', { no: z.zeyilNo }));
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
