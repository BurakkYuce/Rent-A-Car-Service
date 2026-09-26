import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, FormRecord, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';

import { moneySubmission } from '@core/form/money-submission';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { formatMoney } from '@core/bicim/bicim';
import { type UnsavedChangesOwner, pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { TemelStore } from '@core/veri/temel-store';
import {
  type SecimSecenegi,
  serverSelectionSource,
} from '@shared/form/arama-secim/selection-source';

import {
  type AccountKind,
  type CloseItemsRequest,
  type CloseItemsResult,
  type CustomerOpenItem,
  type CustomerOpenItems,
  closeItemsBody,
  customerPath,
} from '../finance-model';
import {
  CHANNEL_OPTIONS,
  FIN_COMMON,
  balanceSide,
  followCustomerQuery,
  kindOptions,
  labelFromData,
  toAmount,
} from '../finance-shared';

type ItemRow = FormGroup<{
  secili: FormControl<boolean | null>;
  tutar: FormControl<string | null>;
}>;

/**
 * Tek Cari — Toplu Kapatma (`/app/tek-cari-toplu`, Blazor `TekCariToplu.razor`): carinin BORÇ kalemleri (sunucu:
 * baz, kapanan, kalan, kapalı) işaretlenip tek tahsilatla kapatılır — `POST finans/cariler/{id}/toplu-kapat` (E05).
 * Tutar YAZILMAZ; kalem başına boş = kalanın tamamı, kısmi tutar yazılabilir. Tahsis KALICIDIR (ters kayıt tahsisi
 * kaldırmaz) — onay metni bunu söyler. Satır formları kalem kimliğiyle kurulur (`@for … track id`); gönderilen seçim
 * donmuş kopyadadır (tekrar AYNI seçimle).
 */
@Component({
  selector: 'rc-single-customer-close',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...FIN_COMMON],
  providers: [FetchPolicy],
  templateUrl: './single-customer-close.html',
  styleUrl: '../finance.scss',
})
export class SingleCustomerClose implements UnsavedChangesOwner {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly t = translationFunction();
  protected readonly customers = serverSelectionSource('musteri');
  protected readonly customer = new FormControl<SecimSecenegi | null>(null);
  protected readonly customerId = signal<string | null>(
    inject(ActivatedRoute).snapshot.queryParamMap.get('cariId'),
  );
  protected readonly kindOptions = kindOptions(this.t);
  protected readonly channelOptions = CHANNEL_OPTIONS;
  protected readonly action = moneySubmission<CloseItemsRequest>({
    scope: () => `tek-cari:${this.customerId() ?? ''}`,
  });

  protected readonly items = new TemelStore(
    (id: string) => this.api.get<CustomerOpenItems>(customerPath(id, '/acik-kalemler')),
    { oncekiVeriyiKoru: true },
  );
  protected readonly side = computed(() => balanceSide(this.items.veri()?.bakiye));
  protected readonly openCount = computed(
    () => (this.items.veri()?.kalemler ?? []).filter((k) => !k.kapali).length,
  );

  protected readonly form = new FormGroup({
    rows: new FormRecord<ItemRow>({}),
    hesap: new FormControl<AccountKind | null>('Kasa', Validators.required),
    kanal: new FormControl<string | null>('Masaüstü'),
    tarih: new FormControl<string | null>(null),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  protected readonly noSelection = signal(false);
  protected readonly isZero = (v: number | string) => (toAmount(v) ?? 0) === 0;

  constructor() {
    pageLeaveGuard(() => this.hasUnsavedChanges());
    // Sonucu bilinmeyen kapatma (sayfa kapanıp açıldıysa) AYNI seçimle + anahtarla KİLİTLİ geri gelir.
    this.action.restore(this.form, (value) => {
      const v = value as ReturnType<typeof this.form.getRawValue>;
      const rows = this.form.controls.rows;
      for (const id of Object.keys(v.rows))
        if (!rows.controls[id])
          rows.addControl(
            id,
            new FormGroup({
              secili: new FormControl<boolean | null>(false),
              tutar: new FormControl<string | null>(null),
            }),
            { emitEvent: false },
          );
      this.form.reset(v, { emitEvent: false });
    });
    this.customer.valueChanges.pipe(takeUntilDestroyed()).subscribe((c) => {
      if (c && c.id !== this.customerId()) this.customerId.set(c.id);
    });
    effect(() => {
      const locked = this.action.pending();
      untracked(() => (locked ? this.customer.disable() : this.customer.enable()));
    });
    // Kalemler gelince satır formları kalem kimliğiyle kurulur; açık kalemin kullanıcı seçimi korunur, kapanan düşer.
    effect(() => {
      const data = this.items.veri();
      const id = this.customerId();
      untracked(() => {
        labelFromData(this.customer, id, data);
        if (!this.action.pending()) this.buildRows(data?.cariId === id ? data.kalemler : []);
      });
    });
    followCustomerQuery(this.customerId, this.customer, () => this.action.pending(), {
      dirty: () => this.form.dirty,
      // Seçimler ve kısmi tutarlar önceki carinin kalemlerine aittir: yeni cariye taşınmaz.
      discard: () => this.form.reset({ hesap: 'Kasa', kanal: 'Masaüstü' }),
    });
    inject(FetchPolicy).connect({
      parametre: this.customerId.asReadonly(),
      yukle: (id) => (id ? this.items.yukle(id) : this.items.reset()),
      sifirla: () => this.items.reset(),
    });
  }

  hasUnsavedChanges(): boolean {
    return this.action.pending() || this.form.dirty;
  }

  unsavedChangesMessage(): string | null {
    return this.action.pending() ? this.t('finans.islem.terkMesaji') : null;
  }

  protected row(id: string): ItemRow | null {
    return this.form.controls.rows.controls[id] ?? null;
  }

  protected submit(): void {
    const id = this.customerId();
    const name = this.items.veri()?.cariAd ?? '';
    // Kalemler hâlâ önceki cariye aitse (yeni cari yükleniyor) gönderim yok: kalem kimlikleri başka cariye gitmesin.
    if (!id || (this.action.frozen() === null && this.items.veri()?.cariId !== id)) return;
    this.noSelection.set(false);
    void this.action.run<CloseItemsResult>({
      form: this.form,
      build: () => {
        const rows = this.form.controls.rows.controls;
        const selection = Object.entries(rows)
          .filter(([, r]) => r.controls.secili.value === true)
          .map(([itemId, r]) => ({ kalemId: itemId, tutar: r.controls.tutar.value }));
        if (selection.length === 0) {
          this.noSelection.set(true);
          return null;
        }
        const body = closeItemsBody(selection, this.form.getRawValue());
        return {
          path: customerPath(id, '/toplu-kapat'),
          body,
          content: { tutar: null, doviz: 'TRY' },
        };
      },
      confirm: () =>
        this.confirm.ask({
          baslik: this.t('finans.tekCari.onayBaslik'),
          mesaj: this.t('finans.tekCari.onayMesaj', { ad: name }),
        }),
      success: (r) => {
        this.toast.basari(
          this.t('finans.tekCari.kapatildi', {
            no: r.belgeNo,
            tutar: formatMoney(toAmount(r.tutar)),
          }),
        );
        this.form.controls.rows.markAsPristine();
      },
      settled: () => this.items.yenile(),
    });
  }

  private buildRows(items: readonly CustomerOpenItem[]): void {
    const rows = this.form.controls.rows;
    const keep = new Map(
      Object.entries(rows.controls).map(([k, r]) => [k, r.getRawValue()] as const),
    );
    for (const k of Object.keys(rows.controls)) rows.removeControl(k, { emitEvent: false });
    for (const item of items) {
      if (item.kapali) continue;
      const old = rows.dirty ? keep.get(item.id) : undefined;
      rows.addControl(
        item.id,
        new FormGroup({
          secili: new FormControl<boolean | null>(old?.secili ?? false),
          tutar: new FormControl<string | null>(old?.tutar ?? null),
        }),
        { emitEvent: false },
      );
    }
  }
}
