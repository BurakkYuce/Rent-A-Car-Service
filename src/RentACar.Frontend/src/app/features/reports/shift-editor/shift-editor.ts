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
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { tarihBicimle } from '@core/bicim/bicim';
import { sayfaTerkKorumasi } from '@core/form/kaydedilmemis-degisiklik';
import type { GunMetni } from '@core/form/tarih-girdisi';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { UyariBandiServisi } from '@core/geri-bildirim/uyari-bandi-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { sunucuDegerleriniBirlestir } from '@features/planlama-ortak/form-yardimcilari';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';

import {
  SHIFTS,
  type Shift,
  type ShiftFormValue,
  type ShiftListRow,
  TIME_PATTERN,
  emptyShift,
  listRowToForm,
  shiftPath,
  shiftRequest,
  shiftToForm,
} from './shift-model';

type Editing = { readonly kind: 'new' } | { readonly kind: 'record'; readonly id: string };

/**
 * F10.3 — personel çalışma raporunun vardiya ekle / düzenle / sil bölümü (Blazor `PersonelCalismaTablosu`
 * formu + "Vardiya Listesi" paritesi). Yalnız OperationsWrite'ta çizilir (uç da onu ister). Düzenle tekil kaydı
 * okur (`surum`); 409 `cakisma` → güncel kayıt kirli forma birleşir, form silinmez. Kaydet/sil sonrası rapor
 * yeniden yüklenir (`degisti`). Varsayılanlar Blazor'la aynı: pencerenin ilk günü, 08:00–18:00; şube kapsamlı
 * kullanıcıda kendi şubesi (başka şube sunucuda 403).
 */
@Component({
  selector: 'rc-shift-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    AramaSecim,
    FormHatalari,
    MetinGirdisi,
    Secim,
    TarihSecici,
  ],
  templateUrl: './shift-editor.html',
  styleUrl: './shift-editor.scss',
})
export class ShiftEditor {
  /** Görüntülenen pencerenin vardiyaları (rapor yanıtının `liste`si). */
  readonly rows = input.required<readonly ShiftListRow[]>();
  /** Pencerenin ilk günü (yeni vardiyanın varsayılan tarihi). */
  readonly firstDay = input<GunMetni | null>(null);
  readonly changed = output<void>();

  private readonly api = inject(ApiIstemcisi);
  private readonly session = inject(OturumServisi);
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly banner = inject(UyariBandiServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly t = ceviriFonksiyonu();

  protected readonly staff = sunucuSecimKaynagi('personel');
  protected readonly branches = signal<readonly SecenekOgesi<string>[]>([]);
  protected readonly editing = signal<Editing>({ kind: 'new' });
  protected readonly base = signal<Shift | null>(null);
  protected readonly busy = signal<string | null>(null);
  private filledFrom: ShiftFormValue | null = null;

  private readonly ownBranch = computed(() => {
    const scope = this.session.ben()?.subeKapsami;
    return scope && !scope.tumSubeler ? (scope.subeAd ?? null) : null;
  });

  /** Seçenekler + düzenlenen kaydın listede olmayan şubesi (seçim boş görünmesin). */
  protected readonly branchOptions = computed<readonly SecenekOgesi<string>[]>(() => {
    const list = this.branches();
    const own = this.ownBranch();
    const current = this.base()?.sube ?? null;
    const extra = [own, current].filter(
      (b): b is string => !!b && !list.some((o) => o.deger === b),
    );
    return [...list, ...[...new Set(extra)].map((b) => ({ deger: b, etiket: b }))];
  });

  protected readonly form = new FormGroup({
    personel: new FormControl<SecimSecenegi | null>(null, Validators.required),
    tarih: new FormControl<GunMetni | null>(null, Validators.required),
    baslangicSaat: new FormControl<string | null>(null, [
      Validators.required,
      Validators.pattern(TIME_PATTERN),
    ]),
    bitisSaat: new FormControl<string | null>(null, [
      Validators.required,
      Validators.pattern(TIME_PATTERN),
    ]),
    sube: new FormControl<string | null>(null, Validators.maxLength(128)),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  protected readonly submission = formGonderimi();

  constructor() {
    // Pencere değişince temiz yeni-kayıt formu varsayılan günü izler (kirli forma dokunulmaz).
    effect(() => {
      const day = this.firstDay();
      const own = this.ownBranch();
      untracked(() => {
        if (this.editing().kind === 'new' && !this.form.dirty)
          this.form.reset(emptyShift(day, own));
      });
    });
    sayfaTerkKorumasi(() => this.form.dirty);
    this.loadBranches();
  }

  protected day(value: string): string {
    return tarihBicimle(value);
  }

  protected duration(minutes: number | string): string {
    const m = Number(minutes);
    if (!Number.isFinite(m) || m <= 0) return '—';
    return this.t('rapor.vardiya.sure', {
      saat: Math.floor(m / 60),
      dakika: String(m % 60).padStart(2, '0'),
    });
  }

  protected async edit(row: ShiftListRow): Promise<void> {
    if (!(await this.releaseForm())) return;
    this.editing.set({ kind: 'record', id: row.id });
    this.base.set(null);
    this.filledFrom = listRowToForm(row);
    this.form.reset({ ...this.filledFrom });
    this.submission.kilit.yenile();
    this.readRecord(row.id);
    afterNextRender(
      () => this.host.nativeElement.querySelector<HTMLElement>('#rc-vardiya-formu')?.focus(),
      { injector: this.injector },
    );
  }

  protected async cancelEdit(): Promise<void> {
    if (!(await this.releaseForm())) return;
    this.resetForm();
  }

  protected save(): void {
    const e = this.editing();
    const base = this.base();
    if (e.kind === 'record' && base === null) return;
    const body = shiftRequest(this.form.getRawValue() as ShiftFormValue, base);
    this.submission.gonder(
      this.form,
      (key) =>
        e.kind === 'new'
          ? this.api.post<Shift>(SHIFTS, body, { islemAnahtari: key })
          : this.api.put<Shift>(shiftPath(e.id), body, { islemAnahtari: key }),
      {
        esleme: { personelId: 'personel' },
        basarili: () => {
          this.toast.basari(
            this.t(e.kind === 'new' ? 'rapor.vardiya.eklendi' : 'rapor.vardiya.kaydedildi'),
          );
          this.resetForm();
          this.changed.emit();
        },
        hata: (h) => {
          if (h.kod === 'cakisma' && e.kind === 'record') this.readRecord(e.id);
        },
      },
    );
  }

  protected async remove(row: ShiftListRow): Promise<void> {
    if (this.busy() !== null) return;
    const yes = await this.confirm.sor({
      baslik: this.t('rapor.vardiya.silBaslik'),
      mesaj: this.t('rapor.vardiya.silMesaj'),
      onayEtiketi: this.t('rapor.vardiya.sil'),
      tehlikeli: true,
    });
    if (!yes || this.busy() !== null) return;
    this.busy.set(row.id);
    this.api
      .delete<unknown>(shiftPath(row.id))
      .pipe(
        finalize(() => this.busy.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.toast.basari(this.t('rapor.vardiya.silindi'));
          const e = this.editing();
          if (e.kind === 'record' && e.id === row.id) this.resetForm();
          this.changed.emit();
        },
        error: (raw: unknown) => {
          const error = apiHatasinaCevir(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
          this.changed.emit();
        },
      });
  }

  private readRecord(id: string): void {
    this.api
      .get<Shift>(shiftPath(id))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (s) => {
          const e = this.editing();
          if (e.kind === 'record' && e.id === id) this.recordArrived(s);
        },
        error: (raw: unknown) => {
          const error = apiHatasinaCevir(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
        },
      });
  }

  /** Güncel kayıt: temiz form sıfırlanır; kirli formda dokunulan alan korunur, çakışan işaretlenir. */
  private recordArrived(s: Shift): void {
    const fresh = shiftToForm(s);
    const previous = this.base();
    const baseline = (previous === null ? this.filledFrom : shiftToForm(previous)) ?? fresh;
    if (!this.form.dirty) {
      this.form.reset({ ...fresh });
    } else {
      const conflicts = sunucuDegerleriniBirlestir(
        this.form,
        { ...fresh },
        { ...baseline },
        this.t('rapor.vardiya.cakismaAlan'),
      );
      if (conflicts.length > 0)
        this.banner.goster({
          tur: 'uyari',
          mesaj: this.t('rapor.vardiya.cakismaBant', { sayi: conflicts.length }),
          kod: 'cakisma',
        });
    }
    this.base.set(s);
  }

  private resetForm(): void {
    this.editing.set({ kind: 'new' });
    this.base.set(null);
    this.filledFrom = null;
    this.form.reset(emptyShift(this.firstDay(), this.ownBranch()));
    this.submission.kilit.yenile();
  }

  private async releaseForm(): Promise<boolean> {
    if (!this.form.dirty) return true;
    return this.confirm.sor({
      baslik: this.t('rapor.vardiya.vazgecBaslik'),
      mesaj: this.t('rapor.vardiya.vazgecMesaj'),
    });
  }

  private loadBranches(): void {
    this.api
      .get<readonly { id: string; etiket: string }[]>('/api/ui/v1/secim/sube', {
        parametreler: { limit: 20 },
        context: istekBaglami({ sessiz: true }),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (items) =>
          this.branches.set(items.map((i) => ({ deger: i.etiket, etiket: i.etiket }))),
        error: () => undefined,
      });
  }
}
