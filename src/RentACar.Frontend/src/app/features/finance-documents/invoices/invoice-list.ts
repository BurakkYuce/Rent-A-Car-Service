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
import { RouterLink } from '@angular/router';
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
import { CustomerLabels } from '@features/vehicle-finance/labels';
import { toNumber } from '@features/vehicles/vehicle-model';
import { ParaPipe, SayiPipe, TarihPipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import { Ikon } from '@shared/ikon/ikon';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';
import { Tablo } from '@shared/tablo/tablo';
import { TabloHucre } from '@shared/tablo/tablo-hucre';
import type { DisaAktarma } from '@shared/tablo/disa-aktarma';

import { invoiceColumns } from '../document-columns';
import {
  CURRENCIES,
  INVOICE_LIST,
  type BatchInvoiceResult,
  type InvoiceRow,
  type RentalRow,
  VAT_LABELS,
  VAT_RATES,
  type VatRate,
} from '../document-model';
import { INVOICES, InvoiceStore, recordPath } from '../document.store';
import { ManualInvoiceForm } from './manual-invoice-form';

/**
 * Faturalar (`/app/faturalar`) — Blazor `InvoiceList.razor` paritesi: süzgeç, liste (+ dışa aktarma), satırda Detay /
 * PDF, detayda kalemler ve İADE (FinanceReverse; kaynak başına tek iade — sunucu yapısal), Manuel Fatura (ayrı bileşen,
 * `Idempotency-Key`) ve Toplu Faturala (faturasız kiralar). Tutarlar ve KDV SUNUCUDAN; istemci hesaplamaz.
 */
@Component({
  selector: 'rc-invoice-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    AramaSecim,
    Ikon,
    ManualInvoiceForm,
    MetinGirdisi,
    ParaPipe,
    SayiPipe,
    Secim,
    Tablo,
    TabloHucre,
    TarihPipe,
    TarihSecici,
  ],
  providers: [FetchPolicy, InvoiceStore, CustomerLabels],
  templateUrl: './invoice-list.html',
  styleUrl: '../finance-documents.scss',
})
export class InvoiceList implements KaydedilmemisDegisiklikSahibi {
  protected readonly store = inject(InvoiceStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly session = inject(OturumServisi);
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly labels = inject(CustomerLabels);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();

  protected readonly query = listeSorgusuUrlSenkronu(INVOICE_LIST);
  protected readonly columns = invoiceColumns(this.t);
  protected readonly rowId = (r: InvoiceRow) => r.id;
  protected readonly num = toNumber;
  protected readonly customers = sunucuSecimKaynagi('musteri');
  protected readonly canWrite = computed(() => this.session.izinVar('FinanceWrite'));
  protected readonly canRefund = computed(() => this.session.izinVar('FinanceReverse'));
  protected readonly busy = signal(false);
  protected readonly selectedId = signal<string | null>(null);

  protected readonly cancelOptions: readonly SecenekOgesi<'true' | 'false'>[] = [
    { deger: 'false', etiket: this.t('finansBelge.fatura.iptalHaric') },
    { deger: 'true', etiket: this.t('finansBelge.fatura.yalnizIptal') },
  ];
  protected readonly currencyOptions: readonly SecenekOgesi<string>[] = CURRENCIES.map((c) => ({
    deger: c,
    etiket: c,
  }));
  protected readonly vatOptions: readonly SecenekOgesi<VatRate>[] = VAT_RATES.map((v) => ({
    deger: v,
    etiket: VAT_LABELS[v],
  }));

  protected readonly filterForm = new FormGroup({
    q: new FormControl<string | null>(null, Validators.maxLength(128)),
    cari: new FormControl<SecimSecenegi | null>(null),
    iptal: new FormControl<'true' | 'false' | null>(null),
    doviz: new FormControl<string | null>(null),
    ofis: new FormControl<string | null>(null, Validators.maxLength(128)),
    bas: new FormControl<string | null>(null),
    bit: new FormControl<string | null>(null),
  });

  protected readonly export = computed<DisaAktarma>(() => ({
    yol: '/listeler/export/faturalar',
    bicimler: ['excel', 'csv', 'pdf'],
  }));

  // ---- toplu faturalama (yapısal: her kira tekil kesim yolundan; ikinci gönderim yeni belge üretmez)
  protected readonly batchOpen = signal(false);
  protected readonly batchSelection = signal<ReadonlySet<string>>(new Set());
  protected readonly batchVat = new FormControl<VatRate | null>(null);
  protected readonly batchResult = signal<BatchInvoiceResult | null>(null);
  protected readonly candidates = computed<readonly RentalRow[]>(() =>
    (this.store.unbilled.veri()?.kayitlar ?? []).filter(
      (r) => r.durum !== 'Iptal' && (toNumber(r.tutar) ?? 0) > 0,
    ),
  );

  private manualDirty = false;

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
      const cari = this.labels.label(f.cariId);
      untracked(() =>
        this.filterForm.reset({
          q: f.q ?? null,
          cari,
          iptal: f.iptal ?? null,
          doviz: f.doviz ?? null,
          ofis: f.ofis ?? null,
          bas: f.bas ?? null,
          bit: f.bit ?? null,
        }),
      );
    });
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.manualDirty || this.batchSelection().size > 0;
  }

  protected manualDirtyChanged(dirty: boolean): void {
    this.manualDirty = dirty;
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    this.labels.remember(v.cari);
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        q: v.q?.trim() || undefined,
        cariId: v.cari?.id ?? undefined,
        iptal: v.iptal ?? undefined,
        doviz: v.doviz ?? undefined,
        ofis: v.ofis?.trim() || undefined,
        bas: v.bas ?? undefined,
        bit: v.bit ?? undefined,
      },
    });
  }

  protected clear(): void {
    void this.query.sifirla();
  }

  protected pdfPath(id: string): string {
    return `/faturalar/${encodeURIComponent(id)}/pdf`;
  }

  // ------------------------------------------------------------------ detay + iade

  protected open(row: InvoiceRow): void {
    this.selectedId.set(row.id);
    this.store.detail.yukle(row.id);
  }

  protected closeDetail(): void {
    this.selectedId.set(null);
    this.store.detail.sifirla();
  }

  protected async refund(): Promise<void> {
    const d = this.store.detail.veri();
    if (!d || this.busy() || !this.canRefund()) return;
    const yes = await this.confirm.sor({
      baslik: this.t('finansBelge.fatura.iadeBaslik'),
      mesaj: this.t('finansBelge.fatura.iadeMesaj', { no: d.no }),
      onayEtiketi: this.t('finansBelge.fatura.iade'),
      tehlikeli: true,
    });
    if (!yes || this.busy()) return;
    this.busy.set(true);
    this.api
      .post<{ id: string; no: string }>(recordPath(INVOICES, d.id, '/iade'), null)
      .pipe(
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (r) => {
          this.toast.basari(this.t('finansBelge.fatura.iadeKesildi', { no: r.no }));
          this.store.detail.yukle(d.id);
          this.store.list.yenile();
        },
        error: (raw: unknown) => this.failed(raw, () => this.store.detail.yukle(d.id)),
      });
  }

  // ------------------------------------------------------------------ manuel fatura sonrası

  protected manualSaved(): void {
    this.store.list.yenile();
  }

  // ------------------------------------------------------------------ toplu

  protected toggleBatch(): void {
    const open = !this.batchOpen();
    this.batchOpen.set(open);
    if (open) this.store.unbilled.yukle();
  }

  protected toggleRental(id: string, checked: boolean): void {
    const next = new Set(this.batchSelection());
    if (checked) next.add(id);
    else next.delete(id);
    this.batchSelection.set(next);
  }

  protected async batch(): Promise<void> {
    const ids = [...this.batchSelection()];
    if (ids.length === 0 || this.busy() || !this.canWrite()) return;
    const yes = await this.confirm.sor({
      baslik: this.t('finansBelge.fatura.topluBaslik'),
      mesaj: this.t('finansBelge.fatura.topluOnay', { adet: ids.length }),
      onayEtiketi: this.t('finansBelge.fatura.topluKes'),
      tehlikeli: true,
    });
    if (!yes || this.busy()) return;
    this.busy.set(true);
    this.batchResult.set(null);
    this.api
      .post<BatchInvoiceResult>(`${INVOICES}/toplu`, {
        kiraIds: ids,
        kdvOrani: this.batchVat.value,
      })
      .pipe(
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (r) => {
          this.batchResult.set(r);
          this.toast.basari(this.t('finansBelge.fatura.topluKesildi', { adet: r.kesilen.length }));
          this.batchSelection.set(new Set());
          this.store.unbilled.yenile();
          this.store.list.yenile();
        },
        error: (raw: unknown) => this.failed(raw, () => this.store.unbilled.yenile()),
      });
  }

  private failed(raw: unknown, reload: () => void): void {
    const error = apiHatasinaCevir(raw);
    if (!genelGosterilir(error)) this.toast.hata(error.detay);
    reload();
    this.store.list.yenile();
  }
}
