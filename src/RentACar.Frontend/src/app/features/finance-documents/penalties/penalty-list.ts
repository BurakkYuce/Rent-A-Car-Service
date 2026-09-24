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
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listeSorgusuUrlSenkronu } from '@core/veri/liste-sorgusu-url';
import { toNumber } from '@features/vehicles/vehicle-model';
import { ParaPipe, TarihPipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';
import { Ikon } from '@shared/ikon/ikon';
import type { DisaAktarma } from '@shared/tablo/disa-aktarma';
import { Tablo } from '@shared/tablo/tablo';
import { TabloHucre } from '@shared/tablo/tablo-hucre';

import { penaltyColumns } from '../document-columns';
import {
  PENALTY_LIST,
  PENALTY_PAYMENT_STATUSES,
  PENALTY_STATUSES,
  type PenaltyRow,
} from '../document-model';
import { PENALTIES, PenaltyStore, recordPath } from '../document.store';
import { PenaltyCreateForm } from './penalty-create-form';
import { PendingDocumentAttempts } from '../document-submission';
import { PenaltyPaymentForm, penaltyPaymentScope } from './penalty-payment-form';

type PenaltyStatus = (typeof PENALTY_STATUSES)[number];
type PaymentStatus = (typeof PENALTY_PAYMENT_STATUSES)[number];

/**
 * Trafik cezaları (`/app/cezalar`) — Blazor `PenaltyList.razor` paritesi: yeni ceza (çok kalemli, OperationsWrite),
 * süzgeç, liste (+ dışa aktarma), satırda Detay / Yansıt (FinanceWrite, yalnız "Yeni" + carili) / İptal
 * (OperationsDelete, yalnız "Yeni" + ödenmemiş), detayda kalemler, ödeme geçmişi ve KALEM ÖDEMESİ (FinanceWrite,
 * `Idempotency-Key`). Kalanlar ve durum SUNUCUDAN; istemci hesaplamaz.
 */
@Component({
  selector: 'rc-penalty-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    Ikon,
    MetinGirdisi,
    ParaPipe,
    PenaltyCreateForm,
    PenaltyPaymentForm,
    Secim,
    Tablo,
    TabloHucre,
    TarihPipe,
    TarihSecici,
  ],
  providers: [FetchPolicy, PenaltyStore, PendingDocumentAttempts],
  templateUrl: './penalty-list.html',
  styleUrl: '../finance-documents.scss',
})
export class PenaltyList implements KaydedilmemisDegisiklikSahibi {
  protected readonly store = inject(PenaltyStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly session = inject(OturumServisi);
  private readonly confirm = inject(OnayServisi);
  private readonly pending = inject(PendingDocumentAttempts);
  private readonly toast = inject(ToastServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();

  protected readonly query = listeSorgusuUrlSenkronu(PENALTY_LIST);
  protected readonly columns = penaltyColumns(this.t);
  protected readonly rowId = (r: PenaltyRow) => r.id;
  protected readonly num = toNumber;
  protected readonly canCreate = computed(() => this.session.izinVar('OperationsWrite'));
  protected readonly canFinance = computed(() => this.session.izinVar('FinanceWrite'));
  protected readonly canCancel = computed(() => this.session.izinVar('OperationsDelete'));
  protected readonly busy = signal<string | null>(null);
  protected readonly selectedId = signal<string | null>(null);
  private readonly createToggle = signal<boolean | null>(null);
  protected readonly createOpen = computed(
    () => this.createToggle() ?? this.store.list.veri()?.toplam === 0,
  );

  protected readonly statusOptions: readonly SecenekOgesi<PenaltyStatus>[] = PENALTY_STATUSES.map(
    (s) => ({ deger: s, etiket: this.t(`finansBelge.ceza.durumlar.${s}`) }),
  );
  protected readonly paymentOptions: readonly SecenekOgesi<PaymentStatus>[] =
    PENALTY_PAYMENT_STATUSES.map((s) => ({
      deger: s,
      etiket: this.t(`finansBelge.ceza.odemeDurumlari.${s}`),
    }));

  protected readonly filterForm = new FormGroup({
    musteri: new FormControl<string | null>(null, Validators.maxLength(128)),
    makbuzNo: new FormControl<string | null>(null, Validators.maxLength(64)),
    plaka: new FormControl<string | null>(null, Validators.maxLength(16)),
    bas: new FormControl<string | null>(null),
    bit: new FormControl<string | null>(null),
    durum: new FormControl<PenaltyStatus | null>(null),
    odemeDurumu: new FormControl<PaymentStatus | null>(null),
    islemSube: new FormControl<string | null>(null, Validators.maxLength(128)),
  });

  protected readonly export = computed<DisaAktarma>(() => ({
    yol: '/listeler/export/cezalar',
    bicimler: ['excel', 'csv', 'pdf'],
  }));

  private readonly dirtyForms = new Set<string>();

  constructor() {
    const policy = inject(FetchPolicy);
    policy.baglan({
      parametre: this.query.apiParametreleri,
      yukle: (p) => this.store.list.yukle(p),
      sifirla: () => this.store.list.sifirla(),
      sekmeyeDonunce: 'yenile',
    });
    effect(() => {
      const f = this.query.sorgu().filtreler;
      untracked(() =>
        this.filterForm.reset({
          musteri: f.musteri ?? null,
          makbuzNo: f.makbuzNo ?? null,
          plaka: f.plaka ?? null,
          bas: f.bas ?? null,
          bit: f.bit ?? null,
          durum: f.durum ?? null,
          odemeDurumu: f.odemeDurumu ?? null,
          islemSube: f.islemSube ?? null,
        }),
      );
    });
    effect(() => {
      if (this.canCreate() && this.createOpen()) untracked(() => this.store.types.yukle());
    });
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.dirtyForms.size > 0 || this.pending.any() > 0;
  }

  protected dirtyChanged(form: string, dirty: boolean): void {
    if (dirty) this.dirtyForms.add(form);
    else this.dirtyForms.delete(form);
  }

  protected statusLabel(s: string): string {
    return (PENALTY_STATUSES as readonly string[]).includes(s)
      ? this.t(`finansBelge.ceza.durumlar.${s as PenaltyStatus}`)
      : s;
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    const text = (s: string | null) => s?.trim() || undefined;
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        musteri: text(v.musteri),
        makbuzNo: text(v.makbuzNo),
        plaka: text(v.plaka),
        bas: v.bas ?? undefined,
        bit: v.bit ?? undefined,
        durum: v.durum ?? undefined,
        odemeDurumu: v.odemeDurumu ?? undefined,
        islemSube: text(v.islemSube),
      },
    });
  }

  protected clear(): void {
    void this.query.sifirla();
  }

  protected toggleCreate(): void {
    this.createToggle.set(!this.createOpen());
  }

  protected created(): void {
    this.store.list.yenile();
  }

  protected async open(row: PenaltyRow): Promise<void> {
    if (this.selectedId() === row.id) {
      this.store.detail.yukle(row.id);
      return;
    }
    if (!(await this.releasePayment())) return;
    this.selectedId.set(row.id);
    this.store.detail.yukle(row.id);
  }

  protected async closeDetail(): Promise<void> {
    if (!(await this.releasePayment())) return;
    this.selectedId.set(null);
    this.store.detail.sifirla();
  }

  /**
   * Açık ödeme formu kirli ya da sonucu belirsiz deneme taşıyorsa başka cezaya geçiş/kapatma onay ister (r300 L4).
   * Donmuş deneme sayfada kalır: ceza yeniden açılınca form aynı gövde + anahtarla KİLİTLİ gelir.
   */
  private async releasePayment(): Promise<boolean> {
    const id = this.selectedId();
    const risky =
      this.dirtyForms.has('odeme') ||
      (id !== null && this.pending.get(penaltyPaymentScope(id)) !== undefined);
    if (risky) {
      const yes = await this.confirm.sor({
        baslik: this.t('finansBelge.ayrilBaslik'),
        mesaj: this.t('finansBelge.ayrilMesaj'),
      });
      if (!yes) return false;
    }
    this.dirtyForms.delete('odeme');
    return true;
  }

  protected paymentDone(): void {
    const id = this.selectedId();
    if (id) this.store.detail.yukle(id);
    this.store.list.yenile();
  }

  protected canReflect(r: PenaltyRow): boolean {
    return this.canFinance() && r.durum === 'Yeni' && r.cariId !== null;
  }

  protected canCancelRow(r: PenaltyRow): boolean {
    return this.canCancel() && r.durum === 'Yeni' && (toNumber(r.odenenTutar) ?? 0) === 0;
  }

  protected async reflect(r: PenaltyRow): Promise<void> {
    await this.transition(
      r,
      '/yansit',
      'finansBelge.ceza.yansitBaslik',
      'finansBelge.ceza.yansitMesaj',
      false,
    );
  }

  protected async cancel(r: PenaltyRow): Promise<void> {
    await this.transition(
      r,
      '/iptal',
      'finansBelge.ceza.iptalBaslik',
      'finansBelge.ceza.iptalMesaj',
      true,
    );
  }

  private async transition(
    r: PenaltyRow,
    suffix: '/yansit' | '/iptal',
    title: 'finansBelge.ceza.yansitBaslik' | 'finansBelge.ceza.iptalBaslik',
    message: 'finansBelge.ceza.yansitMesaj' | 'finansBelge.ceza.iptalMesaj',
    danger: boolean,
  ): Promise<void> {
    if (this.busy() !== null) return;
    const yes = await this.confirm.sor({
      baslik: this.t(title),
      mesaj: this.t(message, { no: r.no }),
      tehlikeli: danger,
    });
    if (!yes || this.busy() !== null) return;
    this.busy.set(r.id);
    this.api
      .post<{ id: string; durum: string }>(recordPath(PENALTIES, r.id, suffix), null)
      .pipe(
        finalize(() => this.busy.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (s) => {
          this.toast.basari(
            this.t('finansBelge.ceza.durumDegisti', { no: r.no, durum: this.statusLabel(s.durum) }),
          );
          this.refresh(r.id);
        },
        error: (raw: unknown) => {
          const error = apiHatasinaCevir(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
          this.refresh(r.id);
        },
      });
  }

  private refresh(id: string): void {
    this.store.list.yenile();
    if (this.selectedId() === id) this.store.detail.yukle(id);
  }
}
