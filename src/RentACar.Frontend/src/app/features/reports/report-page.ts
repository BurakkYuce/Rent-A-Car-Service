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
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi, type SorguParametreleri } from '@core/api/api-istemcisi';
import type { Sayfa } from '@core/api/sayfa';
import type { GunAraligi } from '@core/form/tarih-girdisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listeSorgusuUrlSenkronu } from '@core/veri/liste-sorgusu-url';
import type { StoreDurumu } from '@core/veri/temel-store';
import { TemelStore } from '@core/veri/temel-store';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { OnayKutusu } from '@shared/form/kontroller/onay-kutusu';
import { SayiGirdisi } from '@shared/form/kontroller/sayi-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';
import { Ikon } from '@shared/ikon/ikon';
import { type DisaAktarmaBicimi, disaAktarmaAdresi } from '@shared/tablo/disa-aktarma';
import { Tablo } from '@shared/tablo/tablo';
import { TabloHucre } from '@shared/tablo/tablo-hucre';
import type { TabloSutunu } from '@shared/tablo/tablo-modeli';

import { findReport } from './report-catalog';
import type { ListSource, ReportColumn, ReportFilter } from './report-model';
import {
  VIEW_KEY,
  activeView,
  asNumber,
  exportFromLinks,
  formatValue,
  isNegative,
  reportListDefinition,
  viewParams,
  viewUrl,
} from './report-query';

/** Zarflı rapor yanıtı (tip dışı alanlar yok sayılır). */
interface Envelope {
  readonly ozet?: unknown;
  readonly satirlar?: Sayfa<unknown>;
  readonly export?: { excel: string; csv: string; pdf?: string | null } | null;
}

interface Request {
  readonly yol: `/api/ui/v1/${string}`;
  readonly p: SorguParametreleri;
}

type FilterValue = string | number | boolean | GunAraligi | SecimSecenegi | null;

/**
 * F10.2 ORTAK RAPOR EKRANI. 26 rapor rotası bu bileşeni `data.rapor` koduyla açar; ekran tamamen rapor
 * tanımından çizilir (`report-catalog`): görünüm seçimi, süzgeçler (dönem İstanbul günü, şube kapsamlıda sabit),
 * özet kartları, özet içi tablolar, sayfalı/sıralı satırlar (tablo motoru), export (yalnız sunucu bağlantı
 * verdiyse). URL tek doğruluk kaynağı. 403 (firma geneli raporu şube kapsamlı kullanıcı açtı) açık mesajla gösterilir.
 * KVKK: yanıt yalnız bellekte; hiçbir değer tarayıcı deposuna yazılmaz (müşteri adı sunucuda maskeli).
 */
@Component({
  selector: 'rc-report-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    AramaSecim,
    Ikon,
    MetinGirdisi,
    OnayKutusu,
    SayiGirdisi,
    Secim,
    TarihSecici,
    Tablo,
    TabloHucre,
  ],
  providers: [FetchPolicy],
  templateUrl: './report-page.html',
  styleUrl: './report-page.scss',
})
export class ReportPage {
  private readonly api = inject(ApiIstemcisi);
  private readonly session = inject(OturumServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  protected readonly t = ceviriFonksiyonu();

  protected readonly def = findReport(String(this.route.snapshot.data['rapor'] ?? ''));
  private readonly recordId = this.route.snapshot.paramMap.get('id');
  protected readonly query = listeSorgusuUrlSenkronu(reportListDefinition(this.def));
  protected readonly view = computed(() =>
    activeView(this.def, this.query.sorgu().filtreler as Record<string, unknown>),
  );

  /** Şube kapsamlı kullanıcı (şube süzgeci sabit, firma geneli rapor 403). */
  protected readonly scopedBranch = computed(() => {
    const scope = this.session.ben()?.subeKapsami;
    return scope ? !scope.tumSubeler : false;
  });
  protected readonly ownBranch = computed(() => this.session.ben()?.subeKapsami.subeAd ?? '');

  private readonly request = computed<Request>(() => {
    const v = this.view();
    return {
      yol: viewUrl(v, this.recordId),
      p: viewParams(v, this.query.sorgu(), this.scopedBranch()),
    };
  });

  protected readonly store = new TemelStore(
    (r: Request) =>
      this.api.get<unknown>(r.yol, { parametreler: r.p, context: istekBaglami({ sessiz: true }) }),
    { oncekiVeriyiKoru: true },
  );

  /** Son iyi yanıt (yeniden yüklerken soluk kalır). */
  private readonly response = computed<unknown>(() => {
    const d = this.store.durum();
    return d.tur === 'hazir' ? d.veri : d.tur === 'yukleniyor' ? d.onceki : undefined;
  });
  protected readonly summary = computed<unknown>(() => {
    const r = this.response();
    if (r === undefined || r === null) return null;
    return this.view().zarfsiz ? r : ((r as Envelope).ozet ?? null);
  });
  protected readonly exportLinks = computed(() =>
    this.view().zarfsiz ? null : exportFromLinks((this.response() as Envelope | undefined)?.export),
  );
  protected readonly exportFormats = computed<readonly DisaAktarmaBicimi[]>(
    () => this.exportLinks()?.bicimler ?? [],
  );

  /** Satır tablosunun kaynağı: store durumu, veri `satirlar` sayfasına indirgenir. */
  protected readonly rows = computed<StoreDurumu<Sayfa<unknown>>>(() => {
    const d = this.store.durum();
    const page = (x: unknown) => (x as Envelope | undefined)?.satirlar;
    switch (d.tur) {
      case 'hazir':
        return { tur: 'hazir', veri: page(d.veri) ?? EMPTY_PAGE };
      case 'yukleniyor':
        return { tur: 'yukleniyor', onceki: page(d.onceki) };
      default:
        return d;
    }
  });

  protected readonly tableColumns = computed(() =>
    (this.view().satirlar?.sutunlar ?? []).map((c) => this.toTableColumn(c)),
  );
  protected readonly linkColumns = computed(() =>
    (this.view().satirlar?.sutunlar ?? []).filter((c) => c.bag),
  );

  /** 403: yetki/kapsam mesajı (hata bandı yerine). */
  protected readonly forbidden = computed(() => this.store.hata()?.status === 403);

  // ── süzgeç formu ──
  private readonly allFilters = this.def.gorunumler.flatMap((v) => v.filtreler);
  protected readonly filterForm = new FormGroup<Record<string, FormControl<FilterValue>>>(
    Object.fromEntries(
      this.allFilters.map((f) => [controlName(f), new FormControl<FilterValue>(null)]),
    ),
  );
  protected readonly controlName = controlName;
  private readonly searchLabels = new Map<string, SecimSecenegi>();
  protected readonly listOptions = signal<
    Partial<Record<ListSource, readonly SecenekOgesi<string>[]>>
  >({});
  protected readonly branchNames = signal<readonly string[]>([]);
  protected readonly searchSources = Object.fromEntries(
    this.allFilters
      .filter((f) => f.tur === 'arama')
      .map((f) => [f.ad, sunucuSecimKaynagi(f.kaynak)]),
  );

  constructor() {
    inject(FetchPolicy).baglan({
      parametre: this.request,
      yukle: (r) => this.store.yukle(r),
      sifirla: () => this.store.sifirla(),
      esit: (a, b) => a.yol === b.yol && JSON.stringify(a.p) === JSON.stringify(b.p),
    });
    effect(() => {
      const values = this.query.sorgu().filtreler as Record<string, unknown>;
      const scoped = this.scopedBranch();
      const own = this.ownBranch();
      untracked(() => this.fillForm(values, scoped, own));
    });
    this.loadOptionLists();
  }

  protected viewFilters(): readonly ReportFilter<unknown>[] {
    return this.view().filtreler;
  }

  protected selectOptions(f: ReportFilter<unknown>): readonly SecenekOgesi<string>[] {
    if (f.tur === 'secim')
      return f.secenekler.map((s) => ({ deger: s.deger, etiket: this.t(s.etiket) }));
    if (f.tur === 'liste') return this.listOptions()[f.kaynak] ?? [];
    return [];
  }

  protected suggestions(f: ReportFilter<unknown>): readonly string[] {
    if (f.tur === 'sube') return this.branchNames();
    if (f.tur !== 'metin' || !f.oneriler) return [];
    const s = this.summary();
    return s ? f.oneriler(s) : [];
  }

  protected switchView(kod: string): void {
    if (kod === this.view().kod) return;
    void this.query.degistir({
      sayfa: 1,
      sirala: null,
      filtreler: { ...this.query.sorgu().filtreler, [VIEW_KEY]: kod },
    });
  }

  protected apply(): void {
    const raw = this.filterForm.getRawValue();
    const next: Record<string, string | number | boolean | undefined> = {};
    if (this.def.gorunumler.length > 1) next[VIEW_KEY] = this.view().kod;
    for (const f of this.viewFilters()) {
      const v = raw[controlName(f)];
      if (f.tur === 'donem') {
        const r = v as GunAraligi | null;
        next['bas'] = r?.baslangic ?? undefined;
        next['bit'] = r?.bitis ?? undefined;
      } else if (f.tur === 'arama') {
        const o = v as SecimSecenegi | null;
        if (o) this.searchLabels.set(o.id, o);
        next[f.ad] = o?.id ?? undefined;
      } else if (f.tur === 'bayrak') {
        next[f.ad] = v === true ? true : undefined;
      } else if (f.tur === 'sube' && this.scopedBranch()) {
        next[f.ad] = undefined;
      } else {
        next[f.ad] = v === null || v === '' ? undefined : (v as string | number | boolean);
      }
    }
    void this.query.degistir({ sayfa: 1, filtreler: next });
  }

  protected clear(): void {
    const keep = this.def.gorunumler.length > 1 ? this.view().kod : undefined;
    void this.query.sifirla().then(() => {
      if (keep && keep !== this.def.gorunumler[0]?.kod) {
        void this.query.degistir({ filtreler: { [VIEW_KEY]: keep } });
      }
    });
  }

  protected exportHref(format: DisaAktarmaBicimi): string {
    const e = this.exportLinks();
    return e ? disaAktarmaAdresi(e, format) : '';
  }

  // ── özet içi tablolar / kartlar ──
  protected cell(c: ReportColumn<unknown>, row: unknown): string {
    return this.format(c.tur, c.deger(row), c.paraBirimi?.(row));
  }

  protected format(
    kind: ReportColumn<unknown>['tur'],
    value: unknown,
    currency?: string | null,
  ): string {
    return formatValue(
      kind,
      value,
      { evet: this.t('rapor.evet'), hayir: this.t('rapor.hayir') },
      currency,
    );
  }

  protected negative(value: unknown): boolean {
    return isNegative(value);
  }

  protected header(c: ReportColumn<unknown>): string {
    return c.baslikMetni ?? this.t(c.baslik);
  }

  protected numeric(c: ReportColumn<unknown>): boolean {
    return c.tur === 'para' || c.tur === 'sayi' || c.tur === 'tamsayi' || c.tur === 'yuzde';
  }

  protected readonly rowId = (row: unknown) => this.view().satirlar?.satirKimligi(row) ?? '';

  private toTableColumn(c: ReportColumn<unknown>): TabloSutunu<unknown> {
    const base = {
      kod: c.kod,
      baslik: this.header(c),
      sirala: c.sirala ? true : undefined,
      gizli: c.gizli,
      sabit: c.sabit,
    } as const;
    switch (c.tur) {
      case 'para':
        return {
          ...base,
          tur: 'para',
          deger: (r) => asNumber(c.deger(r)),
          paraBirimi: c.paraBirimi ? (r) => c.paraBirimi?.(r) || 'TRY' : undefined,
        };
      case 'sayi':
      case 'tamsayi':
        return {
          ...base,
          tur: 'sayi',
          haneler: c.tur === 'tamsayi' ? '1.0-0' : undefined,
          deger: (r) => asNumber(c.deger(r)),
        };
      case 'tarih':
      case 'tarihSaat':
        return { ...base, tur: c.tur, deger: c.deger };
      case 'yuzde':
        return {
          ...base,
          tur: 'metin',
          hizala: 'son',
          deger: (r) => this.format('yuzde', c.deger(r)),
        };
      case 'bayrak':
        return { ...base, tur: 'metin', deger: (r) => this.format('bayrak', c.deger(r)) };
      case 'metin':
        return { ...base, tur: 'metin', deger: c.deger };
    }
  }

  private fillForm(values: Record<string, unknown>, scoped: boolean, own: string): void {
    const patch: Record<string, FilterValue> = {};
    for (const f of this.allFilters) {
      const name = controlName(f);
      if (f.tur === 'donem') {
        const bas = values['bas'] as string | undefined;
        const bit = values['bit'] as string | undefined;
        patch[name] = bas && bit ? { baslangic: bas, bitis: bit } : null;
      } else if (f.tur === 'arama') {
        const id = values[f.ad] as string | undefined;
        patch[name] = id
          ? (this.searchLabels.get(id) ?? { id, etiket: this.t('rapor.seciliKayit') })
          : null;
      } else if (f.tur === 'sube' && scoped) {
        patch[name] = own;
      } else {
        patch[name] = (values[f.ad] as FilterValue | undefined) ?? null;
      }
    }
    this.filterForm.reset(patch);
    for (const f of this.allFilters) {
      const control = this.filterForm.controls[controlName(f)];
      if (f.tur === 'sube' && scoped) control?.disable({ emitEvent: false });
      else control?.enable({ emitEvent: false });
    }
  }

  private loadOptionLists(): void {
    const sources = new Set(this.allFilters.flatMap((f) => (f.tur === 'liste' ? [f.kaynak] : [])));
    const quiet = istekBaglami({ sessiz: true });
    const set = (k: ListSource, items: readonly { id: string; etiket: string }[]) =>
      this.listOptions.update((o) => ({
        ...o,
        [k]: items.map((i) => ({ deger: i.id, etiket: i.etiket })),
      }));
    if (sources.has('finansHesap')) {
      this.api
        .get<readonly { id: string; etiket: string }[]>('/api/ui/v1/finans/hesaplar', {
          context: quiet,
        })
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({ next: (items) => set('finansHesap', items), error: () => undefined });
    }
    const needsBranches = sources.has('sube') || this.allFilters.some((f) => f.tur === 'sube');
    if (needsBranches) {
      this.api
        .get<readonly { id: string; etiket: string }[]>('/api/ui/v1/secim/sube', {
          parametreler: { limit: 20 },
          context: quiet,
        })
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (items) => {
            set('sube', items);
            this.branchNames.set(items.map((i) => i.etiket));
          },
          error: () => undefined,
        });
    }
  }
}

const EMPTY_PAGE: Sayfa<unknown> = { kayitlar: [], toplam: 0, sayfaNo: 1, boyut: 50 };

/** Form kontrol adı (dönem iki URL anahtarını tek kontrolde tutar). */
function controlName(f: ReportFilter<unknown>): string {
  return f.tur === 'donem' ? 'donem' : f.ad;
}
