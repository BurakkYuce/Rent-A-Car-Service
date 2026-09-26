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
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { finalize, map, startWith } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi, type SorguParametreleri } from '@core/api/api-istemcisi';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listeSorgusuUrlSenkronu } from '@core/veri/liste-sorgusu-url';
import { SayiPipe } from '@shared/bicim/bicim-pipe';
import { FilterPanelComponent } from '@shared/filtre-paneli/filtre-paneli';
import { Alan } from '@shared/form/alan/alan';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';
import { SavedViewChipsComponent, type SavedView } from '@shared/gorunum-cipleri/gorunum-cipleri';
import { Ikon } from '@shared/ikon/ikon';
import { PlateChipComponent } from '@shared/plaka/plaka';
import { StatusSignCardComponent } from '@shared/tabela-karti/tabela-karti';
import type { DisaAktarma } from '@shared/tablo/disa-aktarma';
import { Tablo } from '@shared/tablo/tablo';
import { TabloHucre } from '@shared/tablo/tablo-hucre';

import { SayfaBandi } from '../../../kabuk/sayfa-bandi/sayfa-bandi';
import { suggestionList } from '../suggestions';
import { SCORECARD_ROLES } from '../vehicle-guards';
import {
  DATE_KINDS,
  GROUP_KINDS,
  STATUS_BADGE,
  VEHICLE_LIST,
  VEHICLE_STATUSES,
  toNumber,
  vehicleStatus,
  type VehicleListRow,
  type VehicleStatus,
} from '../vehicle-model';
import { VehicleListStore, secimSuggestionFetch, vehicleSuggestionFetch } from '../vehicle.store';
import { vehicleColumns } from './vehicle-columns';

/** "Sahibi girilmemiş" seçeneğinin değeri (gerçek bir sahip adıyla çakışmaz). */
const OWNER_EMPTY = '-bos-';

interface FilterValue {
  q: string | null;
  grupTuru: (typeof GROUP_KINDS)[number] | null;
  grup: string | null;
  durum: VehicleStatus | null;
  sube: string | null;
  sahip: string | null;
  tarihTuru: (typeof DATE_KINDS)[number] | null;
  tarihBas: string | null;
  tarihBit: string | null;
}

/**
 * Araç listesi (`/app/araclar`) — Blazor `VehicleList.razor` paritesi: süzgeçler (plaka/marka, Grup|SIPP,
 * durum, şube, araç sahibi + "sahibi girilmemiş" kovası, tarih türü + aralık), özet şerit (tüm filo), düz
 * liste (49 sütun, sabit plaka, kullanıcı sütun düzeni) ↔ "modele göre grupla" görünümü, sunucu dışa
 * aktarması (ViewReports), satırda Detay / Karne (finans rolleri) / Sil (OperationsDelete, onaylı).
 */
@Component({
  selector: 'rc-vehicle-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    FilterPanelComponent,
    Ikon,
    MetinGirdisi,
    PlateChipComponent,
    SavedViewChipsComponent,
    SayfaBandi,
    SayiPipe,
    Secim,
    StatusSignCardComponent,
    Tablo,
    TabloHucre,
    TarihSecici,
  ],
  providers: [FetchPolicy, VehicleListStore],
  templateUrl: './vehicle-list.html',
  styleUrl: '../vehicle-screens.scss',
})
export class VehicleList {
  protected readonly store = inject(VehicleListStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly session = inject(OturumServisi);
  private readonly router = inject(Router);
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();

  protected readonly query = listeSorgusuUrlSenkronu(VEHICLE_LIST);
  protected readonly columns = vehicleColumns(this.t);
  protected readonly rowId = (r: VehicleListRow) => r.id;
  protected readonly num = toNumber;
  protected readonly deleting = signal<string | null>(null);

  protected readonly canWrite = computed(() => this.session.izinVar('OperationsWrite'));
  protected readonly canDelete = computed(() => this.session.izinVar('OperationsDelete'));
  protected readonly canReport = computed(() => this.session.izinVar('ViewReports'));
  protected readonly canScorecard = computed(() =>
    SCORECARD_ROLES.includes(this.session.ben()?.rol ?? ''),
  );
  protected readonly grouped = computed(() => this.query.sorgu().filtreler.gorunum === 'grup');

  protected readonly filterForm = new FormGroup({
    q: new FormControl<string | null>(null),
    grupTuru: new FormControl<(typeof GROUP_KINDS)[number] | null>(null),
    grup: new FormControl<string | null>(null),
    durum: new FormControl<VehicleStatus | null>(null),
    sube: new FormControl<string | null>(null),
    sahip: new FormControl<string | null>(null),
    tarihTuru: new FormControl<(typeof DATE_KINDS)[number] | null>(null),
    tarihBas: new FormControl<string | null>(null),
    tarihBit: new FormControl<string | null>(null),
  });

  private readonly groupKind = toSignal(
    this.filterForm.controls.grupTuru.valueChanges.pipe(
      startWith(this.filterForm.controls.grupTuru.value),
      map((v) => v ?? 'Grup'),
    ),
    { initialValue: 'Grup' as const },
  );
  protected readonly sippMode = computed(() => this.groupKind() === 'Sipp');

  protected readonly groupSuggestions = suggestionList(
    this.filterForm.controls.grup,
    secimSuggestionFetch(this.api, 'arac-grubu'),
    () => this.canWrite() && !this.sippMode(),
  );
  protected readonly branchSuggestions = suggestionList(
    this.filterForm.controls.sube,
    secimSuggestionFetch(this.api, 'sube'),
    () => this.canWrite(),
  );
  private readonly owners = signal<readonly string[]>([]);

  protected readonly statusOptions: readonly SecenekOgesi<VehicleStatus>[] = VEHICLE_STATUSES.map(
    (s) => ({ deger: s, etiket: this.t(`arac.durumlar.${s}`) }),
  );
  protected readonly groupKindOptions: readonly SecenekOgesi<(typeof GROUP_KINDS)[number]>[] = [
    { deger: 'Grup', etiket: this.t('arac.filtre.grupTuruGrup') },
    { deger: 'Sipp', etiket: this.t('arac.filtre.grupTuruSipp') },
  ];
  protected readonly dateKindOptions: readonly SecenekOgesi<(typeof DATE_KINDS)[number]>[] =
    DATE_KINDS.map((k) => ({ deger: k, etiket: this.t(`arac.filtre.tarihTurleri.${k}`) }));
  protected readonly ownerOptions = computed<readonly SecenekOgesi<string>[]>(() => {
    const current = this.query.sorgu().filtreler.aracSahibi;
    const names = [...this.owners()];
    if (current && !names.includes(current)) names.push(current);
    return [
      { deger: OWNER_EMPTY, etiket: this.t('arac.filtre.sahibiGirilmemis') },
      ...names.map((n) => ({ deger: n, etiket: n })),
    ];
  });

  protected readonly export = computed<DisaAktarma | null>(() =>
    this.canReport()
      ? { yol: '/listeler/export/araclar', bicimler: ['excel', 'csv', 'pdf'] }
      : null,
  );

  protected readonly total = computed(() => this.store.list.veri()?.toplam ?? null);

  /**
   * Durum çipleri: mevcut `durum` süzgecinin kısayolu (ayrı durum yok — seçim URL'deki `durum`dur). Sayaçlar
   * filo özetinden; özetin bilmediği durumlarda sayaç yok.
   */
  protected readonly statusViews = computed<readonly SavedView[]>(() => {
    const current = this.query.sorgu().filtreler.durum ?? null;
    const o = this.store.summary.veri();
    const count: Partial<Record<VehicleStatus | 'tumu', number>> = o
      ? {
          tumu: toNumber(o.toplam) ?? undefined,
          Musait: toNumber(o.musait) ?? undefined,
          Serviste: toNumber(o.serviste) ?? undefined,
        }
      : {};
    return [
      {
        id: 'tumu',
        ad: this.t('arac.liste.tumAraclar'),
        sayac: count.tumu ?? null,
        aktif: current === null,
      },
      ...VEHICLE_STATUSES.map((s) => ({
        id: s,
        ad: this.t(`arac.durumlar.${s}`),
        sayac: count[s] ?? null,
        aktif: current === s,
      })),
    ];
  });

  constructor() {
    const policy = inject(FetchPolicy);
    policy.baglan({
      parametre: this.query.apiParametreleri,
      yukle: (p) => {
        if (this.grouped()) {
          this.store.modelGroups.yukle(this.withoutView(p));
        } else {
          this.store.list.yukle(this.withoutView(p));
        }
      },
      sifirla: () => {
        this.store.list.sifirla();
        this.store.modelGroups.sifirla();
      },
      sekmeyeDonunce: 'yenile',
    });
    policy.baglan({
      parametre: signal(null).asReadonly(),
      yukle: () => this.store.summary.yukle(),
      sifirla: () => this.store.summary.sifirla(),
      sekmeyeDonunce: 'yenile',
    });
    vehicleSuggestionFetch(
      this.api,
      'sahip',
    )('')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (list) => this.owners.set(list), error: () => this.owners.set([]) });

    effect(() => {
      const f = this.query.sorgu().filtreler;
      untracked(() =>
        this.filterForm.reset({
          q: f.q ?? null,
          grupTuru: f.grupTuru ?? null,
          grup: f.grup ?? null,
          durum: f.durum ?? null,
          sube: f.sube ?? null,
          sahip: f.sahiplik === 'Girilmemis' ? OWNER_EMPTY : (f.aracSahibi ?? null),
          tarihTuru: f.tarihTuru ?? null,
          tarihBas: f.tarihBas ?? null,
          tarihBit: f.tarihBit ?? null,
        }),
      );
    });
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue() as FilterValue;
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        ...this.query.sorgu().filtreler,
        q: v.q ?? undefined,
        grupTuru: v.grupTuru ?? undefined,
        grup: v.grup ?? undefined,
        durum: v.durum ?? undefined,
        sube: v.sube ?? undefined,
        sahiplik: v.sahip === OWNER_EMPTY ? 'Girilmemis' : undefined,
        aracSahibi: v.sahip && v.sahip !== OWNER_EMPTY ? v.sahip : undefined,
        tarihTuru: v.tarihTuru ?? undefined,
        tarihBas: v.tarihBas ?? undefined,
        tarihBit: v.tarihBit ?? undefined,
      },
    });
  }

  protected selectStatus(view: SavedView): void {
    const status = vehicleStatus(view.id);
    void this.query.degistir({
      sayfa: 1,
      filtreler: { ...this.query.sorgu().filtreler, durum: status ?? undefined },
    });
  }

  /** Oran kartı için pay (toplam 0 ise çubuk yok). */
  protected share(part: number | string, whole: number | string): number | null {
    const w = toNumber(whole);
    const p = toNumber(part);
    return w !== null && p !== null && w > 0 ? p / w : null;
  }

  /** Doluluk yüzdesi → 0–1 oran. */
  protected percentRatio(value: number | string): number | null {
    const v = toNumber(value);
    return v === null ? null : v / 100;
  }

  /** Marka hücresinin ikinci satırı: tip · yakıt · vites · model yılı (boşlar atlanır). */
  protected vehicleSubLine(row: VehicleListRow): string {
    return [row.tip, row.yakit, row.vites, row.modelYili]
      .filter((v) => v !== null && v !== undefined && v !== '')
      .join(' · ');
  }

  protected clear(): void {
    const view = this.query.sorgu().filtreler.gorunum;
    void this.query.sifirla().then(() => {
      if (view) void this.query.degistir({ filtreler: { gorunum: view } });
    });
  }

  protected setView(view: 'grup' | null): void {
    void this.query.degistir({
      sayfa: 1,
      filtreler: { ...this.query.sorgu().filtreler, gorunum: view ?? undefined },
    });
  }

  protected openRow(row: VehicleListRow): void {
    void this.router.navigate(['/araclar', row.id]);
  }

  protected badge(status: string): string {
    const s = vehicleStatus(status);
    return `rc-rozet ${s ? STATUS_BADGE[s] : ''}`;
  }

  protected statusLabel(status: string): string {
    const s = vehicleStatus(status);
    return s ? this.t(`arac.durumlar.${s}`) : status;
  }

  protected async remove(row: VehicleListRow): Promise<void> {
    if (this.deleting() !== null) return;
    const yes = await this.confirm.sor({
      baslik: this.t('arac.silBaslik'),
      mesaj: this.t('arac.silMesaj', { plaka: row.plaka }),
      onayEtiketi: this.t('arac.sil'),
      tehlikeli: true,
    });
    if (!yes) return;
    this.deleting.set(row.id);
    this.api
      .delete<null>(`/api/ui/v1/araclar/${encodeURIComponent(row.id)}`)
      .pipe(
        finalize(() => this.deleting.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.toast.basari(this.t('arac.silindi', { plaka: row.plaka }));
          this.store.list.yenile();
          this.store.summary.yenile();
        },
        error: (raw: unknown) => {
          const error = apiHatasinaCevir(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
        },
      });
  }

  /** `gorunum` yalnız ekran içindir; API'ye gitmez. */
  private withoutView(p: SorguParametreleri): SorguParametreleri {
    return Object.fromEntries(Object.entries(p).filter(([name]) => name !== 'gorunum'));
  }
}
