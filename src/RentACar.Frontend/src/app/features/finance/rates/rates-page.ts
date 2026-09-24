import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, Validators } from '@angular/forms';
import { type Observable, finalize } from 'rxjs';

import { apiHatasinaCevir, type ApiHatasi } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { paraBicimle } from '@core/bicim/bicim';
import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import { sunucuHatalariniTemizle, sunucuHatalariniUygula } from '@core/form/sunucu-hatalari';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { TemelStore } from '@core/veri/temel-store';
import { sunucuDegerleriniBirlestir } from '@features/planlama-ortak/form-yardimcilari';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';

import {
  type ConversionResult,
  type FixedRate,
  type RatesRefreshResult,
  type RatesScreen,
  financePath,
} from '../finance-model';
import { ConfirmGate, FIN_COMMON, toAmount } from '../finance-shared';
import {
  type FixedRateFormValue,
  asRecord,
  fixedRateToForm,
  fixedRateUpdateBody,
} from './rates-model';

/**
 * Döviz Kurları (`/app/kurlar`, Blazor `Kurlar.razor`): TCMB günlük kurları (paylaşımlı tablo; "TCMB'den Güncelle"),
 * mini çevirici (sunucu `kurlar/cevir`; bilgi amaçlı), firma sabit kurları (para çözümünü etkiler → FinanceWrite).
 * Sabit kur DÜZENLEME tam değiştirmedir: zorunlu `surum`, 409 `cakisma` formu SİLMEZ — güncel kayıt kirli forma
 * birleşir (dokunulmayan alan sunucu değerine çekilir; taban formun doldurulduğu değerdir, #291 M1). Silme onaylı.
 */
@Component({
  selector: 'rc-rates-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...FIN_COMMON],
  providers: [FetchPolicy],
  templateUrl: './rates-page.html',
  styleUrl: '../finance.scss',
})
export class RatesPage implements KaydedilmemisDegisiklikSahibi {
  private readonly api = inject(ApiIstemcisi);
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();
  private readonly session = inject(OturumServisi);
  protected readonly canWrite = computed(() => this.session.izinVar('FinanceWrite'));

  protected readonly screen = new TemelStore(
    () => this.api.get<RatesScreen>(financePath('/kurlar')),
    { oncekiVeriyiKoru: true },
  );
  protected readonly codeOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    (this.screen.veri()?.dovizKodlari ?? []).map((c) => ({ deger: c, etiket: c })),
  );
  protected readonly foreignOptions = computed(() =>
    this.codeOptions().filter((o) => o.deger !== 'TRY'),
  );
  protected readonly rateDate = computed(() => this.screen.veri()?.tcmb[0]?.tarih ?? null);
  protected readonly fixedActive = (kod: string) =>
    this.screen.veri()?.sabitler.find((s) => s.kod === kod && s.aktif) ?? null;

  protected readonly busy = signal(false);
  private readonly gate = new ConfirmGate();
  protected readonly errors = signal<readonly string[]>([]);
  protected readonly conversion = signal<string | null>(null);
  protected readonly converter = new FormGroup({
    tutar: new FormControl<string | null>(null, Validators.required),
    kaynak: new FormControl<string | null>('USD', Validators.required),
    hedef: new FormControl<string | null>('TRY', Validators.required),
  });

  /** Düzenlenen sabit kur (`null` = yeni). Taban: formun doldurulduğu değer (birleştirme için). */
  protected readonly editing = signal<FixedRate | null>(null);
  private filledFrom: FixedRateFormValue | null = null;
  protected readonly form = new FormGroup({
    kod: new FormControl<string | null>(null, Validators.required),
    kur: new FormControl<number | null>(null, [Validators.required, Validators.min(0.000001)]),
    basTar: new FormControl<string | null>(null),
    bitTar: new FormControl<string | null>(null),
    aktif: new FormControl<boolean | null>(true),
  });

  constructor() {
    const p = inject(FetchPolicy);
    p.baglan({ parametre: computed(() => 0), yukle: () => this.screen.yukle() });
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.form.dirty;
  }

  protected convert(): void {
    this.converter.markAllAsTouched();
    sunucuHatalariniTemizle(this.converter);
    if (this.converter.invalid) return;
    const v = this.converter.getRawValue();
    this.api
      .get<ConversionResult>(financePath('/kurlar/cevir'), {
        parametreler: { tutar: v.tutar, kaynak: v.kaynak, hedef: v.hedef },
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) =>
          this.conversion.set(
            `${paraBicimle(toAmount(r.tutar), r.kaynak)} = ${paraBicimle(toAmount(r.sonuc), r.hedef)}`,
          ),
        error: (raw: unknown) => {
          this.conversion.set(null);
          this.showError(this.converter, apiHatasinaCevir(raw));
        },
      });
  }

  protected refresh(): void {
    this.call(this.api.post<RatesRefreshResult>(financePath('/kurlar/yenile'), null), (r) => {
      if (r.durum === 'basarisiz') this.toast.uyari(this.t('finans.kur.yenileBasarisiz'));
      else if (r.durum === 'guncel') this.toast.bilgi(this.t('finans.kur.zatenGuncel'));
      else this.toast.basari(this.t('finans.kur.guncellendi', { adet: r.adet }));
    });
  }

  protected edit(row: FixedRate): void {
    this.editing.set(row);
    this.filledFrom = fixedRateToForm(row);
    this.form.reset(this.filledFrom);
    this.form.controls.kod.disable();
  }

  protected newRate(): void {
    this.editing.set(null);
    this.filledFrom = null;
    this.form.reset({ aktif: true });
    this.form.controls.kod.enable();
  }

  protected save(): void {
    sunucuHatalariniTemizle(this.form);
    this.errors.set([]);
    this.form.markAllAsTouched();
    if (this.form.invalid || this.busy()) return;
    const v = this.form.getRawValue() as FixedRateFormValue;
    const row = this.editing();
    const req = row
      ? this.api.put<null>(
          financePath(`/kurlar/sabit/${encodeURIComponent(row.id)}`),
          fixedRateUpdateBody(v, row.surum),
        )
      : this.api.post<{ id: string }>(financePath('/kurlar/sabit'), {
          kod: v.kod,
          kur: v.kur,
          basTar: v.basTar,
          bitTar: v.bitTar,
          aktif: v.aktif === true,
        });
    this.call(
      req,
      () => {
        this.toast.basari(this.t('finans.kur.sabitKaydedildi'));
        this.form.markAsPristine();
        this.newRate();
      },
      (e) => (e.kod === 'cakisma' && row ? this.mergeLatest(row.id) : undefined),
    );
  }

  protected async remove(row: FixedRate): Promise<void> {
    if (this.busy()) return;
    const yes = await this.gate.ask(() =>
      this.confirm.sor({
        baslik: this.t('finans.kur.silBaslik'),
        mesaj: this.t('finans.kur.silMesaj', { kod: row.kod }),
        onayEtiketi: this.t('finans.kur.sil'),
        tehlikeli: true,
      }),
    );
    if (!yes) return;
    this.call(
      this.api.delete<null>(financePath(`/kurlar/sabit/${encodeURIComponent(row.id)}`)),
      () => {
        this.toast.basari(this.t('finans.kur.silindi'));
        if (this.editing()?.id === row.id) this.newRate();
      },
    );
  }

  /** 409 `cakisma`: güncel kayıt okunur, KİRLİ forma birleştirilir; sonraki PUT yeni `surum`la gider. */
  private mergeLatest(id: string): void {
    this.api
      .get<RatesScreen>(financePath('/kurlar'))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((s) => {
        const latest = s.sabitler.find((x) => x.id === id);
        if (!latest || this.editing()?.id !== id) return;
        const fresh = fixedRateToForm(latest);
        sunucuDegerleriniBirlestir(
          this.form,
          asRecord(fresh),
          asRecord(this.filledFrom ?? fresh),
          this.t('finans.kur.cakisti'),
        );
        this.filledFrom = fresh;
        this.editing.set(latest);
      });
  }

  private call<T>(req: Observable<T>, ok: (r: T) => void, failed?: (e: ApiHatasi) => void): void {
    if (this.busy()) return;
    this.busy.set(true);
    req
      .pipe(
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (r) => {
          ok(r);
          this.screen.yenile();
        },
        error: (raw: unknown) => {
          const e = apiHatasinaCevir(raw);
          this.showError(this.form, e);
          failed?.(e);
          if (e.kod !== 'cakisma') this.screen.yenile();
        },
      });
  }

  private showError(form: FormGroup, e: ApiHatasi): void {
    const rest = sunucuHatalariniUygula(form, e.alanlar);
    if (e.alanlar === undefined && !genelGosterilir(e)) this.errors.set([e.detay]);
    else if (rest.length > 0) this.errors.set(rest);
  }
}
