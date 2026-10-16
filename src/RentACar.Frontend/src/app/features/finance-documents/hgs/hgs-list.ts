import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  Injectable,
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

import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi, type QueryParameters } from '@core/api/api-istemcisi';
import type { Sayfa } from '@core/api/sayfa';
import { enumLabel } from '@core/bicim/enum-label';
import { formatMoney } from '@core/bicim/bicim';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { translationFunction } from '@core/i18n/ceviri';
import { DUGME_IZINLERI } from '@core/oturum/dugme-izinleri';
import { genelGosterilir } from '@core/oturum/session-interceptor';
import { SessionService } from '@core/oturum/session-service';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listQueryUrlSync } from '@core/veri/liste-sorgusu-url';
import { TemelStore } from '@core/veri/temel-store';
import { toNumber } from '@features/vehicles/vehicle-model';
import { Alan } from '@shared/form/alan/alan';
import { NumberInput } from '@shared/form/kontroller/number-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { TextInput } from '@shared/form/kontroller/text-input';
import { DatePicker } from '@shared/form/tarih/date-picker';
import { Icon } from '@shared/ikon/icon';
import { PlateChipComponent } from '@shared/plaka/plaka';
import { Table } from '@shared/tablo/table';
import { TableCell } from '@shared/tablo/table-cell';
import type { TabloSutunu } from '@shared/tablo/tablo-modeli';

import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import {
  DEFAULT_SERVICE_RATE,
  HGS,
  HGS_LIST,
  HGS_STATUSES,
  type HgsCrossingRow,
  type HgsReflectResult,
  type HgsRematchResult,
  type HgsStatus,
  MAX_REFLECT,
} from './hgs-model';

@Injectable()
export class HgsStore {
  private readonly api = inject(ApiIstemcisi);
  readonly list = new TemelStore(
    (p: QueryParameters) =>
      this.api.get<Sayfa<HgsCrossingRow>>(`${HGS}/gecisler`, { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );
}

/**
 * HGS geçişleri (`/app/hgs`): dosyadan içe aktarılmış geçişler, kiraya eşleşme durumu, seçili geçişleri müşteriye
 * yansıtma (FinanceWrite; Borç Cari / Alacak Gelir — sunucu tutarı geçiş × hizmet oranı olarak satır başına yuvarlar,
 * istemci hesap yapmaz) ve yeniden eşleştirme. İçe aktarma ayrı sayfada (`/app/hgs/ice-aktar`). Kira dışı geçiş firma
 * gideridir; yansıtılmaz.
 */
@Component({
  selector: 'rc-hgs-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PageBand,
    PlateChipComponent,
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    Icon,
    NumberInput,
    Selection,
    TextInput,
    DatePicker,
    Table,
    TableCell,
  ],
  providers: [FetchPolicy, HgsStore],
  templateUrl: './hgs-list.html',
  styleUrl: './hgs.scss',
})
export class HgsList {
  protected readonly store = inject(HgsStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly session = inject(SessionService);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = translationFunction();

  protected readonly query = listQueryUrlSync(HGS_LIST);
  protected readonly columns = this.buildColumns();
  protected readonly rowId = (r: HgsCrossingRow) => r.id;
  protected readonly canReflect = computed(() =>
    this.session.hasPermissions(DUGME_IZINLERI.hgsYansit.izinler),
  );
  /** İçe aktarma ve yeniden eşleştirme: OperationsWrite ∨ FinanceWrite + şube kısıtsız (sunucu ayrıca zorlar). */
  protected readonly canImport = computed(
    () =>
      (this.session.izinVar('OperationsWrite') || this.session.izinVar('FinanceWrite')) &&
      this.session.ben()?.subeKapsami.tumSubeler === true,
  );
  protected readonly selection = signal<readonly string[]>([]);
  protected readonly busy = signal(false);
  protected readonly lastResult = signal<HgsReflectResult | null>(null);

  protected readonly statusOptions: readonly SecenekOgesi<HgsStatus>[] = HGS_STATUSES.map((s) => ({
    deger: s,
    etiket: enumLabel(s),
  }));

  protected readonly filterForm = new FormGroup({
    plaka: new FormControl<string | null>(null, Validators.maxLength(16)),
    bas: new FormControl<string | null>(null),
    bit: new FormControl<string | null>(null),
    durum: new FormControl<HgsStatus | null>(null),
  });

  protected readonly rateForm = new FormGroup({
    hizmetOrani: new FormControl<number | null>(DEFAULT_SERVICE_RATE, [
      Validators.required,
      Validators.min(1),
      Validators.max(2),
    ]),
  });

  constructor() {
    inject(FetchPolicy).connect({
      parametre: this.query.apiParametreleri,
      yukle: (p) => this.store.list.yukle(p),
      sifirla: () => this.store.list.reset(),
      sekmeyeDonunce: 'yenile',
    });
    effect(() => {
      const f = this.query.sorgu().filtreler;
      untracked(() =>
        this.filterForm.reset({
          plaka: f.plaka ?? null,
          bas: f.bas ?? null,
          bit: f.bit ?? null,
          durum: f.durum ?? null,
        }),
      );
    });
  }

  /** Kira/aktarım süzgeci URL'den geliyorsa (kira formu bağlantısı) bant gösterilir. */
  protected readonly scopedFilter = computed(() => {
    const f = this.query.sorgu().filtreler;
    return f.kiraId !== undefined || f.iceAktarimId !== undefined;
  });

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    const f = this.query.sorgu().filtreler;
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        plaka: v.plaka?.trim() || undefined,
        bas: v.bas ?? undefined,
        bit: v.bit ?? undefined,
        durum: v.durum ?? undefined,
        kiraId: f.kiraId,
        iceAktarimId: f.iceAktarimId,
      },
    });
  }

  protected clear(): void {
    void this.query.sifirla();
  }

  protected async reflect(): Promise<void> {
    const ids = this.selection();
    if (this.busy() || ids.length === 0) return;
    if (ids.length > MAX_REFLECT) {
      this.toast.uyari(this.t('finansBelge.hgs.cokFazlaSecim', { adet: MAX_REFLECT }));
      return;
    }
    if (this.rateForm.invalid) {
      this.rateForm.markAllAsTouched();
      return;
    }
    const rate = this.rateForm.controls.hizmetOrani.value ?? DEFAULT_SERVICE_RATE;
    const yes = await this.confirm.ask({
      baslik: this.t('finansBelge.hgs.yansitBaslik'),
      mesaj: this.t('finansBelge.hgs.yansitMesaj', { adet: ids.length, oran: rate }),
    });
    if (!yes || this.busy()) return;
    this.busy.set(true);
    this.api
      .post<HgsReflectResult>(`${HGS}/gecisler/yansit`, { ids, hizmetOrani: rate })
      .pipe(
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (r) => {
          this.lastResult.set(r);
          this.selection.set([]);
          this.toast.basari(
            this.t('finansBelge.hgs.yansitildi', {
              adet: r.yansitilan,
              tutar: formatMoney(toNumber(r.yansitilanTutar) ?? 0, 'TRY'),
            }),
          );
          this.store.list.yenile();
        },
        error: (raw: unknown) => {
          const error = toApiError(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
          this.store.list.yenile();
        },
      });
  }

  protected async rematch(): Promise<void> {
    if (this.busy()) return;
    const yes = await this.confirm.ask({
      baslik: this.t('finansBelge.hgs.eslestirBaslik'),
      mesaj: this.t('finansBelge.hgs.eslestirMesaj'),
    });
    if (!yes || this.busy()) return;
    this.busy.set(true);
    this.api
      .post<HgsRematchResult>(`${HGS}/gecisler/yeniden-eslestir`, null)
      .pipe(
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (r) => {
          this.toast.basari(this.t('finansBelge.hgs.eslestirildi', { adet: r.degisen }));
          this.store.list.yenile();
        },
        error: (raw: unknown) => {
          const error = toApiError(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
        },
      });
  }

  protected station(r: HgsCrossingRow): string {
    if (r.girisIstasyonu && r.cikisIstasyonu) return `${r.girisIstasyonu} → ${r.cikisIstasyonu}`;
    return r.cikisIstasyonu ?? r.girisIstasyonu ?? '—';
  }

  protected statusLabel(s: string): string {
    return enumLabel(s);
  }

  private buildColumns(): readonly TabloSutunu<HgsCrossingRow>[] {
    const h = (k: string) => this.t(`finansBelge.hgs.sutun.${k}` as CeviriAnahtari);
    const tl = () => 'TRY';
    return [
      {
        kod: 'gecisZamani',
        baslik: h('zaman'),
        deger: (r) => r.gecisZamani,
        tur: 'tarihSaat',
        sirala: true,
        sabit: true,
        gizlenemez: true,
        genislik: 150,
      },
      { kod: 'plaka', baslik: h('plaka'), deger: (r) => r.plaka, sirala: true, genislik: 110 },
      { kod: 'istasyon', baslik: h('istasyon'), deger: (r) => this.station(r), genislik: 220 },
      {
        kod: 'tutar',
        baslik: h('tutar'),
        deger: (r) => toNumber(r.tutar),
        tur: 'para',
        paraBirimi: tl,
        sirala: true,
        genislik: 110,
      },
      {
        kod: 'durum',
        baslik: h('durum'),
        deger: (r) => enumLabel(r.durum),
        sirala: true,
        genislik: 110,
      },
      {
        kod: 'sozlesmeNo',
        baslik: h('sozlesme'),
        deger: (r) => r.sozlesmeNo ?? '—',
        sirala: true,
        genislik: 150,
      },
      {
        kod: 'cariAd',
        baslik: h('musteri'),
        deger: (r) => r.cariAd ?? '—',
        sirala: true,
        genislik: 170,
      },
      {
        kod: 'yansitilanTutar',
        baslik: h('yansitilan'),
        deger: (r) => toNumber(r.yansitilanTutar),
        tur: 'para',
        paraBirimi: tl,
        genislik: 120,
      },
      {
        kod: 'hizmetOrani',
        baslik: h('oran'),
        deger: (r) => toNumber(r.hizmetOrani),
        tur: 'sayi',
        genislik: 80,
      },
      { kod: 'eslesmeNotu', baslik: h('not'), deger: (r) => r.eslesmeNotu ?? '', genislik: 240 },
      { kod: 'etiketNo', baslik: h('etiket'), deger: (r) => r.etiketNo ?? '—', genislik: 120 },
      {
        kod: 'dosyaAdi',
        baslik: h('dosya'),
        deger: (r) => r.dosyaAdi ?? '—',
        gizli: true,
        genislik: 160,
      },
    ];
  }
}
