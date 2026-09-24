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
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { Ikon } from '@shared/ikon/ikon';
import type { DisaAktarma } from '@shared/tablo/disa-aktarma';
import { Tablo } from '@shared/tablo/tablo';
import { TabloHucre } from '@shared/tablo/tablo-hucre';

import { customerColumns } from '../customer-columns';
import {
  CUSTOMER_LIST,
  CUSTOMER_TYPES,
  customerPath,
  rowBadges,
  type CustomerRow,
  type CustomerType,
} from '../customer-model';
import { CustomerListStore } from '../customer.store';

type Tri = 'true' | 'false';
const tri = (v: boolean | undefined): Tri | null => (v === undefined ? null : v ? 'true' : 'false');
const fromTri = (v: Tri | null): boolean | undefined =>
  v === 'true' ? true : v === 'false' ? false : undefined;

/**
 * Cari listesi (`/app/cariler`) — Blazor `CustomerList` paritesi: arama (ad/ünvan/vergi no; TC yalnız TAM 11 hane),
 * tür, İYS, uyarı, kara liste, durum, araç verilmez süzgeçleri; durum rozetleri; satırda Kart / Detay / Ekstre / Sil
 * (OperationsDelete, onaylı; kirada kullanılan cari silinemez — sunucu mesajı). "Yeni Cari" tam karta gider (liste içi
 * kısa form yerine; aynı alanlar). KVKK: TC sütunu YOK; anonim müşteri etiketle. Dışa aktarma ViewReports.
 */
@Component({
  selector: 'rc-customer-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    Ikon,
    MetinGirdisi,
    Secim,
    Tablo,
    TabloHucre,
  ],
  providers: [FetchPolicy, CustomerListStore],
  templateUrl: './customer-list.html',
  styleUrl: '../customers.scss',
})
export class CustomerList {
  protected readonly store = inject(CustomerListStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly session = inject(OturumServisi);
  private readonly router = inject(Router);
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();

  protected readonly query = listeSorgusuUrlSenkronu(CUSTOMER_LIST);
  protected readonly columns = customerColumns(this.t);
  protected readonly rowId = (r: CustomerRow) => r.id;
  protected readonly badges = rowBadges;
  protected readonly canCreate = computed(() => this.session.izinVar('OperationsWrite'));
  protected readonly canDelete = computed(() => this.session.izinVar('OperationsDelete'));
  protected readonly busy = signal<string | null>(null);

  protected readonly typeOptions: readonly SecenekOgesi<CustomerType>[] = CUSTOMER_TYPES.map(
    (x) => ({ deger: x, etiket: this.t(`cari.tipler.${x}`) }),
  );
  protected readonly iysOptions: readonly SecenekOgesi<Tri>[] = [
    { deger: 'true', etiket: this.t('cari.filtre.izinli') },
    { deger: 'false', etiket: this.t('cari.filtre.izinsiz') },
  ];
  protected readonly flagOptions = (label: string): readonly SecenekOgesi<Tri>[] => [
    { deger: 'true', etiket: label },
  ];
  protected readonly warningOptions = this.flagOptions(this.t('cari.filtre.uyarili'));
  protected readonly blacklistOptions = this.flagOptions(this.t('cari.filtre.karaListede'));
  protected readonly noVehicleOptions = this.flagOptions(this.t('cari.filtre.isaretli'));
  protected readonly statusOptions: readonly SecenekOgesi<Tri>[] = [
    { deger: 'false', etiket: this.t('cari.filtre.aktif') },
    { deger: 'true', etiket: this.t('cari.filtre.pasif') },
  ];

  protected readonly filterForm = new FormGroup({
    q: new FormControl<string | null>(null),
    tip: new FormControl<CustomerType | null>(null),
    iysIzinli: new FormControl<Tri | null>(null),
    uyari: new FormControl<Tri | null>(null),
    karaListe: new FormControl<Tri | null>(null),
    pasif: new FormControl<Tri | null>(null),
    aracVerilmez: new FormControl<Tri | null>(null),
  });

  /** Blazor bağlantıları süzgeçsiz tüm listeyi indirir (uç süzgeç okumaz). ViewReports. */
  protected readonly export = computed<DisaAktarma | null>(() =>
    this.session.izinVar('ViewReports')
      ? { yol: '/listeler/export/cariler', parametreler: {}, bicimler: ['excel', 'csv', 'pdf'] }
      : null,
  );

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
          q: f.q ?? null,
          tip: f.tip ?? null,
          iysIzinli: tri(f.iysIzinli),
          uyari: f.uyari ? 'true' : null,
          karaListe: f.karaListe ? 'true' : null,
          pasif: tri(f.pasif),
          aracVerilmez: f.aracVerilmez ? 'true' : null,
        }),
      );
    });
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        q: v.q?.trim() || undefined,
        tip: v.tip ?? undefined,
        iysIzinli: fromTri(v.iysIzinli),
        uyari: fromTri(v.uyari),
        karaListe: fromTri(v.karaListe),
        pasif: fromTri(v.pasif),
        aracVerilmez: fromTri(v.aracVerilmez),
      },
    });
  }

  protected clear(): void {
    void this.query.sifirla();
  }

  protected openRow(row: CustomerRow): void {
    void this.router.navigate(['/cariler', row.id]);
  }

  protected async remove(row: CustomerRow): Promise<void> {
    if (this.busy() !== null) return;
    const yes = await this.confirm.sor({
      baslik: this.t('cari.silBaslik'),
      mesaj: this.t('cari.silMesaj', { ad: row.ad }),
      onayEtiketi: this.t('cari.sil'),
      tehlikeli: true,
    });
    if (!yes || this.busy() !== null) return;
    this.busy.set(row.id);
    this.api
      .delete<unknown>(customerPath(row.id))
      .pipe(
        finalize(() => this.busy.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.toast.basari(this.t('cari.silindi', { ad: row.ad }));
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
