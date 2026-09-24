import { DestroyRef, ElementRef, Injector, afterNextRender, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { FormGroup } from '@angular/forms';
import { finalize } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi, type ApiYolu } from '@core/api/api-istemcisi';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { UyariBandiServisi } from '@core/geri-bildirim/uyari-bandi-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { sunucuDegerleriniBirlestir } from '@features/planlama-ortak/form-yardimcilari';
import type { GonderimKilidi } from '@core/form/gonderim-kilidi';

import { recordPath } from './crm-model';

export type Editing = { readonly kind: 'new' } | { readonly kind: 'record'; readonly id: string };

export interface RecordEditorConfig<TRow, TCard> {
  readonly path: ApiYolu;
  readonly form: FormGroup;
  /** Düzenleme formunun kabı (odak buraya taşınır). */
  readonly anchor: string;
  readonly lock: GonderimKilidi;
  readonly empty: () => object;
  readonly toForm: (row: TRow) => object;
  readonly rowOf: (card: TCard) => TRow;
  /** Liste yenileme (silme, kaydetme sonrası). */
  readonly reload: () => void;
  /** Kart geldiğinde ek iş (ör. anket cevap satırları). `fresh` = temiz form doldurması. */
  readonly cardArrived?: (card: TCard, fresh: boolean) => void;
  readonly reset?: () => void;
}

/**
 * CRM listelerinin ortak düzenleyicisi (F6.2b filo plan deseni): satırdaki "Düzenle" formu doldurur ve kaydı TEKİL
 * uçtan okur (`surum` + güncel alanlar); ilk okuma dönene kadar taban liste satırıdır (dokunulmayan alan TAZE değeri
 * alır). 409 `cakisma` → güncel kayıt KİRLİ forma birleşir: dokunulan korunur, ikisi de değiştiyse işaretlenir (form
 * silinmez). Silme onaylı. Enjeksiyon bağlamında oluşturulur.
 */
export class RecordEditor<TRow, TCard extends { readonly surum?: string | null }> {
  private readonly api = inject(ApiIstemcisi);
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly banner = inject(UyariBandiServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly t = ceviriFonksiyonu();

  readonly editing = signal<Editing>({ kind: 'new' });
  readonly base = signal<TCard | null>(null);
  readonly busy = signal<string | null>(null);
  private filledFrom: object | null = null;

  constructor(private readonly c: RecordEditorConfig<TRow, TCard>) {}

  /** PUT'un hedefi; yeni kayıtta `null`. Kayıt okunmadan (sürüm yok) kaydetme kapalıdır. */
  get recordId(): string | null {
    const e = this.editing();
    return e.kind === 'record' ? e.id : null;
  }

  get waiting(): boolean {
    return this.editing().kind === 'record' && this.base() === null;
  }

  async edit(row: TRow, id: string): Promise<void> {
    if (!(await this.release())) return;
    this.editing.set({ kind: 'record', id });
    this.base.set(null);
    this.filledFrom = this.c.toForm(row);
    this.c.form.reset({ ...this.filledFrom });
    this.c.lock.yenile();
    this.read(id);
    afterNextRender(
      () => this.host.nativeElement.querySelector<HTMLElement>(`#${this.c.anchor}`)?.focus(),
      { injector: this.injector },
    );
  }

  async cancel(): Promise<void> {
    if (!(await this.release())) return;
    this.reset();
  }

  /** Kayıt başarıyla yazıldı: form sıfırlanır, liste yenilenir. */
  saved(): void {
    this.reset();
    this.c.reload();
  }

  /** Kaydetme hatası: bayat sürüm → güncel kaydı oku ve birleştir. */
  failed(kod: string): void {
    const id = this.recordId;
    if (kod === 'cakisma' && id !== null) this.read(id);
  }

  async remove(id: string, title: string, message: string, done: string): Promise<void> {
    if (this.busy() !== null) return;
    const yes = await this.confirm.sor({
      baslik: title,
      mesaj: message,
      onayEtiketi: this.t('crm.sil'),
      tehlikeli: true,
    });
    if (!yes || this.busy() !== null) return;
    this.busy.set(id);
    this.api
      .delete<unknown>(recordPath(this.c.path, id))
      .pipe(
        finalize(() => this.busy.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.toast.basari(done);
          if (this.recordId === id) this.reset();
          this.c.reload();
        },
        error: (raw: unknown) => {
          const error = apiHatasinaCevir(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
          this.c.reload();
        },
      });
  }

  private read(id: string): void {
    this.api
      .get<TCard>(recordPath(this.c.path, id))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (card) => {
          if (this.recordId === id) this.arrived(card);
        },
        error: (raw: unknown) => {
          const error = apiHatasinaCevir(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
        },
      });
  }

  private arrived(card: TCard): void {
    const fresh = this.c.toForm(this.c.rowOf(card)) as Record<string, unknown>;
    const previous = this.base();
    const baseline =
      (previous === null ? this.filledFrom : this.c.toForm(this.c.rowOf(previous))) ?? fresh;
    const clean = !this.c.form.dirty;
    if (clean) {
      this.c.form.reset({ ...fresh });
    } else {
      const conflicts = sunucuDegerleriniBirlestir(
        this.c.form,
        fresh,
        baseline as Record<string, unknown>,
        this.t('crm.cakismaAlan'),
      );
      if (conflicts.length > 0)
        this.banner.goster({
          tur: 'uyari',
          mesaj: this.t('crm.cakismaBant', { sayi: conflicts.length }),
          kod: 'cakisma',
        });
    }
    this.base.set(card);
    this.c.cardArrived?.(card, clean);
  }

  private reset(): void {
    this.editing.set({ kind: 'new' });
    this.base.set(null);
    this.filledFrom = null;
    this.c.form.reset({ ...this.c.empty() });
    this.c.lock.yenile();
    this.c.reset?.();
  }

  private async release(): Promise<boolean> {
    if (!this.c.form.dirty) return true;
    return this.confirm.sor({
      baslik: this.t('crm.vazgecBaslik'),
      mesaj: this.t('crm.vazgecMesaj'),
    });
  }
}
