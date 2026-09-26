import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  untracked,
} from '@angular/core';
import type { AbstractControl } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { type UnsavedChangesOwner, pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { SERVER_ERROR } from '@core/form/sunucu-hatalari';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { WarningBannerService } from '@core/geri-bildirim/warning-banner-service';
import { translationFunction } from '@core/i18n/ceviri';
import { requestContext } from '@core/oturum/request-context';
import { tabContext } from '@core/sekme/tab-state';
import { TemelStore } from '@core/veri/temel-store';
import { serverSelectionSource } from '@shared/form/arama-secim/selection-source';
import { formSubmission } from '@shared/form/form-submission';

import { RF_SHARED } from '../ortak';
import { ReservationActions } from '../reservation-actions';
import {
  OTA_FIELDS,
  RESERVATION_ROOT,
  valuesFromDetail,
  statusBadge,
  isReservationStatus,
  createReservationForm,
  reservationBody,
  optionList,
  toNumber,
  mergeServerValues,
  defaultDates,
  type ReservationField,
  type ReservationDetailResponse,
  type ReservationFormValue,
  type ReservationFormOptions,
  type CreateReservationResponse,
} from '../rezervasyon-modeli';

const SILENT = requestContext({ sessiz: true });

/**
 * Rezervasyon formu — TEK bileşen iki rotada: `/app/rezervasyonlar/yeni` (oluştur) ve `/app/rezervasyonlar/:id`
 * (düzenle + durum eylemleri). Blazor `ReservationList.razor`'daki "+ Yeni Rezervasyon" ve satır içi "Düzenle"
 * formlarının alanları (FAZ-48 talep alanları, ödeme/komisyon, broker bilgisi) + PUT tam değiştirme olduğu için
 * kaydın diğer alanları (km limiti, fazla km, yakıt, açıklama) — gövdede olmayan alan boş yazılırdı.
 *
 * Kurallar: tutar/gün/KDV SUNUCUDA (fiyat motoru); hata hiçbir dalda form değerine dokunmaz (`formGonderimi`);
 * PUT detaydaki `surum`u taşır, bayatsa 409 `cakisma` → güncel kayıt okunur ve KİRLİ forma birleştirilir
 * (dokunulmamış alan güncellenir, dokunulan korunur, ikisi de değiştiyse alan işaretlenir) — form SİLİNMEZ.
 * Sayfada `<form>` yok: arama-seçimde Enter yanlışlıkla kaydetmesin; kayıt düğmeyle.
 */
@Component({
  selector: 'rc-rezervasyon-formu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...RF_SHARED, RouterLink],
  providers: [ReservationActions],
  templateUrl: './rezervasyon-formu.html',
  styleUrl: './rezervasyon-formu.scss',
})
export class ReservationFormPage implements UnsavedChangesOwner {
  private readonly api = inject(ApiIstemcisi);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly bant = inject(WarningBannerService);
  private readonly sekme = tabContext();
  private readonly t = translationFunction();
  protected readonly islemler = inject(ReservationActions);

  /** Kayıtlı rezervasyonun kimliği; yenide `null`. Bileşen örneği boyunca değişmez (sekme = rota + id). */
  readonly id: string | null = inject(ActivatedRoute).snapshot.paramMap.get('id');
  protected readonly yeni = this.id === null;

  readonly form = createReservationForm();
  protected readonly record = formSubmission();
  protected readonly otaFields = OTA_FIELDS;

  readonly detay = new TemelStore(
    (id: string) => this.api.get<ReservationDetailResponse>(`${RESERVATION_ROOT}/${id}`),
    { oncekiVeriyiKoru: true },
  );
  private readonly options = new TemelStore(() =>
    this.api.get<ReservationFormOptions>(`${RESERVATION_ROOT}/form-secenekleri`, {
      context: SILENT,
    }),
  );

  protected readonly customerDataSource = serverSelectionSource('musteri');
  protected readonly vehicleSource = serverSelectionSource('arac');
  protected readonly locationDataSource = serverSelectionSource('lokasyon');
  protected readonly sourceDataSource = serverSelectionSource('rezervasyon-kaynagi');

  protected readonly res = computed(() => this.detay.veri()?.rezervasyon ?? null);
  /** Sayfa bandı başlığı (sayfanın tek `<h1>`'i). */
  protected readonly baslik = computed(() =>
    this.yeni
      ? this.t('rezervasyon.yeniBaslik')
      : this.t('rezervasyon.detayBaslik', { no: this.res()?.no ?? '…' }),
  );
  protected readonly permissions = computed(() => this.detay.veri()?.yetkiler ?? null);
  protected readonly notFound = computed(() => this.detay.hata()?.status === 404);
  protected readonly editable = computed(() => this.yeni || (this.permissions()?.duzenle ?? false));
  /** İşlem/kayıt sonrası kayıt yeniden okunurken Kaydet PASİF (#261 N1): sürüm henüz tazelenmedi. */
  protected readonly canSave = computed(
    () => this.editable() && (this.yeni || !this.detay.isLoading()),
  );
  protected readonly priceTypes = computed(() =>
    optionList(this.options.veri()?.fiyatTurleri, this.res()?.fiyatTuru),
  );
  protected readonly requestTypes = computed(() => this.options.veri()?.talepTurleri ?? []);

  /** Son okunan sunucu hâli: `surum` PUT'a gider; değerler birleştirmede "sunucu neyi değiştirdi" tabanı. */
  private surum: string | null = null;
  private taban: ReservationFormValue | null = null;

  constructor() {
    pageLeaveGuard(() => this.hasUnsavedChanges());
    this.options.yukle();
    if (this.id === null) this.startNew();
    else this.startEdit(this.id);
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty;
  }

  private startNew(): void {
    this.form.reset(defaultDates());
    // Tenant "Varsayılan Fiyat Türü" (FAZ-82) YALNIZ yeni formun ön-seçimi — kullanıcı seçmediyse.
    effect(() => {
      const v = this.options.veri()?.varsayilanFiyatTuru ?? null;
      untracked(() => {
        const k = this.form.controls.fiyatTuru;
        if (v && k.value === null && k.pristine) k.setValue(v);
      });
    });
  }

  private startEdit(id: string): void {
    this.detay.yukle(id);
    effect(() => {
      const d = this.detay.veri();
      if (d) untracked(() => this.detailLoaded(d));
    });
    // Sekmeye dönüşte kayıt yeniden okunur (başka oturum/işlem değiştirmiş olabilir); ilk görünüm sayılmaz.
    let last: number | null = null;
    effect(() => {
      const n = this.sekme.onaGelme();
      untracked(() => {
        if (last !== null && n !== last) this.detay.yenile();
        last = n;
      });
    });
  }

  private detailLoaded(d: ReservationDetailResponse): void {
    this.sekme.etiketAyarla(this.t('rezervasyon.sekmeEtiketi', { no: d.rezervasyon.no }));
    // Kilit birleştirmeden ÖNCE: `enable()` doğrulamayı yeniden koşar ve çakışma işaretlerini silerdi.
    if (!d.yetkiler.duzenle) this.form.disable({ emitEvent: false });
    else if (this.form.disabled) this.form.enable({ emitEvent: false });
    const newItem = valuesFromDetail(d);
    if (!this.form.dirty) {
      this.form.reset(newItem);
    } else {
      const conflicting = mergeServerValues(this.form, newItem, this.taban);
      if (conflicting.length > 0) this.markConflict(conflicting);
    }
    this.taban = newItem;
    this.surum = d.rezervasyon.surum;
  }

  private markConflict(fields: readonly ReservationField[]): void {
    const message = this.t('rezervasyon.cakisma.alan');
    for (const name of fields) {
      const k = this.form.controls[name] as AbstractControl<unknown>;
      k.setErrors({ ...(k.errors ?? {}), [SERVER_ERROR]: [message] });
      k.markAsTouched();
    }
    this.bant.show({
      tur: 'uyari',
      mesaj: this.t('rezervasyon.cakisma.bant', { sayi: fields.length }),
      kod: 'cakisma',
    });
  }

  protected kaydet(): void {
    // Kilitli form (durum ya da tazeleme) `invalid` değildir — istemci doğrulaması onu durdurmaz.
    if (!this.canSave()) return;
    if (this.id === null) {
      this.record.gonder(
        this.form,
        () =>
          this.api.post<CreateReservationResponse>(
            RESERVATION_ROOT,
            reservationBody(this.form.getRawValue()),
          ),
        { basarili: (y) => this.created(y) },
      );
      return;
    }
    const id = this.id;
    this.record.gonder(
      this.form,
      () =>
        this.api.put<ReservationDetailResponse>(`${RESERVATION_ROOT}/${id}`, {
          ...reservationBody(this.form.getRawValue()),
          surum: this.surum ?? '',
        }),
      {
        // Bayat sürüm (409 cakisma): form SİLİNMEZ; güncel kayıt okunur, kirli forma birleştirilir
        // (detayGeldi); kullanıcı kontrol edip yeni sürümle yeniden kaydeder.
        hata: (h) => {
          if (h.kod === 'cakisma') this.detay.yenile();
        },
        basarili: (y) => {
          this.toast.basari(this.t('rezervasyon.bildirim.kaydedildi', { no: y.rezervasyon.no }));
          this.detay.yenile();
        },
      },
    );
  }

  private created(y: CreateReservationResponse): void {
    this.toast.basari(this.t('rezervasyon.bildirim.olusturuldu', { no: y.no }));
    // "Yeni rezervasyon" sekmesi yaşamaya devam eder: sonraki kayıt için temiz forma döner.
    this.form.reset({
      ...defaultDates(),
      fiyatTuru: this.options.veri()?.varsayilanFiyatTuru ?? null,
    });
    this.record.kilit.yenile();
    void this.router.navigate(['/rezervasyonlar', y.id]);
  }

  // ─── durum eylemleri (sunucu `yetkiler`'ine göre görünür) ─────────────────────────────────

  private hedef() {
    const r = this.res();
    return r ? { id: r.id, no: r.no } : null;
  }

  protected onayla(): void {
    const h = this.hedef();
    if (h) this.islemler.onayla(h, () => this.detay.yenile());
  }

  protected iptal(): void {
    const h = this.hedef();
    if (h) void this.islemler.iptal(h, () => this.detay.yenile());
  }

  protected convertToRental(): void {
    const h = this.hedef();
    if (h) void this.islemler.kirayaCevir(h, () => this.detay.yenile(), this.form.dirty);
  }

  // ─── gösterim ──────────────────────────────────────────────────────────────────────────

  protected rozet(status: string): string {
    return statusBadge(status);
  }

  protected statusLabel(status: string): string {
    return isReservationStatus(status) ? this.t(`rezervasyon.durumlar.${status}`) : status;
  }

  protected count(v: number | string | null | undefined): number | null {
    return toNumber(v);
  }
}
