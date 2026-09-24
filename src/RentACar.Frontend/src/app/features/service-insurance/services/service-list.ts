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
import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import type { GunMetni } from '@core/form/tarih-girdisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
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
import { OnayKutusu } from '@shared/form/kontroller/onay-kutusu';
import { SayiGirdisi } from '@shared/form/kontroller/sayi-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';
import { Ikon } from '@shared/ikon/ikon';
import { Tablo } from '@shared/tablo/tablo';
import { TabloHucre } from '@shared/tablo/tablo-hucre';

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

/**
 * Servis / bakım (`/app/servisler`) — Blazor `ServiceRecordList.razor`: durum sekmeleri, liste, "Yeni Servis Kaydı"
 * (OperationsWrite; randevu = Rezerve). Kaza/fatura/ödeme BİLGİ blokları, kalemler, durum akışı ve rücu yansıtma kayıt
 * sayfasında (`/app/servisler/:id`). Okuma OperationsWrite ∨ FinanceWrite ∨ ViewReports; ARACIN şubesi süzer.
 */
@Component({
  selector: 'rc-service-list',
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
    OnayKutusu,
    SayiGirdisi,
    Secim,
    Tablo,
    TabloHucre,
    TarihSecici,
  ],
  providers: [FetchPolicy, ServiceListStore],
  templateUrl: './service-list.html',
  styleUrl: '../service-insurance.scss',
})
export class ServiceList implements KaydedilmemisDegisiklikSahibi {
  protected readonly store = inject(ServiceListStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly router = inject(Router);
  private readonly session = inject(OturumServisi);
  private readonly t = ceviriFonksiyonu();

  protected readonly query = listeSorgusuUrlSenkronu(SERVICE_LIST);
  protected readonly columns = serviceColumns(
    this.t,
    (s) => this.statusLabel(s),
    (s) => this.typeLabel(s),
  );
  protected readonly rowId = (r: ServiceRecordRow) => r.id;
  protected readonly vehicles = sunucuSecimKaynagi('arac');
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
    bas: new FormControl<GunMetni | null>(null),
    bit: new FormControl<GunMetni | null>(null),
  });

  protected readonly form = new FormGroup({
    arac: new FormControl<SecimSecenegi | null>(null, Validators.required),
    tip: new FormControl<ServiceType | null>('Periyodik', Validators.required),
    girisKm: new FormControl<number | null>(0, [Validators.min(0)]),
    girisTarihi: new FormControl<GunMetni | null>(null),
    hasarSorumlu: new FormControl<DamageParty | null>('Yok'),
    kusurOrani: new FormControl<number | null>(null, [Validators.min(0), Validators.max(1)]),
    atolyeAdi: new FormControl<string | null>(null, Validators.maxLength(128)),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(1024)),
    rezervasyon: new FormControl<boolean | null>(false),
    planBasTarihi: new FormControl<GunMetni | null>(null),
    planBitTarihi: new FormControl<GunMetni | null>(null),
  });
  protected readonly submission = formGonderimi();

  constructor() {
    inject(FetchPolicy).baglan({
      parametre: this.query.apiParametreleri,
      yukle: (p) => {
        this.store.list.yukle(p);
        this.store.counts.yukle(p);
      },
      sifirla: () => {
        this.store.list.sifirla();
        this.store.counts.sifirla();
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
    sayfaTerkKorumasi(() => this.form.dirty);
  }

  kaydedilmemisDegisiklikVar(): boolean {
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
        plaka: metinDegeri(v.plaka) ?? undefined,
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
      girisTarihi: anDegeri(v.girisTarihi, null),
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
