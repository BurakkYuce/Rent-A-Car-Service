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

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import type { GunMetni } from '@core/form/tarih-girdisi';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { sekmeBaglami } from '@core/sekme/sekme-durumu';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { anDegeri, metinDegeri } from '@features/planlama-ortak/form-yardimcilari';
import { ParaPipe, TarihPipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';
import { Ikon } from '@shared/ikon/ikon';

import {
  type Endorsement,
  type EndorsementRequest,
  REGULATION,
  num,
  recordPath,
} from '../service-insurance-model';
import { PolicyDetailStore, RegulationOptionsStore } from '../service-insurance.store';
import { PolicyPayPanel } from './policy-pay-panel';

/**
 * Sigorta poliçesi kaydı (`/app/regulasyon/sigortalar/:id`) — Blazor `/regulasyon` poliçe satırı + "Öde" formu +
 * "Zeyil (Poliçe Ekleri)" bölümü (Blazor tüm zeyilleri tek tabloda gösteriyordu; burada poliçenin kendi zeyilleri).
 * Ödeme FinanceWrite, zeyil OperationsWrite (sunucu `yetkiler`). Zeyiller BİLGİdir: deftere yazmaz, kalanı değiştirmez.
 */
@Component({
  selector: 'rc-policy-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    FormHatalari,
    Ikon,
    MetinGirdisi,
    ParaGirdisi,
    ParaPipe,
    PolicyPayPanel,
    TarihPipe,
    TarihSecici,
  ],
  providers: [FetchPolicy, PolicyDetailStore, RegulationOptionsStore],
  templateUrl: './policy-detail.html',
  styleUrl: '../service-insurance.scss',
})
export class PolicyDetail implements KaydedilmemisDegisiklikSahibi {
  protected readonly store = inject(PolicyDetailStore);
  private readonly optionsStore = inject(RegulationOptionsStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly confirm = inject(OnayServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly tab = sekmeBaglami();
  private readonly t = ceviriFonksiyonu();
  private readonly panel = viewChild(PolicyPayPanel);

  protected readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';
  protected readonly num = num;
  protected readonly deleting = signal<string | null>(null);
  protected readonly endorsementTypes = computed(
    () => this.optionsStore.options.veri()?.zeyilTipleri ?? [],
  );

  protected readonly form = new FormGroup({
    zeyilNo: new FormControl<string | null>(null, [Validators.required, Validators.maxLength(32)]),
    tarih: new FormControl<GunMetni | null>(null, Validators.required),
    tanzim: new FormControl<GunMetni | null>(null),
    deger: new FormControl<string | null>(null),
    brut: new FormControl<string | null>(null),
    net: new FormControl<string | null>(null),
    fonVergi: new FormControl<string | null>(null),
    tipi: new FormControl<string | null>(null, Validators.maxLength(64)),
    neden: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  protected readonly submission = formGonderimi();

  constructor() {
    inject(FetchPolicy).baglan({
      parametre: signal(this.id).asReadonly(),
      yukle: (id) => this.store.detail.yukle(id),
      sifirla: () => this.store.detail.sifirla(),
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
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.form.dirty || (this.panel()?.hasPendingWork() ?? false);
  }

  protected reload(): void {
    this.store.detail.yenile();
  }

  protected addEndorsement(): void {
    const v = this.form.getRawValue();
    const body: EndorsementRequest = {
      zeyilNo: metinDegeri(v.zeyilNo),
      tarih: anDegeri(v.tarih, null),
      tanzim: anDegeri(v.tanzim, null),
      deger: v.deger,
      brut: v.brut,
      net: v.net,
      fonVergi: v.fonVergi,
      tipi: metinDegeri(v.tipi),
      neden: metinDegeri(v.neden),
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
    const yes = await this.confirm.sor({
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
          const error = apiHatasinaCevir(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
          this.reload();
        },
      });
  }
}
