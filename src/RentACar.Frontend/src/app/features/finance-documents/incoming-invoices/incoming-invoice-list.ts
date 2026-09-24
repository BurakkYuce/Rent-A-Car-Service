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
import { Alan } from '@shared/form/alan/alan';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';
import { Ikon } from '@shared/ikon/ikon';
import { Tablo } from '@shared/tablo/tablo';
import { TabloHucre } from '@shared/tablo/tablo-hucre';

import { incomingColumns } from '../document-columns';
import {
  CURRENCIES,
  type DocumentResult,
  INCOMING_LIST,
  INCOMING_STATUSES,
  type IncomingInvoiceRow,
  type IncomingInvoiceSyncResult,
} from '../document-model';
import {
  type IncomingCreateForm as CreateValue,
  incomingCreateRequest,
} from '../document-requests';
import { BranchNames, INCOMING, IncomingInvoiceStore, recordPath } from '../document.store';
import { IncomingExpenseForm } from './incoming-expense-form';
import { IncomingLinkForm } from './incoming-link-form';

type IncomingStatus = (typeof INCOMING_STATUSES)[number];
type Panel =
  | { readonly kind: 'link'; readonly id: string }
  | { readonly kind: 'expense'; readonly id: string }
  | { readonly kind: 'reject'; readonly id: string };

/**
 * Gelen e-fatura (`/app/gelen-efatura`) — Blazor `GelenEFaturaList.razor` paritesi: triage kutusu (elle giriş, GİB'den
 * çek — entegrasyon yoksa sunucu dürüstçe reddeder), Onayla / Reddet / KDV kırılımı-Bağla (sürümlü PUT) / Giderleştir
 * (TEK para yolu, sunucu deterministik anahtar) / Defter dışı işle. FinanceWrite; şubeye bağlı kullanıcı 403.
 */
@Component({
  selector: 'rc-incoming-invoice-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    FormHatalari,
    Ikon,
    IncomingExpenseForm,
    IncomingLinkForm,
    MetinGirdisi,
    ParaGirdisi,
    Secim,
    Tablo,
    TabloHucre,
    TarihSecici,
  ],
  providers: [FetchPolicy, IncomingInvoiceStore, BranchNames],
  templateUrl: './incoming-invoice-list.html',
  styleUrl: '../finance-documents.scss',
})
export class IncomingInvoiceList implements KaydedilmemisDegisiklikSahibi {
  protected readonly store = inject(IncomingInvoiceStore);
  protected readonly branches = inject(BranchNames);
  private readonly api = inject(ApiIstemcisi);
  private readonly confirm = inject(OnayServisi);
  private readonly session = inject(OturumServisi);
  private readonly toast = inject(ToastServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();

  protected readonly query = listeSorgusuUrlSenkronu(INCOMING_LIST);
  protected readonly columns = incomingColumns(this.t);
  protected readonly rowId = (r: IncomingInvoiceRow) => r.id;
  protected readonly busy = signal<string | null>(null);
  protected readonly panel = signal<Panel | null>(null);
  protected readonly panelRow = computed<IncomingInvoiceRow | null>(() => {
    const p = this.panel();
    return p === null
      ? null
      : (this.store.list.veri()?.kayitlar.find((r) => r.id === p.id) ?? null);
  });
  protected readonly createOpen = signal(false);
  protected readonly syncOpen = signal(false);
  /** Gelen e-fatura kiracı geneli tedarikçi belgesidir: şubeye bağlı kullanıcıda uç 403 (r300 L3 — düğme yok). */
  protected readonly unrestricted = computed(
    () => this.session.ben()?.subeKapsami.tumSubeler === true,
  );

  protected readonly statusOptions: readonly SecenekOgesi<IncomingStatus>[] = INCOMING_STATUSES.map(
    (s) => ({ deger: s, etiket: this.t(`finansBelge.gelen.durumlar.${s}`) }),
  );
  protected readonly ledgerOptions: readonly SecenekOgesi<'true' | 'false'>[] = [
    { deger: 'true', etiket: this.t('finansBelge.gelen.giderlestirilmis') },
    { deger: 'false', etiket: this.t('finansBelge.gelen.giderlestirilmemis') },
  ];
  protected readonly currencyOptions: readonly SecenekOgesi<string>[] = CURRENCIES.map((c) => ({
    deger: c,
    etiket: c,
  }));

  protected readonly filterForm = new FormGroup({
    firma: new FormControl<string | null>(null, Validators.maxLength(256)),
    ettnBas: new FormControl<string | null>(null, Validators.maxLength(64)),
    ettnBit: new FormControl<string | null>(null, Validators.maxLength(64)),
    plaka: new FormControl<string | null>(null, Validators.maxLength(16)),
    durum: new FormControl<IncomingStatus | null>(null),
    bas: new FormControl<string | null>(null),
    bit: new FormControl<string | null>(null),
    giderlestirildi: new FormControl<'true' | 'false' | null>(null),
  });

  protected readonly createForm = new FormGroup({
    ettn: new FormControl<string | null>(null, [Validators.required, Validators.maxLength(64)]),
    gonderenVkn: new FormControl<string | null>(null, [
      Validators.required,
      Validators.maxLength(16),
    ]),
    gonderenUnvan: new FormControl<string | null>(null, [
      Validators.required,
      Validators.maxLength(256),
    ]),
    tarih: new FormControl<string | null>(null),
    netTutar: new FormControl<string | null>(null),
    kdvTutar: new FormControl<string | null>(null),
    genelToplam: new FormControl<string | null>(null, Validators.required),
    doviz: new FormControl<string | null>('TRY'),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  protected readonly createSubmission = formGonderimi();

  protected readonly syncForm = new FormGroup({
    bas: new FormControl<string | null>(null, Validators.required),
    bit: new FormControl<string | null>(null, Validators.required),
  });
  protected readonly syncSubmission = formGonderimi();

  protected readonly rejectForm = new FormGroup({
    neden: new FormControl<string | null>(null, Validators.maxLength(512)),
  });

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
          firma: f.firma ?? null,
          ettnBas: f.ettnBas ?? null,
          ettnBit: f.ettnBit ?? null,
          plaka: f.plaka ?? null,
          durum: f.durum ?? null,
          bas: f.bas ?? null,
          bit: f.bit ?? null,
          giderlestirildi: f.giderlestirildi ?? null,
        }),
      );
    });
    this.store.categories.yukle();
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.createForm.dirty || this.dirtyForms.size > 0;
  }

  protected dirtyChanged(form: string, dirty: boolean): void {
    if (dirty) this.dirtyForms.add(form);
    else this.dirtyForms.delete(form);
  }

  protected statusLabel(s: string): string {
    return (INCOMING_STATUSES as readonly string[]).includes(s)
      ? this.t(`finansBelge.gelen.durumlar.${s as IncomingStatus}`)
      : s;
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    const text = (s: string | null) => s?.trim() || undefined;
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        firma: text(v.firma),
        ettnBas: text(v.ettnBas),
        ettnBit: text(v.ettnBit),
        plaka: text(v.plaka),
        durum: v.durum ?? undefined,
        bas: v.bas ?? undefined,
        bit: v.bit ?? undefined,
        giderlestirildi: v.giderlestirildi ?? undefined,
      },
    });
  }

  protected clear(): void {
    void this.query.sifirla();
  }

  // ------------------------------------------------------------------ elle giriş + GİB'den çek

  protected create(): void {
    const body = incomingCreateRequest(this.createForm.getRawValue() as CreateValue);
    this.createSubmission.gonder(
      this.createForm,
      (key) => this.api.post<DocumentResult>(INCOMING, body, { islemAnahtari: key }),
      {
        basarili: (r) => {
          this.toast.basari(this.t('finansBelge.gelen.eklendi', { ettn: r.no }));
          this.createForm.reset({ doviz: 'TRY' });
          this.createSubmission.kilit.yenile();
          this.store.list.yenile();
        },
      },
    );
  }

  protected sync(): void {
    const v = this.syncForm.getRawValue();
    this.syncSubmission.gonder(
      this.syncForm,
      () =>
        this.api.post<IncomingInvoiceSyncResult>(`${INCOMING}/sync`, { bas: v.bas, bit: v.bit }),
      {
        basarili: (r) => {
          this.toast.basari(this.t('finansBelge.gelen.cekildi', { adet: r.eklenen }));
          this.store.list.yenile();
        },
      },
    );
  }

  // ------------------------------------------------------------------ satır işlemleri

  protected async openPanel(kind: Panel['kind'], row: IncomingInvoiceRow): Promise<void> {
    const p = this.panel();
    if (p?.kind === kind && p.id === row.id) return;
    if (!(await this.releasePanel())) return;
    this.rejectForm.reset();
    if (kind !== 'reject') this.branches.list.yukle();
    this.panel.set({ kind, id: row.id });
  }

  protected async closePanel(): Promise<void> {
    if (await this.releasePanel()) this.panel.set(null);
  }

  /**
   * Kirli KDV kırılımı / red nedeni başka panele geçerken ya da kapatılırken onaysız ATILMAZ (r300 M4: kullanıcı
   * kırılımı girip "Giderleştir"e basınca kırılım sessizce kayboluyor, giderleştirme kayıtlı değerlerle gidiyordu).
   */
  private async releasePanel(): Promise<boolean> {
    if (this.dirtyForms.has('panel') || this.rejectForm.dirty) {
      const yes = await this.confirm.sor({
        baslik: this.t('finansBelge.ayrilBaslik'),
        mesaj: this.t('finansBelge.gelen.panelAyrilMesaj'),
      });
      if (!yes) return false;
    }
    this.dirtyForms.delete('panel');
    return true;
  }

  protected panelDone(): void {
    this.store.list.yenile();
  }

  protected expenseDone(): void {
    this.panel.set(null);
    this.store.list.yenile();
  }

  protected approve(row: IncomingInvoiceRow): void {
    this.transition(row, '/onayla', null);
  }

  protected async process(row: IncomingInvoiceRow): Promise<void> {
    const yes = await this.confirm.sor({
      baslik: this.t('finansBelge.gelen.isle'),
      mesaj: this.t('finansBelge.gelen.isleOnay', { ettn: row.ettn }),
    });
    if (yes) this.transition(row, '/isle', null);
  }

  protected reject(): void {
    const p = this.panel();
    const row = this.panelRow();
    if (p?.kind !== 'reject' || row === null) return;
    const neden = this.rejectForm.getRawValue().neden?.trim() || null;
    this.transition(row, '/reddet', { neden }, () => this.panel.set(null));
  }

  private transition(
    row: IncomingInvoiceRow,
    suffix: '/onayla' | '/isle' | '/reddet',
    body: unknown,
    after?: () => void,
  ): void {
    if (this.busy() !== null) return;
    this.busy.set(row.id);
    this.api
      .post<{ id: string; durum: string }>(recordPath(INCOMING, row.id, suffix), body)
      .pipe(
        finalize(() => this.busy.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (s) => {
          this.toast.basari(
            this.t('finansBelge.gelen.durumDegisti', {
              ettn: row.ettn,
              durum: this.statusLabel(s.durum),
            }),
          );
          after?.();
          this.store.list.yenile();
        },
        error: (raw: unknown) => {
          const error = apiHatasinaCevir(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
          this.store.list.yenile();
        },
      });
  }
}
