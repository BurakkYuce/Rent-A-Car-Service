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
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { SessionService } from '@core/oturum/session-service';
import { genelGosterilir } from '@core/oturum/session-interceptor';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listQueryUrlSync } from '@core/veri/liste-sorgusu-url';
import { Alan } from '@shared/form/alan/alan';
import { SearchSelection } from '@shared/form/arama-secim/search-selection';
import {
  type SecimSecenegi,
  serverSelectionSource,
} from '@shared/form/arama-secim/selection-source';
import { TextInput } from '@shared/form/kontroller/text-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { DatePicker } from '@shared/form/tarih/date-picker';
import { Icon } from '@shared/ikon/icon';
import type { DisaAktarma } from '@shared/tablo/disa-aktarma';
import { Table } from '@shared/tablo/table';
import { TableCell } from '@shared/tablo/table-cell';

import { orderColumns } from '../finance-columns';
import {
  ORDER_EXPORT_NAMES,
  ORDER_LIST,
  ORDER_STATUSES,
  type OrderDetail,
  type OrderRow,
  exportParameters,
} from '../finance-model';
import { ORDERS, OrderListStore, recordPath } from '../finance.store';
import { CustomerLabels } from '../labels';
import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { FilterPanelComponent } from '@shared/filtre-paneli/filtre-paneli';

type OrderStatus = (typeof ORDER_STATUSES)[number];
type Transition = 'onayla' | 'teslim-al' | 'iptal';

/**
 * Araç sipariş / tedarik (`/app/arac-siparis`) — Blazor `AracSiparisList.razor` paritesi: süzgeçler, 20 sütunluk
 * liste + dışa aktarma, satırda Onayla / Teslim Al / İptal (onaylı) / Düzenle. Düğmeler satırın sunucu `yetkiler`
 * bayraklarından (servisin tek geçiş tablosu + OperationsWrite). Form `/arac-siparis/yeni` ve `/:id`. Deftere yazmaz.
 */
@Component({
  selector: 'rc-order-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FilterPanelComponent,
    PageBand,
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    SearchSelection,
    Icon,
    TextInput,
    Selection,
    Table,
    TableCell,
    DatePicker,
  ],
  providers: [FetchPolicy, OrderListStore, CustomerLabels],
  templateUrl: './order-list.html',
  styleUrl: '../vehicle-finance.scss',
})
export class OrderList {
  protected readonly store = inject(OrderListStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly labels = inject(CustomerLabels);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = translationFunction();

  protected readonly query = listQueryUrlSync(ORDER_LIST);
  protected readonly rowId = (r: OrderRow) => r.id;
  protected readonly customers = serverSelectionSource('musteri');
  protected readonly canCreate = computed(() => this.session.izinVar('OperationsWrite'));
  protected readonly busy = signal<string | null>(null);

  private readonly loanLabels = computed(
    () =>
      new Map(
        (this.store.loans.veri()?.kayitlar ?? []).map((k) => [k.id, `${k.no} — ${k.bankaAdi}`]),
      ),
  );
  protected readonly columns = orderColumns(this.t, (id) =>
    id ? (this.loanLabels().get(id) ?? '—') : '—',
  );

  protected readonly statusOptions: readonly SecenekOgesi<OrderStatus>[] = ORDER_STATUSES.map(
    (s) => ({
      deger: s,
      etiket: this.t(`aracFinans.siparis.durumlar.${s}`),
    }),
  );

  protected readonly filterForm = new FormGroup({
    cari: new FormControl<SecimSecenegi | null>(null),
    ara: new FormControl<string | null>(null),
    arac: new FormControl<string | null>(null),
    dosyaNo: new FormControl<string | null>(null),
    durum: new FormControl<OrderStatus | null>(null),
    bas: new FormControl<string | null>(null),
    bit: new FormControl<string | null>(null),
  });

  protected readonly export = computed<DisaAktarma>(() => ({
    yol: '/listeler/export/arac-siparisleri',
    parametreler: exportParameters(this.query.sorgu().filtreler, ORDER_EXPORT_NAMES),
    bicimler: ['excel', 'csv', 'pdf'],
  }));

  constructor() {
    const policy = inject(FetchPolicy);
    policy.connect({
      parametre: this.query.apiParametreleri,
      yukle: (p) => this.store.list.yukle(p),
      sifirla: () => this.store.list.reset(),
      sekmeyeDonunce: 'yenile',
    });
    policy.connect({
      parametre: signal(0).asReadonly(),
      yukle: () => this.store.loans.yukle(),
      sifirla: () => this.store.loans.reset(),
    });
    effect(() => {
      const f = this.query.sorgu().filtreler;
      const account = this.labels.label(f.cariId);
      untracked(() =>
        this.filterForm.reset({
          cari: account,
          ara: f.ara ?? null,
          arac: f.arac ?? null,
          dosyaNo: f.dosyaNo ?? null,
          durum: f.durum ?? null,
          bas: f.bas ?? null,
          bit: f.bit ?? null,
        }),
      );
    });
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    this.labels.remember(v.cari);
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        cariId: v.cari?.id ?? undefined,
        ara: v.ara ?? undefined,
        arac: v.arac ?? undefined,
        dosyaNo: v.dosyaNo ?? undefined,
        durum: v.durum ?? undefined,
        bas: v.bas ?? undefined,
        bit: v.bit ?? undefined,
      },
    });
  }

  protected clear(): void {
    void this.query.sifirla();
  }

  protected openRow(row: OrderRow): void {
    void this.router.navigate(['/arac-siparis', row.id]);
  }

  protected statusLabel(s: string): string {
    return (ORDER_STATUSES as readonly string[]).includes(s)
      ? this.t(`aracFinans.siparis.durumlar.${s as OrderStatus}`)
      : s;
  }

  protected async transition(row: OrderRow, kind: Transition): Promise<void> {
    if (this.busy() !== null) return;
    if (kind === 'iptal') {
      const yes = await this.confirm.ask({
        baslik: this.t('aracFinans.siparis.iptalBaslik'),
        mesaj: this.t(
          row.durum === 'Onaylandi'
            ? 'aracFinans.siparis.iptalMesajOnayli'
            : 'aracFinans.siparis.iptalMesaj',
          { no: row.no },
        ),
        onayEtiketi: this.t('aracFinans.siparis.iptal'),
        tehlikeli: true,
      });
      if (!yes || this.busy() !== null) return;
    }
    this.busy.set(row.id);
    this.api
      .post<OrderDetail>(recordPath(ORDERS, row.id, `/${kind}`), null)
      .pipe(
        finalize(() => this.busy.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (d) => {
          this.toast.basari(this.t('aracFinans.siparis.durumDegisti', { no: d.no }));
          this.store.list.yenile();
        },
        error: (raw: unknown) => {
          const error = toApiError(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
          this.store.list.yenile();
        },
      });
  }
}
