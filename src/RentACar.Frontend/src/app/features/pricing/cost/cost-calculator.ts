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

import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { type UnsavedChangesOwner, pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { type DayText, bugun } from '@core/form/tarih-girdisi';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { WarningBannerService } from '@core/geri-bildirim/warning-banner-service';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { translationFunction } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/session-interceptor';
import { SessionService } from '@core/oturum/session-service';
import { tabContext } from '@core/sekme/tab-state';
import {
  momentValue,
  dayValue,
  textValue,
  mergeServerValues,
} from '@features/planlama-ortak/form-yardimcilari';
import { MoneyPipe } from '@shared/bicim/bicim-pipe';
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
import { DatePicker } from '@shared/form/tarih/date-picker';

import {
  COST_FIELDS,
  COST_OFFERS,
  CREDIT_METHODS,
  type CostFormValue,
  type CostOfferDetail,
  type CostOfferRequest,
  type CostResult,
  costFormToInput,
  costInputToForm,
  initialCostForm,
} from './cost-model';
import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';

interface HeaderForm {
  readonly baslik: string | null;
  readonly plaka: string | null;
  readonly tarih: DayText | null;
  readonly cari: SecimSecenegi | null;
  readonly hazirlayan: SecimSecenegi | null;
  readonly aciklama: string | null;
}

/** Formun karşılaştırılabilir anlık kopyası (seçim öğeleri dahil). */
const snapshot = (f: FormGroup): string => JSON.stringify(f.getRawValue());

const toNum = (v: number | string | null | undefined): number | null => {
  if (v === null || v === undefined || v === '') return null;
  const n = typeof v === 'number' ? v : Number(v);
  return Number.isFinite(n) ? n : null;
};

/**
 * Filo / uzun dönem maliyet hesaplayıcı (`/app/maliyet-hesapla`, Blazor `MaliyetHesaplama.razor`) ve kayıtlı teklif
 * (`/app/maliyet-teklifleri/:id`). HESAP SUNUCUDA (`POST /maliyet-hesapla` → `MaliyetHesapService`); UI formül
 * taşımaz. Kaydet: `POST /maliyet-teklifleri` (Idempotency-Key) ya da tam değiştirme `PUT` (`surum`; 409 `cakisma`
 * → güncel kayıt KİRLİ forma birleşir). Sonuç istemciden ALINMAZ — sunucu girdiden yeniden hesaplar (önizleme ==
 * kayıt); girdi hesaplamadan sonra değiştiyse kaydetmeden önce yeniden hesaplanır. Planlama belgesi: deftere yazmaz.
 */
@Component({
  selector: 'rc-cost-calculator',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
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
    Selection,
    DatePicker,
  ],
  templateUrl: './cost-calculator.html',
  styleUrl: '../pricing.scss',
})
export class CostCalculator implements UnsavedChangesOwner {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastService);
  private readonly banner = inject(WarningBannerService);
  private readonly router = inject(Router);
  private readonly session = inject(SessionService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly tab = tabContext();
  private readonly t = translationFunction();

  /** Kayıtlı teklif kimliği (`maliyet-teklifleri/:id`); yeni hesapta `null`. */
  protected readonly offerId = inject(ActivatedRoute).snapshot.paramMap.get('id');
  protected readonly num = toNum;
  protected readonly fields = COST_FIELDS;
  protected readonly canWrite = computed(() => this.session.izinVar('FinanceWrite'));
  protected readonly canPickStaff = computed(() => this.session.izinVar('OperationsWrite'));
  protected readonly customers = serverSelectionSource('musteri');
  protected readonly staff = serverSelectionSource('personel');
  protected readonly creditOptions: readonly SecenekOgesi<string>[] = CREDIT_METHODS.map((c) => ({
    deger: c,
    etiket: this.t(`fiyatTarife.maliyet.kredi.${c}` as CeviriAnahtari),
  }));

  protected readonly result = signal<CostResult | null>(null);
  /** Sonuç son hesaplanan girdiye mi ait (girdi değişince kaydetmeden önce yeniden hesaplanır). */
  protected readonly resultStale = signal(false);
  protected readonly offer = signal<CostOfferDetail | null>(null);
  protected readonly loadError = signal(false);

  protected readonly form = new FormGroup(
    Object.fromEntries(
      Object.entries(initialCostForm()).map(([k, v]) => [
        k,
        new FormControl<unknown>(v, k === 'alisBedeli' ? Validators.required : []),
      ]),
    ) as Record<keyof CostFormValue, FormControl<unknown>>,
  );
  protected readonly calc = formSubmission();

  protected readonly header = new FormGroup({
    baslik: new FormControl<string | null>(null, [Validators.required, Validators.maxLength(256)]),
    plaka: new FormControl<string | null>(null, Validators.maxLength(32)),
    tarih: new FormControl<DayText | null>(bugun()),
    cari: new FormControl<SecimSecenegi | null>(null),
    hazirlayan: new FormControl<SecimSecenegi | null>(null),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(1024)),
  });
  /** Kaydet: iki form birlikte doğrulanır (girdi alan hataları `girdi.<alan>` → girdi formu). */
  private readonly saveGroup = new FormGroup({ girdi: this.form, kunye: this.header });
  protected readonly save = formSubmission();

  constructor() {
    // Her girdi değişikliği sonucu bayatlatır — ilk hesap uçarken (sonuç henüz yokken) de (inceleme M2).
    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.resultStale.set(true));
    if (this.offerId) this.readOffer(this.offerId);
    pageLeaveGuard(() => this.hasUnsavedChanges());
  }

  hasUnsavedChanges(): boolean {
    return this.header.dirty || (this.offerId !== null && this.form.dirty);
  }

  protected calculate(): void {
    if (!this.canWrite()) return;
    // Gönderilen girdinin anlık kopyası: yanıt geldiğinde girdi değişmişse sonuç BAYAT kalır (inceleme M2).
    const sent = snapshot(this.form);
    const input = costFormToInput(this.form.getRawValue() as unknown as CostFormValue);
    this.calc.gonder(
      this.form,
      (key) =>
        this.api.post<CostResult>('/api/ui/v1/maliyet-hesapla', input, { islemAnahtari: key }),
      {
        basarili: (r) => {
          this.result.set(r);
          this.resultStale.set(snapshot(this.form) !== sent);
        },
      },
    );
  }

  protected saveOffer(): void {
    if (!this.canWrite() || this.resultStale() || this.result() === null) return;
    const offer = this.offer();
    if (this.offerId && offer === null) return; // sürüm okunmadan tam değiştirme gönderilmez
    const h = this.header.getRawValue() as HeaderForm;
    const sent = { input: snapshot(this.form), header: snapshot(this.header) };
    const body: CostOfferRequest = {
      baslik: textValue(h.baslik),
      plaka: textValue(h.plaka),
      tarih: momentValue(h.tarih, offer?.teklif.tarih),
      cariId: h.cari?.id ?? null,
      hazirlayanId: h.hazirlayan?.id ?? null,
      aciklama: textValue(h.aciklama),
      girdi: costFormToInput(this.form.getRawValue() as unknown as CostFormValue),
      ...(offer ? { surum: offer.surum } : {}),
    };
    this.save.gonder(
      this.saveGroup,
      (key) =>
        offer
          ? this.api.put<CostOfferDetail>(
              `${COST_OFFERS}/${encodeURIComponent(offer.teklif.id)}`,
              body,
              {
                islemAnahtari: key,
              },
            )
          : this.api.post<CostOfferDetail>(COST_OFFERS, body, { islemAnahtari: key }),
      {
        esleme: {
          baslik: 'kunye.baslik',
          plaka: 'kunye.plaka',
          tarih: 'kunye.tarih',
          cariId: 'kunye.cari',
          hazirlayanId: 'kunye.hazirlayan',
          aciklama: 'kunye.aciklama',
        },
        basarili: (d) => {
          this.toast.basari(this.t('fiyatTarife.maliyet.kaydedildi', { no: d.teklif.kayitNo }));
          if (offer) {
            this.offerArrived(d, sent);
          } else {
            // Gönderim sürerken yazılan künye korunur (kirli kalır; bu sekme açık kalır).
            if (snapshot(this.header) === sent.header) this.header.markAsPristine();
            void this.router.navigate(['/maliyet-teklifleri', d.teklif.id]);
          }
        },
        hata: (e) => {
          if (e.kod === 'cakisma' && this.offerId) this.readOffer(this.offerId);
        },
      },
    );
  }

  protected reload(): void {
    if (this.offerId) this.readOffer(this.offerId);
  }

  private readOffer(id: string): void {
    this.loadError.set(false);
    this.api
      .get<CostOfferDetail>(`${COST_OFFERS}/${encodeURIComponent(id)}`)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (d) => this.offerArrived(d, null),
        error: (raw: unknown) => {
          this.loadError.set(true);
          const e = toApiError(raw);
          if (!genelGosterilir(e)) this.toast.hata(e.detay);
        },
      });
  }

  /**
   * Kayıtlı teklif geldi: temiz formlar sıfırlanır; kirli formda dokunulan alan korunur, sunucuda da değişen
   * işaretlenir (409 `cakisma` sonrası form SİLİNMEZ). Sonuç = kaydın snapshot'ı.
   * `sent`: bu kayıt BİZİM PUT'umuzun yanıtıysa gönderilen formların kopyası — PUT uçarken yapılan düzenleme
   * sessizce silinmez (form gönderilenden farklıysa dokunulmaz, sonuç bayat kalır; inceleme M2).
   */
  private offerArrived(d: CostOfferDetail, sent: { input: string; header: string } | null): void {
    const previous = this.offer();
    const freshInput = costInputToForm(d.girdi);
    const freshHeader = this.headerOf(d);
    let conflicts = 0;
    const inputEdited = sent !== null && snapshot(this.form) !== sent.input;
    const headerEdited = sent !== null && snapshot(this.header) !== sent.header;
    if (sent !== null) {
      if (!inputEdited) this.form.reset({ ...freshInput });
      if (!headerEdited) this.header.reset({ ...freshHeader });
      this.offer.set(d);
      this.result.set(d.sonuc);
      this.resultStale.set(inputEdited);
      this.tab.etiketAyarla(this.t('fiyatTarife.maliyet.sekmeEtiketi', { no: d.teklif.kayitNo }));
      return;
    }
    if (!this.form.dirty) this.form.reset({ ...freshInput });
    else
      conflicts += mergeServerValues(
        this.form,
        { ...freshInput },
        previous ? { ...costInputToForm(previous.girdi) } : { ...freshInput },
        this.t('fiyatTarife.cakismaAlan'),
      ).length;
    if (!this.header.dirty) this.header.reset({ ...freshHeader });
    else
      conflicts += mergeServerValues(
        this.header,
        { ...freshHeader },
        previous ? { ...this.headerOf(previous) } : { ...freshHeader },
        this.t('fiyatTarife.cakismaAlan'),
      ).length;
    if (conflicts > 0)
      this.banner.show({
        tur: 'uyari',
        mesaj: this.t('fiyatTarife.cakismaBant', { sayi: conflicts }),
        kod: 'cakisma',
      });
    this.offer.set(d);
    this.result.set(d.sonuc);
    this.resultStale.set(this.form.dirty);
    this.tab.etiketAyarla(this.t('fiyatTarife.maliyet.sekmeEtiketi', { no: d.teklif.kayitNo }));
  }

  private headerOf(d: CostOfferDetail): HeaderForm {
    return {
      baslik: d.teklif.baslik,
      plaka: d.teklif.plaka,
      tarih: dayValue(d.teklif.tarih),
      cari: d.teklif.cariId ? { id: d.teklif.cariId, etiket: d.teklif.cariAd ?? '—' } : null,
      hazirlayan: d.teklif.hazirlayanId
        ? { id: d.teklif.hazirlayanId, etiket: this.t('fiyatTarife.maliyet.kayitliPersonel') }
        : null,
      aciklama: d.aciklama,
    };
  }
}
