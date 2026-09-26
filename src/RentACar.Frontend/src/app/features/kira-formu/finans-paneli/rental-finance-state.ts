import {
  DestroyRef,
  Injectable,
  type Signal,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { type AbstractControl, FormControl, FormGroup, Validators } from '@angular/forms';
import { EMPTY, type Observable, startWith } from 'rxjs';
import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { SubmitLock } from '@core/form/submit-lock';
import { type MoneySubmission, moneySubmission } from '@core/form/money-submission';
import {
  CollectionAttemptRecord,
  CollectionAttempt,
  type TahsilatGonderimi,
  reportCollectionDuplicate,
  amountCleared,
} from '@core/form/collection-attempt';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { requestContext } from '@core/oturum/request-context';
import { genelGosterilir } from '@core/oturum/session-interceptor';
import { SessionService } from '@core/oturum/session-service';
import { TemelStore } from '@core/veri/temel-store';
import type { SecimSecenegi } from '@shared/form/arama-secim/selection-source';
import { type FormSubmission, formSubmission } from '@shared/form/form-submission';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import type { RentalDetailResponse } from '../kira-tipleri';
import {
  CHANNELS,
  CollectionCopy,
  takeDepositBody,
  outsourcedServiceBody,
  currencyCode,
  invoiceBody,
  forfeitBody,
  paymentBody,
  prefillAmount,
  moneyText,
  collectionBody,
} from './finans-modeli';
import type {
  TakeDepositRequest,
  DepositForfeitRequest,
  OutsourcedServiceRequest,
  PeriodInvoiceRequest,
  PeriodInvoiceResponse,
  FinanceAccountItem,
  FinanceTransactionResponse,
  AccountType,
  RentalPenaltyHgs,
  RentalOutsourcedService,
  RentalPeriod,
  RentalInvoice,
  PaymentRequest,
  ExchangeRateSelectItem,
  CollectionInfo,
} from './finans-tipleri';

const RENTAL = '/api/ui/v1/kiralar' as const;
const FINANCE = '/api/ui/v1/finans' as const;
const SILENT = requestContext({ sessiz: true });
const EXCHANGE_RATE_LIMIT = 20;

type Money = string | number | null;
const amountControl = () =>
  new FormControl<Money>(null, [Validators.required, Validators.min(0.01)]);
const optionControl = (value: string | null = null) => new FormControl<string | null>(value);

/** Sabit paneldeki tahsilat formu (Nakit = Kasa, Kart/Havale = Banka): değerler + satır kopyası + gönderim. */
export interface TahsilatFormu {
  readonly hesap: AccountType;
  readonly form: FormGroup<{
    tutar: FormControl<Money>;
    doviz: FormControl<string | null>;
    kur: FormControl<Money>;
    hesapId: FormControl<string | null>;
    kanal: FormControl<string | null>;
    aciklama: FormControl<string | null>;
  }>;
  readonly kopya: CollectionCopy;
  /** Sonucu bilinmeyen deneme izi (M-C) + L-2 tutar yenileme isteği. */
  readonly deneme: CollectionAttempt;
  readonly gonderim: FormSubmission;
  /** Seçili döviz (ISO) — kur alanı ve para simgesi için. */
  readonly doviz: Signal<string>;
  /** Yazılan tutar (invariant metin) — yalnız kalan bakiye UYARISI için karşılaştırılır, hesap yapılmaz. */
  readonly tutar: Signal<Money>;
}

/** Dönem satırı mini formu (tahsilat istendi mi + hesap). */
export type PeriodForm = FormGroup<{
  tahsilat: FormControl<boolean | null>;
  hesap: FormControl<AccountType | null>;
}>;

/**
 * Kira formunun sabit yan panelindeki finans işlemleri (F4.4) — durum + eylemler (panel bileşeninin
 * `providers`'ında; alt bileşenler yalnız çizer). Blazor `StickyPanel.razor` paritesi.
 *
 * Para kuralları (docs/api/idempotency-envanteri.md "SPA sözleşmesi"):
 * - Başlık anahtarlı para işlemleri (giden havale, depozito al/irat, dış hizmet) çekirdek `MoneySubmission` ile:
 *   işlem başına anahtar (2xx ya da `mevcut`lu 409 sonrası yeni; `mevcut`suz 409'da KORUNUR), uçuşta form kilitli,
 *   sonucu bilinmeyen hatada gövde DONAR ve tekrar yalnız donmuş kopyayla; deneme kira başına kayıtlı.
 * - Tahsilat anahtarı DETAYDAN (`tahsilat.anahtar`, deterministik); başlık gönderilmez, gövde satır
 *   kopyasından kurulur ve sonuçlanmamış gönderimde kopya DONAR (`TahsilatKopyasi`).
 * - 409 `mukerrer`de otomatik yeniden gönderim YOK: kayıt yeniden yüklenir (interceptor
 *   `mukerrerdeYenile`), sunucu `detail`'ı bilgi olarak gösterilir.
 * - `dogrulama`/`cakisma` formu silmez (`formGonderimi`); `yetki_yok` bant (interceptor).
 * - İşlem sonrası kira detayı + panel alt kayıtları tazelenir (`degisti`).
 */
@Injectable()
export class RentalFinanceState {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastService);
  private readonly approval = inject(ConfirmService);
  private readonly oturum = inject(SessionService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = translationFunction();
  /** Belirsiz tahsilat denemeleri ANAHTARA bağlı, uygulama geneli (Nakit ↔ Kart, liste ↔ Panel ortak). */
  private readonly attemptRecord = inject(CollectionAttemptRecord);

  /** Sayfaya "kayıt değişti" bildirimi (panelin `degisti` çıktısı); panel kurucuda bağlar. */
  changed: () => void = () => undefined;

  private readonly _detail = signal<RentalDetailResponse | null>(null);
  readonly detay: Signal<RentalDetailResponse | null> = this._detail.asReadonly();
  readonly kira = computed(() => this._detail()?.kira ?? null);
  readonly finans = computed(() => this._detail()?.yetkiler.finans ?? false);
  readonly iptal = computed(() => this.kira()?.durum === 'Iptal');
  /** Dış hizmet iptali dar izin ister (Blazor `AuthorizeView Policy="izin:FinanceReverse"`); kapı sunucuda. */
  readonly reversePermission = computed(() => this.oturum.izinVar('FinanceReverse'));

  // ─── alt kayıtlar (sekme ilk açılınca yüklenir; işlemden sonra yüklü olanlar tazelenir) ────────
  readonly faturalar = new TemelStore(
    (id: string) => this.api.get<RentalInvoice[]>(`${RENTAL}/${id}/faturalar`),
    { oncekiVeriyiKoru: true },
  );
  readonly cezalar = new TemelStore(
    (id: string) => this.api.get<RentalPenaltyHgs>(`${RENTAL}/${id}/cezalar`),
    { oncekiVeriyiKoru: true },
  );
  /** Dönem planı OperationsWrite ister (servis guard'ı; Blazor'da da); izinsizde sessiz hata notu. */
  readonly periods = new TemelStore(
    (id: string) =>
      this.api.get<RentalPeriod[]>(`${RENTAL}/${id}/donem-plani`, { context: SILENT }),
    { oncekiVeriyiKoru: true },
  );
  readonly outsourcedServices = new TemelStore(
    (id: string) => this.api.get<RentalOutsourcedService[]>(`${RENTAL}/${id}/dis-hizmetler`),
    { oncekiVeriyiKoru: true },
  );
  /** TCMB günün kurları (`secim/kur`, OperationsWrite VEYA FinanceWrite); hata sessiz not. */
  readonly kurlar = new TemelStore(() =>
    this.api.get<ExchangeRateSelectItem[]>('/api/ui/v1/secim/kur', {
      parametreler: { limit: EXCHANGE_RATE_LIMIT },
      context: SILENT,
    }),
  );
  readonly cashAccounts = this.accountStore('Kasa');
  readonly bankAccounts = this.accountStore('Banka');

  // ─── formlar ────────────────────────────────────────────────────────────────────────────────
  readonly nakit: TahsilatFormu = this.collectionForm('Kasa');
  readonly kart: TahsilatFormu = this.collectionForm('Banka');
  /**
   * Nakit ve Kart/Havale AYNI deterministik anahtarı taşır (kira başına tek satır kopyası). Biri uçarken ya da
   * sonuçlanıp tazeleme beklerken öteki formun anahtarı da bayattır: iki tahsilat düğmesi birlikte pasif, yeni
   * detay gelene dek (DEVIR §5 "yenileme bitene kadar Kaydet pasif").
   */
  readonly isCollectionBusy = computed(() =>
    [this.nakit, this.kart].some((tf) => tf.gonderim.gonderiliyor() || tf.kopya.refreshPending()),
  );
  /** Tahsilat sonrası kira detayı tazeleniyor (iki formda da "tazeleniyor" notu). */
  readonly isCollectionRefreshing = computed(() =>
    [this.nakit, this.kart].some((tf) => tf.kopya.refreshPending()),
  );
  private readonly _detailError = signal(false);
  /**
   * #318 L1: tahsilat sonrası tazeleme hatayla (5xx/ağ) bitti — düğmeler bayat anahtarla açılamaz; formda
   * "Yeniden yükle" gösterilir. Donmuş deneme ve anahtar korunur (kesin sonuç yok).
   */
  readonly collectionLoadFailed = computed(
    () => this._detailError() && this.isCollectionRefreshing(),
  );
  readonly paymentForm = new FormGroup({
    tutar: amountControl(),
    hesapId: optionControl(),
    kanal: optionControl(CHANNELS[0]),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  readonly takeDepositForm = new FormGroup({
    tutar: amountControl(),
    hesap: new FormControl<AccountType | null>('Kasa', Validators.required),
    hesapId: optionControl(),
  });
  readonly forfeitForm = new FormGroup({
    tutar: amountControl(),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  readonly invoiceForm = new FormGroup({
    otv: new FormControl<Money>(null, Validators.min(0)),
    tevkifatOran: new FormControl<Money>(null, [Validators.min(0), Validators.max(100)]),
    tevkifatTutar: new FormControl<Money>(null, Validators.min(0)),
    damgaVergisi: new FormControl<Money>(null, Validators.min(0)),
    iadeMi: new FormControl<boolean | null>(false),
    manuelMi: new FormControl<boolean | null>(false),
  });
  readonly outsourcedServiceForm = new FormGroup({
    cari: new FormControl<SecimSecenegi | null>(null, Validators.required),
    alinanHizmet: new FormControl<string | null>(null, [
      Validators.required,
      Validators.maxLength(256),
    ]),
    hizmetAlinanFirma: new FormControl<string | null>(null, Validators.maxLength(256)),
    hizmetBedeli: amountControl(),
    komisyonOran: new FormControl<Money>(null, [Validators.min(0), Validators.max(100)]),
    doviz: optionControl('TRY'),
    kur: new FormControl<Money>(null, Validators.min(0.000001)),
    komisyonFaturaNo: new FormControl<string | null>(null, Validators.maxLength(64)),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  private readonly periodForms = new Map<number, PeriodForm>();
  /** Depozito tutarı kiranın depozitosuyla yalnız bir kez (depozito alınana dek) önerilir (adversarial L2). */
  private depositPrefill = true;
  /** Dış hizmet formunda seçili döviz (kur alanı için). */
  readonly outsourcedServiceCurrency = this.currencySignal(
    this.outsourcedServiceForm.controls.doviz,
  );
  /** Depozito al formunda seçili hesap türü (hesap listesi için). */
  readonly depositAccountType = this.valueSignal(this.takeDepositForm.controls.hesap);

  // ─── gönderimler (her işlem kendi kilidi + kendi anahtarı) ─────────────────────────────────
  readonly paymentSubmission = this.moneySubmission<PaymentRequest>('odeme');
  readonly takeDepositSubmission = this.moneySubmission<TakeDepositRequest>('depozito-al');
  readonly forfeitSubmission = this.moneySubmission<DepositForfeitRequest>('irat');
  readonly invoiceSubmission = formSubmission();
  readonly outsourcedServiceSubmission =
    this.moneySubmission<OutsourcedServiceRequest>('dis-hizmet');
  /** Kesinleşmemiş denemeleri geri getirilen kira (panel yeniden kurulunca bir kez). */
  private restoredRental: string | null = null;
  readonly periodLock = new SubmitLock();
  readonly cancelLock = new SubmitLock();
  /** Hangi dönem satırı gönderiliyor (düğme metni için). */
  readonly submittedPeriod = signal<number | null>(null);
  /** Son dönem işleminin sunucu bilgisi (`tahsilatYazildi=false` + `bilgi`) — panelde kalıcı not. */
  readonly periodInfo = signal<string | null>(null);

  readonly channelOptions: readonly SecenekOgesi<string>[] = CHANNELS.map((k) => ({
    deger: k,
    etiket: k,
  }));

  constructor() {
    effect(() => {
      const d = this._detail();
      untracked(() => this.detailLoaded(d));
    });
  }

  /** Sayfanın detay tazelemesi belirsiz hatayla bitti mi (ekran son iyi veriyle duruyor). */
  setDetailError(error: boolean): void {
    this._detailError.set(error);
  }

  /** Panel girdisi değişti (kira yüklendi/tazelendi). Aynı nesne yeniden verilirse hiçbir şey olmaz. */
  setDetail(d: RentalDetailResponse | null): void {
    this._detail.set(d);
  }

  /** Sekme ilk kez açıldı: o sekmenin verisi yüklenir (bir kez; sonra işlemlerle tazelenir). */
  tabOpened(tab: string): void {
    const id = this.kira()?.id;
    if (!id) return;
    const first = <T, P>(store: TemelStore<T, P>, p: P): void => {
      if (store.tur() === 'bos') store.yukle(p);
    };
    switch (tab) {
      case 'nakit':
      case 'kart':
        if (this.finans()) {
          first(this.cashAccounts, undefined);
          first(this.bankAccounts, undefined);
        }
        break;
      case 'faturalar':
        first(this.faturalar, id);
        break;
      case 'donem':
        first(this.periods, id);
        break;
      case 'dishizmet':
        first(this.outsourcedServices, id);
        break;
      case 'kurlar':
        first(this.kurlar, undefined);
        break;
      case 'ceza':
        first(this.cezalar, id);
        break;
    }
  }

  accountOptions(type: AccountType | null): readonly SecenekOgesi<string>[] {
    const store = type === 'Banka' ? this.bankAccounts : type === 'Kasa' ? this.cashAccounts : null;
    return (store?.veri() ?? []).map((h) => ({ deger: h.id, etiket: h.etiket }));
  }

  periodForm(order: number): PeriodForm {
    let f = this.periodForms.get(order);
    if (!f) {
      f = new FormGroup({
        tahsilat: new FormControl<boolean | null>(false),
        hesap: new FormControl<AccountType | null>('Kasa'),
      });
      this.periodForms.set(order, f);
    }
    return f;
  }

  /**
   * Sayfa terk koruması (adversarial L3): panel formlarından biri kirli ya da bir para gönderimi SONUÇLANMADI
   * (donmuş tahsilat kopyası / bekleyen `Idempotency-Key`) — sayfadan ayrılınca anahtar sessizce kaybolmasın.
   */
  /** Sonucu bilinmeyen (ağ/5xx/oturum sonrası sonuçlanmamış) para gönderimi var mı — terk sorusu özel metinle. */
  hasUnknownOutcome(): boolean {
    return (
      this.nakit.kopya.unsettled ||
      this.kart.kopya.unsettled ||
      [
        this.paymentSubmission,
        this.takeDepositSubmission,
        this.forfeitSubmission,
        this.outsourcedServiceSubmission,
      ].some((g) => g.pending())
    );
  }

  isDirty(): boolean {
    const forms = [
      this.nakit.form,
      this.kart.form,
      this.paymentForm,
      this.takeDepositForm,
      this.forfeitForm,
      this.invoiceForm,
      this.outsourcedServiceForm,
    ];
    return forms.some((f) => f.dirty) || this.hasUnknownOutcome();
  }

  // ─── işlemler ──────────────────────────────────────────────────────────────────────────────

  /**
   * Kira tahsilatı (E01). Anahtar = detaydaki deterministik `tahsilatAnahtar` (satır kopyası); başlık YOK.
   * 409 `mukerrer`: otomatik yeniden gönderim YOK; kayıt yeniden yüklenir (yeni anahtar tazelenen detaydan), ön-doldurma
   * kapanır. Toast'u interceptor değil `tahsilatMukerrerBildir` gösterir (sınıf, isteğin TEKRAR olup olmadığına bağlı):
   * - `zatenKaydedildi` (HIGH-1, kaybolan yanıttan sonraki birebir tekrar): form temizlenir.
   * - `oncekiDenemeKaydedilmis` (4. tur M-C): sonucu bilinmeyen bir denemenin (ör. 500, yanıt kayboldu) tutarı
   *   değiştirilmiş (600) tekrarı — 500 yazılmış, 600 YAZILMADI. Tutar TEMİZLENİR: form korunursa ikinci basış yeni
   *   anahtarla 600'ü de yazıyordu (niyet 600, kayıt 1100). "Tekrar mı" bilgisi gönderimden ÖNCE yakalanır.
   * - `baskaIslemDenemeYazilmadi` (5. tur LOW-1): belirsiz deneme var ama kayıt onun değil — tutar TEMİZLENİR.
   * - Belirsiz deneme kaydı forma değil ANAHTARA bağlıdır (5. tur MEDIUM-1: Nakit'te kaybolan 500 → Kart'ta 600).
   * - `baskaIslemYazildi` (3. tur M-A, iki sekme/iki kullanıcı): form SİLİNMEZ; tutar elle yazılmadıysa (ön-dolu)
   *   yeni bakiyeyle yenilenir (L-2), yazıldıysa korunur.
   * - `bayatAnahtar` (ekran açıldıktan sonra kirada işlem oldu): tutar temizlenir, kullanıcı güncel bakiyeyle girer.
   */
  doCollection(tf: TahsilatFormu): void {
    if (!tf.kopya.canSubmit() || this.isCollectionBusy()) return;
    let g: TahsilatGonderimi | null = null;
    tf.gonderim.gonder(
      tf.form,
      () => {
        const copy = tf.kopya.submitting();
        if (copy === null) return EMPTY; // düğme zaten kapalı; kilit finalize'la bırakılır
        const value = tf.form.getRawValue();
        // M-C / 5. tur: belirsiz denemeler gönderimden ÖNCE, ANAHTAR üzerinden (Nakit ↔ Kart ortak kayıt).
        g = tf.deneme.start(copy.anahtar, {
          tutar: value.tutar,
          doviz: currencyCode(value.doviz),
          hesap: tf.hesap,
        });
        return this.api.post<FinanceTransactionResponse>(
          `${FINANCE}/tahsilat`,
          collectionBody(copy, tf.hesap, value),
          {
            context: requestContext({
              mukerrerdeYenile: () => this.yenile(),
              mukerrerCagiranGosterir: true,
            }),
          },
        );
      },
      {
        deterministikAnahtar: tf.kopya.kopya()?.anahtar ?? null,
        basarili: () => {
          if (g) tf.deneme.successful(g);
          tf.kopya.settled();
          this.tamam('kiraFinans.bildirim.tahsilat');
        },
        hata: (h) => {
          if (!g) return;
          const type = tf.deneme.errorReceived(g, h);
          if (type === null) return;
          tf.kopya.settled(false);
          reportCollectionDuplicate(this.toast, this.t, type, h, {
            gonderim: g,
            bayatBaslik: this.t('kiraFinans.kayitDegismis'),
            ek: this.t('geriBildirim.mukerrerYenilendi'),
          });
          const amount = tf.form.controls.tutar;
          if (type === 'zatenKaydedildi')
            tf.form.reset(this.collectionDefaults(tf.kopya.kopya(), false));
          else if (amountCleared(type)) amount.setValue(null);
          else if (amount.pristine && amount.value !== null)
            tf.deneme.requestAmountRefresh(g.anahtar);
        },
      },
    );
  }

  /** Giden havale (E02): başlık anahtarı zorunlu; kiraya bağlanmaz (Blazor paritesi). */
  makePayment(): void {
    const customerId = this.kira()?.musteriId;
    if (!customerId) return;
    const empty = () =>
      this.paymentForm.reset({ tutar: null, hesapId: null, kanal: CHANNELS[0], aciklama: null });
    void this.paymentSubmission.run<FinanceTransactionResponse>({
      form: this.paymentForm,
      build: () => ({
        path: `${FINANCE}/odeme`,
        body: paymentBody(customerId, this.paymentForm.getRawValue()),
      }),
      success: () => {
        empty();
        this.tamam('kiraFinans.bildirim.odeme');
      },
      afterDuplicate: empty,
      settled: (reason) => reason !== 'done' && this.yenile(),
    });
  }

  /** Depozito al (E09): başlık anahtarı zorunlu; aynı içerik tekrarında sunucu aynı kaydı döner. */
  takeDeposit(): void {
    const customerId = this.kira()?.musteriId;
    if (!customerId) return;
    const empty = () => {
      this.depositPrefill = false;
      this.takeDepositForm.reset({ tutar: null, hesap: 'Kasa', hesapId: null });
    };
    void this.takeDepositSubmission.run<FinanceTransactionResponse>({
      form: this.takeDepositForm,
      build: () => ({
        path: `${FINANCE}/depozito/al`,
        body: takeDepositBody(customerId, this.takeDepositForm.getRawValue()),
      }),
      success: () => {
        empty();
        this.tamam('kiraFinans.bildirim.depozitoAl');
      },
      // Önceki deneme kayıtlı: depozito yeniden ÖNERİLMEZ (ikinci tık ikinci depozito olmasın).
      afterDuplicate: empty,
      settled: (reason) => reason !== 'done' && this.yenile(),
    });
  }

  /** Depozito irat (E12): GERİ ALINAMAZ → önce onay; gelir bu kiranın aracına atfedilir. */
  async depositForfeit(): Promise<void> {
    const k = this.kira();
    if (!k) return;
    await this.forfeitSubmission.run<FinanceTransactionResponse>({
      form: this.forfeitForm,
      build: () => ({
        path: `${FINANCE}/depozito/irat`,
        body: forfeitBody(k.musteriId, k.id, this.forfeitForm.getRawValue()),
      }),
      confirm: () =>
        this.approval.ask({
          baslik: this.t('kiraFinans.irat.onayBaslik'),
          mesaj: this.t('kiraFinans.irat.onayMesaj'),
          onayEtiketi: this.t('kiraFinans.irat.onayla'),
          tehlikeli: true,
        }),
      success: () => {
        this.forfeitForm.reset();
        this.tamam('kiraFinans.bildirim.irat');
      },
      afterDuplicate: () => this.forfeitForm.reset(),
      settled: (reason) => reason !== 'done' && this.yenile(),
    });
  }

  /** Kiradan fatura (E15, yapısal): anahtar kullanılmaz; ikinci çağrı sunucudan 400 (form üstü hata). */
  issueInvoice(invalid?: () => void): void {
    const k = this.kira();
    if (!k) return;
    this.invoiceSubmission.gonder(
      this.invoiceForm,
      () =>
        this.api.post<FinanceTransactionResponse>(
          `${FINANCE}/fatura`,
          invoiceBody(k.id, this.invoiceForm.getRawValue()),
        ),
      {
        // L4: geçersiz alan kapalı <details> içindeyse görünmüyordu (sessiz "Fatura kes") → bileşen açıp odaklar.
        ...(invalid ? { gecersiz: invalid } : {}),
        basarili: () => {
          this.invoiceForm.reset(this.invoiceDefaults());
          this.tamam('kiraFinans.bildirim.fatura');
        },
      },
    );
  }

  /**
   * Dönem faturası kes (+ isteğe bağlı tahsilat) — E18/E19 yapısal + deterministik `RowKey(kira, sıra)`:
   * tekrar SESSİZ 200 (aynı fatura). `tahsilatYazildi=false` ise sunucunun `bilgi`'si GİZLENMEZ.
   */
  issuePeriod(d: RentalPeriod): void {
    const k = this.kira();
    if (!k || this.periodLock.gonderiliyor()) return;
    const order = Number(d.donemSira);
    const f = this.periodForm(order).getRawValue();
    const body: PeriodInvoiceRequest = {
      kiraId: k.id,
      donemSira: order,
      tahsilat: f.tahsilat ?? false,
      ...(f.tahsilat ? { hesap: f.hesap } : {}),
    };
    this.submittedPeriod.set(order);
    this.locked(
      this.periodLock,
      () => this.api.post<PeriodInvoiceResponse>(`${FINANCE}/donem-fatura`, body),
      (y) => {
        this.submittedPeriod.set(null);
        const info = body.tahsilat && !y.tahsilatYazildi ? y.bilgi : null;
        this.periodInfo.set(info);
        if (info) this.toast.bilgi(info, { baslik: this.t('kiraFinans.donem.tahsilatYok') });
        this.tamam(
          body.tahsilat && y.tahsilatYazildi
            ? 'kiraFinans.bildirim.donemTahsil'
            : 'kiraFinans.bildirim.donem',
        );
      },
      () => {
        this.submittedPeriod.set(null);
        this.yenile(); // L7: 400 (ör. başka sekmede kesildi) sonrası plan ve kira tazelensin
      },
    );
  }

  /** B2B dış hizmet alımı (E33): başlık anahtarı zorunlu. */
  saveOutsourcedService(): void {
    const k = this.kira();
    if (!k) return;
    void this.outsourcedServiceSubmission.run<FinanceTransactionResponse>({
      form: this.outsourcedServiceForm,
      fieldMap: () => ({ cariId: 'cari' }),
      build: () => {
        const d = this.outsourcedServiceForm.getRawValue();
        return {
          path: `${FINANCE}/dis-hizmet`,
          body: outsourcedServiceBody(k.id, { ...d, cariId: d.cari?.id ?? null }),
        };
      },
      success: () => {
        this.outsourcedServiceForm.reset({ doviz: 'TRY' });
        this.tamam('kiraFinans.bildirim.disHizmet');
      },
      afterDuplicate: () => this.outsourcedServiceForm.reset({ doviz: 'TRY' }),
      settled: (reason) => reason !== 'done' && this.yenile(),
    });
  }

  /** Dış hizmet iptali (E34, FinanceReverse): ters kayıt, geri alınamaz → onay. */
  async cancelOutsourcedService(s: RentalOutsourcedService): Promise<void> {
    if (this.cancelLock.gonderiliyor()) return;
    const approval = await this.approval.ask({
      baslik: this.t('kiraFinans.disHizmet.iptalBaslik', { no: s.no }),
      mesaj: this.t('kiraFinans.disHizmet.iptalMesaj'),
      onayEtiketi: this.t('kiraFinans.disHizmet.iptalOnay'),
      tehlikeli: true,
    });
    if (!approval) return;
    this.locked(
      this.cancelLock,
      () => this.api.post<null>(`${FINANCE}/dis-hizmet/${s.id}/iptal`, null),
      () => this.tamam('kiraFinans.bildirim.disHizmetIptal'),
    );
  }

  /** Kayıt + panel verisi yeniden yüklenir (işlem sonrası ve `mukerrer`de). */
  yenile(): void {
    for (const s of [this.faturalar, this.cezalar, this.periods, this.outsourcedServices]) {
      if (s.tur() !== 'bos') s.yenile();
    }
    this.changed();
  }

  // ─── iç ────────────────────────────────────────────────────────────────────────────────────

  private tamam(message: CeviriAnahtari): void {
    this.toast.basari(this.t(message));
    this.yenile();
  }

  /** Kira başına kapsamlı para gönderimi (panel yeniden kurulunca kesinleşmemiş deneme geri gelir). */
  private moneySubmission<T>(operation: string): MoneySubmission<T> {
    return moneySubmission<T>({
      scope: () => `kira-finans:${operation}:${this.kira()?.id ?? ''}`,
    });
  }

  /** Alan formu olmayan işlem: kilit + hata bildirimi (interceptor göstermediyse). */
  private locked<T>(
    lockEntry: SubmitLock,
    request: () => Observable<T>,
    successful: (y: T) => void,
    onError?: () => void,
  ): void {
    lockEntry
      .gonder(request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: successful,
        error: (raw: unknown) => {
          onError?.();
          const h = toApiError(raw);
          if (genelGosterilir(h) || (h.kod === 'cakisma' && h.alanlar === undefined)) return;
          this.toast.hata(h.detay);
        },
      });
  }

  private detailLoaded(d: RentalDetailResponse | null): void {
    if (d === null) return;
    const k = d.kira;
    if (this.restoredRental !== k.id) {
      this.restoredRental = k.id;
      this.paymentSubmission.restore(this.paymentForm);
      this.takeDepositSubmission.restore(this.takeDepositForm);
      this.forfeitSubmission.restore(this.forfeitForm);
      this.outsourcedServiceSubmission.restore(this.outsourcedServiceForm);
    }
    // Tazeleme bekleyen (sonuçlanmış) form varsa ortak anahtar kesin bayat — döngü bayrağı temizlemeden ÖNCE okunur.
    const settled = [this.nakit, this.kart].filter((tf) => tf.kopya.refreshPending());
    this._detailError.set(false);
    for (const tf of [this.nakit, this.kart]) {
      const siblingResolved = settled.some((s) => s !== tf);
      const result = tf.kopya.detailLoaded(d.tahsilat ?? null, tf.form.dirty, siblingResolved);
      if (result === 'ondoldur') tf.form.reset(this.collectionDefaults(tf.kopya.kopya(), true));
      // L-2: "YAZILMADI" (başka işlem) sonrası dokunulmamış ön-dolu tutar yeni bakiyeyle yenilenir (eski bakiye
      // gönderilip fazla tahsilat yazılmasın); kullanıcının yazdığı tutar (dirty) korunur.
      else if (
        tf.deneme.shouldRefreshAmount(tf.kopya.kopya()?.anahtar) &&
        tf.form.controls.tutar.pristine
      )
        tf.form.controls.tutar.setValue(prefillAmount(tf.kopya.kopya()?.varsayilanTutar));
    }
    // L2: kiranın depozitosu yalnız ilk açılışta önerilir; alındıktan sonra yeniden ön-doldurulmaz (ikinci tık
    // ikinci depozito yazıyordu).
    if (
      this.takeDepositForm.pristine &&
      this.depositPrefill &&
      !this.takeDepositSubmission.pending()
    ) {
      this.takeDepositForm.reset({ tutar: moneyText(k.depozito, 2), hesap: 'Kasa', hesapId: null });
    }
    if (this.invoiceForm.pristine) this.invoiceForm.reset(this.invoiceDefaults());
  }

  /** Tahsilat formunun boş hâli; `ondoldur` ise tutar sunucunun önerisiyle (yalnız pozitifse) dolar. */
  private collectionDefaults(info: CollectionInfo | null, prefill: boolean) {
    return {
      tutar: prefill ? prefillAmount(info?.varsayilanTutar) : null,
      doviz: currencyCode(info?.doviz ?? this.kira()?.doviz),
      kur: null,
      hesapId: null,
      kanal: CHANNELS[0],
      aciklama: null,
    };
  }

  private invoiceDefaults() {
    return {
      otv: null,
      tevkifatOran: null,
      tevkifatTutar: null,
      damgaVergisi: moneyText(this.kira()?.damgaVergisi, 2),
      iadeMi: false,
      manuelMi: false,
    };
  }

  private collectionForm(account: AccountType): TahsilatFormu {
    const form = new FormGroup({
      tutar: amountControl(),
      doviz: new FormControl<string | null>('TRY', Validators.required),
      kur: new FormControl<Money>(null, Validators.min(0.000001)),
      hesapId: optionControl(),
      kanal: optionControl(CHANNELS[0]),
      aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
    });
    // L6: kullanıcı dövizi değiştirince DOKUNULMAMIŞ ön-dolu tutar temizlenir (TRY bakiye "2.600" USD gitmesin).
    form.controls.doviz.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      if (form.controls.doviz.dirty && form.controls.tutar.pristine)
        form.controls.tutar.setValue(null);
    });
    return {
      hesap: account,
      form,
      kopya: new CollectionCopy(),
      deneme: new CollectionAttempt(this.attemptRecord),
      gonderim: formSubmission(),
      doviz: this.currencySignal(form.controls.doviz),
      tutar: this.valueSignal(form.controls.tutar),
    };
  }

  private valueSignal<T>(check: AbstractControl<T>): Signal<T> {
    return toSignal(check.valueChanges.pipe(startWith(check.value)), {
      initialValue: check.value,
    });
  }

  private currencySignal(check: AbstractControl<string | null>): Signal<string> {
    const value = this.valueSignal(check);
    return computed(() => currencyCode(value()));
  }

  private accountStore(type: AccountType): TemelStore<FinanceAccountItem[]> {
    return new TemelStore(() =>
      this.api.get<FinanceAccountItem[]>(`${FINANCE}/hesaplar`, {
        parametreler: { tur: type },
        context: SILENT,
      }),
    );
  }
}
