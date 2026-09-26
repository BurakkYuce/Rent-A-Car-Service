import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  type ValidatorFn,
  Validators,
} from '@angular/forms';
import { NgTemplateOutlet } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi, type ApiPath, type QueryParameters } from '@core/api/api-istemcisi';
import type { Sayfa } from '@core/api/sayfa';
import { formatDateTime } from '@core/bicim/bicim';
import type { SelectionItem } from '@core/api/ui-tipleri';
import { type UnsavedChangesOwner, pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { WarningBannerService } from '@core/geri-bildirim/warning-banner-service';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { translationFunction } from '@core/i18n/ceviri';
import { requestContext } from '@core/oturum/request-context';
import { genelGosterilir } from '@core/oturum/session-interceptor';
import { SessionService } from '@core/oturum/session-service';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listQueryUrlSync } from '@core/veri/liste-sorgusu-url';
import { TemelStore } from '@core/veri/temel-store';
import { textValue, mergeServerValues } from '@features/planlama-ortak/form-yardimcilari';
import { Alan } from '@shared/form/alan/alan';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { TextArea } from '@shared/form/kontroller/text-area';
import { TextInput } from '@shared/form/kontroller/text-input';
import { Checkbox } from '@shared/form/kontroller/checkbox';
import { MoneyInput } from '@shared/form/kontroller/money-input';
import { NumberInput } from '@shared/form/kontroller/number-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { DatePicker } from '@shared/form/tarih/date-picker';
import { Icon } from '@shared/ikon/icon';
import { Table } from '@shared/tablo/table';
import { TableCell } from '@shared/tablo/table-cell';

import { RatePriceQuery } from '../rate-price-query/rate-price-query';
import { ServiceDefinitionSuggestions } from '../service-definition-suggestions/service-definition-suggestions';
import { CATALOGS } from './catalog-configs';
import { catalogColumns } from './catalog-columns';
import {
  type CatalogConfig,
  type CatalogField,
  type CatalogRow,
  catalogListDefinition,
  decimalsOf,
  emptyFormValue,
  formToBody,
  rowToForm,
} from './catalog-model';
import { WeekdayPicker } from './weekday-picker';
import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';

type Editing = { readonly kind: 'new' } | { readonly kind: 'record'; readonly id: string };

interface FieldGroup {
  readonly legend: CeviriAnahtari | null;
  readonly fields: readonly CatalogField[];
}

/**
 * F9.2 fiyat/tarife tanım ekranı — sekiz Blazor sayfasının ortak iskeleti (rota verisi `catalog`): açıklama,
 * süzgeç (`q` + ekrana özgü), sunucu sayfalı liste, "Yeni" / "Düzenle" bölümü, onaylı silme. Tam değiştirme PUT'u
 * `surum` ister: düzenleme açılınca kayıt tekil uçtan okunur (sürüm), okuma dönmeden Kaydet pasif; 409 `cakisma`
 * → güncel kayıt KİRLİ forma birleşir (form SİLİNMEZ), sonraki PUT yeni sürümle. Tanımlar deftere yazmaz.
 * Yazma OperationsWrite (servis tanımları okuması FinanceWrite ∨ ViewReports'a da açık — düğmeler gizlenir).
 */
@Component({
  selector: 'rc-catalog-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PageBand,
    NgTemplateOutlet,
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    FormErrors,
    Icon,
    TextArea,
    TextInput,
    Checkbox,
    MoneyInput,
    RatePriceQuery,
    NumberInput,
    Selection,
    ServiceDefinitionSuggestions,
    Table,
    TableCell,
    DatePicker,
    WeekdayPicker,
  ],
  providers: [FetchPolicy],
  templateUrl: './catalog-page.html',
  styleUrl: '../pricing.scss',
})
export class CatalogPage implements UnsavedChangesOwner {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastService);
  private readonly banner = inject(WarningBannerService);
  private readonly confirm = inject(ConfirmService);
  private readonly session = inject(SessionService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly t = translationFunction();

  protected readonly config: CatalogConfig =
    CATALOGS[(inject(ActivatedRoute).snapshot.data['catalog'] as string | undefined) ?? ''] ??
    CATALOGS['tarifeler']!;
  protected readonly query = listQueryUrlSync(catalogListDefinition(this.config));
  protected readonly list = new TemelStore(
    (p: QueryParameters) =>
      this.api.get<Sayfa<CatalogRow>>(this.config.listPath ?? this.config.root, {
        parametreler: p,
      }),
    { oncekiVeriyiKoru: true },
  );
  protected readonly canWrite = computed(() => this.session.izinVar('OperationsWrite'));
  protected readonly columns = catalogColumns(this.config, this.t, this.canWrite());
  protected readonly rowId = (r: CatalogRow) => r.id;
  protected readonly editableFields = this.config.fields.filter((f) => f.kind !== 'readonly');
  protected readonly readonlyFields = this.config.fields.filter((f) => f.kind === 'readonly');
  protected readonly groups = groupFields(this.editableFields);
  protected readonly decimalsOf = decimalsOf;
  protected readonly busy = signal<string | null>(null);

  // ---- düzenleme
  protected readonly editing = signal<Editing | null>(null);
  /** Düzenlenen kaydın sunucu hâli (`surum` taşır); okunana kadar Kaydet pasif. */
  protected readonly base = signal<CatalogRow | null>(null);
  private filledFrom: Record<string, unknown> | null = null;
  protected readonly form = new FormGroup(
    Object.fromEntries(
      this.editableFields.map((f) => [f.name, new FormControl<unknown>(null, validators(f))]),
    ) as Record<string, FormControl<unknown>>,
  );
  protected readonly submission = formSubmission();

  // ---- süzgeç
  protected readonly filterForm = new FormGroup(
    Object.fromEntries(
      [{ name: 'q' }, ...(this.config.filters ?? [])].map((f) => [
        f.name,
        new FormControl<unknown>(null),
      ]),
    ) as Record<string, FormControl<unknown>>,
  );

  // ---- seç-veya-yaz önerileri ve seçim listeleri (sessiz: hata → öneri yok, alan serbest metin kalır)
  protected readonly suggestions = signal<Readonly<Partial<Record<string, readonly string[]>>>>({});
  protected readonly lookups = signal<
    Readonly<Partial<Record<string, readonly SecenekOgesi<string>[]>>>
  >({});

  constructor() {
    inject(FetchPolicy).connect({
      parametre: this.query.apiParametreleri,
      yukle: (p) => this.list.yukle(p),
      sifirla: () => this.list.reset(),
      sekmeyeDonunce: 'yenile',
    });
    effect(() => {
      const f = this.query.sorgu().filtreler as Readonly<Record<string, unknown>>;
      untracked(() => {
        const value: Record<string, unknown> = {};
        for (const name of Object.keys(this.filterForm.controls)) {
          const v = f[name];
          value[name] = typeof v === 'boolean' ? String(v) : (v ?? null);
        }
        this.filterForm.reset(value);
      });
    });
    effect(() => {
      if (this.editing() !== null && this.canWrite()) untracked(() => this.loadChoices());
    });
    pageLeaveGuard(() => this.hasUnsavedChanges());
  }

  hasUnsavedChanges(): boolean {
    return this.editing() !== null && this.form.dirty;
  }

  // ------------------------------------------------------------------ görünüm yardımcıları

  protected optionsOf(f: CatalogField): readonly SecenekOgesi<string>[] {
    if (f.kind === 'lookup') return this.lookups()[f.name] ?? [];
    return (f.options ?? []).map((o) => ({
      deger: o,
      etiket: f.optionLabels ? this.t(`${f.optionLabels}.${o}` as CeviriAnahtari) : o,
    }));
  }

  protected filterOptions(name: string): readonly SecenekOgesi<string>[] {
    const f = this.config.filters?.find((x) => x.name === name);
    if (!f) return [];
    if (f.kind === 'bool')
      return [
        { deger: 'true', etiket: this.t('fiyatTarife.evet') },
        { deger: 'false', etiket: this.t('fiyatTarife.hayir') },
      ];
    return (f.options ?? []).map((o) => ({
      deger: o,
      etiket: f.optionLabels ? this.t(`${f.optionLabels}.${o}` as CeviriAnahtari) : o,
    }));
  }

  protected listId(f: CatalogField): string | undefined {
    return f.suggestion ? `dl-${this.config.key}-${f.name}` : undefined;
  }

  protected readonlyText(f: CatalogField): string {
    const b = this.base();
    if (!b) return '—';
    if (f.name === 'onaylayan') {
      const who = b['onaylayan'];
      const at = b['onayZaman'];
      return typeof who === 'string' && who !== ''
        ? `${who}${typeof at === 'string' ? ` · ${formatDateTime(at)}` : ''}`
        : '—';
    }
    const v = b[f.name];
    return v === null || v === undefined ? '—' : String(v);
  }

  // ------------------------------------------------------------------ süzgeç

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    const filters: Record<string, string | undefined> = {};
    for (const [name, value] of Object.entries(v))
      filters[name] = typeof value === 'string' ? (textValue(value) ?? undefined) : undefined;
    void this.query.degistir({ sayfa: 1, filtreler: filters });
  }

  protected clear(): void {
    void this.query.sifirla();
  }

  // ------------------------------------------------------------------ düzenleme

  protected async startNew(): Promise<void> {
    if (!(await this.releaseForm())) return;
    this.base.set(null);
    this.filledFrom = null;
    this.form.reset(emptyFormValue(this.editableFields));
    this.applyLocks(null);
    this.submission.kilit.yenile();
    this.editing.set({ kind: 'new' });
    this.focusEditor();
  }

  protected async edit(row: CatalogRow): Promise<void> {
    if (!(await this.releaseForm())) return;
    this.base.set(null);
    this.filledFrom = rowToForm(this.editableFields, row);
    this.form.reset(this.filledFrom);
    this.applyLocks(row);
    this.submission.kilit.yenile();
    this.editing.set({ kind: 'record', id: row.id });
    this.readRecord(row.id);
    this.focusEditor();
  }

  protected async cancel(): Promise<void> {
    if (!(await this.releaseForm())) return;
    this.close();
  }

  protected save(): void {
    const e = this.editing();
    if (e === null || !this.canWrite()) return;
    const base = this.base();
    if (e.kind === 'record' && base === null) return; // sürüm okunmadan tam değiştirme gönderilmez
    const body = formToBody(
      this.editableFields,
      this.form.getRawValue(),
      e.kind === 'record' ? base : null,
    );
    const title = String(this.form.getRawValue()[this.config.titleField] ?? '');
    this.submission.gonder(
      this.form,
      (key) =>
        e.kind === 'new'
          ? this.api.post<CatalogRow>(this.config.root, body, { islemAnahtari: key })
          : this.api.put<CatalogRow>(this.recordPath(e.id), body, { islemAnahtari: key }),
      {
        basarili: () => {
          this.toast.basari(
            this.t(e.kind === 'new' ? 'fiyatTarife.eklendi' : 'fiyatTarife.kaydedildi', {
              ad: title,
            }),
          );
          this.close();
          this.list.yenile();
        },
        hata: (h) => {
          if (h.kod === 'cakisma' && e.kind === 'record') this.readRecord(e.id);
          if (h.kod === 'mukerrer') this.list.yenile();
        },
      },
    );
  }

  protected async remove(row: CatalogRow): Promise<void> {
    if (this.busy() !== null) return;
    const title = String(row[this.config.titleField] ?? '');
    const yes = await this.confirm.ask({
      baslik: this.t('fiyatTarife.silBaslik'),
      mesaj: this.t('fiyatTarife.silMesaj', { ad: title }),
      onayEtiketi: this.t('fiyatTarife.sil'),
      tehlikeli: true,
    });
    if (!yes || this.busy() !== null) return;
    this.busy.set(row.id);
    this.api
      .delete<unknown>(this.recordPath(row.id))
      .pipe(
        finalize(() => this.busy.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.toast.basari(this.t('fiyatTarife.silindi', { ad: title }));
          const e = this.editing();
          if (e?.kind === 'record' && e.id === row.id) this.close();
          this.list.yenile();
        },
        error: (raw: unknown) => {
          const error = toApiError(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
          this.list.yenile();
        },
      });
  }

  protected reload(): void {
    this.list.yenile();
  }

  // ------------------------------------------------------------------ iç

  private recordPath(id: string): ApiPath {
    return `${this.config.root}/${encodeURIComponent(id)}`;
  }

  private readRecord(id: string): void {
    this.api
      .get<CatalogRow>(this.recordPath(id))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => {
          const e = this.editing();
          if (e?.kind === 'record' && e.id === id) this.recordArrived(r);
        },
        error: (raw: unknown) => {
          const error = toApiError(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
        },
      });
  }

  /**
   * Güncel kayıt: temiz form sıfırlanır; kirli formda dokunulan alan korunur, sunucuda da değişen işaretlenir.
   * Taban formun DOLDURULDUĞU değer (ilk okumada liste satırı; sonra son okunan kayıt) — dokunulmayan alan TAZE
   * değeri alır, bayat liste satırı başka oturumun değişikliğini ezmez.
   */
  private recordArrived(r: CatalogRow): void {
    const fresh = rowToForm(this.editableFields, r);
    const previous = this.base();
    const baseline =
      previous === null ? (this.filledFrom ?? fresh) : rowToForm(this.editableFields, previous);
    if (!this.form.dirty) {
      this.form.reset(fresh);
    } else {
      const conflicts = mergeServerValues(
        this.form,
        fresh,
        baseline,
        this.t('fiyatTarife.cakismaAlan'),
      );
      if (conflicts.length > 0)
        this.banner.show({
          tur: 'uyari',
          mesaj: this.t('fiyatTarife.cakismaBant', { sayi: conflicts.length }),
          kod: 'cakisma',
        });
    }
    this.base.set(r);
    this.applyLocks(r);
  }

  /** Satır bayrağıyla kilitli alan (ör. `SYS-*` kodu) pasif; değeri gövdede AYNEN gider (getRawValue). */
  private applyLocks(row: CatalogRow | null): void {
    for (const f of this.editableFields) {
      const control = this.form.controls[f.name];
      if (!control) continue;
      const locked = row !== null && f.lockedBy !== undefined && row[f.lockedBy] === true;
      if (locked) control.disable({ emitEvent: false });
      else control.enable({ emitEvent: false });
    }
  }

  private close(): void {
    this.editing.set(null);
    this.base.set(null);
    this.filledFrom = null;
    this.form.reset(emptyFormValue(this.editableFields));
  }

  private async releaseForm(): Promise<boolean> {
    if (this.editing() === null || !this.form.dirty) return true;
    return this.confirm.ask({
      baslik: this.t('fiyatTarife.vazgecBaslik'),
      mesaj: this.t('fiyatTarife.vazgecMesaj'),
    });
  }

  private focusEditor(): void {
    afterNextRender(
      () => this.host.nativeElement.querySelector<HTMLElement>('#rc-tanim-duzenleyici h2')?.focus(),
      { injector: this.injector },
    );
  }

  private choicesLoaded = false;

  /** Önerileri ve seçim listelerini bir kez yükler (düzenleme ilk açıldığında). */
  private loadChoices(): void {
    if (this.choicesLoaded) return;
    this.choicesLoaded = true;
    const quiet = requestContext({ sessiz: true });
    for (const f of this.editableFields) {
      const s = f.suggestion;
      if (s)
        this.api
          .get<readonly SelectionItem[]>(`/api/ui/v1/secim/${s.endpoint}`, {
            parametreler: { limit: 20 },
            context: quiet,
          })
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            next: (items) =>
              this.suggestions.update((m) => ({
                ...m,
                [f.name]: items
                  .map((i) => (s.value === 'kod' ? (i.kod ?? i.etiket) : i.etiket))
                  .filter((x): x is string => typeof x === 'string' && x !== ''),
              })),
            error: () => undefined,
          });
      if (f.lookup)
        this.api
          .get<readonly SelectionItem[]>(`/api/ui/v1/secim/${f.lookup}`, {
            parametreler: { limit: 20 },
            context: quiet,
          })
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            next: (items) =>
              this.lookups.update((m) => ({
                ...m,
                [f.name]: items.map((i) => ({
                  deger: i.id,
                  etiket: i.kod ? `${i.kod} — ${i.etiket}` : i.etiket,
                })),
              })),
            error: () => undefined,
          });
    }
  }
}

function validators(f: CatalogField): ValidatorFn[] {
  const v: ValidatorFn[] = [];
  if (f.required) v.push(Validators.required);
  if (f.maxLength) v.push(Validators.maxLength(f.maxLength));
  return v;
}

/** Ardışık aynı gruptaki alanlar tek bölüm; grupsuzlar ana ızgarada. */
function groupFields(fields: readonly CatalogField[]): readonly FieldGroup[] {
  const groups: { legend: CeviriAnahtari | null; fields: CatalogField[] }[] = [];
  for (const f of fields) {
    const legend = f.group ?? null;
    const last = groups.at(-1);
    if (last && last.legend === legend) last.fields.push(f);
    else groups.push({ legend, fields: [f] });
  }
  return groups;
}
