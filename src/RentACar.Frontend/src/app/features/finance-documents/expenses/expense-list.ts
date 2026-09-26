import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import { PendingMoneyAttempts } from '@core/form/money-attempts';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listeSorgusuUrlSenkronu } from '@core/veri/liste-sorgusu-url';
import { CustomerLabels } from '@features/vehicle-finance/labels';
import { exportParameters } from '@features/vehicle-finance/finance-model';
import { toNumber } from '@features/vehicles/vehicle-model';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';
import { Ikon } from '@shared/ikon/ikon';
import type { DisaAktarma } from '@shared/tablo/disa-aktarma';
import { Tablo } from '@shared/tablo/tablo';
import { TabloHucre } from '@shared/tablo/tablo-hucre';

import { expenseColumns } from '../document-columns';
import {
  EXPENSE_EXPORT_NAMES,
  EXPENSE_LIST,
  EXPENSE_TYPES,
  type ExpenseRow,
  type ExpenseType,
} from '../document-model';
import { BranchNames, ExpenseStore } from '../document.store';
import { ExpenseCreateForm } from './expense-create-form';
import { ExpensePaymentForm, expensePaymentScope } from './expense-payment-form';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { SayfaBandi } from '../../../kabuk/sayfa-bandi/sayfa-bandi';
import { PlateChipComponent } from '@shared/plaka/plaka';

/**
 * Giderler (`/app/giderler`) — Blazor `ExpenseList.razor` paritesi: yeni gider (FinanceWrite, `Idempotency-Key`),
 * arama paneli (+ dışa aktarma süzgeçle), liste; açık hesap giderinde "Öde" (ödeme TAKİBİ, deftere yazmaz). Okuma
 * FinanceWrite ∨ ViewReports; şube kapsamı sunucuda. Tutarlar SUNUCUDAN; istemci KDV/kalan hesaplamaz.
 */
@Component({
  selector: 'rc-expense-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PlateChipComponent,
    SayfaBandi,
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    AramaSecim,
    ExpenseCreateForm,
    ExpensePaymentForm,
    Ikon,
    MetinGirdisi,
    Secim,
    Tablo,
    TabloHucre,
    TarihSecici,
  ],
  providers: [FetchPolicy, ExpenseStore, BranchNames, CustomerLabels, PendingMoneyAttempts],
  templateUrl: './expense-list.html',
  styleUrl: '../finance-documents.scss',
})
export class ExpenseList implements KaydedilmemisDegisiklikSahibi {
  protected readonly store = inject(ExpenseStore);
  protected readonly branches = inject(BranchNames);
  private readonly session = inject(OturumServisi);
  protected readonly pending = inject(PendingMoneyAttempts);
  private readonly confirm = inject(OnayServisi);
  private readonly labels = inject(CustomerLabels);
  private readonly t = ceviriFonksiyonu();

  protected readonly query = listeSorgusuUrlSenkronu(EXPENSE_LIST);
  protected readonly columns = expenseColumns(this.t);
  protected readonly rowId = (r: ExpenseRow) => r.id;
  protected readonly customers = sunucuSecimKaynagi('musteri');
  protected readonly canWrite = computed(() => this.session.izinVar('FinanceWrite'));
  private readonly payingId = signal<string | null>(null);
  /** Ödeme formunun gideri: listenin GÜNCEL satırı (ödeme sonrası kalan tazelenir). */
  protected readonly paying = computed<ExpenseRow | null>(() => {
    const id = this.payingId();
    return id === null ? null : (this.store.list.veri()?.kayitlar.find((r) => r.id === id) ?? null);
  });
  private readonly createToggle = signal<boolean | null>(null);
  protected readonly createOpen = computed(
    () => this.createToggle() ?? this.store.list.veri()?.toplam === 0,
  );

  protected readonly typeOptions: readonly SecenekOgesi<ExpenseType>[] = EXPENSE_TYPES.map((x) => ({
    deger: x,
    etiket: this.t(`finansBelge.gider.turler.${x}`),
  }));

  protected readonly filterForm = new FormGroup({
    q: new FormControl<string | null>(null, Validators.maxLength(128)),
    plaka: new FormControl<string | null>(null, Validators.maxLength(16)),
    cari: new FormControl<SecimSecenegi | null>(null),
    tip: new FormControl<ExpenseType | null>(null),
    sube: new FormControl<string | null>(null, Validators.maxLength(64)),
    bas: new FormControl<string | null>(null),
    bit: new FormControl<string | null>(null),
  });

  protected readonly export = computed<DisaAktarma>(() => ({
    yol: '/listeler/export/giderler',
    parametreler: exportParameters(this.query.sorgu().filtreler, EXPENSE_EXPORT_NAMES),
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
      const cari = this.labels.label(f.cariId);
      untracked(() =>
        this.filterForm.reset({
          q: f.q ?? null,
          plaka: f.plaka ?? null,
          cari,
          tip: f.tip ?? null,
          sube: f.sube ?? null,
          bas: f.bas ?? null,
          bit: f.bit ?? null,
        }),
      );
    });
    effect(() => {
      if (this.canWrite() && this.createOpen())
        untracked(() => {
          this.store.accounts.yukle();
          this.branches.list.yukle();
        });
    });
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.dirtyForms.size > 0 || this.pending.count() > 0;
  }

  protected dirtyChanged(form: string, dirty: boolean): void {
    if (dirty) this.dirtyForms.add(form);
    else this.dirtyForms.delete(form);
  }

  protected typeLabel(tip: string): string {
    return (EXPENSE_TYPES as readonly string[]).includes(tip)
      ? this.t(`finansBelge.gider.turler.${tip as ExpenseType}`)
      : tip;
  }

  protected canPay(r: ExpenseRow): boolean {
    return this.canWrite() && r.takipEdilir && (toNumber(r.kalan) ?? 0) > 0;
  }

  /**
   * #300 L1: gönderim UÇUŞTAYKEN süzgeç, sayfa ve sıralama değişmez — ödenen satır listeden düşerse ödeme formu yok
   * edilir (deneme sayfada kalsa da kullanıcı sonucu göremez).
   */
  protected changeQuery(patch: Parameters<typeof this.query.degistir>[0]): void {
    if (this.pending.inFlight()) return;
    void this.query.degistir(patch);
  }

  protected filter(): void {
    if (this.pending.inFlight()) return;
    const v = this.filterForm.getRawValue();
    this.labels.remember(v.cari);
    const text = (s: string | null) => s?.trim() || undefined;
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        q: text(v.q),
        plaka: text(v.plaka),
        cariId: v.cari?.id ?? undefined,
        tip: v.tip ?? undefined,
        sube: text(v.sube),
        bas: v.bas ?? undefined,
        bit: v.bit ?? undefined,
      },
    });
  }

  protected clear(): void {
    if (this.pending.inFlight()) return;
    void this.query.sifirla();
  }

  protected async toggleCreate(): Promise<void> {
    if (this.pending.inFlight()) return;
    if (this.createOpen()) {
      const risky = this.dirtyForms.has('yeni') || this.pending.get('yeni-gider') !== undefined;
      if (risky && !(await this.confirmLeave())) return;
      this.dirtyForms.delete('yeni');
    }
    this.createToggle.set(!this.createOpen());
  }

  protected async pay(r: ExpenseRow): Promise<void> {
    if (this.pending.inFlight()) return;
    if (this.payingId() === r.id || !(await this.releasePayment())) return;
    this.payingId.set(r.id);
  }

  protected async closePayment(): Promise<void> {
    if (this.pending.inFlight()) return;
    if (await this.releasePayment()) this.payingId.set(null);
  }

  /**
   * Açık ödeme formu kirli ya da sonucu kesinleşmemiş (uçuşta ya da belirsiz) bir deneme taşıyorsa kapatma/başka satır
   * onay ister (r300 L4, r300b N1). Deneme sayfada kalır: gider yeniden açılınca form aynı gövde + anahtarla KİLİTLİ.
   */
  private async releasePayment(): Promise<boolean> {
    const id = this.payingId();
    const risky =
      this.dirtyForms.has('odeme') ||
      (id !== null && this.pending.get(expensePaymentScope(id)) !== undefined);
    if (risky && !(await this.confirmLeave())) return false;
    this.dirtyForms.delete('odeme');
    return true;
  }

  private confirmLeave(): Promise<boolean> {
    return this.confirm.sor({
      baslik: this.t('finansBelge.ayrilBaslik'),
      mesaj: this.t('finansBelge.ayrilMesaj'),
    });
  }

  protected paymentDone(): void {
    this.store.list.yenile();
  }

  protected created(): void {
    this.store.list.yenile();
  }
}
