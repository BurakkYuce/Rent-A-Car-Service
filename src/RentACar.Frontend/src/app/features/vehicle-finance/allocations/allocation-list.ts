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
import { Router } from '@angular/router';
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
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listeSorgusuUrlSenkronu } from '@core/veri/liste-sorgusu-url';
import { readAllocationPrefill } from '@features/vehicles/allocation-prefill';
import { suggestionList } from '@features/vehicles/suggestions';
import { secimSuggestionFetch } from '@features/vehicles/vehicle.store';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { OnayKutusu } from '@shared/form/kontroller/onay-kutusu';
import { SayiGirdisi } from '@shared/form/kontroller/sayi-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';
import { Ikon } from '@shared/ikon/ikon';
import type { DisaAktarma } from '@shared/tablo/disa-aktarma';
import { Tablo } from '@shared/tablo/tablo';
import { TabloHucre } from '@shared/tablo/tablo-hucre';

import { allocationColumns } from '../finance-columns';
import {
  ALLOCATION_LIST,
  ALLOCATION_LOCATIONS,
  ALLOCATION_PURPOSES,
  ALLOCATION_STATUSES,
  type Allocation,
  type AllocationPurpose,
} from '../finance-model';
import { ALLOCATIONS, AllocationStore, recordPath } from '../finance.store';
import {
  TIME_PATTERN,
  type AllocationFormValue,
  allocationRequest,
  emptyAllocation,
} from './allocation-model';
import { AllocationReturnPanel } from './allocation-return-panel';

type AllocationStatus = (typeof ALLOCATION_STATUSES)[number];
type AllocationLocation = (typeof ALLOCATION_LOCATIONS)[number];

/**
 * BAF — personel araç tahsis (`/app/baf`) — Blazor `BafList.razor` paritesi: FAZ-18 arama paneli, "Yeni Tahsis"
 * (OperationsWrite; şubeye bağlı kullanıcı yalnız kendi şubesine), liste + dışa aktarma, satırda "Teslim Al" (satır
 * formu) ve İptal (OperationsDelete, onaylı). Deftere yazmaz. Şube kapsamı sunucuda (süzgeç genişletemez).
 */
@Component({
  selector: 'rc-allocation-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    AllocationReturnPanel,
    AramaSecim,
    FormHatalari,
    Ikon,
    MetinGirdisi,
    OnayKutusu,
    SayiGirdisi,
    Secim,
    Tablo,
    TabloHucre,
    TarihSecici,
  ],
  providers: [FetchPolicy, AllocationStore],
  templateUrl: './allocation-list.html',
  styleUrl: '../vehicle-finance.scss',
})
export class AllocationList implements KaydedilmemisDegisiklikSahibi {
  protected readonly store = inject(AllocationStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly session = inject(OturumServisi);
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();

  protected readonly query = listeSorgusuUrlSenkronu(ALLOCATION_LIST);
  protected readonly rowId = (r: Allocation) => r.id;
  protected readonly staff = sunucuSecimKaynagi('personel');
  protected readonly vehicles = sunucuSecimKaynagi('arac');
  protected readonly canCancel = computed(() => this.session.izinVar('OperationsDelete'));
  protected readonly busy = signal<string | null>(null);
  protected readonly createOpen = signal(false);
  /** Açık "Teslim Al" formu (0 ya da 1 satır; `@for track id` ile — satır değişince form yeniden kurulur). */
  protected readonly returning = signal<readonly Allocation[]>([]);
  /** URL'deki personel kimliğinin etiketi (yalnız bellekte). */
  private readonly staffLabels = new Map<string, string>();

  protected readonly columns = allocationColumns(this.t, (p) => this.purposeLabel(p));
  /** Blazor dışa aktarması süzgeçsizdir (uç süzgeç okumaz). */
  protected readonly export: DisaAktarma = {
    yol: '/listeler/export/baflar',
    bicimler: ['excel', 'csv', 'pdf'],
  };
  protected readonly purposeOptions: readonly SecenekOgesi<AllocationPurpose>[] =
    ALLOCATION_PURPOSES.map((p) => ({ deger: p, etiket: this.purposeLabel(p) }));
  protected readonly statusOptions: readonly SecenekOgesi<AllocationStatus>[] =
    ALLOCATION_STATUSES.map((s) => ({ deger: s, etiket: this.t(`aracFinans.baf.durumlar.${s}`) }));
  protected readonly locationOptions: readonly SecenekOgesi<AllocationLocation>[] =
    ALLOCATION_LOCATIONS.map((l) => ({
      deger: l,
      etiket: this.t(`aracFinans.baf.lokasyonlar.${l}`),
    }));

  protected readonly filterForm = new FormGroup({
    personel: new FormControl<SecimSecenegi | null>(null),
    plaka: new FormControl<string | null>(null),
    durum: new FormControl<AllocationStatus | null>(null),
    kullanimAmaci: new FormControl<AllocationPurpose | null>(null),
    lokasyon: new FormControl<AllocationLocation | null>(null),
    ofis: new FormControl<string | null>(null),
    bas: new FormControl<string | null>(null),
    bit: new FormControl<string | null>(null),
  });

  protected readonly form = new FormGroup({
    personel: new FormControl<SecimSecenegi | null>(null, Validators.required),
    arac: new FormControl<SecimSecenegi | null>(null, Validators.required),
    cikisTarihi: new FormControl<string | null>(null),
    cikisSaat: new FormControl<string | null>(null, Validators.pattern(TIME_PATTERN)),
    cikisKm: new FormControl<number | null>(0, [Validators.min(0), Validators.max(10_000_000)]),
    cikisYakit: new FormControl<number | null>(null, [Validators.min(0), Validators.max(12)]),
    sube: new FormControl<string | null>(null, Validators.maxLength(100)),
    kullanimAmaci: new FormControl<AllocationPurpose | null>(null),
    onaylayan: new FormControl<SecimSecenegi | null>(null),
    kirayaVer: new FormControl<boolean | null>(false),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  protected readonly submission = formGonderimi();
  protected readonly branchSuggestions = suggestionList(
    this.form.controls.sube,
    secimSuggestionFetch(this.api, 'sube'),
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
          personel: f.personelId
            ? {
                id: f.personelId,
                etiket: this.staffLabels.get(f.personelId) ?? this.t('aracFinans.seciliPersonel'),
              }
            : null,
          plaka: f.plaka ?? null,
          durum: f.durum ?? null,
          kullanimAmaci: f.kullanimAmaci ?? null,
          lokasyon: f.lokasyon ?? null,
          ofis: f.ofis ?? null,
          bas: f.bas ?? null,
          bit: f.bit ?? null,
        }),
      );
    });
    this.form.reset({ ...emptyAllocation() });
    // Durum panosu "Tahsis" (router state): form araç + çıkış KM + şube dolu açılır, personel burada seçilir.
    const prefill = readAllocationPrefill(inject(Router).currentNavigation()?.extras.state);
    if (prefill) {
      this.form.reset({
        ...emptyAllocation(),
        arac: { id: prefill.vehicleId, etiket: prefill.plaka },
        cikisKm: prefill.km ?? 0,
        sube: prefill.sube,
      });
      this.createOpen.set(true);
    }
    sayfaTerkKorumasi(() => this.form.dirty);
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.createOpen() && this.form.dirty;
  }

  protected purposeLabel(p: string | null): string {
    if (p === null) return '—';
    return (ALLOCATION_PURPOSES as readonly string[]).includes(p)
      ? this.t(`aracFinans.baf.amaclar.${p as AllocationPurpose}`)
      : p;
  }

  protected statusLabel(s: string): string {
    return (ALLOCATION_STATUSES as readonly string[]).includes(s)
      ? this.t(`aracFinans.baf.durumlar.${s as AllocationStatus}`)
      : s;
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    if (v.personel) this.staffLabels.set(v.personel.id, v.personel.etiket);
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        personelId: v.personel?.id ?? undefined,
        plaka: v.plaka ?? undefined,
        durum: v.durum ?? undefined,
        kullanimAmaci: v.kullanimAmaci ?? undefined,
        lokasyon: v.lokasyon ?? undefined,
        ofis: v.ofis ?? undefined,
        bas: v.bas ?? undefined,
        bit: v.bit ?? undefined,
      },
    });
  }

  protected clear(): void {
    void this.query.sifirla();
  }

  protected toggleCreate(): void {
    this.createOpen.update((v) => !v);
  }

  protected create(): void {
    const body = allocationRequest(this.form.getRawValue() as AllocationFormValue);
    this.submission.gonder(
      this.form,
      (key) => this.api.post<Allocation>(ALLOCATIONS, body, { islemAnahtari: key }),
      {
        esleme: { personelId: 'personel', vehicleId: 'arac' },
        basarili: (a) => {
          this.toast.basari(this.t('aracFinans.baf.olusturuldu', { no: a.no }));
          this.form.reset({ ...emptyAllocation() });
          this.createOpen.set(false);
          this.store.list.yenile();
        },
        hata: (h) => {
          if (h.kod === 'mukerrer') {
            if (h.mevcut?.ayniIcerik) this.form.reset({ ...emptyAllocation() });
            this.store.list.yenile();
          }
        },
      },
    );
  }

  protected openReturn(row: Allocation): void {
    this.returning.set([row]);
  }

  protected returnDone(): void {
    this.returning.set([]);
    this.store.list.yenile();
  }

  protected async cancel(row: Allocation): Promise<void> {
    if (this.busy() !== null) return;
    const yes = await this.confirm.sor({
      baslik: this.t('aracFinans.baf.iptalBaslik'),
      mesaj: this.t('aracFinans.baf.iptalMesaj', { no: row.no }),
      onayEtiketi: this.t('aracFinans.baf.iptal'),
      tehlikeli: true,
    });
    if (!yes || this.busy() !== null) return;
    this.busy.set(row.id);
    this.api
      .post<Allocation>(recordPath(ALLOCATIONS, row.id, '/iptal'), null)
      .pipe(
        finalize(() => this.busy.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.toast.basari(this.t('aracFinans.baf.iptalEdildi', { no: row.no }));
          if (this.returning()[0]?.id === row.id) this.returning.set([]);
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
