import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { type UnsavedChangesOwner, pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { WarningBannerService } from '@core/geri-bildirim/warning-banner-service';
import { translationFunction } from '@core/i18n/ceviri';
import { SessionService } from '@core/oturum/session-service';
import { genelGosterilir } from '@core/oturum/session-interceptor';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listQueryUrlSync } from '@core/veri/liste-sorgusu-url';
import { mergeServerValues } from '@features/planlama-ortak/form-yardimcilari';
import { toNumber } from '@features/vehicles/vehicle-model';
import { MoneyPipe, NumberPipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { SearchSelection } from '@shared/form/arama-secim/search-selection';
import {
  type SecimSecenegi,
  serverSelectionSource,
} from '@shared/form/arama-secim/selection-source';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { TextInput } from '@shared/form/kontroller/text-input';
import { Checkbox } from '@shared/form/kontroller/checkbox';
import { MoneyInput } from '@shared/form/kontroller/money-input';
import { NumberInput } from '@shared/form/kontroller/number-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { DatePicker } from '@shared/form/tarih/date-picker';
import { Table } from '@shared/tablo/table';
import { TableCell } from '@shared/tablo/table-cell';

import { customerInstallmentColumns } from '../finance-columns';
import {
  CUSTOMER_INSTALLMENT_LIST,
  CUSTOMER_INSTALLMENT_STATUSES,
  type CustomerInstallment,
  type CustomerInstallmentStatus,
} from '../finance-model';
import { CUSTOMER_INSTALLMENTS, CustomerInstallmentStore, recordPath } from '../finance.store';
import { CustomerLabels, VehicleLabels } from '../labels';
import {
  type InstallmentFormValue,
  type PlanFormValue,
  emptyInstallment,
  emptyPlan,
  installmentRequest,
  installmentToForm,
  planRequest,
} from './installment-form-model';
import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { FilterPanelComponent } from '@shared/filtre-paneli/filtre-paneli';
import { PlateChipComponent } from '@shared/plaka/plaka';

type Editing = { readonly kind: 'new' } | { readonly kind: 'record'; readonly id: string };

/**
 * Müşteri taksit takibi (`/app/musteri-taksit`) — Blazor `MusteriTaksitList.razor` paritesi: süzgeçler, 5 özet kart
 * (BAZ para), liste, satırda Ödendi / Geri Al / Düzenle / Sil (FinanceWrite), "Tek Taksit Ekle / Düzenle" ve "Taksit
 * Planı Üret" (kalan-yöntemi, tek işlem). DEFTERE YAZMAZ: "ödendi" takip bayrağıdır. Okuma FinanceWrite ∨ ViewReports.
 * Düzenleme tam değiştirmedir (`surum`); 409 `cakisma` → güncel kayıt KİRLİ forma birleşir (form silinmez).
 */
@Component({
  selector: 'rc-customer-installment-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PlateChipComponent,
    FilterPanelComponent,
    PageBand,
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    SearchSelection,
    FormErrors,
    TextInput,
    Checkbox,
    MoneyInput,
    MoneyPipe,
    NumberInput,
    NumberPipe,
    Selection,
    Table,
    TableCell,
    DatePicker,
  ],
  providers: [FetchPolicy, CustomerInstallmentStore, CustomerLabels, VehicleLabels],
  templateUrl: './customer-installment-list.html',
  styleUrl: '../vehicle-finance.scss',
})
export class CustomerInstallmentList implements UnsavedChangesOwner {
  protected readonly store = inject(CustomerInstallmentStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly session = inject(SessionService);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly banner = inject(WarningBannerService);
  private readonly customerLabels = inject(CustomerLabels);
  private readonly vehicleLabels = inject(VehicleLabels);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly t = translationFunction();

  protected readonly query = listQueryUrlSync(CUSTOMER_INSTALLMENT_LIST);
  protected readonly columns = customerInstallmentColumns(this.t);
  protected readonly rowId = (r: CustomerInstallment) => r.id;
  protected readonly num = toNumber;
  protected readonly customers = serverSelectionSource('musteri');
  protected readonly vehicles = serverSelectionSource('arac');
  protected readonly canWrite = computed(() => this.session.izinVar('FinanceWrite'));
  protected readonly busy = signal<string | null>(null);

  protected readonly statusOptions: readonly SecenekOgesi<CustomerInstallmentStatus>[] =
    CUSTOMER_INSTALLMENT_STATUSES.map((s) => ({
      deger: s,
      etiket: this.t(`aracFinans.taksit.durumlar.${s}`),
    }));

  protected readonly filterForm = new FormGroup({
    cari: new FormControl<SecimSecenegi | null>(null),
    arac: new FormControl<SecimSecenegi | null>(null),
    durum: new FormControl<CustomerInstallmentStatus | null>(null),
    vadeMin: new FormControl<string | null>(null),
    vadeMax: new FormControl<string | null>(null),
    gecikmis: new FormControl<boolean | null>(false),
  });

  // ---- tek taksit (ekle / düzenle)
  protected readonly editing = signal<Editing>({ kind: 'new' });
  protected readonly base = signal<CustomerInstallment | null>(null);
  /** Formun doldurulduğu değer (ilk okuma dönene kadar birleştirme tabanı: liste satırı). */
  private filledFrom: InstallmentFormValue | null = null;
  protected readonly form = new FormGroup({
    cari: new FormControl<SecimSecenegi | null>(null, Validators.required),
    arac: new FormControl<SecimSecenegi | null>(null),
    vade: new FormControl<string | null>(null, Validators.required),
    taksitTutari: new FormControl<string | null>(null, Validators.required),
    doviz: new FormControl<string | null>('TRY', Validators.maxLength(3)),
    kur: new FormControl<number | null>(null),
    durum: new FormControl<CustomerInstallmentStatus | null>('Bekliyor'),
    odemeTarihi: new FormControl<string | null>(null),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  protected readonly submission = formSubmission();
  protected readonly formCurrency = toSignal(this.form.controls.doviz.valueChanges, {
    initialValue: this.form.controls.doviz.value,
  });

  // ---- plan
  protected readonly planForm = new FormGroup({
    cari: new FormControl<SecimSecenegi | null>(null, Validators.required),
    arac: new FormControl<SecimSecenegi | null>(null),
    toplamTutar: new FormControl<string | null>(null, Validators.required),
    taksitSayisi: new FormControl<number | null>(12, [
      Validators.required,
      Validators.min(1),
      Validators.max(600), // sunucu MusteriTaksitService.MaxTaksit
    ]),
    ilkVade: new FormControl<string | null>(null, Validators.required),
    doviz: new FormControl<string | null>('TRY', Validators.maxLength(3)),
    kur: new FormControl<number | null>(null),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  protected readonly planSubmission = formSubmission();
  protected readonly planCurrency = toSignal(this.planForm.controls.doviz.valueChanges, {
    initialValue: this.planForm.controls.doviz.value,
  });

  constructor() {
    const policy = inject(FetchPolicy);
    policy.connect({
      parametre: this.query.apiParametreleri,
      yukle: (p) => {
        this.store.list.yukle(p);
        this.store.summary.yukle(p);
      },
      sifirla: () => {
        this.store.list.reset();
        this.store.summary.reset();
      },
      sekmeyeDonunce: 'yenile',
    });
    effect(() => {
      const f = this.query.sorgu().filtreler;
      const account = this.customerLabels.label(f.cariId);
      const vehicle = this.vehicleLabels.label(f.vehicleId);
      untracked(() =>
        this.filterForm.reset({
          cari: account,
          arac: vehicle,
          durum: f.durum ?? null,
          vadeMin: f.vadeMin ?? null,
          vadeMax: f.vadeMax ?? null,
          gecikmis: f.gecikmis ?? false,
        }),
      );
    });
    effect(() => {
      if (!this.canWrite())
        untracked(() => {
          this.form.disable({ emitEvent: false });
          this.planForm.disable({ emitEvent: false });
        });
    });
    this.form.reset({ ...emptyInstallment() });
    this.planForm.reset({ ...emptyPlan() });
    pageLeaveGuard(() => this.hasUnsavedChanges());
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty || this.planForm.dirty;
  }

  // ------------------------------------------------------------------ süzgeç

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    this.customerLabels.remember(v.cari);
    this.vehicleLabels.remember(v.arac);
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        cariId: v.cari?.id ?? undefined,
        vehicleId: v.arac?.id ?? undefined,
        durum: v.durum ?? undefined,
        vadeMin: v.vadeMin ?? undefined,
        vadeMax: v.vadeMax ?? undefined,
        gecikmis: v.gecikmis ? true : undefined,
      },
    });
  }

  protected clear(): void {
    void this.query.sifirla();
  }

  // ------------------------------------------------------------------ tek taksit formu

  protected async edit(row: CustomerInstallment): Promise<void> {
    if (!(await this.releaseForm())) return;
    this.editing.set({ kind: 'record', id: row.id });
    this.base.set(null);
    this.filledFrom = installmentToForm(row);
    this.form.reset({ ...this.filledFrom });
    this.submission.kilit.yenile();
    this.readRecord(row.id);
    afterNextRender(
      () => this.host.nativeElement.querySelector<HTMLElement>('#rc-taksit-formu')?.focus(),
      { injector: this.injector },
    );
  }

  protected async cancelEdit(): Promise<void> {
    if (!(await this.releaseForm())) return;
    this.resetForm();
  }

  protected save(): void {
    if (!this.canWrite()) return;
    const e = this.editing();
    const base = this.base();
    if (e.kind === 'record' && base === null) return; // sürüm okunmadan tam değiştirme gönderilmez
    const body = installmentRequest(this.form.getRawValue() as InstallmentFormValue, base);
    const mapping = { cariId: 'cari', vehicleId: 'arac' };
    this.submission.gonder(
      this.form,
      (key) =>
        e.kind === 'new'
          ? this.api.post<unknown>(CUSTOMER_INSTALLMENTS, body, { islemAnahtari: key })
          : this.api.put<CustomerInstallment>(recordPath(CUSTOMER_INSTALLMENTS, e.id), body, {
              islemAnahtari: key,
            }),
      {
        esleme: mapping,
        basarili: () => {
          this.toast.basari(
            this.t(e.kind === 'new' ? 'aracFinans.taksit.eklendi' : 'aracFinans.taksit.kaydedildi'),
          );
          this.resetForm();
          this.refresh();
        },
        hata: (h) => {
          if (h.kod === 'cakisma' && e.kind === 'record') this.readRecord(e.id);
          if (h.kod === 'mukerrer') {
            if (h.mevcut?.ayniIcerik) this.resetForm();
            this.refresh();
          }
        },
      },
    );
  }

  // ------------------------------------------------------------------ plan

  protected generatePlan(): void {
    if (!this.canWrite()) return;
    const body = planRequest(this.planForm.getRawValue() as PlanFormValue);
    this.planSubmission.gonder(
      this.planForm,
      (key) =>
        this.api.post<{ adet: number | string }>(`${CUSTOMER_INSTALLMENTS}/plan`, body, {
          islemAnahtari: key,
        }),
      {
        esleme: { cariId: 'cari', vehicleId: 'arac' },
        basarili: (r) => {
          this.toast.basari(this.t('aracFinans.taksit.planUretildi', { adet: r.adet }));
          this.planForm.reset({ ...emptyPlan() });
          this.refresh();
        },
        hata: (h) => {
          if (h.kod === 'mukerrer') {
            if (h.mevcut?.ayniIcerik) this.planForm.reset({ ...emptyPlan() });
            this.refresh();
          }
        },
      },
    );
  }

  // ------------------------------------------------------------------ satır işlemleri

  protected markPaid(row: CustomerInstallment): void {
    this.rowAction(
      row,
      recordPath(CUSTOMER_INSTALLMENTS, row.id, '/odendi'),
      {},
      'aracFinans.taksit.odendiBildirim',
    );
  }

  protected undoPaid(row: CustomerInstallment): void {
    this.rowAction(
      row,
      recordPath(CUSTOMER_INSTALLMENTS, row.id, '/geri-al'),
      null,
      'aracFinans.taksit.geriAlindi',
    );
  }

  protected async remove(row: CustomerInstallment): Promise<void> {
    if (this.busy() !== null) return;
    const yes = await this.confirm.ask({
      baslik: this.t('aracFinans.taksit.silBaslik'),
      mesaj: this.t('aracFinans.taksit.silMesaj', { sira: row.sira, musteri: row.cariAd }),
      onayEtiketi: this.t('aracFinans.sil'),
      tehlikeli: true,
    });
    if (!yes || this.busy() !== null) return;
    this.busy.set(row.id);
    this.api
      .delete<unknown>(recordPath(CUSTOMER_INSTALLMENTS, row.id))
      .pipe(
        finalize(() => this.busy.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.toast.basari(this.t('aracFinans.taksit.silindi'));
          const e = this.editing();
          if (e.kind === 'record' && e.id === row.id) this.resetForm();
          this.refresh();
        },
        error: (raw: unknown) => this.actionFailed(raw),
      });
  }

  private rowAction(
    row: CustomerInstallment,
    path: `/api/ui/v1/${string}`,
    body: unknown,
    message: 'aracFinans.taksit.odendiBildirim' | 'aracFinans.taksit.geriAlindi',
  ): void {
    if (this.busy() !== null) return;
    this.busy.set(row.id);
    this.api
      .post<CustomerInstallment>(path, body)
      .pipe(
        finalize(() => this.busy.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (fresh) => {
          this.toast.basari(this.t(message, { sira: row.sira }));
          // Açık düzenleme aynı kayıtsa yeni sürüm birleşir (bayat PUT olmasın).
          const e = this.editing();
          if (e.kind === 'record' && e.id === row.id) this.recordArrived(fresh);
          this.refresh();
        },
        error: (raw: unknown) => this.actionFailed(raw),
      });
  }

  private actionFailed(raw: unknown): void {
    const error = toApiError(raw);
    if (!genelGosterilir(error)) this.toast.hata(error.detay);
    this.refresh();
  }

  // ------------------------------------------------------------------ yardımcılar

  private readRecord(id: string): void {
    this.api
      .get<CustomerInstallment>(recordPath(CUSTOMER_INSTALLMENTS, id))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => {
          const e = this.editing();
          if (e.kind === 'record' && e.id === id) this.recordArrived(r);
        },
        error: (raw: unknown) => {
          const error = toApiError(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
        },
      });
  }

  /**
   * Güncel kayıt: temiz form sıfırlanır; kirli formda dokunulan alan korunur, çakışan işaretlenir. Birleştirmenin
   * tabanı formun DOLDURULDUĞU değerdir: ilk okumada (sürüm henüz yok) liste satırı — bayat olabilir; kullanıcı ilk
   * okuma dönmeden yazdıysa dokunmadığı alanlar yine TAZE değeri alır (inceleme M1: aksi halde sonraki PUT liste
   * satırının eski değerlerini taze sürümle gönderip başka oturumun değişikliğini sessizce ezerdi).
   */
  private recordArrived(r: CustomerInstallment): void {
    const fresh = installmentToForm(r);
    const previous = this.base();
    const baseline = (previous === null ? this.filledFrom : installmentToForm(previous)) ?? fresh;
    if (!this.form.dirty) {
      this.form.reset({ ...fresh });
    } else {
      const conflicts = mergeServerValues(
        this.form,
        { ...fresh },
        { ...baseline },
        this.t('aracFinans.cakismaAlan'),
      );
      if (conflicts.length > 0)
        this.banner.show({
          tur: 'uyari',
          mesaj: this.t('aracFinans.cakismaBant', { sayi: conflicts.length }),
          kod: 'cakisma',
        });
    }
    this.base.set(r);
  }

  private resetForm(): void {
    this.editing.set({ kind: 'new' });
    this.base.set(null);
    this.filledFrom = null;
    this.form.reset({ ...emptyInstallment() });
    this.submission.kilit.yenile();
  }

  private async releaseForm(): Promise<boolean> {
    if (!this.form.dirty) return true;
    return this.confirm.ask({
      baslik: this.t('aracFinans.vazgecBaslik'),
      mesaj: this.t('aracFinans.vazgecMesaj'),
    });
  }

  private refresh(): void {
    this.store.list.yenile();
    this.store.summary.yenile();
  }
}
