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
import { ActivatedRoute, Router } from '@angular/router';
import {
  type Observable,
  debounceTime,
  distinctUntilChanged,
  map,
  of,
  skip,
  startWith,
} from 'rxjs';
import { toApiError } from '@core/api/api-hatasi';
import { formatMoney } from '@core/bicim/bicim';
import { ApiIstemcisi, type QueryParameters } from '@core/api/api-istemcisi';
import { SubmitLock } from '@core/form/submit-lock';
import { SERVER_ERROR } from '@core/form/sunucu-hatalari';
import { type DayText, bugun, addDays } from '@core/form/tarih-girdisi';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { WarningBannerService } from '@core/geri-bildirim/warning-banner-service';
import { translationFunction } from '@core/i18n/ceviri';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { trSearchKey } from '@core/metin/tr-normalize';
import { requestContext } from '@core/oturum/request-context';
import { genelGosterilir } from '@core/oturum/session-interceptor';
import { SessionService } from '@core/oturum/session-service';
import { canSeeStatement } from '@features/customers/customer-model';
import { tabContext } from '@core/sekme/tab-state';
import { TemelStore } from '@core/veri/temel-store';
import {
  type SelectionSource,
  type SecimSecenegi,
  serverSelectionSource,
} from '@shared/form/arama-secim/selection-source';
import { type FormSubmission, formSubmission } from '@shared/form/form-submission';
import {
  type RentalFormMode,
  type RentalForm,
  type RentalServerValues,
  type KiraSorgusu,
  type MusaitPencere,
  SERVER_FIELD_MAPPING,
  FROZEN_WHEN_COMPLETED,
  vehicleOption,
  syncMirrorStates,
  copyErrorToMirrors,
  bindMirrors,
  valuesFromDetail,
  addOnRow,
  resetForm,
  toDayMoment,
  updateBody,
  calculateParams,
  isoCurrency,
  createRentalForm,
  resolveRentalQuery,
  availabilityWindow,
  createBody,
  datesFromWindow,
  toNumber,
  optionList,
  isSystemItem,
  mergeServerValues,
} from './kira-formu-modeli';
import type {
  AracSecenegi,
  AddOnCatalogItem,
  AddOnItem,
  SourceReservationResponse,
  ScorecardSummary,
  RentalDetailResponse,
  RentalReturnPreview,
  RentalAddOnCatalog,
  RentalAddOnResponse,
  RentalFormDefaults,
  RentalCalculationResult,
  RentalCustomerSummary,
  CreateRentalResponse,
  RentalContract,
  AvailableVehicle,
  QuickCustomerResponse,
  SelectionVehicle,
  SelectionCustomer,
} from './kira-tipleri';

const ROOT = '/api/ui/v1/kiralar' as const;
const SILENT = requestContext({ sessiz: true });
const SUGGESTION_LIMIT = 20;
/**
 * Detay okumasının GEÇİCİ hataları (5xx, ağ, 429 hız sınırı): kayıt hâlâ var ve görülebilir → son iyi veri kalır.
 * Geri kalanı (403, kodsuz 404 = `bilinmeyen`, 400, 401) kesindir → eski veri gösterilmez (#280 KVKK L-3).
 */
const TRANSIENT_DETAIL_ERRORS: ReadonlySet<string> = new Set(['sunucu', 'ag', 'cok_istek']);
/** Öneri kutusuna yazarken sunucu araması gecikmesi (datalist `q` ile sunucuda süzülür). */
const SUGGESTION_DELAY = 250;

/** Katalog sunucuda kesildi mi (tanım sayısı > dönen satır)? Kesikse kalanlar sunucu aramasıyla eklenir. */
export function catalogTruncated(k: RentalAddOnCatalog): boolean {
  return (toNumber(k.toplam) ?? 0) > k.ogeler.length;
}

/** Kayıtlı kiraya ek hizmet eklerken seçenek etiketi (Blazor: "Ad (birim net)"). */
function addOnLabel(x: AddOnCatalogItem, currency: string): string {
  const price = formatMoney(toNumber(x.birimUcret), currency);
  return price ? `${x.ad} (${price} net)` : x.ad;
}

/** Dönüş formundaki "Bitiş sebebi" seçenekleri (Blazor `SekmeDonus` ile aynı liste). */
export const END_REASONS = ['Normal', 'Erken İade', 'Hasar', 'Arıza', 'Değişim', 'Diğer'] as const;

/**
 * Kira formu sayfa durumu (sayfanın `providers`'ında; sekme bileşenleri enjekte eder). Form(lar),
 * veri store'ları, canlı hesap akışı ve eylemler burada; sekme bileşenleri yalnız çizer.
 *
 * Kurallar: canlı hesap SUNUCUDAN (`hesapla`, `donus-hesapla`); hata hiçbir dalda form değerine
 * dokunmaz (`formGonderimi`); ek hizmet ekleme gönderim başına `Idempotency-Key` + istek boyunca kilit.
 */
@Injectable()
export class RentalFormState {
  private readonly api = inject(ApiIstemcisi);
  private readonly rota = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly oturum = inject(SessionService);
  private readonly toast = inject(ToastService);
  private readonly approval = inject(ConfirmService);
  private readonly bant = inject(WarningBannerService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly sekme = tabContext();
  private readonly t = translationFunction();

  /** Kayıtlı kiranın kimliği; yeni kirada `null`. Bileşen örneği boyunca değişmez (sekme = rota + id). */
  readonly id: string | null = this.rota.snapshot.paramMap.get('id');
  readonly mod: RentalFormMode = this.id === null ? 'yeni' : 'duzenle';
  readonly yeni = this.mod === 'yeni';

  readonly form: RentalForm = createRentalForm(this.mod);

  // ─── ikincil formlar (ana formun DIŞINDA gönderilir; kirli sayılır) ──────────────────────
  readonly newCustomerForm = new FormGroup({
    ad: new FormControl<string | null>(null, Validators.maxLength(64)),
    soyad: new FormControl<string | null>(null, Validators.maxLength(64)),
    unvan: new FormControl<string | null>(null, Validators.maxLength(128)),
    tcKimlik: new FormControl<string | null>(null, Validators.maxLength(11)),
    cepTel: new FormControl<string | null>(null, Validators.maxLength(32)),
    email: new FormControl<string | null>(null, [Validators.email, Validators.maxLength(128)]),
    dogumTarihi: new FormControl<DayText | null>(null),
    ehliyetNo: new FormControl<string | null>(null, Validators.maxLength(32)),
    ehliyetSinifi: new FormControl<string | null>(null, Validators.maxLength(8)),
    ehliyetTarihi: new FormControl<DayText | null>(null),
    ehliyetYeri: new FormControl<string | null>(null, Validators.maxLength(64)),
    il: new FormControl<string | null>(null, Validators.maxLength(64)),
    ilce: new FormControl<string | null>(null, Validators.maxLength(64)),
  });
  readonly availabilityForm = new FormGroup({
    vfrom: new FormControl<DayText | null>(null, Validators.required),
    vto: new FormControl<DayText | null>(null, Validators.required),
    vgrup: new FormControl<string | null>(null, Validators.maxLength(64)),
  });
  readonly deliveryForm = new FormGroup({
    cikisKm: new FormControl<number | null>(0, [Validators.required, Validators.min(0)]),
    cikisYakit: new FormControl<number | null>(8, [
      Validators.required,
      Validators.min(0),
      Validators.max(12),
    ]),
  });
  readonly returnForm = new FormGroup({
    donusKm: new FormControl<number | null>(null, [Validators.required, Validators.min(0)]),
    donusYakit: new FormControl<number | null>(null, [
      Validators.required,
      Validators.min(0),
      Validators.max(12),
    ]),
    gercekDonus: new FormControl<string | null>(null, Validators.required),
    kmHediye: new FormControl<number | null>(null, Validators.min(0)),
    bitisSebebi: new FormControl<string | null>(null),
    teslimAlanPersonel: new FormControl<SecimSecenegi | null>(null),
  });
  readonly extendForm = new FormGroup({
    yeniBitTar: new FormControl<string | null>(null, Validators.required),
  });
  readonly closePreAuthForm = new FormGroup({
    kapamaTutar: new FormControl<string | number | null>(null),
    iade: new FormControl<boolean | null>(false),
  });
  readonly addAddOnForm = new FormGroup({
    tanim: new FormControl<SecimSecenegi | null>(null, Validators.required),
    miktar: new FormControl<number | null>(1, [Validators.required, Validators.min(0.01)]),
  });

  // ─── gönderimler (her biri kendi kilidi; çift tık tek istek) ───────────────────────────────
  readonly kayit: FormSubmission = formSubmission();
  readonly newCustomerSubmission = formSubmission();
  readonly deliverySubmission = formSubmission();
  readonly returnSubmission = formSubmission();
  readonly extendSubmission = formSubmission();
  readonly closePreAuthSubmission = formSubmission();
  readonly addAddOnSubmission = formSubmission();
  readonly cancelLock = new SubmitLock();
  readonly takePreAuthLock = new SubmitLock();
  readonly deleteAddOnLock = new SubmitLock();

  // ─── veri ──────────────────────────────────────────────────────────────────────────────────
  readonly detay = new TemelStore(
    (id: string) => this.api.get<RentalDetailResponse>(`${ROOT}/${id}`),
    {
      oncekiVeriyiKoru: true,
    },
  );
  readonly defaults = new TemelStore(() =>
    this.api.get<RentalFormDefaults>(`${ROOT}/form-varsayilanlari`),
  );
  readonly hesap = new TemelStore(
    (p: QueryParameters) =>
      this.api.get<RentalCalculationResult>(`${ROOT}/hesapla`, {
        parametreler: p,
        context: SILENT,
      }),
    { oncekiVeriyiKoru: true },
  );
  readonly musait = new TemelStore((p: MusaitPencere) =>
    this.api.get<AvailableVehicle[]>(`${ROOT}/musait-arac`, {
      parametreler: { vfrom: p.vfrom, vto: p.vto, vgrup: p.vgrup },
    }),
  );
  readonly returnPreview = new TemelStore(
    (p: QueryParameters) =>
      this.api.get<RentalReturnPreview>(`${ROOT}/${this.id ?? ''}/donus-hesapla`, {
        parametreler: p,
        context: SILENT,
      }),
    { oncekiVeriyiKoru: true },
  );
  readonly sourceReservation = new TemelStore((id: string) =>
    this.api.get<SourceReservationResponse>(`${ROOT}/${id}/kaynak-rezervasyon`, {
      context: SILENT,
    }),
  );
  readonly karne = new TemelStore((id: string) =>
    this.api.get<ScorecardSummary>(`${ROOT}/${id}/karne-ozeti`, { context: SILENT }),
  );
  readonly sourceSuggestions = this.suggestionStore('rezervasyon-kaynagi');
  readonly customCodeSuggestions = this.suggestionStore('ozel-kod');
  /** F4.3b: Müşteri sekmesinin salt-okunur cari özeti (kimlik/belge no YALNIZ maskeli — sunucu kuralı). */
  readonly customerSummary = new TemelStore((id: string) =>
    this.api.get<RentalCustomerSummary>(`${ROOT}/${id}/musteri-ozet`, { context: SILENT }),
  );
  /** F4.3b: ek hizmet matrisi (tanım fiyat/KDV'si — yalnız gösterim; satır tutarı `hesapla`'dan). */
  readonly addOnCatalog = new TemelStore(() =>
    this.api.get<RentalAddOnCatalog>(`${ROOT}/ek-hizmet-katalogu`, { context: SILENT }),
  );
  readonly documentTemplates = new TemelStore(() =>
    this.api.get<readonly SecimSecenegi[]>('/api/ui/v1/secim/belge-sablonu', {
      parametreler: { tur: 0, limit: SUGGESTION_LIMIT },
      context: SILENT,
    }),
  );

  // ─── seçim kaynakları ──────────────────────────────────────────────────────────────────────
  readonly customerDataSource = this.authorizedSource(serverSelectionSource('musteri'));
  readonly locationDataSource = this.authorizedSource(serverSelectionSource('lokasyon'));
  readonly staffDataSource = this.authorizedSource(serverSelectionSource('personel'));
  private readonly serverVehicle = serverSelectionSource('arac');
  private readonly serverAddOn = serverSelectionSource('ek-hizmet');
  /** Müsait liste getirildiyse araç araması O LİSTEDE (Blazor: araç listesi müsaitlerle süzülür). */
  readonly vehicleSource: SelectionSource<AracSecenegi> = (search, limit) => {
    const list = this.musait.veri();
    if (!list) return this.operasyon() ? this.serverVehicle(search, limit) : of([]);
    const key = trSearchKey(search);
    return of(
      list
        .map(vehicleOption)
        .filter((a) => key === '' || trSearchKey(a.etiket).includes(key))
        .slice(0, limit),
    );
  };
  /**
   * Ek hizmet tanımları (kayıtlı kiraya ekleme + yeni kirada katalog dışı ekleme): katalog TAMSA ondan (yerel
   * arama), etiket Blazor gibi "Ad (birim net)". Katalog yüklenmediyse ya da KESİLDİYSE (toplam > satır — #262 L2:
   * 200'den sonrası eklenemiyordu) F1.6 sunucu araması (`q`) — tüm tanımlarda arar; katalogdaki öğe yine fiyatlı
   * etiketle. Sistem ücret kalemleri (SYS-*) manuel seçilemez (sunucu da reddeder).
   */
  readonly addOnSource: SelectionSource = (search, limit) => {
    if (!this.operasyon()) return of([]);
    const catalog = this.addOnCatalog.veri() ?? null;
    const currency = this.rentalCurrency();
    const inCatalog = new Map((catalog?.ogeler ?? []).map((x) => [x.id, x]));
    if (catalog === null || catalogTruncated(catalog)) {
      return this.serverAddOn(search, limit).pipe(
        map((list) =>
          list
            .filter((x) => !isSystemItem(x.kod))
            .map((x) => {
              const k = inCatalog.get(x.id);
              return { id: x.id, etiket: k ? addOnLabel(k, currency) : x.etiket };
            }),
        ),
      );
    }
    const key = trSearchKey(search);
    return of(
      catalog.ogeler
        .filter(
          (x) => key === '' || trSearchKey(x.ad).includes(key) || trSearchKey(x.kod).includes(key),
        )
        .slice(0, limit)
        .map((x) => ({ id: x.id, etiket: addOnLabel(x, currency) })),
    );
  };

  // ─── türetilmiş durum ──────────────────────────────────────────────────────────────────────
  /** L5: son BAŞARILI detay okuması (yalnız `detayGeldi` yazar). */
  private readonly lastGoodDetail = signal<RentalDetailResponse | null>(null);
  /**
   * L5: ekranın gösterdiği detay. Tazeleme sonucu belirsiz bir hatayla (5xx, ağ) biterse form ve finans paneli
   * KAYBOLMAZ — son iyi veriyle kalır, üstte hata bandı (`tazelemeHatasi`) çıkar. Kesin hatalarda (404 silinmiş,
   * 403 kapsam dışı) eski veri gösterilmez: kayıt yoktur ya da artık görülemez.
   */
  readonly visibleDetail: Signal<RentalDetailResponse | null> = computed(() => {
    const v = this.detay.veri();
    if (v) return v;
    // #318 L1: belirsiz hatadan sonraki yeniden okuma sürerken de son iyi veri kalır. Store hata durumunda veriyi
    // bıraktığı için `onceki` boş gelir; yoksa sayfa iskelete düşer, finans paneli yeniden kurulur ve donmuş
    // (sonucu bilinmeyen) tahsilat anahtarı kaybolurdu. Kesin hatada `sonIyiDetay` zaten temizlenmiştir.
    if (this.detay.tur() === 'yukleniyor') return this.lastGoodDetail();
    const h = this.detay.hata();
    // Yalnız geçici hatalar (5xx, ağ, 429 — #318): kodsuz 404 istemcide `bilinmeyen` olur (silinmiş kayıt) — o
    // eski veriyle GÖSTERİLMEZ.
    return h && TRANSIENT_DETAIL_ERRORS.has(h.kod) ? this.lastGoodDetail() : null;
  });
  /** L5 bandı: tazeleme belirsiz hatayla bitti ama ekran son iyi veriyle duruyor. */
  readonly tazelemeHatasi = computed(() =>
    this.detay.tur() === 'hata' && this.visibleDetail() !== null
      ? (this.detay.hata() ?? null)
      : null,
  );
  readonly kira: Signal<RentalContract | null> = computed(() => this.visibleDetail()?.kira ?? null);
  /** Oturumun etkin izni (seçim/varsayılan uçları bunu ister); kayıtta asıl kapı sunucuda. */
  private readonly owPermission = this.oturum.izinVar('OperationsWrite');
  readonly operasyon = computed(() =>
    this.yeni ? this.owPermission : (this.visibleDetail()?.yetkiler.operasyon ?? this.owPermission),
  );
  readonly silme = computed(() => this.visibleDetail()?.yetkiler.silme ?? false);
  readonly finans = computed(() =>
    this.yeni
      ? this.oturum.izinVar('FinanceWrite')
      : (this.visibleDetail()?.yetkiler.finans ?? false),
  );
  /**
   * F7.3: cari ekstresi bağlantısı (SPA `/cariler/:id/ekstre`) yalnız ekstre rotasının kapısıyla görünür —
   * FinanceWrite ∨ ViewReports (#295 KVKK M1; bakiye + tüm şubelerin hareketleri). Kural `canSeeStatement`'te tek.
   */
  readonly canSeeCustomerStatement = computed(() => canSeeStatement((p) => this.oturum.izinVar(p)));
  /** Risk onayı yalnız Yönetici/Admin (servis rolü AYRICA doğrular) — Blazor `AuthorizeView Roles`. */
  readonly isRiskApprovalVisible = computed(() => {
    const rol = this.oturum.ben()?.rol;
    return rol === 'Admin' || rol === 'Yonetici';
  });
  readonly durum = computed(() => this.kira()?.durum ?? null);
  readonly kirada = computed(() => this.durum() === 'Kirada');
  readonly iptal = computed(() => this.durum() === 'Iptal');
  readonly delivered = computed(() => {
    const k = this.kira();
    return k !== null && k.cikisKm !== null;
  });
  /**
   * #261 yeniden doğrulama N1: bir işlemden (teslim, ek hizmet, uzat, provizyon, sabit panel para işlemi) ya da
   * Kaydet'ten sonra kayıt yeniden okunurken Kaydet PASİF — işlem kiranın sürümünü değiştirdi; tazeleme bitmeden
   * gönderilen PUT kendi değişikliği yüzünden 409 "başka oturumda değişti" alıyordu. İşlem yanıtındaki sürümü
   * tabana yazmak yetmez: işlemin yazdığı alanlar (provizyon tarihi, çıkış km…) forma birleşmeden gönderilen tam
   * değiştirme onları geri alırdı — birleştirme detay yenilemesinde yapılır.
   */
  readonly isRecordRefreshing = computed(() => !this.yeni && this.detay.isLoading());
  /** L5: tazeleme başarısızken de Kaydet PASİF — ekrandaki veri (sürüm dahil) bayat olabilir; bant "yeniden dene" der. */
  readonly canSave = computed(
    () =>
      this.operasyon() &&
      !this.iptal() &&
      !this.isRecordRefreshing() &&
      this.tazelemeHatasi() === null,
  );

  // Sabit seçenek listeleri (sunucudan; kayıtlı eski değer listede yoksa eklenir — kaybolmaz).
  private readonly vars = computed(() => this.defaults.veri());
  readonly kiralamaTurleri = computed(() =>
    optionList(this.vars()?.kiralamaTurleri, this.kira()?.kiralamaTuru),
  );
  readonly fiyatTurleri = computed(() =>
    optionList(this.vars()?.fiyatTurleri, this.kira()?.fiyatTuru),
  );
  readonly dovizler = computed(() => optionList(this.vars()?.dovizler, this.kira()?.doviz));
  readonly faturalamaTipleri = computed(() =>
    optionList(this.vars()?.faturalamaTipleri, this.kira()?.faturalamaTipi),
  );
  readonly odemeSekilleri = computed(() => this.vars()?.odemeSekilleri ?? []);
  readonly documentTemplateOptions = computed(() => {
    const list = (this.documentTemplates.veri() ?? []).map((s) => ({
      deger: s.id,
      etiket: s.etiket,
    }));
    const existing = this.kira()?.belgeSablonId;
    if (existing && !list.some((s) => s.deger === existing)) {
      list.push({ deger: existing, etiket: this.t('kiraFormu.alan.kayitliSablon') });
    }
    return list;
  });

  readonly selectedVehicle = this.valueSignal(this.form.controls.arac);
  readonly selectedCustomer = this.valueSignal(this.form.controls.musteri);
  private readonly currencyValue = this.valueSignal(this.form.controls.doviz);
  readonly rentalCurrency = computed(() =>
    isoCurrency(this.yeni ? this.currencyValue() : this.kira()?.doviz),
  );
  /** Yeni kirada ek hizmet satırları değişince şablon yeniden çizilsin (FormArray sinyal değil). */
  readonly extraRowVersion = signal(0);
  readonly isNewCustomerOpen = signal(false);
  /** Müsaitlik sonucu notu (liste yenilendi / önceki araç müsait değil). */
  readonly availabilityNote = signal<string | null>(null);
  /** `?varac=` araç kimliği; müsait liste gelince etiketiyle çözülür. */
  private pendingVehicle: string | null = null;
  /**
   * Son okunan sunucu hâli (F4.3 adversarial F2): `surum` PUT'a gider (bayatsa 409 `cakisma`), değerler
   * kirli formla birleştirmede "sunucu bu arada neyi değiştirdi" karşılaştırmasının tabanıdır.
   */
  private taban: RentalContract | null = null;
  private baseValues: RentalServerValues | null = null;
  /**
   * #261 N2: sürüm çakışmasından (409 `cakisma`) sonra TEK SEFERLİK sessiz yeniden gönderim hakkı. Güncel kayıt
   * gelince birleştirmede kullanıcının DOKUNDUĞU alanlarla çakışan sunucu değişikliği YOKSA (ör. başka sekmede
   * 5 TL tahsilat — PUT alanlarına dokunmaz) birleştirilmiş gövde yeni sürümle yeniden gönderilir; çakışan alan
   * varsa bant + işaretleme (bugünkü davranış). Yalnız sürüm GERÇEKTEN değiştiyse (başka tür 409 — ör. müsaitlik
   * — aynı sonucu verir, tekrar edilmez) ve yalnız bir kez (ikinci 409 yeniden göndermez).
   */
  private autoRetry: { readonly surum: string; readonly gonder: () => void } | null = null;

  constructor() {
    for (const subscription of bindMirrors(this.form)) {
      this.destroyRef.onDestroy(() => subscription.unsubscribe());
    }
    if (this.owPermission) {
      this.defaults.yukle();
      this.documentTemplates.yukle();
      this.addOnCatalog.yukle();
      // Datalist önerileri YAZILANLA sunucuda süzülür (`q`; en çok 20) — ilk 20'nin dışındaki kayıt da bulunur.
      this.suggestionSearch(this.form.controls.kaynak, this.sourceSuggestions);
      this.suggestionSearch(this.form.controls.ozelKod, this.customCodeSuggestions);
    }
    if (this.id !== null) this.startEdit(this.id);
    else this.startNew();
  }

  // ─── yeni kira ─────────────────────────────────────────────────────────────────────────────

  private startNew(): void {
    if (!this.operasyon()) this.form.disable();
    this.applyQuery(resolveRentalQuery((name) => this.rota.snapshot.queryParamMap.get(name)));
    // Açık "yeni kira" sekmesine yeni bir bağlantıyla gelinirse (aynı bileşen yaşar) bağlantı uygulanır.
    this.rota.queryParamMap
      .pipe(skip(1), takeUntilDestroyed(this.destroyRef))
      .subscribe((p) => this.applyQuery(resolveRentalQuery((name) => p.get(name))));

    // Tenant varsayılan fiyat türü — yalnız ön doldurma, kullanıcı seçmediyse.
    effect(() => {
      const v = this.defaults.veri();
      untracked(() => {
        const check = this.form.controls.fiyatTuru;
        if (v?.fiyatTuru && check.value === null && check.pristine) {
          check.setValue(v.fiyatTuru);
          this.form.controls.ayna.controls.fiyatTuru.setValue(v.fiyatTuru, { emitEvent: false });
        }
      });
    });

    // Müsait liste geldi: araç seçimi/tarihler (Blazor `bindMusaitAjax` davranışı).
    effect(() => {
      const list = this.musait.veri();
      if (list) untracked(() => this.availableListReceived(list));
    });

    // Canlı hesap: 300 ms gecikme, son istek kazanır (TemelStore switchMap), tarih yoksa istek yok.
    this.form.valueChanges
      .pipe(
        startWith(null),
        debounceTime(300),
        map(() => calculateParams(this.form.getRawValue())),
        distinctUntilChanged((a, b) => JSON.stringify(a) === JSON.stringify(b)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((p) => (p === null ? this.hesap.reset() : this.hesap.yukle(p)));
  }

  /** Sorgu sözleşmesi: araç, müşteri, müsaitlik penceresi (bozuk parametre sessizce yok sayılır). */
  applyQuery(s: KiraSorgusu): void {
    const window = availabilityWindow(s);
    this.availabilityForm.reset({ vfrom: s.vfrom, vto: s.vto, vgrup: s.vgrup });
    if (s.musteriId !== null && this.form.controls.musteri.value?.id !== s.musteriId) {
      this.form.controls.musteri.setValue({
        id: s.musteriId,
        etiket: this.t('kiraFormu.musteri.baglantidan'),
      });
      this.resolveCustomerLabel(s.musteriId);
    }
    if (window) {
      const dates = datesFromWindow(window);
      this.form.patchValue(dates);
      this.musait.yukle(window);
    }
    if (s.varac !== null) {
      this.pendingVehicle = s.varac;
      this.form.controls.arac.setValue({
        id: s.varac,
        etiket: this.t('kiraFormu.arac.baglantidan'),
      });
      // Pencere yoksa (araç durumu "Kirala"): etiket kimlikle sunucudan (F4.3b `secim/arac/{id}`); tam kart
      // için bugün–yarın müsait listesine de bakılır. Bulunamazsa kimlik yine geçerlidir (kayıt YALNIZ kimlikle).
      if (!window) {
        this.resolveVehicleLabel(s.varac);
        this.resolveVehicleCard(s.varac);
      }
    }
  }

  /** `?musteriId=` → gerçek görünen ad (F4.3b `secim/musteri/{id}`; PII yok). Hata/yok → geçici etiket kalır. */
  private resolveCustomerLabel(customerId: string): void {
    if (!this.operasyon()) return;
    this.api
      .get<SelectionCustomer>(`/api/ui/v1/secim/musteri/${customerId}`, { context: SILENT })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (m) => {
          const check = this.form.controls.musteri;
          if (m?.id === customerId && check.value?.id === customerId) {
            check.setValue({ id: m.id, etiket: m.etiket });
          }
        },
        error: () => undefined,
      });
  }

  /** `?varac=` (pencere yok) → plaka etiketi kimlikle (şube kapsamı sunucuda; kapsam dışı → geçici etiket). */
  private resolveVehicleLabel(vehicleId: string): void {
    if (!this.operasyon()) return;
    this.api
      .get<SelectionVehicle>(`/api/ui/v1/secim/arac/${vehicleId}`, { context: SILENT })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (a) => {
          const check = this.form.controls.arac;
          // Müsait listeden tam kart zaten geldiyse (marka dolu) ezilmez.
          if (a?.id === vehicleId && check.value?.id === vehicleId && !check.value.marka) {
            check.setValue({ ...a });
          }
        },
        error: () => undefined,
      });
  }

  private resolveVehicleCard(vehicleId: string): void {
    const day = bugun();
    this.api
      .get<AvailableVehicle[]>(`${ROOT}/musait-arac`, {
        parametreler: { vfrom: day, vto: addDays(day, 1) },
        context: SILENT,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => {
          const found = list.find((a) => a.id === vehicleId);
          if (found && this.form.controls.arac.value?.id === vehicleId) {
            this.form.controls.arac.setValue(vehicleOption(found));
          }
        },
        error: () => undefined,
      });
  }

  getAvailable(): void {
    this.availabilityForm.markAllAsTouched();
    const d = this.availabilityForm.getRawValue();
    const window = availabilityWindow({
      vfrom: d.vfrom,
      vto: d.vto,
      vgrup: d.vgrup?.trim() || null,
    });
    if (!window) {
      if (d.vfrom && d.vto) {
        this.availabilityForm.controls.vto.setErrors({
          [SERVER_ERROR]: [this.t('kiraFormu.arac.aralikHatali')],
        });
      }
      return;
    }
    this.availabilityNote.set(null);
    this.musait.yukle(window);
  }

  clearAvailable(): void {
    this.musait.reset();
    this.availabilityNote.set(null);
  }

  private availableListReceived(list: readonly AvailableVehicle[]): void {
    const vehicle = this.form.controls.arac;
    const pending = this.pendingVehicle;
    this.pendingVehicle = null;
    const targetId = pending ?? vehicle.value?.id ?? null;
    const found = targetId === null ? undefined : list.find((a) => a.id === targetId);
    if (found) {
      vehicle.setValue(vehicleOption(found));
    } else if (targetId !== null) {
      // Blazor: önceki seçim artık müsait değilse seçim düşer (kayıt da reddedilirdi).
      vehicle.setValue(null);
      vehicle.markAsDirty();
      this.availabilityNote.set(this.t('kiraFormu.arac.secimMusaitDegil'));
    }
    // Tarihler yalnız BOŞSA doldurulur (kullanıcının girdiği ezilmez).
    const window = availabilityWindow(this.availabilityForm.getRawValue());
    if (window && !this.form.controls.basTar.value && !this.form.controls.bitTar.value) {
      this.form.patchValue(datesFromWindow(window));
    }
    if (!this.availabilityNote()) {
      this.availabilityNote.set(this.t('kiraFormu.arac.musaitSayisi', { sayi: list.length }));
    }
  }

  selectVehicle(a: AvailableVehicle): void {
    this.form.controls.arac.setValue(vehicleOption(a));
    this.form.controls.arac.markAsDirty();
  }

  addAddOnRow(definition: SecimSecenegi | null): void {
    if (definition === null) return;
    const array = this.form.controls.ekHizmetler;
    if (!array.controls.some((s) => s.controls.tanim.value?.id === definition.id)) {
      array.push(addOnRow(definition));
      array.markAsDirty();
      this.extraRowVersion.update((s) => s + 1);
    }
  }

  /** Matris onay kutusu (Blazor `ekSecim`): işaret → satır (miktar 1), kaldır → satır çıkar. */
  addOnSelection(oge: AddOnCatalogItem, selected: boolean): void {
    const order = this.form.controls.ekHizmetler.controls.findIndex(
      (s) => s.controls.tanim.value?.id === oge.id,
    );
    if (selected && order < 0) this.addAddOnRow({ id: oge.id, etiket: oge.ad });
    else if (!selected && order >= 0) this.deleteAddOnRow(order);
  }

  deleteAddOnRow(order: number): void {
    const array = this.form.controls.ekHizmetler;
    array.removeAt(order);
    array.markAsDirty();
    this.extraRowVersion.update((s) => s + 1);
  }

  /** Ana form müşteri dışında geçerli mi? Geçersiz alanlar "dokunuldu" olur (hata görünür); müşteri alanı hariç. */
  private isValidExceptCustomer(): boolean {
    let valid = true;
    const gez = (group: FormGroup, atla: string): void => {
      for (const [name, check] of Object.entries(
        group.controls as Record<string, AbstractControl>,
      )) {
        if (name === atla) continue;
        if (check instanceof FormGroup) {
          gez(check, atla);
          continue;
        }
        check.markAllAsTouched();
        if (check.invalid) valid = false;
      }
    };
    gez(this.form, 'musteri');
    return valid;
  }

  isNewCustomerFilled(): boolean {
    return Object.values(this.newCustomerForm.getRawValue()).some((v) => (v ?? '') !== '');
  }

  /** Hızlı müşteri: cari ANINDA açılır ve seçilir; form yerinde kalır. PII yalnız bu istekte. */
  saveNewCustomer(after?: () => void): void {
    const d = this.newCustomerForm.getRawValue();
    this.newCustomerSubmission.gonder(
      this.newCustomerForm,
      () =>
        this.api.post<QuickCustomerResponse>(`${ROOT}/musteri`, {
          ...d,
          dogumTarihi: toDayMoment(d.dogumTarihi),
          ehliyetTarihi: toDayMoment(d.ehliyetTarihi),
        }),
      {
        basarili: (m) => {
          const selection: SecimSecenegi = { id: m.id, etiket: m.etiket };
          this.form.controls.musteri.setValue(selection);
          this.form.controls.musteri.markAsDirty();
          this.newCustomerForm.reset();
          this.isNewCustomerOpen.set(false);
          this.toast.basari(this.t('kiraFormu.musteri.kaydedildi', { ad: m.etiket }));
          after?.();
        },
      },
    );
  }

  // ─── düzenleme ─────────────────────────────────────────────────────────────────────────────

  private startEdit(id: string): void {
    this.detay.yukle(id);
    let first = true;
    effect(() => {
      const d = this.detay.veri();
      if (!d) return;
      untracked(() => {
        if (this.detay.tur() === 'hazir') this.lastGoodDetail.set(d);
        this.detailLoaded(d, first);
        first = false;
      });
    });
    // N2: yeniden okuma başarısızsa sessiz yeniden gönderim hakkı düşer (sonraki bir okumada kendiliğinden
    // gönderim olmasın).
    effect(() => {
      if (this.detay.tur() === 'hata') this.autoRetry = null;
    });
    // L5 (#280 KVKK L-3): a definitive error (403/401 with a code, 404 without one) drops the last good detail
    // for good — otherwise a later 5xx/network failure would bring stale customer/vehicle/finance data back.
    effect(() => {
      const error = this.detay.hata();
      if (error && !TRANSIENT_DETAIL_ERRORS.has(error.kod)) {
        untracked(() => this.lastGoodDetail.set(null));
      }
    });
    effect(() => {
      const v = this.defaults.veri();
      if (v && this.deliveryForm.pristine) {
        untracked(() => this.deliveryForm.controls.cikisYakit.setValue(Number(v.cikisYakit)));
      }
    });
    // Sekmeye dönüşte kayıt yeniden okunur (başka oturum/işlem değiştirmiş olabilir): temiz form güncel hâle
    // gelir, kirli formda yalnız dokunulmamış alanlar (F4.3 adversarial F2). İlk görünüm sayılmaz.
    let lastView: number | null = null;
    effect(() => {
      const n = this.sekme.onaGelme();
      untracked(() => {
        if (lastView !== null && n !== lastView) this.yenile();
        lastView = n;
      });
    });
    // Dönüş canlı önizlemesi (ReturnMath): girdiler değişince, 300 ms gecikmeyle.
    this.returnForm.valueChanges
      .pipe(debounceTime(300), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.previewReturn());
  }

  private detailLoaded(d: RentalDetailResponse, first: boolean): void {
    const k = d.kira;
    this.sekme.etiketAyarla(this.t('kiraFormu.sekmeEtiketi', { no: k.sozlesmeNo }));
    // Temiz form sunucu hâline sıfırlanır. KİRLİ formda kullanıcının dokunduğu alanlar EZİLMEZ; dokunmadığı
    // alanlar sunucu değerine çekilir — işlem (provizyon al, teslim…) ya da başka oturumun yazdığı değer bayat
    // tam değiştirmeyle geri alınmasın (F4.3 adversarial F2 / P261-10). İkisi de değiştiyse alan işaretlenir.
    const newItem = valuesFromDetail(d, this.t('kiraFormu.alan.kayitBulunamadi'));
    const auto = this.autoRetry;
    this.autoRetry = null;
    if (!this.form.dirty) {
      resetForm(this.form, newItem);
      this.lockByStatus(d);
    } else {
      const conflicting = mergeServerValues(this.form, newItem, this.baseValues);
      this.lockByStatus(d);
      if (conflicting.length > 0) {
        this.markConflict(conflicting);
      } else if (auto && k.surum !== auto.surum && this.isInSavableState(d)) {
        // N2: çakışma yok → birleştirilmiş gövde yeni sürümle, sessiz ve tek sefer (409 bandı kapanır).
        if (this.bant.bant()?.kod === 'cakisma') this.bant.kapat();
        queueMicrotask(auto.gonder);
      }
    }
    this.taban = k;
    this.baseValues = newItem;
    if (this.deliveryForm.pristine) {
      this.deliveryForm.reset({
        cikisKm: d.arac ? Number(d.arac.km) : 0,
        cikisYakit: Number(this.defaults.veri()?.cikisYakit ?? 8),
      });
    }
    if (this.returnForm.pristine) {
      this.returnForm.reset(
        {
          donusKm: k.cikisKm === null ? null : Number(k.cikisKm),
          donusYakit: k.cikisYakit === null ? 8 : Number(k.cikisYakit),
          gercekDonus: k.bitTar,
          kmHediye: null,
          bitisSebebi: null,
          teslimAlanPersonel: null,
        },
        { emitEvent: false },
      );
      this.previewReturn();
    }
    if (this.extendForm.pristine) {
      this.extendForm.reset({
        yeniBitTar: new Date(Date.parse(k.bitTar) + 86_400_000).toISOString(),
      });
    }
    if (this.closePreAuthForm.pristine) {
      this.closePreAuthForm.reset({ kapamaTutar: k.provizyon ?? null, iade: false });
    }
    if (first) {
      if (k.reservationId) this.sourceReservation.yukle(k.id);
      if (d.yetkiler.finans) this.karne.yukle(k.id);
      this.customerSummary.yukle(k.id);
    }
  }

  private lockByStatus(d: RentalDetailResponse): void {
    const k = d.kira;
    if (!d.yetkiler.operasyon || k.durum === 'Iptal') {
      this.form.disable({ emitEvent: false });
    } else if (k.durum === 'Tamamlandi') {
      for (const name of FROZEN_WHEN_COMPLETED)
        this.form.controls[name].disable({ emitEvent: false });
    }
    syncMirrorStates(this.form);
  }

  /** Hem kullanıcının hem başka oturumun değiştirdiği alanlar: alanın altında not + bant (engellemez —
   *  bir sonraki Kaydet'te sunucu hataları temizlenir, kullanıcının değeri bilinçli olarak yazılır). */
  /** Yeni okunan kayıtta Kaydet anlamlı mı (izin + iptal değil) — sessiz yeniden gönderim için. */
  private isInSavableState(d: RentalDetailResponse): boolean {
    return d.yetkiler.operasyon && d.kira.durum !== 'Iptal';
  }

  private markConflict(fields: readonly (keyof RentalServerValues)[]): void {
    const message = this.t('kiraFormu.cakisma.alan');
    for (const name of fields) {
      const check = this.form.controls[name] as AbstractControl<unknown>;
      check.setErrors({ ...(check.errors ?? {}), [SERVER_ERROR]: [message] });
      check.markAsTouched();
    }
    copyErrorToMirrors(this.form);
    this.bant.show({
      tur: 'uyari',
      mesaj: this.t('kiraFormu.cakisma.bant', { sayi: fields.length }),
      kod: 'cakisma',
    });
  }

  private previewReturn(): void {
    const k = this.kira();
    const d = this.returnForm.getRawValue();
    if (!k || k.durum !== 'Kirada' || k.cikisKm === null) return;
    if (d.donusKm === null || !d.gercekDonus) {
      this.returnPreview.reset();
      return;
    }
    this.returnPreview.yukle({
      donusKm: d.donusKm,
      donusYakit: d.donusYakit ?? 0,
      gercekDonus: d.gercekDonus,
      kmHediye: d.kmHediye,
    });
  }

  yenile(): void {
    this.detay.yenile();
  }

  // ─── kaydet ────────────────────────────────────────────────────────────────────────────────

  /**
   * Ana formu gönderir. Yeni kirada müşteri seçilmemiş ama "yeni müşteri" alanları doluysa önce cari
   * açılır, sonra kira (Blazor tek adım davranışı). `gecersizeGit` bileşenden: hatalı alanın sekmesine.
   */
  kaydet(goToInvalid: () => void, autoAllowed = true): void {
    if (this.yeni && this.form.controls.musteri.value === null && this.isNewCustomerFilled()) {
      // F4.3 adversarial F7: önce ANA form (müşteri dışında) doğrulanır — araç/tarih eksikken cari açılıp
      // kira hiç açılmazsa PII'li yetim cari kalırdı.
      if (!this.isValidExceptCustomer()) {
        goToInvalid();
        return;
      }
      this.isNewCustomerOpen.set(true);
      this.saveNewCustomer(() => this.kaydet(goToInvalid));
      return;
    }
    const mapping = {
      ...SERVER_FIELD_MAPPING,
      // Görünmeyen kontrole yazılan hata kaybolmasın: form üstü hataya düşer.
      ...(this.isRiskApprovalVisible() ? {} : { riskOnay: '-' }),
    };
    const invalid = (): void => {
      copyErrorToMirrors(this.form);
      goToInvalid();
    };
    if (this.yeni) {
      this.kayit.gonder(
        this.form,
        () => this.api.post<CreateRentalResponse>(ROOT, createBody(this.form.getRawValue())),
        { esleme: mapping, gecersiz: invalid, basarili: (y) => this.created(y) },
      );
      return;
    }
    const id = this.id ?? '';
    const submittedVersion = this.taban?.surum ?? '';
    this.kayit.gonder(
      this.form,
      () =>
        this.api.put<RentalContract>(
          `${ROOT}/${id}`,
          updateBody(this.form.getRawValue(), {
            surum: submittedVersion,
            provizyonTarihAni: this.taban?.provizyonTarih ?? null,
            provizyonTarihDegisti: this.form.controls.provizyonTarih.dirty,
          }),
          { context: requestContext({ mukerrerdeYenile: () => this.yenile() }) },
        ),
      {
        esleme: mapping,
        gecersiz: invalid,
        // Bayat sürüm (409 cakisma): form SİLİNMEZ; güncel kayıt okunur, dokunulmamış alanlar güncellenir,
        // dokunulanlar korunur (detayGeldi → birleştirme); kullanıcı kontrol edip yeniden kaydeder.
        hata: (h) => {
          if (h.kod !== 'cakisma') return;
          // N2: alansız (sürüm) çakışmada tek seferlik sessiz yeniden gönderim hakkı — karar güncel kayıt
          // birleştirilince (detayGeldi) verilir.
          this.autoRetry =
            autoAllowed && h.alanlar === undefined
              ? { surum: submittedVersion, gonder: () => this.kaydet(goToInvalid, false) }
              : null;
          this.yenile();
        },
        basarili: () => {
          this.toast.basari(this.t('kiraFormu.bildirim.kaydedildi'));
          this.yenile();
        },
      },
    );
  }

  private created(y: CreateRentalResponse): void {
    if (y.uyari) this.toast.uyari(y.uyari, { sure: 0 });
    this.toast.basari(this.t('kiraFormu.bildirim.olusturuldu', { no: y.sozlesmeNo }));
    // Kayıt yapıldı: "yeni kira" sekmesi temiz bir forma döner (sonraki kira için).
    resetForm(this.form, { fiyatTuru: this.defaults.veri()?.fiyatTuru ?? null });
    this.extraRowVersion.update((s) => s + 1);
    this.hesap.reset();
    this.musait.reset();
    this.availabilityForm.reset();
    this.availabilityNote.set(null);
    // Çıkış ofisi kapsamı sunucuda GİRİŞTE denetlenir (F4.1 M1): dönen kira oturumun kapsamında.
    void this.router.navigate(['/kiralar', y.id]);
  }

  // ─── operasyon eylemleri (F4.1 uçları) ─────────────────────────────────────────────────────

  deliver(): void {
    const d = this.deliveryForm.getRawValue();
    this.deliverySubmission.gonder(
      this.deliveryForm,
      () =>
        this.api.post<RentalContract>(`${ROOT}/${this.id ?? ''}/teslim`, {
          cikisKm: d.cikisKm,
          cikisYakit: d.cikisYakit,
        }),
      { basarili: () => this.actionDone(this.deliveryForm, 'kiraFormu.bildirim.teslimEdildi') },
    );
  }

  doReturn(): void {
    const d = this.returnForm.getRawValue();
    this.returnSubmission.gonder(
      this.returnForm,
      () =>
        this.api.post<RentalContract>(`${ROOT}/${this.id ?? ''}/donus`, {
          donusKm: d.donusKm,
          donusYakit: d.donusYakit,
          gercekDonus: d.gercekDonus,
          kmHediye: d.kmHediye,
          bitisSebebi: d.bitisSebebi,
          teslimAlanPersonelId: d.teslimAlanPersonel?.id ?? null,
        }),
      {
        esleme: { teslimAlanPersonelId: 'teslimAlanPersonel' },
        basarili: () => this.actionDone(this.returnForm, 'kiraFormu.bildirim.donusYapildi'),
      },
    );
  }

  extend(): void {
    const d = this.extendForm.getRawValue();
    this.extendSubmission.gonder(
      this.extendForm,
      () =>
        this.api.post<RentalContract>(`${ROOT}/${this.id ?? ''}/uzat`, {
          yeniBitTar: d.yeniBitTar,
        }),
      { basarili: () => this.actionDone(this.extendForm, 'kiraFormu.bildirim.uzatildi') },
    );
  }

  closePreAuth(): void {
    const d = this.closePreAuthForm.getRawValue();
    this.closePreAuthSubmission.gonder(
      this.closePreAuthForm,
      () =>
        this.api.post<RentalContract>(`${ROOT}/${this.id ?? ''}/provizyon/kapat`, {
          kapamaTutar: d.iade ? null : d.kapamaTutar,
          iade: d.iade ?? false,
        }),
      {
        basarili: () =>
          this.actionDone(this.closePreAuthForm, 'kiraFormu.bildirim.provizyonKapandi'),
      },
    );
  }

  takePreAuth(): void {
    this.lockedAction(
      this.takePreAuthLock,
      () => this.api.post<RentalContract>(`${ROOT}/${this.id ?? ''}/provizyon/al`, {}),
      'kiraFormu.bildirim.provizyonAlindi',
    );
  }

  async cancelAction(): Promise<void> {
    const approval = await this.approval.ask({
      baslik: this.t('kiraFormu.iptal.baslik'),
      mesaj: this.t('kiraFormu.iptal.mesaj'),
      onayEtiketi: this.t('kiraFormu.iptal.onayla'),
      tehlikeli: true,
    });
    if (!approval) return;
    this.lockedAction(
      this.cancelLock,
      () => this.api.post<RentalContract>(`${ROOT}/${this.id ?? ''}/iptal`, {}),
      'kiraFormu.bildirim.iptalEdildi',
    );
  }

  /**
   * Ek hizmet ekleme: gönderim başına `Idempotency-Key` (sunucu zorunlu tutar). Aynı gönderimin yeniden
   * denemesi (ağ/5xx) aynı anahtarla gider → ikinci kalem yazılmaz, 409 `mukerrer`. 409'da otomatik yeniden
   * gönderim yok; kayıt yeniden yüklenir, `mevcut.ayniIcerik` ise (kendi tekrarım) form temizlenir.
   */
  addAddOn(): void {
    const d = this.addAddOnForm.getRawValue();
    this.addAddOnSubmission.gonder(
      this.addAddOnForm,
      (key) =>
        this.api.post<RentalAddOnResponse>(
          `${ROOT}/${this.id ?? ''}/ek-hizmetler`,
          { ekHizmetTanimId: d.tanim?.id ?? null, miktar: d.miktar },
          {
            islemAnahtari: key,
            context: requestContext({ mukerrerdeYenile: () => this.yenile() }),
          },
        ),
      {
        esleme: { ekHizmetTanimId: 'tanim' },
        basarili: () => {
          this.addAddOnForm.reset({ tanim: null, miktar: 1 });
          this.toast.basari(this.t('kiraFormu.bildirim.ekHizmetEklendi'));
          this.yenile();
        },
        hata: (h) => {
          if (h.kod === 'mukerrer' && h.mevcut?.ayniIcerik === true)
            this.addAddOnForm.reset({ tanim: null, miktar: 1 });
        },
      },
    );
  }

  async deleteAddOn(item: AddOnItem): Promise<void> {
    const approval = await this.approval.ask({
      baslik: this.t('kiraFormu.ekHizmet.silBaslik'),
      mesaj: this.t('kiraFormu.ekHizmet.silMesaj', { ad: item.ad }),
      tehlikeli: true,
    });
    if (!approval) return;
    this.lockedAction(
      this.deleteAddOnLock,
      () =>
        this.api.delete<RentalAddOnResponse>(`${ROOT}/${this.id ?? ''}/ek-hizmetler/${item.id}`),
      'kiraFormu.bildirim.ekHizmetSilindi',
    );
  }

  private actionDone(form: AbstractControl, message: CeviriAnahtari): void {
    form.markAsPristine();
    this.toast.basari(this.t(message));
    this.yenile();
  }

  private lockedAction<T>(
    lockEntry: SubmitLock,
    request: () => Observable<T>,
    message: CeviriAnahtari,
  ): void {
    lockEntry
      .gonder(request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.toast.basari(this.t(message));
          this.yenile();
        },
        error: (error: unknown) => this.actionError(error),
      });
  }

  /** Alan formu olmayan eylemin hatası: interceptor göstermediyse (400 doğrulama…) hata bildirimi. */
  private actionError(raw: unknown): void {
    const error = toApiError(raw);
    if (genelGosterilir(error)) return;
    if (error.kod === 'cakisma' && error.alanlar === undefined) return; // bant zaten gösterdi
    this.toast.hata(error.detay);
  }

  /** Çıkışta ve yeni kayda geçişte kirli sayılacak tüm formlar. */
  isDirty(): boolean {
    return (
      this.form.dirty ||
      this.newCustomerForm.dirty ||
      this.deliveryForm.dirty ||
      this.returnForm.dirty ||
      this.extendForm.dirty ||
      this.closePreAuthForm.dirty ||
      this.addAddOnForm.dirty
    );
  }

  // ─── yardımcılar ───────────────────────────────────────────────────────────────────────────

  private suggestionStore(
    endpoint: 'rezervasyon-kaynagi' | 'ozel-kod',
  ): TemelStore<readonly SecimSecenegi[], string> {
    return new TemelStore(
      (q: string) =>
        this.api.get<readonly SecimSecenegi[]>(`/api/ui/v1/secim/${endpoint}`, {
          parametreler: { q: q === '' ? null : q, limit: SUGGESTION_LIMIT },
          context: SILENT,
        }),
      { oncekiVeriyiKoru: true },
    );
  }

  /** Datalist önerisi: alanın değeri değiştikçe (gecikmeli, aynı metin tekrar sorulmaz) `q` ile sunucu araması. */
  private suggestionSearch(
    check: AbstractControl<string | null>,
    store: TemelStore<readonly SecimSecenegi[], string>,
  ): void {
    check.valueChanges
      .pipe(
        startWith(check.value),
        map((v) => (v ?? '').trim()),
        debounceTime(SUGGESTION_DELAY),
        distinctUntilChanged(),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((q) => store.yukle(q));
  }

  /** Seçim uçları OperationsWrite ister; izni olmayan oturumda istek atılmaz (403 bandı çıkmasın). */
  private authorizedSource<T extends SecimSecenegi>(
    source: SelectionSource<T>,
  ): SelectionSource<T> {
    return (search, limit) => (this.operasyon() ? source(search, limit) : of([]));
  }

  private valueSignal<T>(check: AbstractControl<T>): Signal<T> {
    return toSignal(check.valueChanges.pipe(startWith(check.value)), {
      initialValue: check.value,
    });
  }
}
