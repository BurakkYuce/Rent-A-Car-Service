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
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { formatMoney } from '@core/bicim/bicim';
import { type UnsavedChangesOwner, pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { applyServerErrors } from '@core/form/sunucu-hatalari';
import { moneySubmission } from '@core/form/money-submission';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { WarningBannerService } from '@core/geri-bildirim/warning-banner-service';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { translationFunction } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/session-interceptor';
import { tabContext } from '@core/sekme/tab-state';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { mergeServerValues } from '@features/planlama-ortak/form-yardimcilari';
import { MoneyPipe, NumberPipe, DatePipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { SearchSelection } from '@shared/form/arama-secim/search-selection';
import {
  type SecimSecenegi,
  serverSelectionSource,
} from '@shared/form/arama-secim/selection-source';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { TextInput } from '@shared/form/kontroller/text-input';
import { MoneyInput } from '@shared/form/kontroller/money-input';
import { NumberInput } from '@shared/form/kontroller/number-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { MoneySubmitBar } from '@shared/form/money-submit/money-submit-bar';
import { DatePicker } from '@shared/form/tarih/date-picker';

import { planText } from '../service-insurance-columns';
import { lineNetAmount } from '../money-math';
import {
  DECLARATION_TYPES,
  PAYMENT_METHODS,
  type PaymentMethod,
  SERVICES,
  SERVICE_STATUSES,
  SERVICE_TYPES,
  type ServiceLineRequest,
  type ServiceRecordDetail,
  type ServiceReflectRequest,
  num,
  recordPath,
} from '../service-insurance-model';
import { ServiceDetailStore } from '../service-insurance.store';
import {
  type ServiceInfoForm,
  type ServiceLineForm,
  emptyInfoForm,
  infoRequest,
  infoToForm,
  lineRequest,
} from './service-form-model';
import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { PlateChipComponent } from '@shared/plaka/plaka';

type Transition = 'servise-al' | 'baslat' | 'tamamla' | 'iptal';

/**
 * Servis kaydı (`/app/servisler/:id`) — Blazor `ServiceRecordList.razor` satırının eylemleri + "Ayrıntı" bölümü:
 * - durum akışı (Servise Al → Servise Başla → Tamamla; İptal onaylı) — düğmeler sunucunun `yetkiler`'inden;
 * - kalemler (net tutar `toplamIscilik`'i büyütür = rücu tabanı → PARA: `Idempotency-Key` zorunlu, donmuş kopya);
 * - rücu yansıtma (FinanceWrite; Borç Cari / Alacak Gelir; tutar SUNUCU önizlemesi `yansitilacakTutar`);
 * - kaza / fatura / ödeme / yakıt / plan BİLGİ blokları (tam değiştirme PUT, `surum`; 409 `cakisma` formu silmez).
 */
@Component({
  selector: 'rc-service-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PlateChipComponent,
    PageBand,
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    SearchSelection,
    FormErrors,
    TextInput,
    MoneyInput,
    MoneyPipe,
    NumberInput,
    NumberPipe,
    MoneySubmitBar,
    Selection,
    DatePipe,
    DatePicker,
  ],
  providers: [FetchPolicy, ServiceDetailStore],
  templateUrl: './service-detail.html',
  styleUrl: '../service-insurance.scss',
})
export class ServiceDetail implements UnsavedChangesOwner {
  protected readonly store = inject(ServiceDetailStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastService);
  private readonly banner = inject(WarningBannerService);
  private readonly confirm = inject(ConfirmService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly tab = tabContext();
  private readonly t = translationFunction();

  protected readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';
  protected readonly num = num;
  protected readonly planText = planText;
  protected readonly customers = serverSelectionSource('musteri');
  protected readonly declarationTypes = DECLARATION_TYPES;
  protected readonly busy = signal<Transition | null>(null);
  protected readonly detail = computed(() => this.store.detail.veri());
  protected readonly refreshing = computed(() => this.store.detail.isLoading());

  protected readonly paymentOptions: readonly SecenekOgesi<PaymentMethod>[] = PAYMENT_METHODS.map(
    (x) => ({
      deger: x,
      etiket: this.t(`servisSigorta.servis.odemeTurleri.${x}` as CeviriAnahtari),
    }),
  );

  // ---- durum akışı girdileri
  protected readonly flowForm = new FormGroup({
    girisKm: new FormControl<number | null>(null),
    cikisKm: new FormControl<number | null>(null),
    sonrakiBakimKm: new FormControl<number | null>(null),
  });

  // ---- kalem (PARA)
  protected readonly lineForm = new FormGroup({
    aciklama: new FormControl<string | null>(null, [
      Validators.required,
      Validators.maxLength(512),
    ]),
    birimFiyat: new FormControl<string | null>(null),
    miktar: new FormControl<number | null>(null),
    indirim: new FormControl<string | null>(null),
    kdvOran: new FormControl<number | null>(null, [Validators.min(0), Validators.max(1)]),
    tutar: new FormControl<string | null>(null),
  });

  // ---- rücu yansıtma (PARA)
  protected readonly reflectForm = new FormGroup({
    cari: new FormControl<SecimSecenegi | null>(null, Validators.required),
  });
  /** Kalem ve yansıtma: kayıt yenilenince form gizlenebilir → `mukerrer` bildirimi toast'ta. */
  protected readonly line = moneySubmission<ServiceLineRequest>({
    scope: () => `kalem:${this.id}`,
    duplicateDisplay: 'toast',
  });
  protected readonly reflect = moneySubmission<ServiceReflectRequest>({
    scope: () => `yansit:${this.id}`,
    duplicateDisplay: 'toast',
    // E30 yapısal (servis başına tek yansıtma): başkası yansıtmış olabilir — nötr metin (r316 L1).
    recordedMessage: 'servisSigorta.para.zatenYansitildi',
  });

  // ---- bilgi blokları (tam değiştirme)
  protected readonly infoForm = new FormGroup(
    Object.fromEntries(
      Object.entries(emptyInfoForm()).map(([k, v]) => [k, new FormControl<unknown>(v)]),
    ) as Record<keyof ServiceInfoForm, FormControl<unknown>>,
  );
  protected readonly infoSubmission = formSubmission();
  /** Formun doldurulduğu kayıt (birleştirme tabanı + `surum`). */
  private base: ServiceRecordDetail | null = null;

  constructor() {
    inject(FetchPolicy).connect({
      parametre: signal(this.id).asReadonly(),
      yukle: (id) => this.store.detail.yukle(id),
      sifirla: () => this.store.detail.reset(),
    });
    effect(() => {
      const d = this.store.detail.veri();
      if (!d) return;
      untracked(() => {
        this.tab.etiketAyarla(this.t('servisSigorta.servis.sekmeEtiketi', { no: d.kayit.no }));
        this.recordArrived(d);
      });
    });
    // Sonucu bilinmeyen kalem/yansıtma denemesi (sekme kapanıp açıldıysa) aynı gövde + anahtarla KİLİTLİ gelir.
    this.line.restore(this.lineForm);
    this.reflect.restore(this.reflectForm);
    pageLeaveGuard(() => this.hasUnsavedChanges());
  }

  hasUnsavedChanges(): boolean {
    return (
      this.infoForm.dirty || this.lineForm.dirty || this.reflectForm.dirty || this.hasPendingMoney()
    );
  }

  /** Uçuştaki / sonucu bilinmeyen para işlemi varken özel terk metni (inceleme L2). */
  unsavedChangesMessage(): string | null {
    return this.hasPendingMoney() ? this.t('servisSigorta.para.terkMesaji') : null;
  }

  private hasPendingMoney(): boolean {
    return this.line.pending() || this.reflect.pending();
  }

  protected statusLabel(s: string): string {
    return (SERVICE_STATUSES as readonly string[]).includes(s)
      ? this.t(`servisSigorta.servis.durumlar.${s}` as CeviriAnahtari)
      : s;
  }

  protected typeLabel(s: string): string {
    return (SERVICE_TYPES as readonly string[]).includes(s)
      ? this.t(`servisSigorta.servis.tipler.${s}` as CeviriAnahtari)
      : s;
  }

  protected partyLabel(s: string): string {
    return this.t(`servisSigorta.servis.sorumlular.${s}` as CeviriAnahtari);
  }

  protected reload(): void {
    this.store.detail.yenile();
  }

  // ------------------------------------------------------------------ durum akışı

  protected async transition(kind: Transition): Promise<void> {
    const d = this.detail();
    if (!d || this.busy() !== null) return;
    const f = this.flowForm.getRawValue();
    let body: unknown = null;
    if (kind === 'servise-al') body = { girisKm: f.girisKm };
    if (kind === 'tamamla') {
      if (f.cikisKm === null) {
        this.flowForm.controls.cikisKm.setErrors({ required: true });
        this.flowForm.controls.cikisKm.markAsTouched();
        return;
      }
      body = { cikisKm: f.cikisKm, sonrakiBakimKm: f.sonrakiBakimKm };
    }
    if (kind === 'iptal') {
      const yes = await this.confirm.ask({
        baslik: this.t('servisSigorta.servis.iptalBaslik'),
        mesaj: this.t('servisSigorta.servis.iptalMesaj', { no: d.kayit.no }),
        onayEtiketi: this.t('servisSigorta.servis.iptal'),
        tehlikeli: true,
      });
      if (!yes || this.busy() !== null) return;
    }
    this.busy.set(kind);
    this.api
      .post<ServiceRecordDetail>(recordPath(SERVICES, d.kayit.id, `/${kind}`), body)
      .pipe(
        finalize(() => this.busy.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (fresh) => {
          this.toast.basari(
            this.t('servisSigorta.servis.durumDegisti', {
              no: fresh.kayit.no,
              durum: this.statusLabel(fresh.kayit.durum),
            }),
          );
          this.flowForm.reset();
          this.reload();
        },
        error: (raw: unknown) => {
          const error = toApiError(raw);
          const unmatched = applyServerErrors(this.flowForm, error.alanlar);
          if (unmatched.length > 0) this.toast.hata(unmatched.join(' '));
          else if (error.alanlar === undefined && !genelGosterilir(error))
            this.toast.hata(error.detay);
          this.reload();
        },
      });
  }

  // ------------------------------------------------------------------ kalem (PARA)

  protected addLine(): void {
    const d = this.detail();
    if (!d || this.refreshing()) return;
    void this.line.run<ServiceRecordDetail>({
      form: this.lineForm,
      build: () => {
        const v = this.lineForm.getRawValue() as ServiceLineForm;
        return {
          path: recordPath(SERVICES, d.kayit.id, '/kalemler'),
          target: `kalem:${d.kayit.id}`,
          body: lineRequest(v),
          // Bildirimdeki "girdiğiniz" NET satır tutarı: sunucunun `mevcut.tutar`'ı ile aynı büyüklük (inceleme M3).
          content: { tutar: lineNetAmount(v), doviz: 'TRY' },
        };
      },
      success: () => {
        this.toast.basari(this.t('servisSigorta.servis.kalemEklendi'));
        this.lineForm.reset();
      },
      // Önceki deneme kayıtlı: TÜM kalem formu sıfırlanır (birim fiyat/miktar/indirim de — M3).
      afterDuplicate: () => this.lineForm.reset(),
      settled: () => this.reload(),
    });
  }

  // ------------------------------------------------------------------ rücu yansıtma (PARA)

  protected reflectCost(): void {
    const d = this.detail();
    if (!d || this.refreshing()) return;
    void this.reflect.run<ServiceRecordDetail>({
      form: this.reflectForm,
      fieldMap: () => ({ cariId: 'cari' }),
      build: () => {
        const account = this.reflectForm.getRawValue().cari;
        return {
          path: recordPath(SERVICES, d.kayit.id, '/yansit'),
          target: `yansit:${d.kayit.id}`,
          body: { cariId: account?.id ?? null },
          content: { tutar: num(d.yetkiler.yansitilacakTutar), doviz: 'TRY' },
        };
      },
      success: (fresh) => {
        this.toast.basari(
          this.t('servisSigorta.servis.yansitildiBildirim', {
            tutar: formatMoney(num(fresh.yansitma?.tutar ?? null), 'TRY'),
          }),
        );
        this.reflectForm.reset();
      },
      afterDuplicate: () => this.reflectForm.reset(),
      settled: () => this.reload(),
    });
  }

  // ------------------------------------------------------------------ bilgi blokları

  protected saveInfo(): void {
    const base = this.base;
    if (!base || this.refreshing()) return; // sürüm okunmadan tam değiştirme gönderilmez
    const body = infoRequest(
      this.infoForm.getRawValue() as unknown as ServiceInfoForm,
      base.bilgi,
      base.surum,
    );
    this.infoSubmission.gonder(
      this.infoForm,
      (key) =>
        this.api.put<ServiceRecordDetail>(recordPath(SERVICES, base.kayit.id, '/bilgi'), body, {
          islemAnahtari: key,
        }),
      {
        basarili: () => {
          this.toast.basari(this.t('servisSigorta.servis.bilgiKaydedildi'));
          this.reload();
        },
        hata: (h) => {
          if (h.kod === 'cakisma') this.reload(); // güncel kayıt KİRLİ forma birleşir (recordArrived)
        },
      },
    );
  }

  // ------------------------------------------------------------------ yardımcılar

  /**
   * Güncel kayıt: temiz bilgi formu sıfırlanır; kirli formda dokunulan alan korunur, sunucuda da değişen işaretlenir
   * (form SİLİNMEZ). Sonraki PUT yeni `surum` ile gider.
   */
  private recordArrived(d: ServiceRecordDetail): void {
    const fresh = infoToForm(d.bilgi);
    const baseline = this.base ? infoToForm(this.base.bilgi) : fresh;
    if (!this.infoForm.dirty) {
      this.infoForm.reset({ ...fresh });
    } else {
      const conflicts = mergeServerValues(
        this.infoForm,
        { ...fresh },
        { ...baseline },
        this.t('servisSigorta.cakismaAlan'),
      );
      if (conflicts.length > 0)
        this.banner.show({
          tur: 'uyari',
          mesaj: this.t('servisSigorta.cakismaBant', { sayi: conflicts.length }),
          kod: 'cakisma',
        });
    }
    this.base = d;
  }
}
