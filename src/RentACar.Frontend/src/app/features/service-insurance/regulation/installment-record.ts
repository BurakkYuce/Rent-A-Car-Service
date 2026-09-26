import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { type UnsavedChangesOwner, pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { tabContext } from '@core/sekme/tab-state';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { translationFunction } from '@core/i18n/ceviri';
import { MoneyPipe, DatePipe } from '@shared/bicim/bicim-pipe';

import { num } from '../service-insurance-model';
import { InstallmentRecordStore } from '../service-insurance.store';
import { type InstallmentKind, InstallmentPayPanel } from './installment-pay-panel';
import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';

/**
 * MTV / muayene kaydı (`/app/regulasyon/mtv/:id`, `/app/regulasyon/muayeneler/:id`) — Blazor `/regulasyon` MTV ve
 * Muayene tablolarındaki satır + "Öde" formu + ödeme geçmişi (kayıt kapansa da). Kayıt ARACIN şubesinden geçer
 * (başka şube 403). Ödeme FinanceWrite: düğme sunucunun `yetkiler.odeyebilir` bayrağından.
 */
@Component({
  selector: 'rc-installment-record',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageBand, RouterLink, TranslocoPipe, InstallmentPayPanel, MoneyPipe, DatePipe],
  providers: [FetchPolicy, InstallmentRecordStore],
  templateUrl: './installment-record.html',
  styleUrl: '../service-insurance.scss',
})
export class InstallmentRecord implements UnsavedChangesOwner {
  protected readonly store = inject(InstallmentRecordStore);
  private readonly route = inject(ActivatedRoute);
  private readonly tab = tabContext();
  private readonly t = translationFunction();
  protected readonly kind: InstallmentKind =
    (this.route.snapshot.data['kind'] as InstallmentKind | undefined) ?? 'mtv';
  protected readonly id = this.route.snapshot.paramMap.get('id') ?? '';
  protected readonly num = num;
  private readonly panel = viewChild(InstallmentPayPanel);

  private readonly source = this.kind === 'mtv' ? this.store.mtv : this.store.inspection;
  protected readonly state = computed(() => this.source.durum());
  protected readonly refreshing = computed(() => this.source.isLoading());

  /** Ortak görünüm: MTV'de tutar/dönem/vade, muayenede ücret/ceza/muayene tarihi/bitiş. */
  protected readonly view = computed(() => {
    const m = this.store.mtv.veri();
    const i = this.store.inspection.veri();
    if (this.kind === 'mtv' && m)
      return {
        plaka: m.mtv.plaka,
        vehicleId: m.mtv.vehicleId,
        title: m.mtv.donem,
        amount: num(m.mtv.tutar) ?? 0,
        fine: null,
        remaining: num(m.mtv.kalan) ?? 0,
        date: m.mtv.vade,
        end: null,
        km: null,
        paid: m.mtv.odendi,
        note: m.mtv.aciklama,
        payments: m.odemeler,
        canPay: m.yetkiler.odeyebilir,
      };
    if (this.kind === 'muayene' && i)
      return {
        plaka: i.muayene.plaka,
        vehicleId: i.muayene.vehicleId,
        title: null,
        amount: num(i.muayene.ucret) ?? 0,
        fine: num(i.muayene.ceza) ?? 0,
        remaining: num(i.muayene.kalan) ?? 0,
        date: i.muayene.muayeneTarihi,
        end: i.muayene.bitis,
        km: num(i.muayene.islemKm),
        paid: i.muayene.odendi,
        note: i.muayene.aciklama,
        payments: i.odemeler,
        canPay: i.yetkiler.odeyebilir,
      };
    return null;
  });

  constructor() {
    inject(FetchPolicy).connect({
      parametre: signal(this.id).asReadonly(),
      yukle: (id) => this.source.yukle(id),
      sifirla: () => this.source.reset(),
    });
    effect(() => {
      const v = this.view();
      if (!v) return;
      untracked(() => {
        this.tab.etiketAyarla(`${v.plaka} ${v.title ?? ''}`.trim());
        if (v.canPay && this.store.accounts.tur() === 'bos') this.store.accounts.yukle();
      });
    });
    pageLeaveGuard(() => this.hasUnsavedChanges());
  }

  hasUnsavedChanges(): boolean {
    return this.panel()?.hasPendingWork() ?? false;
  }

  /** Uçuştaki / sonucu bilinmeyen ödeme varken özel terk metni (inceleme L2). */
  unsavedChangesMessage(): string | null {
    return this.panel()?.hasPendingPayment() ? this.t('servisSigorta.para.terkMesaji') : null;
  }

  protected reload(): void {
    this.source.yenile();
  }
}
