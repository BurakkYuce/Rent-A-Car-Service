import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { Sema } from '@core/api/ui-tipleri';
import { sayfaTerkKorumasi } from '@core/form/kaydedilmemis-degisiklik';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { Alan } from '@shared/form/alan/alan';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { OnayKutusu } from '@shared/form/kontroller/onay-kutusu';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';

import { mergeServerValues } from '../shared/version-merge';
import { LISTINGS_ROOT } from './website-hub';

type ListingDetail = Sema<'ListingDetailDto'>;
type PriceResult = Sema<'ListingPriceResultDto'>;

function priceValues(d: ListingDetail): Record<string, unknown> {
  return {
    gunlukFiyat: +d.gunlukFiyat > 0 ? d.gunlukFiyat : null,
    haftalikToplam: d.haftalikToplam ?? null,
    aylikToplam: d.aylikToplam ?? null,
    kdvDahil: d.kdvDahil,
  };
}

/**
 * F11.2b ilan sihirbazı adım 2/3 (Blazor `IlanFiyat`): günlük fiyat + haftalık/aylık TOPLAM (günlük değil) + KDV dahil.
 * Tam değiştirme PUT'u (`surum` zorunlu); 409 `cakisma` formu silmez. Aynı taslaktan açılmış kardeş ilanlara fiyat
 * sunucuda kopyalanır (adedi bildirilir).
 */
@Component({
  selector: 'rc-listing-price',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    FormHatalari,
    OnayKutusu,
    ParaGirdisi,
  ],
  styleUrl: '../system.scss',
  templateUrl: './listing-price.html',
})
export class ListingPrice {
  private readonly api = inject(ApiIstemcisi);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly session = inject(OturumServisi);
  private readonly t = ceviriFonksiyonu();
  protected readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';

  protected readonly module = computed(() => this.session.ben()?.moduller.webSitesi === true);
  protected readonly detail = signal<ListingDetail | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly form = new FormGroup({
    gunlukFiyat: new FormControl<string | number | null>(null, Validators.required),
    haftalikToplam: new FormControl<string | number | null>(null),
    aylikToplam: new FormControl<string | number | null>(null),
    kdvDahil: new FormControl<boolean | null>(true),
  });
  protected readonly submit = formGonderimi();

  constructor() {
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
    if (this.module()) this.load();
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.form.dirty;
  }

  protected load(): void {
    this.loadError.set(null);
    this.fetch().subscribe({
      next: (d) => {
        this.form.reset(priceValues(d));
        this.detail.set(d);
      },
      error: (e: unknown) => this.loadError.set(apiHatasinaCevir(e).detay),
    });
  }

  protected save(): void {
    const d = this.detail();
    if (d === null) return;
    const v = this.form.getRawValue();
    const body = { ...v, kdvDahil: v.kdvDahil === true, surum: d.surum ?? null };
    this.submit.gonder(
      this.form,
      (key) =>
        this.api.put<PriceResult>(
          `${LISTINGS_ROOT}/ilanlar/${encodeURIComponent(this.id)}/fiyat`,
          body,
          {
            islemAnahtari: key,
          },
        ),
      {
        basarili: (r) => {
          this.detail.set(r.ilan);
          this.form.reset(priceValues(r.ilan));
          this.toast.basari(
            +r.kardesKopyalanan > 0
              ? this.t('sistem.web.fiyatSayfa.kopyalandi', { adet: String(r.kardesKopyalanan) })
              : this.t('sistem.ortak.kaydedildi'),
          );
          void this.router.navigate(['/web-sitesi/ilan', this.id, 'ozellikler']);
        },
        hata: (h) => {
          if (h.kod === 'cakisma') {
            this.fetch().subscribe({
              next: (fresh) => {
                mergeServerValues(
                  this.form,
                  priceValues(d),
                  priceValues(fresh),
                  this.t('form.tanim.cakismaAlan'),
                );
                this.detail.set(fresh);
              },
              error: () => undefined,
            });
          }
        },
      },
    );
  }

  private fetch() {
    return this.api
      .get<ListingDetail>(`${LISTINGS_ROOT}/ilanlar/${encodeURIComponent(this.id)}`)
      .pipe(takeUntilDestroyed(this.destroyRef));
  }
}
