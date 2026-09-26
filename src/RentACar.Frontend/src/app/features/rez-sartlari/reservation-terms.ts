import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  effect,
  inject,
  Injector,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { tarihBicimle } from '@core/bicim/bicim';
import type { UnsavedChangesOwner } from '@core/form/kaydedilmemis-degisiklik';
import { pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { bugun } from '@core/form/tarih-girdisi';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { WarningBannerService } from '@core/geri-bildirim/warning-banner-service';
import { translationFunction } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/session-interceptor';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listQueryUrlSync } from '@core/veri/liste-sorgusu-url';
import { Alan } from '@shared/form/alan/alan';
import { SearchSelection } from '@shared/form/arama-secim/search-selection';
import {
  type SecimSecenegi,
  serverSelectionSource,
} from '@shared/form/arama-secim/selection-source';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { TextInput } from '@shared/form/kontroller/text-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { DatePicker } from '@shared/form/tarih/date-picker';
import { Icon } from '@shared/ikon/icon';
import { Table } from '@shared/tablo/table';
import { TableCell } from '@shared/tablo/table-cell';

import { mergeServerValues } from '@features/planlama-ortak/form-yardimcilari';

import { PageBand } from '../../kabuk/sayfa-bandi/page-band';
import {
  RESERVATION_TERM_STATUSES,
  RESERVATION_TERM_LIST,
  type ReservationTerm,
  type ReservationTermStatus,
  type RezSartFormDegeri,
  pendingParams,
  valuesFromRecord,
  reservationTermBody,
  newValues,
} from './rez-sart-modeli';
import { reservationTermColumns } from './reservation-term-columns';
import { ReservationTermsStore } from './reservation-terms.store';

const ROOT = '/api/ui/v1/rez-sartlari';
const jsonEqual = (a: unknown, b: unknown) => JSON.stringify(a) === JSON.stringify(b);

type Edit = { readonly tur: 'yeni' } | { readonly tur: 'kayit'; readonly id: string };

/**
 * Rez şartları (müşteri özel talepleri, `/app/rez-sartlari`) — Blazor `RezSartList.razor` paritesi:
 * oluştur (talep tarihi bugün), süzgeç (müşteri/durum/talep günü aralığı), "N kayıt · N bekleyen",
 * satırda Karşılandı / Geri al / Düzenle / Sil (onaylı). Operasyonel not defteridir: para ve defter yok.
 *
 * Düzenleme tam değiştirmedir (`PUT` + `surum`); bayat sürüm 409 `cakisma` → güncel kayıt okunur ve
 * KİRLİ forma birleştirilir (dokunulmayan alan güncellenir, dokunulan korunur, ikisi de değiştiyse işaret)
 * — form silinmez, kullanıcı kontrol edip yeniden kaydeder.
 */
@Component({
  selector: 'rc-rez-sartlari',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PageBand,
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    SearchSelection,
    FormErrors,
    Icon,
    TextInput,
    Selection,
    Table,
    TableCell,
    DatePicker,
  ],
  providers: [FetchPolicy, ReservationTermsStore],
  templateUrl: './reservation-terms.html',
  styleUrl: './reservation-terms.scss',
})
export class ReservationTerms implements UnsavedChangesOwner {
  protected readonly store = inject(ReservationTermsStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly approval = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly bant = inject(WarningBannerService);
  private readonly teardown = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly t = translationFunction();

  protected readonly liste = listQueryUrlSync(RESERVATION_TERM_LIST);
  protected readonly columns = reservationTermColumns(this.t);
  protected readonly identity = (r: ReservationTerm) => r.id;
  protected readonly customers = serverSelectionSource('musteri');

  // ---- süzgeç
  protected readonly filterForm = new FormGroup({
    musteri: new FormControl<SecimSecenegi | null>(null),
    durum: new FormControl<ReservationTermStatus | null>(null),
    bas: new FormControl<string | null>(null),
    bit: new FormControl<string | null>(null),
  });
  protected readonly statusOptions: readonly SecenekOgesi<ReservationTermStatus>[] =
    RESERVATION_TERM_STATUSES.map((d) => ({
      deger: d,
      etiket: this.t(`rezSartlari.durumlar.${d}`),
    }));
  /** Seçilen müşterinin etiketi (URL'de yalnız kimlik durur). Yalnız bellekte. */
  private readonly customerLabels = new Map<string, string>();

  protected readonly ozet = computed(() => {
    const l = this.store.liste.veri();
    if (!l) return null;
    const pending =
      this.liste.sorgu().filtreler.durum === 'karsilanan' ? 0 : this.store.pending.veri()?.toplam;
    return pending === undefined
      ? this.t('rezSartlari.ozetKayit', { toplam: l.toplam })
      : this.t('rezSartlari.ozet', { toplam: l.toplam, bekleyen: pending });
  });

  // ---- oluştur / düzenle formu
  protected readonly editing = signal<Edit | null>(null);
  protected readonly taban = signal<ReservationTerm | null>(null);
  protected readonly detailLoading = signal(false);
  protected readonly form = new FormGroup({
    musteri: new FormControl<SecimSecenegi | null>(null, Validators.required),
    sart: new FormControl<string | null>(null, [Validators.required, Validators.maxLength(512)]),
    grup: new FormControl<string | null>(null, Validators.maxLength(64)),
    basTar: new FormControl<string | null>(null),
    bitTar: new FormControl<string | null>(null),
    talepTarihi: new FormControl<string | null>(null),
    karsilamaTarihi: new FormControl<string | null>(null),
    teslimEden: new FormControl<string | null>(null, Validators.maxLength(128)),
  });
  protected readonly submission = formSubmission();
  protected readonly busy = signal<string | null>(null);
  protected readonly groupSuggestions = computed(() => this.store.groups.veri() ?? []);

  constructor() {
    const policy = inject(FetchPolicy);
    policy.connect({
      parametre: this.liste.apiParametreleri,
      yukle: (p) => this.store.liste.yukle(p),
      sifirla: () => this.store.liste.reset(),
      sekmeyeDonunce: 'yenile',
    });
    policy.connect({
      parametre: computed(() => pendingParams(this.liste.apiParametreleri()), {
        equal: jsonEqual,
      }),
      yukle: (p) => (p === null ? this.store.pending.reset() : this.store.pending.yukle(p)),
      sifirla: () => this.store.pending.reset(),
      sekmeyeDonunce: 'yenile',
      esit: jsonEqual,
    });
    policy.connect({
      parametre: signal(0).asReadonly(),
      yukle: () => this.store.groups.yukle(),
      sifirla: () => this.store.groups.reset(),
    });
    // URL'deki müşteri kimliğinin etiketi (paylaşılan bağlantı / yenileme) sunucudan çözülür.
    policy.connect({
      parametre: computed(() => this.liste.sorgu().filtreler.musteriId ?? null),
      yukle: (id) => {
        if (id !== null && !this.customerLabels.has(id)) this.store.musteri.yukle(id);
      },
    });
    effect(() => {
      const f = this.liste.sorgu().filtreler;
      const resolved = this.store.musteri.veri();
      if (resolved) this.customerLabels.set(resolved.id, resolved.etiket);
      untracked(() =>
        this.filterForm.reset({
          musteri:
            f.musteriId === undefined
              ? null
              : {
                  id: f.musteriId,
                  etiket:
                    this.customerLabels.get(f.musteriId) ?? this.t('rezSartlari.seciliMusteri'),
                },
          durum: f.durum ?? null,
          bas: f.bas ?? null,
          bit: f.bit ?? null,
        }),
      );
    });
    pageLeaveGuard(() => this.hasUnsavedChanges());
  }

  hasUnsavedChanges(): boolean {
    return this.editing() !== null && this.form.dirty;
  }

  // ------------------------------------------------------------------ süzgeç

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    if (v.musteri) this.customerLabels.set(v.musteri.id, v.musteri.etiket);
    void this.liste.degistir({
      filtreler: {
        musteriId: v.musteri?.id ?? undefined,
        durum: v.durum ?? undefined,
        bas: v.bas ?? undefined,
        bit: v.bit ?? undefined,
      },
    });
  }

  protected clear(): void {
    void this.liste.sifirla();
  }

  // ------------------------------------------------------------------ form

  protected async yeni(): Promise<void> {
    if (!(await this.releaseOpenForm())) return;
    this.taban.set(null);
    this.fillForm(newValues(bugun()));
    this.editing.set({ tur: 'yeni' });
    this.focus();
  }

  protected async edit(row: ReservationTerm): Promise<void> {
    if (!(await this.releaseOpenForm())) return;
    this.editing.set({ tur: 'kayit', id: row.id });
    this.taban.set(null);
    this.fillForm(valuesFromRecord(row));
    this.readDetail(row.id, true);
    this.focus();
  }

  protected async cancel(): Promise<void> {
    if (this.form.dirty) {
      const yes = await this.approval.ask({
        baslik: this.t('rezSartlari.vazgecBaslik'),
        mesaj: this.t('rezSartlari.vazgecMesaj'),
      });
      if (!yes) return;
    }
    this.close();
  }

  protected kaydet(): void {
    const d = this.editing();
    if (d === null) return;
    const floor = this.taban();
    if (d.tur === 'kayit' && floor === null) return; // sürüm okunmadan tam değiştirme gönderilmez
    const body = reservationTermBody(this.form.getRawValue() as RezSartFormDegeri, floor);
    const mapping = { musteriId: 'musteri' };
    if (d.tur === 'yeni') {
      this.submission.gonder(
        this.form,
        (key) => this.api.post<ReservationTerm>(ROOT, body, { islemAnahtari: key }),
        {
          esleme: mapping,
          basarili: () => {
            this.toast.basari(this.t('rezSartlari.olusturuldu'));
            this.close();
            this.yenile();
          },
        },
      );
      return;
    }
    this.submission.gonder(
      this.form,
      (key) =>
        this.api.put<ReservationTerm>(`${ROOT}/${encodeURIComponent(d.id)}`, body, {
          islemAnahtari: key,
        }),
      {
        esleme: mapping,
        basarili: () => {
          this.toast.basari(this.t('rezSartlari.kaydedildi'));
          this.close();
          this.yenile();
        },
        // Bayat sürüm: güncel kayıt okunur, kirli forma birleştirilir (form SİLİNMEZ; yeniden gönderim yok).
        hata: (h) => {
          if (h.kod === 'cakisma') this.readDetail(d.id, false);
        },
      },
    );
  }

  // ------------------------------------------------------------------ satır işlemleri

  protected karsilandi(row: ReservationTerm): void {
    this.islem(
      row,
      `${ROOT}/${encodeURIComponent(row.id)}/karsilandi`,
      { teslimEden: null },
      'rezSartlari.karsilandiBildirim',
    );
  }

  protected undo(row: ReservationTerm): void {
    this.islem(
      row,
      `${ROOT}/${encodeURIComponent(row.id)}/geri-al`,
      null,
      'rezSartlari.geriAlindi',
    );
  }

  protected async remove(row: ReservationTerm): Promise<void> {
    if (this.busy() !== null) return;
    const yes = await this.approval.ask({
      baslik: this.t('rezSartlari.silBaslik'),
      mesaj: this.t('rezSartlari.silMesaj', { sart: row.sart }),
      onayEtiketi: this.t('rezSartlari.sil'),
      tehlikeli: true,
    });
    if (!yes || this.busy() !== null) return;
    this.busy.set(row.id);
    this.api
      .delete<unknown>(`${ROOT}/${encodeURIComponent(row.id)}`)
      .pipe(
        finalize(() => this.busy.set(null)),
        takeUntilDestroyed(this.teardown),
      )
      .subscribe({
        next: () => {
          this.toast.basari(this.t('rezSartlari.silindi'));
          const d = this.editing();
          if (d?.tur === 'kayit' && d.id === row.id) this.close();
          this.yenile();
        },
        error: (raw: unknown) => this.operationError(raw),
      });
  }

  private islem(
    row: ReservationTerm,
    path: `/api/ui/v1/${string}`,
    body: unknown,
    notification: 'rezSartlari.karsilandiBildirim' | 'rezSartlari.geriAlindi',
  ): void {
    if (this.busy() !== null) return;
    this.busy.set(row.id);
    this.api
      .post<ReservationTerm>(path, body)
      .pipe(
        finalize(() => this.busy.set(null)),
        takeUntilDestroyed(this.teardown),
      )
      .subscribe({
        next: (current) => {
          this.toast.basari(this.t(notification));
          // Açık düzenleme formu aynı kayıtsa yeni sürüm ve değerler birleştirilir (bayat PUT olmasın).
          const d = this.editing();
          if (d?.tur === 'kayit' && d.id === row.id) this.detailLoaded(current, false);
          this.yenile();
        },
        error: (raw: unknown) => this.operationError(raw),
      });
  }

  private operationError(raw: unknown): void {
    const error = toApiError(raw);
    if (!genelGosterilir(error)) this.toast.hata(error.detay);
    this.yenile();
  }

  // ------------------------------------------------------------------ yardımcılar

  protected statusText(row: ReservationTerm): string {
    return row.karsilamaTarihi
      ? this.t('rezSartlari.karsilandiTarih', { tarih: tarihBicimle(row.karsilamaTarihi) })
      : this.t('rezSartlari.bekliyor');
  }

  private readDetail(id: string, first: boolean): void {
    this.detailLoading.set(true);
    this.api
      .get<ReservationTerm>(`${ROOT}/${encodeURIComponent(id)}`)
      .pipe(
        finalize(() => this.detailLoading.set(false)),
        takeUntilDestroyed(this.teardown),
      )
      .subscribe({
        next: (s) => {
          const d = this.editing();
          if (d?.tur === 'kayit' && d.id === id) this.detailLoaded(s, first);
        },
        error: (raw: unknown) => {
          const error = toApiError(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
        },
      });
  }

  /** Güncel kayıt: temiz form sıfırlanır; kirli formda birleştirme (dokunulan alan korunur). */
  private detailLoaded(s: ReservationTerm, first: boolean): void {
    const newItem = valuesFromRecord(s);
    const old = this.taban();
    if (first || !this.form.dirty || old === null) {
      if (!this.form.dirty) this.fillForm(newItem);
    } else {
      const conflicting = mergeServerValues(
        this.form,
        { ...newItem },
        { ...valuesFromRecord(old) },
        this.t('rezSartlari.cakismaAlan'),
      );
      if (conflicting.length > 0) {
        this.bant.show({
          tur: 'uyari',
          mesaj: this.t('rezSartlari.cakismaBant', { sayi: conflicting.length }),
          kod: 'cakisma',
        });
      }
    }
    this.taban.set(s);
  }

  private fillForm(v: RezSartFormDegeri): void {
    this.form.reset({ ...v });
  }

  private close(): void {
    this.editing.set(null);
    this.taban.set(null);
    this.form.reset(newValues(bugun()));
  }

  /** Açık kirli form başka kayda geçmeden önce sorulur. */
  private async releaseOpenForm(): Promise<boolean> {
    if (this.editing() === null || !this.form.dirty) return true;
    return this.approval.ask({
      baslik: this.t('rezSartlari.vazgecBaslik'),
      mesaj: this.t('rezSartlari.vazgecMesaj'),
    });
  }

  private focus(): void {
    afterNextRender(
      () => this.element.nativeElement.querySelector<HTMLElement>('.duzenleyici h2')?.focus(),
      { injector: this.injector },
    );
  }

  private yenile(): void {
    this.store.liste.yenile();
    this.store.pending.yenile();
    this.store.groups.yenile();
  }
}
