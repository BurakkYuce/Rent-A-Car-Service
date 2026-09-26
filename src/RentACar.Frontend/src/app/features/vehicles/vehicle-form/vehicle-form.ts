import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { type UnsavedChangesOwner, pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { WarningBannerService } from '@core/geri-bildirim/warning-banner-service';
import { translationFunction } from '@core/i18n/ceviri';
import { trLowerCase } from '@core/metin/tr-normalize';
import { SessionService } from '@core/oturum/session-service';
import { tabContext } from '@core/sekme/tab-state';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { NumberPipe, DateTimePipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { SearchSelection } from '@shared/form/arama-secim/search-selection';
import { serverSelectionSource } from '@shared/form/arama-secim/selection-source';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { TextInput } from '@shared/form/kontroller/text-input';
import { Checkbox } from '@shared/form/kontroller/checkbox';
import { MoneyInput } from '@shared/form/kontroller/money-input';
import { NumberInput } from '@shared/form/kontroller/number-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { TabbedForm, TabPanel, type SekmeTanimi } from '@shared/form/sekmeli-form/tabbed-form';
import { DatePicker } from '@shared/form/tarih/date-picker';
import { Icon } from '@shared/ikon/icon';

import { mergeServerValues } from '@features/planlama-ortak/form-yardimcilari';

import { kmSourceKey } from '../vehicle-detail/vehicle-detail';
import { suggestionList } from '../suggestions';
import { SCORECARD_ROLES } from '../vehicle-guards';
import { toNumber, type VehicleCard } from '../vehicle-model';
import {
  VehicleCardStore,
  selectionSuggestionFetch,
  vehicleSuggestionFetch,
  type SuggestionFetch,
} from '../vehicle.store';
import {
  GROUP_FIELD,
  SECTIONS,
  buildControls,
  cardToForm,
  formToRequest,
  newVehicleValue,
  type FieldSpec,
  type SuggestionKind,
} from './vehicle-form-model';
import { VehiclePhotos } from './vehicle-photos';
import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';

/**
 * Araç kartı (`/app/araclar/yeni`, `/app/araclar/:id`) — Blazor `VehicleEdit.razor` + liste içi "Yeni Araç"
 * formu paritesi. TÜM alanlar beş sekmede (kart, alım/maliyet, detay alanları, kart derinliği, bayraklar) +
 * düzenlemede fotoğraf galerisi. Grup tanımlı gruplardan seçilir (boş = bilinçli "(Grupsuz)"; yeni araçta
 * firmanın varsayılan grubu önseçili; tanımsız mevcut değer korunur ve işaretlenir). Seç-veya-yaz alanları
 * sunucu önerileriyle. PUT tam değiştirmedir (`surum`); bayat sürüm 409 `cakisma` → güncel kart okunur ve
 * KİRLİ forma birleştirilir (form silinmez). Yazma OperationsWrite; izinsiz kullanıcı kartı salt okur.
 */
@Component({
  selector: 'rc-vehicle-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
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
    MoneyInput,
    NumberInput,
    NumberPipe,
    Selection,
    TabbedForm,
    TabPanel,
    DateTimePipe,
    DatePicker,
    VehiclePhotos,
  ],
  providers: [FetchPolicy, VehicleCardStore],
  templateUrl: './vehicle-form.html',
  styleUrls: ['../vehicle-screens.scss', './vehicle-form.scss'],
})
export class VehicleForm implements UnsavedChangesOwner {
  protected readonly store = inject(VehicleCardStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly banner = inject(WarningBannerService);
  private readonly tab = tabContext();
  private readonly t = translationFunction();
  private readonly tabs = viewChild(TabbedForm);

  /** Düzenlenen aracın kimliği; `null` = yeni araç. */
  protected readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id');
  protected readonly isNew = this.id === null;

  protected readonly form = new FormGroup(buildControls());
  protected readonly submission = formSubmission();
  /** Son okunan sunucu hâli: `surum` PUT'a gider, birleştirmenin tabanıdır. */
  protected readonly base = signal<VehicleCard | null>(null);

  protected readonly sections = SECTIONS;
  protected readonly groupField = GROUP_FIELD;
  protected readonly canWrite = computed(() => this.session.izinVar('OperationsWrite'));
  protected readonly canScorecard = computed(() =>
    SCORECARD_ROLES.includes(this.session.ben()?.rol ?? ''),
  );
  protected readonly num = toNumber;
  protected readonly kmSourceKey = kmSourceKey;
  protected readonly groups = serverSelectionSource('arac-grubu');
  protected readonly refreshPhotos = () => this.store.photos.yenile();

  protected readonly tabDefs = computed<readonly SekmeTanimi[]>(() => {
    const list: SekmeTanimi[] = SECTIONS.map((s) => ({
      kimlik: s.id,
      etiket: this.t(`arac.bolum.${s.id}`),
    }));
    if (!this.isNew) list.push({ kimlik: 'fotograflar', etiket: this.t('arac.bolum.fotograflar') });
    return list;
  });

  /** Kartın grubu tanımlı gruplarla eşleşmiyorsa (Blazor "— tanımsız"). */
  protected readonly undefinedGroup = computed(() => {
    const card = this.base();
    const matches = this.store.groupMatches.veri();
    if (!card?.grup || matches === undefined) return null;
    const name = trLowerCase(card.grup.trim());
    return matches.some((m) => trLowerCase(m.etiket.trim()) === name) ? null : card.grup;
  });

  protected readonly lastKm = computed(() =>
    (this.store.detail.veri()?.kmKayitlari ?? []).slice(0, 3),
  );

  private readonly suggestionSignals: Record<SuggestionKind, () => readonly string[]>;

  constructor() {
    const readable = () =>
      this.session.izinVar('OperationsWrite') || this.session.izinVar('ViewReports');
    const c = this.form.controls;
    const fetchers: Record<SuggestionKind, SuggestionFetch> = {
      marka: vehicleSuggestionFetch(this.api, 'marka'),
      tip: vehicleSuggestionFetch(this.api, 'tip', () => ({
        marka: typeof c['marka']?.value === 'string' ? c['marka'].value : null,
      })),
      renk: vehicleSuggestionFetch(this.api, 'renk'),
      segment: vehicleSuggestionFetch(this.api, 'segment'),
      sahip: vehicleSuggestionFetch(this.api, 'sahip'),
      sube: selectionSuggestionFetch(this.api, 'sube'),
    };
    const control = (kind: SuggestionKind) =>
      c[kind === 'sahip' ? 'aracSahibi' : kind] ?? c['plaka']!;
    this.suggestionSignals = {
      marka: suggestionList(control('marka'), fetchers.marka, readable),
      tip: suggestionList(control('tip'), fetchers.tip, readable),
      renk: suggestionList(control('renk'), fetchers.renk, readable),
      segment: suggestionList(control('segment'), fetchers.segment, readable),
      sahip: suggestionList(control('sahip'), fetchers.sahip, readable),
      sube: suggestionList(control('sube'), fetchers.sube, () => this.canWrite()),
    };

    const policy = inject(FetchPolicy);
    if (this.id !== null) {
      const id = this.id;
      policy.connect({
        parametre: signal(id).asReadonly(),
        yukle: (x) => {
          this.store.card.yukle(x);
          this.store.detail.yukle(x);
          this.store.photos.yukle(x);
        },
        sifirla: () => this.store.card.reset(),
      });
      effect(() => {
        const d = this.store.card.durum();
        if (d.tur === 'hazir') untracked(() => this.cardArrived(d.veri));
      });
    } else {
      policy.connect({
        parametre: signal(null).asReadonly(),
        yukle: () => this.store.defaultGroup.yukle(),
      });
      this.form.reset({ ...newVehicleValue(null) });
      effect(() => {
        const g = this.store.defaultGroup.veri();
        if (g?.ad) {
          untracked(() => {
            const control = this.form.controls[GROUP_FIELD];
            if (control?.pristine && control.value === null) {
              control.setValue(newVehicleValue(g.ad)[GROUP_FIELD], { emitEvent: false });
            }
          });
        }
      });
    }
    effect(() => {
      if (!this.canWrite()) untracked(() => this.form.disable({ emitEvent: false }));
    });
    pageLeaveGuard(() => this.form.dirty);
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty;
  }

  protected suggestions(kind: SuggestionKind | undefined): readonly string[] {
    return kind ? this.suggestionSignals[kind]() : [];
  }

  protected listId(spec: FieldSpec): string | undefined {
    return spec.suggest ? `dl-arac-kart-${spec.name}` : undefined;
  }

  protected options(spec: FieldSpec): readonly SecenekOgesi<string>[] {
    const prefix =
      spec.name === 'durum'
        ? 'arac.durumlar'
        : spec.name === 'filoDurum'
          ? 'arac.filoDurumlari'
          : spec.name === 'vites'
            ? 'arac.vitesler'
            : 'arac.yakitlar';
    return (spec.options ?? []).map((o) => ({
      deger: o,
      etiket: this.t(`${prefix}.${o}` as 'arac.durumlar.Musait'),
    }));
  }

  protected label(spec: FieldSpec): string {
    return this.t(`arac.alan.${spec.name}` as 'arac.alan.plaka');
  }

  protected save(): void {
    if (!this.canWrite()) return;
    const base = this.base();
    if (!this.isNew && (base === null || this.store.card.isLoading())) return;
    const body = formToRequest(this.form.getRawValue(), base);
    const id = this.id;
    this.submission.gonder(
      this.form,
      (key) =>
        id === null
          ? this.api.post<VehicleCard>('/api/ui/v1/araclar', body, { islemAnahtari: key })
          : this.api.put<VehicleCard>(
              `/api/ui/v1/araclar/${encodeURIComponent(id)}`,
              { ...body, surum: base?.surum ?? null },
              { islemAnahtari: key },
            ),
      {
        gecersiz: () => this.tabs()?.goToFirstInvalid(),
        basarili: (card) => {
          if (id === null) {
            this.toast.basari(this.t('arac.kart.olusturuldu', { plaka: card.plaka ?? '' }));
            void this.router.navigate(['/araclar', card.id], { replaceUrl: true });
            return;
          }
          this.toast.basari(this.t('arac.kart.kaydedildi', { plaka: card.plaka ?? '' }));
          this.base.set(card);
          this.form.reset({ ...cardToForm(card) });
          this.store.detail.yenile();
        },
        hata: (h) => {
          if (h.kod === 'cakisma') this.store.card.yenile();
          else if (h.alanlar) this.tabs()?.goToFirstInvalid();
        },
      },
    );
  }

  /** Temiz form sunucu hâline sıfırlanır; kirli formda dokunulan alanlar korunur (çakışan işaretlenir). */
  private cardArrived(card: VehicleCard): void {
    this.tab.etiketAyarla(this.t('arac.kart.sekmeEtiketi', { plaka: card.plaka ?? '' }));
    const fresh = cardToForm(card);
    const previous = this.base();
    if (!this.form.dirty || previous === null) {
      this.form.reset({ ...fresh });
    } else {
      const conflicts = mergeServerValues(
        this.form,
        fresh,
        cardToForm(previous),
        this.t('arac.kart.cakismaAlan'),
      );
      if (conflicts.length > 0) {
        this.banner.show({
          tur: 'uyari',
          mesaj: this.t('arac.kart.cakismaBant', { sayi: conflicts.length }),
          kod: 'cakisma',
        });
      }
    }
    this.base.set(card);
    if (card.grup && this.canWrite()) this.store.groupMatches.yukle(card.grup);
    if (!this.canWrite()) this.form.disable({ emitEvent: false });
  }
}
