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
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { type UnsavedChangesOwner, pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import type { DayText } from '@core/form/tarih-girdisi';
import { ToastService } from '@core/geri-bildirim/toast-service';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { translationFunction } from '@core/i18n/ceviri';
import { SessionService } from '@core/oturum/session-service';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listQueryUrlSync } from '@core/veri/liste-sorgusu-url';
import { momentValue, textValue } from '@features/planlama-ortak/form-yardimcilari';
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
import { NumberInput } from '@shared/form/kontroller/number-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { DatePicker } from '@shared/form/tarih/date-picker';
import { Icon } from '@shared/ikon/icon';
import { Table } from '@shared/tablo/table';
import { TableCell } from '@shared/tablo/table-cell';

import { serviceColumns } from '../service-insurance-columns';
import {
  DAMAGE_PARTIES,
  type DamageParty,
  SERVICES,
  SERVICE_LIST,
  SERVICE_STATUSES,
  SERVICE_TYPES,
  type ServiceRecordDetail,
  type ServiceRecordRequest,
  type ServiceRecordRow,
  type ServiceStatus,
  type ServiceType,
} from '../service-insurance-model';
import { ServiceListStore } from '../service-insurance.store';
import { emptyInfoForm, infoRequest } from './service-form-model';
import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { FilterPanelComponent } from '@shared/filtre-paneli/filtre-paneli';
import { PlateChipComponent } from '@shared/plaka/plaka';

/**
 * Servis / bakım (`/app/servisler`) — Blazor `ServiceRecordList.razor`: durum sekmeleri, liste, "Yeni Servis Kaydı"
 * (OperationsWrite; randevu = Rezerve). Kaza/fatura/ödeme BİLGİ blokları, kalemler, durum akışı ve rücu yansıtma kayıt
 * sayfasında (`/app/servisler/:id`). Okuma OperationsWrite ∨ FinanceWrite ∨ ViewReports; ARACIN şubesi süzer.
 */
@Component({
  selector: 'rc-service-list',
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
    Icon,
    TextInput,
    Checkbox,
    NumberInput,
    Selection,
    Table,
    TableCell,
    DatePicker,
  ],
  providers: [FetchPolicy, ServiceListStore],
  templateUrl: './service-list.html',
  styleUrl: '../service-insurance.scss',
})
export class ServiceList implements UnsavedChangesOwner {
  protected readonly store = inject(ServiceListStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly session = inject(SessionService);
  private readonly t = translationFunction();

  protected readonly query = listQueryUrlSync(SERVICE_LIST);
  protected readonly columns = serviceColumns(
    this.t,
    (s) => this.statusLabel(s),
    (s) => this.typeLabel(s),
  );
  protected readonly rowId = (r: ServiceRecordRow) => r.id;
  protected readonly vehicles = serverSelectionSource('arac');
  protected readonly statuses = SERVICE_STATUSES;
  protected readonly activeStatus = computed(() => this.query.sorgu().filtreler.durum ?? null);
  protected readonly canWrite = computed(() => this.session.izinVar('OperationsWrite'));
  private readonly createToggle = signal<boolean | null>(null);
  protected readonly createOpen = computed(
    () => this.canWrite() && (this.createToggle() ?? this.store.list.veri()?.toplam === 0),
  );

  protected readonly typeOptions: readonly SecenekOgesi<ServiceType>[] = SERVICE_TYPES.map((x) => ({
    deger: x,
    etiket: this.typeLabel(x),
  }));
  protected readonly partyOptions: readonly SecenekOgesi<DamageParty>[] = DAMAGE_PARTIES.map(
    (x) => ({
      deger: x,
      etiket: this.t(`servisSigorta.servis.sorumlular.${x}` as CeviriAnahtari),
    }),
  );

  protected readonly filterForm = new FormGroup({
    plaka: new FormControl<string | null>(null),
    tip: new FormControl<ServiceType | null>(null),
    bas: new FormControl<DayText | null>(null),
    bit: new FormControl<DayText | null>(null),
  });

  protected readonly form = new FormGroup({
    arac: new FormControl<SecimSecenegi | null>(null, Validators.required),
    tip: new FormControl<ServiceType | null>('Periyodik', Validators.required),
    girisKm: new FormControl<number | null>(0, [Validators.min(0)]),
    girisTarihi: new FormControl<DayText | null>(null),
    hasarSorumlu: new FormControl<DamageParty | null>('Yok'),
    kusurOrani: new FormControl<number | null>(null, [Validators.min(0), Validators.max(1)]),
    atolyeAdi: new FormControl<string | null>(null, Validators.maxLength(128)),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(1024)),
    rezervasyon: new FormControl<boolean | null>(false),
    planBasTarihi: new FormControl<DayText | null>(null),
    planBitTarihi: new FormControl<DayText | null>(null),
  });
  protected readonly submission = formSubmission();

  constructor() {
    inject(FetchPolicy).connect({
      parametre: this.query.apiParametreleri,
      yukle: (p) => {
        this.store.list.yukle(p);
        this.store.counts.yukle(p);
      },
      sifirla: () => {
        this.store.list.reset();
        this.store.counts.reset();
      },
      sekmeyeDonunce: 'yenile',
    });
    effect(() => {
      const f = this.query.sorgu().filtreler;
      untracked(() =>
        this.filterForm.reset({
          plaka: f.plaka ?? null,
          tip: f.tip ?? null,
          bas: f.bas ?? null,
          bit: f.bit ?? null,
        }),
      );
    });
    pageLeaveGuard(() => this.form.dirty);
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty;
  }

  protected statusLabel(s: string): string {
    return (SERVICE_STATUSES as readonly string[]).includes(s)
      ? this.t(`servisSigorta.servis.durumlar.${s}` as CeviriAnahtari)
      : s;
  }

  protected typeLabel(s: string): string {
    return (SERVICE_TYPES as readonly string[]).includes(s)
      ? this.t(`servisSigorta.servis.tipler.${s}` as CeviriAnahtari)
      : s;
  }

  /** Sekme etiketi: sayaç geldiyse "Açık (3)" (sunucu sayar), gelmediyse yalın ad. */
  protected tabLabel(s: ServiceStatus | null): string {
    const name = s === null ? this.t('servisSigorta.tumu') : this.statusLabel(s);
    const c = this.store.counts.veri();
    if (!c) return name;
    const n = s === null ? c.tumu : c.durumlar.find((x) => x.durum === s)?.adet;
    return n === undefined
      ? name
      : this.t('servisSigorta.servis.sekmeSayili', { ad: name, adet: n });
  }

  protected selectStatus(s: ServiceStatus | null): void {
    void this.query.degistir({
      sayfa: 1,
      filtreler: { ...this.query.sorgu().filtreler, durum: s ?? undefined },
    });
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        durum: this.activeStatus() ?? undefined,
        plaka: textValue(v.plaka) ?? undefined,
        tip: v.tip ?? undefined,
        bas: v.bas ?? undefined,
        bit: v.bit ?? undefined,
      },
    });
  }

  protected clear(): void {
    void this.query.sifirla();
  }

  protected toggleCreate(): void {
    this.createToggle.set(!this.createOpen());
  }

  protected create(): void {
    const v = this.form.getRawValue();
    const body: ServiceRecordRequest = {
      vehicleId: v.arac?.id ?? null,
      tip: v.tip,
      girisKm: v.girisKm,
      girisTarihi: momentValue(v.girisTarihi, null),
      hasarSorumlu: v.hasarSorumlu,
      kusurOrani: v.kusurOrani,
      rezervasyon: v.rezervasyon === true,
      bilgi: {
        ...infoRequest(
          {
            ...emptyInfoForm(),
            atolyeAdi: v.atolyeAdi,
            aciklama: v.aciklama,
            planBasTarihi: v.planBasTarihi,
            planBitTarihi: v.planBitTarihi,
          },
          null,
          null,
        ),
      },
      kalem: null,
    };
    this.submission.gonder(
      this.form,
      (key) => this.api.post<ServiceRecordDetail>(SERVICES, body, { islemAnahtari: key }),
      {
        esleme: { vehicleId: 'arac', planBitTarihi: 'planBitTarihi' },
        basarili: (d) => {
          this.toast.basari(this.t('servisSigorta.servis.eklendi', { no: d.kayit.no }));
          this.form.reset({
            tip: 'Periyodik',
            girisKm: 0,
            hasarSorumlu: 'Yok',
            rezervasyon: false,
          });
          void this.router.navigate(['/servisler', d.kayit.id]);
        },
        hata: (h) => {
          if (h.kod === 'mukerrer') {
            if (h.mevcut?.ayniIcerik)
              this.form.reset({
                tip: 'Periyodik',
                girisKm: 0,
                hasarSorumlu: 'Yok',
                rezervasyon: false,
              });
            this.store.list.yenile();
            this.store.counts.yenile();
          }
        },
      },
    );
  }
}
