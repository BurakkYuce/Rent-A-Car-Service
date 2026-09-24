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

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listeSorgusuUrlSenkronu } from '@core/veri/liste-sorgusu-url';
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
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    AramaSecim,
    Ikon,
    MetinGirdisi,
    Secim,
    Tablo,
    TabloHucre,
    TarihSecici,
  ],
  providers: [FetchPolicy, OrderListStore, CustomerLabels],
  templateUrl: './order-list.html',
  styleUrl: '../vehicle-finance.scss',
})
export class OrderList {
  protected readonly store = inject(OrderListStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly session = inject(OturumServisi);
  private readonly router = inject(Router);
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly labels = inject(CustomerLabels);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();

  protected readonly query = listeSorgusuUrlSenkronu(ORDER_LIST);
  protected readonly rowId = (r: OrderRow) => r.id;
  protected readonly customers = sunucuSecimKaynagi('musteri');
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
    policy.baglan({
      parametre: this.query.apiParametreleri,
      yukle: (p) => this.store.list.yukle(p),
      sifirla: () => this.store.list.sifirla(),
      sekmeyeDonunce: 'yenile',
    });
    policy.baglan({
      parametre: signal(0).asReadonly(),
      yukle: () => this.store.loans.yukle(),
      sifirla: () => this.store.loans.sifirla(),
    });
    effect(() => {
      const f = this.query.sorgu().filtreler;
      const cari = this.labels.label(f.cariId);
      untracked(() =>
        this.filterForm.reset({
          cari,
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
      const yes = await this.confirm.sor({
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
          const error = apiHatasinaCevir(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
          this.store.list.yenile();
        },
      });
  }
}
