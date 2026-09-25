import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { catchError, debounceTime, finalize, map, of, startWith, switchMap } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { SecimUcuOgesi } from '@core/api/ui-tipleri';
import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { UyariBandiServisi } from '@core/geri-bildirim/uyari-bandi-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listeSorgusuUrlSenkronu } from '@core/veri/liste-sorgusu-url';
import { sunucuDegerleriniBirlestir } from '@features/planlama-ortak/form-yardimcilari';
import { SUGGESTION_DELAY_MS } from '@features/vehicles/suggestions';
import { toNumber } from '@features/vehicles/vehicle-model';
import { SayiPipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { SayiGirdisi } from '@shared/form/kontroller/sayi-girdisi';
import { Tablo } from '@shared/tablo/tablo';
import { TabloHucre } from '@shared/tablo/tablo-hucre';

import { fleetPlanColumns, signed } from '../finance-columns';
import { FLEET_PLAN_LIST, type FleetPlan } from '../finance-model';
import { FLEET_PLANS, FleetPlanStore, recordPath } from '../finance.store';
import {
  type FleetPlanFormValue,
  emptyFleetPlan,
  fleetPlanRequest,
  fleetPlanToForm,
  fleetPlanTotals,
} from './fleet-plan-model';
import { SayfaBandi } from '../../../kabuk/sayfa-bandi/sayfa-bandi';

type Editing = { readonly kind: 'new' } | { readonly kind: 'record'; readonly id: string };

/**
 * Filo plan hedefi (`/app/filo-plan`) — Blazor `FiloPlanList.razor` paritesi: hedef ekle (grup ve/veya SIPP), liste
 * (hedef/gerçekleşen/kayıtlı/fark/durum), satırda + / − (sunucuda ±1, kilit altında), Düzenle (tam değiştirme,
 * `surum`; 409 `cakisma` → güncel kayıt kirli forma birleşir), Sil (onaylı) ve toplam satırı (hedef ve gerçekleşen
 * AYRI toplanır). Sipariş oluşturmaz. Okuma ViewReports ∨ OperationsWrite; yazma OperationsWrite.
 */
@Component({
  selector: 'rc-fleet-plan-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    SayfaBandi,
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    FormHatalari,
    MetinGirdisi,
    SayiGirdisi,
    SayiPipe,
    Tablo,
    TabloHucre,
  ],
  providers: [FetchPolicy, FleetPlanStore],
  templateUrl: './fleet-plan-list.html',
  styleUrl: '../vehicle-finance.scss',
})
export class FleetPlanList implements KaydedilmemisDegisiklikSahibi {
  protected readonly store = inject(FleetPlanStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly session = inject(OturumServisi);
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly banner = inject(UyariBandiServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly t = ceviriFonksiyonu();

  protected readonly query = listeSorgusuUrlSenkronu(FLEET_PLAN_LIST);
  protected readonly columns = fleetPlanColumns(this.t);
  protected readonly rowId = (r: FleetPlan) => r.id;
  protected readonly signed = signed;
  protected readonly canWrite = computed(() => this.session.izinVar('OperationsWrite'));
  protected readonly busy = signal<string | null>(null);
  protected readonly editing = signal<Editing>({ kind: 'new' });
  protected readonly base = signal<FleetPlan | null>(null);
  /** Formun doldurulduğu değer (ilk okuma dönene kadar birleştirme tabanı: liste satırı). */
  private filledFrom: FleetPlanFormValue | null = null;

  protected readonly totals = computed(() => {
    const rows = this.store.list.veri()?.kayitlar ?? [];
    return rows.length === 0 ? null : fleetPlanTotals(rows);
  });

  protected readonly form = new FormGroup({
    aracGrupAdi: new FormControl<string | null>(null, Validators.maxLength(64)),
    sipp: new FormControl<string | null>(null, Validators.maxLength(16)),
    donem: new FormControl<string | null>(null, Validators.maxLength(32)),
    hedefAdet: new FormControl<number | null>(0, [
      Validators.required,
      Validators.min(0),
      Validators.max(100_000),
    ]),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  protected readonly submission = formGonderimi();

  /** Grup önerileri: tanımlı araç gruplarının KODLARI (Blazor ComboBox). */
  protected readonly groupSuggestions = toSignal(
    this.form.controls.aracGrupAdi.valueChanges.pipe(
      startWith(null),
      map((v) => v?.trim() ?? ''),
      debounceTime(SUGGESTION_DELAY_MS),
      switchMap((q) =>
        this.api
          .get<readonly SecimUcuOgesi<'arac-grubu'>[]>('/api/ui/v1/secim/arac-grubu', {
            parametreler: { q: q === '' ? null : q, limit: 20 },
            context: istekBaglami({ sessiz: true }),
          })
          .pipe(
            map((l) => [...new Set(l.map((g) => g.kod ?? g.etiket).filter((k) => k !== ''))]),
            catchError(() => of([] as string[])),
          ),
      ),
    ),
    { initialValue: [] as string[] },
  );

  constructor() {
    const policy = inject(FetchPolicy);
    policy.baglan({
      parametre: this.query.apiParametreleri,
      yukle: (p) => this.store.list.yukle(p),
      sifirla: () => this.store.list.sifirla(),
      sekmeyeDonunce: 'yenile',
    });
    this.form.reset({ ...emptyFleetPlan() });
    sayfaTerkKorumasi(() => this.form.dirty);
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.form.dirty;
  }

  /** Blazor rozet renkleri: fark 0 başarı, pozitif (eksik) uyarı, negatif (fazla) bilgi. */
  protected statusClass(r: FleetPlan): string {
    const f = toNumber(r.fark) ?? 0;
    return `rc-rozet ${f === 0 ? 'rc-rozet--basari' : f > 0 ? 'rc-rozet--uyari' : 'rc-rozet--bilgi'}`;
  }

  protected async edit(row: FleetPlan): Promise<void> {
    if (!(await this.releaseForm())) return;
    this.editing.set({ kind: 'record', id: row.id });
    this.base.set(null);
    this.filledFrom = fleetPlanToForm(row);
    this.form.reset({ ...this.filledFrom });
    this.submission.kilit.yenile();
    this.readRecord(row.id);
    afterNextRender(
      () => this.host.nativeElement.querySelector<HTMLElement>('#rc-plan-formu')?.focus(),
      { injector: this.injector },
    );
  }

  protected async cancelEdit(): Promise<void> {
    if (!(await this.releaseForm())) return;
    this.resetForm();
  }

  protected save(): void {
    if (!this.canWrite()) return;
    const e = this.editing();
    const base = this.base();
    if (e.kind === 'record' && base === null) return;
    const body = fleetPlanRequest(this.form.getRawValue() as FleetPlanFormValue, base);
    this.submission.gonder(
      this.form,
      (key) =>
        e.kind === 'new'
          ? this.api.post<FleetPlan>(FLEET_PLANS, body, { islemAnahtari: key })
          : this.api.put<FleetPlan>(recordPath(FLEET_PLANS, e.id), body, { islemAnahtari: key }),
      {
        basarili: () => {
          this.toast.basari(
            this.t(e.kind === 'new' ? 'aracFinans.plan.eklendi' : 'aracFinans.plan.kaydedildi'),
          );
          this.resetForm();
          this.store.list.yenile();
        },
        hata: (h) => {
          if (h.kod === 'cakisma' && e.kind === 'record') this.readRecord(e.id);
        },
      },
    );
  }

  protected delta(row: FleetPlan, direction: 'artir' | 'azalt'): void {
    if (this.busy() !== null) return;
    this.busy.set(row.id);
    this.api
      .post<FleetPlan>(recordPath(FLEET_PLANS, row.id, '/delta'), { yon: direction })
      .pipe(
        finalize(() => this.busy.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (fresh) => {
          const e = this.editing();
          if (e.kind === 'record' && e.id === row.id) this.recordArrived(fresh);
          this.store.list.yenile();
        },
        error: (raw: unknown) => this.actionFailed(raw),
      });
  }

  protected async remove(row: FleetPlan): Promise<void> {
    if (this.busy() !== null) return;
    const yes = await this.confirm.sor({
      baslik: this.t('aracFinans.plan.silBaslik'),
      mesaj: this.t('aracFinans.plan.silMesaj'),
      onayEtiketi: this.t('aracFinans.sil'),
      tehlikeli: true,
    });
    if (!yes || this.busy() !== null) return;
    this.busy.set(row.id);
    this.api
      .delete<unknown>(recordPath(FLEET_PLANS, row.id))
      .pipe(
        finalize(() => this.busy.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.toast.basari(this.t('aracFinans.plan.silindi'));
          const e = this.editing();
          if (e.kind === 'record' && e.id === row.id) this.resetForm();
          this.store.list.yenile();
        },
        error: (raw: unknown) => this.actionFailed(raw),
      });
  }

  private actionFailed(raw: unknown): void {
    const error = apiHatasinaCevir(raw);
    if (!genelGosterilir(error)) this.toast.hata(error.detay);
    this.store.list.yenile();
  }

  private readRecord(id: string): void {
    this.api
      .get<FleetPlan>(recordPath(FLEET_PLANS, id))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (p) => {
          const e = this.editing();
          if (e.kind === 'record' && e.id === id) this.recordArrived(p);
        },
        error: (raw: unknown) => {
          const error = apiHatasinaCevir(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
        },
      });
  }

  /**
   * Güncel kayıt: temiz form sıfırlanır; kirli formda dokunulan alan korunur, çakışan işaretlenir. İlk okumada taban
   * formun doldurulduğu (bayat olabilecek) liste satırıdır — dokunulmamış alanlar TAZE değeri alır (inceleme M1).
   */
  private recordArrived(p: FleetPlan): void {
    const fresh = fleetPlanToForm(p);
    const previous = this.base();
    const baseline = (previous === null ? this.filledFrom : fleetPlanToForm(previous)) ?? fresh;
    if (!this.form.dirty) {
      this.form.reset({ ...fresh });
    } else {
      const conflicts = sunucuDegerleriniBirlestir(
        this.form,
        { ...fresh },
        { ...baseline },
        this.t('aracFinans.cakismaAlan'),
      );
      if (conflicts.length > 0)
        this.banner.goster({
          tur: 'uyari',
          mesaj: this.t('aracFinans.cakismaBant', { sayi: conflicts.length }),
          kod: 'cakisma',
        });
    }
    // Tekil okuma, PUT ve delta yanıtı `surum` taşır: sonraki PUT güncel sürümle gider.
    this.base.set(p);
  }

  private resetForm(): void {
    this.editing.set({ kind: 'new' });
    this.base.set(null);
    this.filledFrom = null;
    this.form.reset({ ...emptyFleetPlan() });
    this.submission.kilit.yenile();
  }

  private async releaseForm(): Promise<boolean> {
    if (!this.form.dirty) return true;
    return this.confirm.sor({
      baslik: this.t('aracFinans.vazgecBaslik'),
      mesaj: this.t('aracFinans.vazgecMesaj'),
    });
  }
}
