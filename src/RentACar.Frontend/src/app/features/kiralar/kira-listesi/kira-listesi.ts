import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { finalize, map } from 'rxjs';

import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi, type QueryParameters } from '@core/api/api-istemcisi';
import type { RentalListRow } from '@core/api/ui-tipleri';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { DUGME_IZINLERI, type ButtonName } from '@core/oturum/dugme-izinleri';
import { genelGosterilir } from '@core/oturum/session-interceptor';
import { SessionService } from '@core/oturum/session-service';
import { sayiBicimle } from '@core/bicim/bicim';
import { bugun, formatDay } from '@core/form/tarih-girdisi';
import { ShellCounters, type KabukSayaclariDegeri } from '@core/sayac/shell-counters';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { parseQuery, urlParameters } from '@core/veri/liste-sorgusu';
import { listQueryUrlSync } from '@core/veri/liste-sorgusu-url';
import { FilterPanelComponent } from '@shared/filtre-paneli/filtre-paneli';
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
import { SavedViewChipsComponent, type SavedView } from '@shared/gorunum-cipleri/gorunum-cipleri';
import { Icon } from '@shared/ikon/icon';
import { PlateChipComponent } from '@shared/plaka/plaka';
import { exportUrl, type DisaAktarma } from '@shared/tablo/disa-aktarma';
import { Table } from '@shared/tablo/table';
import { TableCell } from '@shared/tablo/table-cell';

import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import {
  RENTAL_VIEW_CODES,
  viewFilters,
  isViewCode,
  badgeClass,
  rowView,
  rowClass,
  filtersEqual,
  allFilters,
  type RentalFilters,
  type RentalViewCode,
  type RowView,
} from './kira-gorunumleri';

import {
  RENTAL_STATUSES,
  RENTAL_LIST,
  RentalListStore,
  OFFICE_STATUSES,
  DATE_TYPES,
  summaryParameters,
  type RentalStatus,
  type OfficeStatus,
  type DateType,
} from './rental-list.store';
import { rentalColumns, count } from './rental-columns';
import { CollectPanel } from './collect-panel';

/** Kayıtlı görünüm → kabuk sayaç anahtarı + çeviri (kenar çubuğuyla aynı sözlük: `kabuk.gorunum.*`). */
const VIEW_DEFINITION: Readonly<
  Record<
    RentalViewCode,
    {
      readonly etiket: CeviriAnahtari;
      readonly sayac: keyof KabukSayaclariDegeri | null;
      readonly hata?: true;
    }
  >
> = {
  kirada: { etiket: 'kabuk.gorunum.kirada', sayac: 'kirada' },
  geciken: { etiket: 'kabuk.gorunum.geciken', sayac: 'geciken', hata: true },
  'bugun-cikan': { etiket: 'kabuk.gorunum.bugunCikan', sayac: 'bugunCikan' },
  'bugun-donecek': { etiket: 'kabuk.gorunum.bugunDonecek', sayac: 'bugunDonecek' },
  faturasiz: { etiket: 'kabuk.gorunum.faturasiz', sayac: null },
  kapali: { etiket: 'kabuk.gorunum.kapali', sayac: null },
};

const jsonEqual = (a: QueryParameters, b: QueryParameters) =>
  JSON.stringify(a) === JSON.stringify(b);

/**
 * Kira sözleşmeleri listesi (`/app/kiralar`) — Blazor `RentalList.razor` paritesi (F4.2): tüm FAZ-46
 * sütunları ve süzgeçleri, sunucu sayfalama/sıralama (`ListeIstegi`/`Sayfa<T>`, URL tek doğruluk kaynağı),
 * özet satırı, sunucu dışa aktarması, sözleşme PDF bağlantıları, iptal ve `TahsilatAnahtar`'lı "Tahsil Et".
 *
 * Düğmeler SPA tarafında da izne göre gizlenir (asıl kapı sunucuda): PDF ve "Yeni kira" OperationsWrite,
 * dışa aktarma ViewReports, iptal OperationsDelete; "Tahsil Et" yalnız sunucu satıra `tahsilat` verdiyse
 * (FinanceWrite + bakiye > 0 + iptal değil — karar sunucuda). PDF ve dışa aktarma Blazor GET uçlarıdır:
 * tarayıcının kendi gezinmesi (`<a href>`), SPA'ya yönlenmez.
 */
@Component({
  selector: 'rc-kira-listesi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    SearchSelection,
    FilterPanelComponent,
    Icon,
    TextInput,
    PlateChipComponent,
    SavedViewChipsComponent,
    PageBand,
    Selection,
    Table,
    TableCell,
    CollectPanel,
    DatePicker,
  ],
  providers: [FetchPolicy, RentalListStore],
  templateUrl: './kira-listesi.html',
  styleUrl: './kira-listesi.scss',
})
export class RentalList {
  protected readonly store = inject(RentalListStore);
  private readonly oturum = inject(SessionService);
  private readonly router = inject(Router);
  private readonly rota = inject(ActivatedRoute);
  private readonly counters = inject(ShellCounters);
  private readonly api = inject(ApiIstemcisi);
  private readonly approval = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly teardown = inject(DestroyRef);
  private readonly t = translationFunction();

  protected readonly liste = listQueryUrlSync(RENTAL_LIST);
  protected readonly columns = rentalColumns(this.t);
  protected readonly defaultSort = RENTAL_LIST.varsayilanSirala;
  protected readonly identity = (r: RentalListRow) => r.id;
  protected readonly exportUrl = exportUrl;

  /** İstanbul'da bugün; her liste yüklemesinde tazelenir (gece yarısını geçen sekme bayat kalmasın). */
  private readonly day = computed(() => {
    this.store.liste.durum();
    return bugun();
  });
  protected readonly rowClass = computed(() => {
    const day = this.day();
    return (s: RentalListRow) => rowClass(s, day);
  });

  // ---- kayıtlı görünümler (`?gorunum=`; kenar çubuğu aynı parametreyle işaretler)
  protected readonly gorunum = toSignal(
    this.rota.queryParamMap.pipe(map((p) => p.get('gorunum'))),
    { requireSync: true },
  );
  protected readonly views = computed<readonly SavedView[]>(() => {
    const selected = this.gorunum();
    const counter = this.counters.counters();
    const all: SavedView = {
      id: 'tum',
      ad: this.t('kabuk.gorunum.tum'),
      aktif: selected === null && this.liste.etkinFiltreSayisi() === 0,
      link: '/kiralar',
    };
    return [
      all,
      ...RENTAL_VIEW_CODES.map((code): SavedView => {
        const definition = VIEW_DEFINITION[code];
        return {
          id: code,
          ad: this.t(definition.etiket),
          sayac: definition.sayac && counter ? counter[definition.sayac] : null,
          tur: definition.hata ? 'hata' : undefined,
          aktif: selected === code,
          link: '/kiralar',
          sorgu: this.viewQuery(code),
        };
      }),
    ];
  });

  // ---- izinler (görünürlük; asıl kapı sunucuda). Her kapı DUGME_IZINLERI'nden — UiDugmeIzinTests her girişi
  // tetiklediği ucun izin kapısıyla karşılaştırır (düğme görünür ⇔ uç izin verir).
  private readonly izin = (name: ButtonName) =>
    computed(() => this.oturum.hasPermissions(DUGME_IZINLERI[name].izinler));
  protected readonly newRentalPermission = this.izin('kiraYeni');
  protected readonly sampleContractPermission = this.izin('kiraOrnekSozlesme');
  protected readonly pdfPermission = this.izin('kiraPdf');
  protected readonly cancelPermission = this.izin('kiraIptal');
  protected readonly locationSelectionPermission = this.izin('kiraSecimLokasyon');
  protected readonly sourceSelectionPermission = this.izin('kiraSecimKaynak');
  protected readonly staffSelectionPermission = this.izin('kiraSecimPersonel');
  private readonly rapor = this.izin('kiraDisaAktar');

  /** Dışa aktarma: Blazor liste export ucu; ekrandaki süzgeçler taşınır (sayfa taşınmaz). */
  protected readonly exportItem = computed<DisaAktarma | null>(() =>
    this.rapor()
      ? {
          yol: '/listeler/export/kiralar',
          parametreler: this.liste.apiParametreleri(),
          bicimler: ['excel', 'csv', 'pdf'],
        }
      : null,
  );

  /** Bant pill'i: etkin görünüm (ya da "Tüm sözleşmeler"/"Süzgeçli") · kayıt sayısı. */
  protected readonly bannerPill = computed(() => {
    const code = this.gorunum();
    const name = isViewCode(code)
      ? this.t(VIEW_DEFINITION[code].etiket)
      : this.liste.etkinFiltreSayisi() > 0
        ? this.t('kiraListesi.bant.suzgecli')
        : this.t('kabuk.gorunum.tum');
    const d = this.store.liste.durum();
    return d.tur === 'hazir'
      ? this.t('kiraListesi.bant.pill', {
          ad: name,
          adet: sayiBicimle(count(d.veri.toplam), '1.0-0'),
        })
      : name;
  });

  /** Bant ikincil metni: şube (süzgeç ya da oturum kapsamı) · tarih aralığı. */
  protected readonly bannerSubtext = computed(() => {
    const f = this.liste.sorgu().filtreler;
    const scope = this.oturum.ben()?.subeKapsami;
    const branch = f.ofis ?? (scope && !scope.tumSubeler ? scope.subeAd : null);
    const range =
      f.basMin || f.basMax
        ? `${f.basMin ? formatDay(f.basMin) : '…'} – ${f.basMax ? formatDay(f.basMax) : '…'}`
        : null;
    const parts = [branch, range].filter((p): p is string => !!p);
    return parts.length ? parts.join(' · ') : null;
  });

  protected readonly ozet = computed(() => {
    const o = this.store.ozet.veri();
    return o
      ? this.t('kiraListesi.ozet', {
          toplam: sayiBicimle(count(o.toplam), '1.0-0'),
          kirada: sayiBicimle(count(o.kirada), '1.0-0'),
          faturasiz: sayiBicimle(count(o.faturasiz), '1.0-0'),
        })
      : null;
  });

  // ---- süzgeç formu (uygula düğmesiyle; URL'e yazılır, URL'den geri okunur)
  protected readonly filterForm = new FormGroup({
    q: new FormControl<string | null>(null),
    durum: new FormControl<RentalStatus | null>(null),
    fatura: new FormControl<boolean | null>(null),
    tarihTuru: new FormControl<DateType | null>(null),
    basMin: new FormControl<string | null>(null),
    basMax: new FormControl<string | null>(null),
    ofis: new FormControl<SecimSecenegi | null>(null),
    ofisDurum: new FormControl<OfficeStatus | null>(null),
    sahip: new FormControl<string | null>(null),
    grup: new FormControl<string | null>(null),
    kaynak: new FormControl<SecimSecenegi | null>(null),
    personel: new FormControl<SecimSecenegi | null>(null),
  });

  protected readonly statusOptions: readonly SecenekOgesi<RentalStatus>[] = RENTAL_STATUSES.map(
    (d) => ({ deger: d, etiket: this.t(`kiraListesi.durumlar.${d}`) }),
  );
  protected readonly invoiceOptions: readonly SecenekOgesi<boolean>[] = [
    { deger: true, etiket: this.t('kiraListesi.filtre.faturali') },
    { deger: false, etiket: this.t('kiraListesi.filtre.faturasiz') },
  ];
  protected readonly dateTypeOptions: readonly SecenekOgesi<DateType>[] = DATE_TYPES.map((d) => ({
    deger: d,
    etiket: this.t(`kiraListesi.tarihTurleri.${d}`),
  }));
  protected readonly officeStatusOptions: readonly SecenekOgesi<OfficeStatus>[] =
    OFFICE_STATUSES.map((d) => ({ deger: d, etiket: this.t(`kiraListesi.ofisDurumlari.${d}`) }));
  protected readonly ownerOptions = computed(() =>
    this.textOptions(this.store.options.veri()?.sahipler, this.liste.sorgu().filtreler.sahip),
  );
  protected readonly groupOptions = computed(() =>
    this.textOptions(this.store.options.veri()?.gruplar, this.liste.sorgu().filtreler.grup),
  );

  // Seçim uçları OperationsWrite ister; bu alanlar yalnız o izinle çizilir (Muhasebe'de 403 bandı olmasın).
  protected readonly lokasyonlar = serverSelectionSource('lokasyon');
  protected readonly sources = serverSelectionSource('rezervasyon-kaynagi');
  protected readonly staff = serverSelectionSource('personel');
  /** Seçilen personelin etiketi (URL'de yalnız kimlik durur — ad yazılmaz). Yalnız bellekte. */
  private readonly staffLabels = new Map<string, string>();

  // ---- satır işlemleri
  protected readonly collectRow = signal<RentalListRow | null>(null);
  /** Yeni anahtarı beklenen açık tahsil panelinin kirası (3. tur M-A). */
  private keyPending: string | null = null;
  private readonly tahsilPaneli = viewChild(CollectPanel);
  /** Tahsilat isteği uçarken başka satır açılamaz (uçan istek iptal edilip sonucu kaybolmasın). */
  protected readonly isCollectInProgress = computed(
    () => this.tahsilPaneli()?.submitting() ?? false,
  );
  protected readonly cancelled = signal<string | null>(null);

  constructor() {
    const policy = inject(FetchPolicy);
    policy.connect({
      parametre: this.liste.apiParametreleri,
      yukle: (p) => this.store.liste.yukle(p),
      sifirla: () => this.store.liste.reset(),
      // Bakiye ve tahsilat anahtarı başka sekmede (kira formu, panel) değişebilir: dönüşte taze veri.
      sekmeyeDonunce: 'yenile',
    });
    policy.connect({
      parametre: computed(() => summaryParameters(this.liste.apiParametreleri()), {
        equal: jsonEqual,
      }),
      yukle: (p) => this.store.ozet.yukle(p),
      sifirla: () => this.store.ozet.reset(),
      sekmeyeDonunce: 'yenile',
      esit: jsonEqual,
    });
    policy.connect({
      parametre: signal(0).asReadonly(),
      yukle: () => this.store.options.yukle(),
      sifirla: () => this.store.options.reset(),
    });

    this.applyViewPreset();

    // URL → form (geri/ileri tuşu, paylaşılan bağlantı, "Temizle").
    effect(() => {
      const f = this.liste.sorgu().filtreler;
      untracked(() => this.filterForm.reset(this.formValue(f)));
    });

    // Açık tahsilat paneli bayat veriyle kalmasın: liste yenilenince satır yoksa ya da sunucu yeni
    // anahtar verdiyse (bakiye/işlem sayısı değişti) panel kapanır; kullanıcı güncel satırdan yeniden açar.
    effect(() => {
      const open = this.collectRow();
      const status = this.store.liste.durum();
      if (open === null || status.tur !== 'hazir' || this.isCollectInProgress()) return;
      const current = status.veri.kayitlar.find((r) => r.id === open.id);
      // M-A: "başka tahsilat yazıldı" sonrası beklenen tazeleme → panel AÇIK kalır, güncel satırı (yeni anahtar)
      // alır; yazılan tutar panelde korunur.
      if (this.keyPending === open.id) {
        this.keyPending = null;
        if (current?.tahsilat) {
          untracked(() => this.collectRow.set(current));
          return;
        }
      }
      if (current?.tahsilat && current.tahsilat.anahtar === open.tahsilat?.anahtar) return;
      untracked(() => {
        this.collectRow.set(null);
        this.toast.uyari(this.t('kiraListesi.tahsil.satirDegisti', { no: open.sozlesmeNo }));
      });
    });
  }

  // ------------------------------------------------------------------ kayıtlı görünüm

  /** Görünüm bağlantısı: `gorunum` + ön ayarın URL süzgeçleri (liste ilk istekte doğru süzgeçle yüklenir). */
  private viewQuery(code: RentalViewCode): Record<string, string | null> {
    const baseline = parseQuery(RENTAL_LIST, {});
    return {
      ...urlParameters(RENTAL_LIST, {
        ...baseline,
        filtreler: viewFilters(code, this.day()),
      }),
      gorunum: code,
    };
  }

  /**
   * `?gorunum=` ön ayarı (Yol v2 §9, yalnız istemci): görünüm DEĞİŞİNCE süzgeçler ön ayara çekilir (kenar
   * çubuğu bağlantısı yalnız `gorunum` taşır); kullanıcı süzgeci sonra değiştirirse `gorunum` URL'den düşer
   * (çip ve kenar çubuğu artık o görünümü işaretlemez). Bilinmeyen kod düşürülür. URL tek doğruluk kaynağı kalır.
   */
  private applyViewPreset(): void {
    let applied: RentalViewCode | null = null;
    let waiting = false;
    effect(() => {
      const code = this.gorunum();
      const filters = this.liste.sorgu().filtreler;
      untracked(() => {
        if (waiting) return;
        if (code === null) {
          applied = null;
          return;
        }
        if (!isViewCode(code)) {
          this.dropView();
          return;
        }
        const preset = viewFilters(code, bugun());
        const same = filtersEqual(filters, preset);
        if (code !== applied) {
          applied = code;
          if (!same) {
            waiting = true;
            void this.liste
              .degistir({ filtreler: allFilters(preset) }, { yaziyor: true })
              .finally(() => (waiting = false));
          }
          return;
        }
        if (!same) this.dropView();
      });
    });
  }

  private dropView(): void {
    void this.router.navigate([], {
      relativeTo: this.rota,
      queryParams: { gorunum: null },
      queryParamsHandling: 'merge',
      preserveFragment: true,
      replaceUrl: true,
    });
  }

  // ------------------------------------------------------------------ süzgeç

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    if (v.personel) this.staffLabels.set(v.personel.id, v.personel.etiket);
    void this.liste.degistir({
      filtreler: {
        q: v.q ?? undefined,
        durum: v.durum ?? undefined,
        fatura: v.fatura ?? undefined,
        tarihTuru: v.tarihTuru ?? undefined,
        basMin: v.basMin ?? undefined,
        basMax: v.basMax ?? undefined,
        ofis: v.ofis?.etiket ?? undefined,
        ofisDurum: v.ofisDurum ?? undefined,
        sahip: v.sahip ?? undefined,
        grup: v.grup ?? undefined,
        kaynak: v.kaynak?.etiket ?? undefined,
        personelId: v.personel?.id ?? undefined,
      },
    });
  }

  protected clear(): void {
    void this.liste.sifirla();
  }

  private formValue(f: RentalFilters) {
    const option = (value: string | undefined): SecimSecenegi | null =>
      value === undefined ? null : { id: value, etiket: value };
    return {
      q: f.q ?? null,
      durum: f.durum ?? null,
      fatura: f.fatura ?? null,
      tarihTuru: f.tarihTuru ?? null,
      basMin: f.basMin ?? null,
      basMax: f.basMax ?? null,
      ofis: option(f.ofis),
      ofisDurum: f.ofisDurum ?? null,
      sahip: f.sahip ?? null,
      grup: f.grup ?? null,
      kaynak: option(f.kaynak),
      personel:
        f.personelId === undefined
          ? null
          : {
              id: f.personelId,
              etiket:
                this.staffLabels.get(f.personelId) ?? this.t('kiraListesi.filtre.seciliPersonel'),
            },
    };
  }

  /** Sunucu öneri listesi + URL'deki değer (listede yoksa da görünsün; seçim kutusu boş kalmasın). */
  private textOptions(
    list: readonly string[] | undefined,
    selected: string | undefined,
  ): readonly SecenekOgesi<string>[] {
    const values = [...(list ?? [])];
    if (selected !== undefined && !values.includes(selected)) values.unshift(selected);
    return values.map((d) => ({ deger: d, etiket: d }));
  }

  // ------------------------------------------------------------------ satır

  protected openRow(row: RentalListRow): void {
    void this.router.navigate(['/kiralar', row.id]);
  }

  protected statusView(row: RentalListRow): RowView {
    return rowView(row, this.day());
  }

  protected rozet(g: RowView): string {
    return badgeClass(g);
  }

  protected statusLabel(g: RowView): string {
    if (g.tur === 'gecikmis') {
      return this.t('kiraListesi.gecikti', { gun: sayiBicimle(g.gun, '1.0-0') });
    }
    if (g.tur === 'bugunDonuyor') return this.t('kiraListesi.bugunDonuyor');
    return (RENTAL_STATUSES as readonly string[]).includes(g.durum)
      ? this.t(`kiraListesi.durumlar.${g.durum as RentalStatus}`)
      : g.durum;
  }

  /** Blazor sözleşme PDF'i (tarayıcıda görüntülenir; SPA'ya yönlenmez). */
  protected pdfUrl(row: RentalListRow): string {
    return `/kiralar/${encodeURIComponent(row.id)}/pdf`;
  }

  protected openCollect(row: RentalListRow): void {
    if (this.isCollectInProgress() || row.tahsilat === null) return;
    this.collectRow.set(row);
  }

  protected closeCollect(): void {
    if (this.isCollectInProgress()) return;
    this.collectRow.set(null);
  }

  /** M-A: panel açık kalır; liste yeniden yüklenince açık satır güncel hâliyle (yeni anahtar) değişir. */
  protected refreshCollectKey(): void {
    this.keyPending = this.collectRow()?.id ?? null;
    this.yenile();
  }

  /** 2xx ya da 409 `mukerrer`: panel kapanır, liste (yeni anahtarla) yeniden yüklenir. Yeniden gönderim YOK. */
  protected collectSettled(): void {
    this.collectRow.set(null);
    this.yenile();
  }

  protected async iptal(row: RentalListRow): Promise<void> {
    if (this.cancelled() !== null) return;
    const yes = await this.approval.ask({
      baslik: this.t('kiraListesi.iptalBaslik'),
      mesaj: this.t('kiraListesi.iptalMesaj', { no: row.sozlesmeNo }),
      onayEtiketi: this.t('kiraListesi.iptalOnay'),
      tehlikeli: true,
    });
    if (!yes || this.cancelled() !== null) return;
    this.cancelled.set(row.id);
    this.api
      .post<unknown>(`/api/ui/v1/kiralar/${row.id}/iptal`, null)
      .pipe(
        finalize(() => this.cancelled.set(null)),
        takeUntilDestroyed(this.teardown),
      )
      .subscribe({
        next: () => {
          this.toast.basari(this.t('kiraListesi.iptalEdildi', { no: row.sozlesmeNo }));
          this.yenile();
        },
        error: (raw: unknown) => {
          // Bant/toast'ta gösterilenler (yetki, 5xx…) interceptor'da; iş kuralı hatası (400) burada.
          const error = toApiError(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
          this.yenile();
        },
      });
  }

  private yenile(): void {
    this.store.liste.yenile();
    this.store.ozet.yenile();
  }
}
