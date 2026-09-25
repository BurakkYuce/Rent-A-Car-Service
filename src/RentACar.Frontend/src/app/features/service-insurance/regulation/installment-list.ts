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
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import type { Observable } from 'rxjs';

import { ApiIstemcisi, type SorguParametreleri } from '@core/api/api-istemcisi';
import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import type { GunMetni } from '@core/form/tarih-girdisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import type { Sayfa } from '@core/api/sayfa';
import { FetchPolicy } from '@core/veri/fetch-policy';
import type { TemelStore } from '@core/veri/temel-store';
import type { FiltreKatalogu, ListeTanimi } from '@core/veri/liste-sorgusu';
import { listeSorgusuUrlSenkronu } from '@core/veri/liste-sorgusu-url';
import { anDegeri, metinDegeri } from '@features/planlama-ortak/form-yardimcilari';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import { SayiGirdisi } from '@shared/form/kontroller/sayi-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';
import { Ikon } from '@shared/ikon/ikon';
import { Tablo } from '@shared/tablo/tablo';
import { TabloHucre } from '@shared/tablo/tablo-hucre';
import type { TabloSutunu } from '@shared/tablo/tablo-modeli';

import { inspectionColumns, mtvColumns } from '../service-insurance-columns';
import {
  INSPECTION_LIST,
  type InspectionDetail,
  type InspectionRequest,
  MTV_LIST,
  type MtvDetail,
  type MtvRequest,
  REGULATION,
} from '../service-insurance-model';
import { InspectionListStore, MtvListStore } from '../service-insurance.store';
import type { InstallmentKind } from './installment-pay-panel';
import { RegulationTabs } from './regulation-tabs';
import { SayfaBandi } from '../../../kabuk/sayfa-bandi/sayfa-bandi';
import { FilterPanelComponent } from '@shared/filtre-paneli/filtre-paneli';
import { PlateChipComponent } from '@shared/plaka/plaka';

interface Row {
  readonly id: string;
  readonly plaka: string;
}

/**
 * MTV (`/app/regulasyon/mtv`) ve muayene (`/app/regulasyon/muayene`) listeleri — Blazor `/regulasyon` MTV / Muayene
 * bölümleri: süzgeç, liste, "Ekle" (OperationsWrite; bilgi kaydı, deftere yazmaz). Kısmi ödeme ve ödeme geçmişi
 * kayıt sayfasında. Muayene ekleme Blazor'da FinanceWrite ile kapılıydı; uç OperationsWrite ister — SPA uca uyar.
 */
@Component({
  selector: 'rc-installment-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PlateChipComponent,
    FilterPanelComponent,
    SayfaBandi,
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    AramaSecim,
    FormHatalari,
    Ikon,
    MetinGirdisi,
    ParaGirdisi,
    RegulationTabs,
    SayiGirdisi,
    Secim,
    Tablo,
    TabloHucre,
    TarihSecici,
  ],
  providers: [FetchPolicy, MtvListStore, InspectionListStore],
  templateUrl: './installment-list.html',
  styleUrl: '../service-insurance.scss',
})
export class InstallmentList implements KaydedilmemisDegisiklikSahibi {
  protected readonly kind: InstallmentKind =
    (inject(ActivatedRoute).snapshot.data['kind'] as InstallmentKind | undefined) ?? 'mtv';
  private readonly mtvStore = inject(MtvListStore);
  private readonly inspectionStore = inject(InspectionListStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly session = inject(OturumServisi);
  private readonly t = ceviriFonksiyonu();

  protected readonly list = (this.kind === 'mtv'
    ? this.mtvStore.list
    : this.inspectionStore.list) as unknown as TemelStore<Sayfa<Row>, SorguParametreleri>;
  protected readonly query = listeSorgusuUrlSenkronu<FiltreKatalogu>(
    (this.kind === 'mtv' ? MTV_LIST : INSPECTION_LIST) as ListeTanimi<FiltreKatalogu>,
  );
  protected readonly columns = (this.kind === 'mtv'
    ? mtvColumns(this.t)
    : inspectionColumns(this.t)) as readonly TabloSutunu<Row>[];
  protected readonly rowId = (r: Row) => r.id;
  protected readonly detailRoot =
    this.kind === 'mtv' ? '/regulasyon/mtv' : '/regulasyon/muayeneler';
  protected readonly vehicles = sunucuSecimKaynagi('arac');
  protected readonly canWrite = computed(() => this.session.izinVar('OperationsWrite'));
  private readonly createToggle = signal<boolean | null>(null);
  protected readonly createOpen = computed(
    () => this.canWrite() && (this.createToggle() ?? this.list.veri()?.toplam === 0),
  );
  protected readonly paidOptions: readonly SecenekOgesi<'true' | 'false'>[] = [
    { deger: 'true', etiket: this.t('servisSigorta.evet') },
    { deger: 'false', etiket: this.t('servisSigorta.hayir') },
  ];

  protected readonly filterForm = new FormGroup({
    plaka: new FormControl<string | null>(null),
    odendi: new FormControl<'true' | 'false' | null>(null),
    bas: new FormControl<GunMetni | null>(null),
    bit: new FormControl<GunMetni | null>(null),
  });

  /** MTV: dönem + tutar + vade. Muayene: muayene tarihi + bitiş + ücret + işlem km. */
  protected readonly form = new FormGroup({
    arac: new FormControl<SecimSecenegi | null>(null, Validators.required),
    donem: new FormControl<string | null>(
      null,
      this.kind === 'mtv' ? [Validators.required, Validators.maxLength(16)] : [],
    ),
    tutar: new FormControl<string | null>(null),
    tarih: new FormControl<GunMetni | null>(null, Validators.required),
    bitis: new FormControl<GunMetni | null>(
      null,
      this.kind === 'muayene' ? Validators.required : [],
    ),
    islemKm: new FormControl<number | null>(null),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  protected readonly submission = formGonderimi();

  constructor() {
    inject(FetchPolicy).baglan({
      parametre: this.query.apiParametreleri,
      yukle: (p: SorguParametreleri) => this.list.yukle(p),
      sifirla: () => this.list.sifirla(),
      sekmeyeDonunce: 'yenile',
    });
    effect(() => {
      const f = this.query.sorgu().filtreler as Readonly<Record<string, string | undefined>>;
      const [bas, bit] = this.kind === 'mtv' ? ['vadeBas', 'vadeBit'] : ['bitisBas', 'bitisBit'];
      untracked(() =>
        this.filterForm.reset({
          plaka: f['plaka'] ?? null,
          odendi: (f['odendi'] as 'true' | 'false' | undefined) ?? null,
          bas: f[bas] ?? null,
          bit: f[bit] ?? null,
        }),
      );
    });
    sayfaTerkKorumasi(() => this.form.dirty);
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.form.dirty;
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    const range =
      this.kind === 'mtv'
        ? { vadeBas: v.bas ?? undefined, vadeBit: v.bit ?? undefined }
        : { bitisBas: v.bas ?? undefined, bitisBit: v.bit ?? undefined };
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        plaka: metinDegeri(v.plaka) ?? undefined,
        odendi: v.odendi ?? undefined,
        ...range,
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
    const mtv: MtvRequest = {
      vehicleId: v.arac?.id ?? null,
      donem: metinDegeri(v.donem),
      tutar: v.tutar,
      vade: anDegeri(v.tarih, null),
      aciklama: metinDegeri(v.aciklama),
    };
    const inspection: InspectionRequest = {
      vehicleId: v.arac?.id ?? null,
      muayeneTarihi: anDegeri(v.tarih, null),
      bitis: anDegeri(v.bitis, null),
      ucret: v.tutar,
      islemKm: v.islemKm,
      aciklama: metinDegeri(v.aciklama),
    };
    const reset = () => this.form.reset();
    this.submission.gonder<unknown>(
      this.form,
      (key): Observable<unknown> =>
        this.kind === 'mtv'
          ? this.api.post<MtvDetail>(`${REGULATION}/mtv`, mtv, { islemAnahtari: key })
          : this.api.post<InspectionDetail>(`${REGULATION}/muayeneler`, inspection, {
              islemAnahtari: key,
            }),
      {
        esleme:
          this.kind === 'mtv'
            ? { vehicleId: 'arac', vade: 'tarih' }
            : { vehicleId: 'arac', muayeneTarihi: 'tarih', ucret: 'tutar' },
        basarili: () => {
          this.toast.basari(
            this.t(
              this.kind === 'mtv' ? 'servisSigorta.mtv.eklendi' : 'servisSigorta.muayene.eklendi',
            ),
          );
          reset();
          this.list.yenile();
        },
        hata: (h) => {
          if (h.kod === 'mukerrer') {
            if (h.mevcut?.ayniIcerik) reset();
            this.list.yenile();
          }
        },
      },
    );
  }
}
