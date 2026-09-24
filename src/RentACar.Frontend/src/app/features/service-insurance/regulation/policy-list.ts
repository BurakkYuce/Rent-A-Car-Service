import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import type { GunMetni } from '@core/form/tarih-girdisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listeSorgusuUrlSenkronu } from '@core/veri/liste-sorgusu-url';
import { anDegeri, metinDegeri } from '@features/planlama-ortak/form-yardimcilari';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
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

import { policyColumns } from '../service-insurance-columns';
import {
  INSURANCE_TYPES,
  type InsuranceType,
  POLICY_CURRENCIES,
  POLICY_LIST,
  type PolicyDetail,
  type PolicyRequest,
  REGULATION,
} from '../service-insurance-model';
import { PolicyListStore, RegulationOptionsStore } from '../service-insurance.store';
import { RegulationTabs } from './regulation-tabs';

/**
 * Sigorta poliçeleri (`/app/regulasyon`) — Blazor `/regulasyon` "Sigorta" bölümü: süzgeç, liste (Araç/İMM/Aksesuar
 * değeri ve Kalan BİLGİ), "Ekle" formu (OperationsWrite; deftere yazmaz). Ödeme ve zeyiller poliçe kaydında
 * (`/app/regulasyon/sigortalar/:id`). Okuma OperationsWrite ∨ FinanceWrite ∨ ViewReports; ARACIN şubesi süzer.
 */
@Component({
  selector: 'rc-policy-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
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
    Secim,
    Tablo,
    TabloHucre,
    TarihSecici,
  ],
  providers: [FetchPolicy, PolicyListStore, RegulationOptionsStore],
  templateUrl: './policy-list.html',
  styleUrl: '../service-insurance.scss',
})
export class PolicyList implements KaydedilmemisDegisiklikSahibi {
  protected readonly store = inject(PolicyListStore);
  private readonly optionsStore = inject(RegulationOptionsStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly t = ceviriFonksiyonu();

  protected readonly query = listeSorgusuUrlSenkronu(POLICY_LIST);
  protected readonly columns = policyColumns(this.t);
  protected readonly rowId = (r: { id: string }) => r.id;
  protected readonly vehicles = sunucuSecimKaynagi('arac');
  private readonly session = inject(OturumServisi);
  protected readonly canWrite = computed(() => this.session.izinVar('OperationsWrite'));
  private readonly createToggle = signal<boolean | null>(null);
  protected readonly createOpen = computed(
    () => this.canWrite() && (this.createToggle() ?? this.store.list.veri()?.toplam === 0),
  );

  protected readonly typeOptions: readonly SecenekOgesi<InsuranceType>[] = INSURANCE_TYPES.map(
    (x) => ({ deger: x, etiket: x }),
  );
  protected readonly paidOptions: readonly SecenekOgesi<'true' | 'false'>[] = [
    { deger: 'true', etiket: this.t('servisSigorta.evet') },
    { deger: 'false', etiket: this.t('servisSigorta.hayir') },
  ];
  protected readonly currencyOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    (this.optionsStore.options.veri()?.dovizler ?? POLICY_CURRENCIES).map((d) => ({
      deger: d,
      etiket: d,
    })),
  );
  protected readonly companies = computed(() => this.optionsStore.options.veri()?.firmalar ?? []);

  protected readonly filterForm = new FormGroup({
    plaka: new FormControl<string | null>(null),
    tip: new FormControl<InsuranceType | null>(null),
    odendi: new FormControl<'true' | 'false' | null>(null),
  });

  protected readonly form = new FormGroup({
    arac: new FormControl<SecimSecenegi | null>(null, Validators.required),
    tip: new FormControl<InsuranceType | null>('Trafik', Validators.required),
    baslangic: new FormControl<GunMetni | null>(null, Validators.required),
    bitis: new FormControl<GunMetni | null>(null, Validators.required),
    prim: new FormControl<string | null>(null),
    doviz: new FormControl<string | null>('TRY'),
    policeNo: new FormControl<string | null>(null, Validators.maxLength(64)),
    firma: new FormControl<string | null>(null, Validators.maxLength(128)),
    acenta: new FormControl<string | null>(null, Validators.maxLength(128)),
    aracDegeri: new FormControl<string | null>(null),
    immDegeri: new FormControl<string | null>(null),
    aksesuarDegeri: new FormControl<string | null>(null),
  });
  protected readonly submission = formGonderimi();
  protected readonly currency = toSignal(this.form.controls.doviz.valueChanges, {
    initialValue: this.form.controls.doviz.value,
  });

  constructor() {
    inject(FetchPolicy).baglan({
      parametre: this.query.apiParametreleri,
      yukle: (p) => this.store.list.yukle(p),
      sifirla: () => this.store.list.sifirla(),
      sekmeyeDonunce: 'yenile',
    });
    effect(() => {
      const f = this.query.sorgu().filtreler;
      untracked(() =>
        this.filterForm.reset({
          plaka: f.plaka ?? null,
          tip: f.tip ?? null,
          odendi: f.odendi ?? null,
        }),
      );
    });
    effect(() => {
      if (this.createOpen() && this.optionsStore.options.tur() === 'bos')
        untracked(() => this.optionsStore.options.yukle());
    });
    sayfaTerkKorumasi(() => this.form.dirty);
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.form.dirty;
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        plaka: metinDegeri(v.plaka) ?? undefined,
        tip: v.tip ?? undefined,
        odendi: v.odendi ?? undefined,
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
    const body: PolicyRequest = {
      vehicleId: v.arac?.id ?? null,
      tip: v.tip,
      baslangic: anDegeri(v.baslangic, null),
      bitis: anDegeri(v.bitis, null),
      prim: v.prim,
      doviz: v.doviz,
      policeNo: metinDegeri(v.policeNo),
      firma: metinDegeri(v.firma),
      acenta: metinDegeri(v.acenta),
      aracDegeri: v.aracDegeri,
      immDegeri: v.immDegeri,
      aksesuarDegeri: v.aksesuarDegeri,
    };
    this.submission.gonder(
      this.form,
      (key) =>
        this.api.post<PolicyDetail>(`${REGULATION}/sigortalar`, body, { islemAnahtari: key }),
      {
        esleme: { vehicleId: 'arac' },
        basarili: (d) => {
          this.toast.basari(this.t('servisSigorta.sigorta.eklendi', { plaka: d.police.plaka }));
          this.form.reset({ tip: 'Trafik', doviz: 'TRY' });
          this.store.list.yenile();
        },
        hata: (h) => {
          if (h.kod === 'mukerrer') {
            if (h.mevcut?.ayniIcerik) this.form.reset({ tip: 'Trafik', doviz: 'TRY' });
            this.store.list.yenile();
          }
        },
      },
    );
  }
}
