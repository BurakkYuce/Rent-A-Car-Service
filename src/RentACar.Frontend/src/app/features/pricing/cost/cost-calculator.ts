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
import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import { type GunMetni, bugun } from '@core/form/tarih-girdisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { UyariBandiServisi } from '@core/geri-bildirim/uyari-bandi-servisi';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { sekmeBaglami } from '@core/sekme/sekme-durumu';
import {
  anDegeri,
  gunDegeri,
  metinDegeri,
  sunucuDegerleriniBirlestir,
} from '@features/planlama-ortak/form-yardimcilari';
import { ParaPipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import { SayiGirdisi } from '@shared/form/kontroller/sayi-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';

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

interface HeaderForm {
  readonly baslik: string | null;
  readonly plaka: string | null;
  readonly tarih: GunMetni | null;
  readonly cari: SecimSecenegi | null;
  readonly hazirlayan: SecimSecenegi | null;
  readonly aciklama: string | null;
}

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
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    AramaSecim,
    FormHatalari,
    MetinGirdisi,
    ParaGirdisi,
    ParaPipe,
    SayiGirdisi,
    Secim,
    TarihSecici,
  ],
  templateUrl: './cost-calculator.html',
  styleUrl: '../pricing.scss',
})
export class CostCalculator implements KaydedilmemisDegisiklikSahibi {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly banner = inject(UyariBandiServisi);
  private readonly router = inject(Router);
  private readonly session = inject(OturumServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly tab = sekmeBaglami();
  private readonly t = ceviriFonksiyonu();

  /** Kayıtlı teklif kimliği (`maliyet-teklifleri/:id`); yeni hesapta `null`. */
  protected readonly offerId = inject(ActivatedRoute).snapshot.paramMap.get('id');
  protected readonly num = toNum;
  protected readonly fields = COST_FIELDS;
  protected readonly canWrite = computed(() => this.session.izinVar('FinanceWrite'));
  protected readonly canPickStaff = computed(() => this.session.izinVar('OperationsWrite'));
  protected readonly customers = sunucuSecimKaynagi('musteri');
  protected readonly staff = sunucuSecimKaynagi('personel');
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
  protected readonly calc = formGonderimi();

  protected readonly header = new FormGroup({
    baslik: new FormControl<string | null>(null, [Validators.required, Validators.maxLength(256)]),
    plaka: new FormControl<string | null>(null, Validators.maxLength(32)),
    tarih: new FormControl<GunMetni | null>(bugun()),
    cari: new FormControl<SecimSecenegi | null>(null),
    hazirlayan: new FormControl<SecimSecenegi | null>(null),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(1024)),
  });
  /** Kaydet: iki form birlikte doğrulanır (girdi alan hataları `girdi.<alan>` → girdi formu). */
  private readonly saveGroup = new FormGroup({ girdi: this.form, kunye: this.header });
  protected readonly save = formGonderimi();

  constructor() {
    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      if (this.result() !== null) this.resultStale.set(true);
    });
    if (this.offerId) this.readOffer(this.offerId);
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.header.dirty || (this.offerId !== null && this.form.dirty);
  }

  protected calculate(): void {
    if (!this.canWrite()) return;
    const input = costFormToInput(this.form.getRawValue() as unknown as CostFormValue);
    this.calc.gonder(
      this.form,
      (key) =>
        this.api.post<CostResult>('/api/ui/v1/maliyet-hesapla', input, { islemAnahtari: key }),
      {
        basarili: (r) => {
          this.result.set(r);
          this.resultStale.set(false);
        },
      },
    );
  }

  protected saveOffer(): void {
    if (!this.canWrite() || this.resultStale() || this.result() === null) return;
    const offer = this.offer();
    if (this.offerId && offer === null) return; // sürüm okunmadan tam değiştirme gönderilmez
    const h = this.header.getRawValue() as HeaderForm;
    const body: CostOfferRequest = {
      baslik: metinDegeri(h.baslik),
      plaka: metinDegeri(h.plaka),
      tarih: anDegeri(h.tarih, offer?.teklif.tarih),
      cariId: h.cari?.id ?? null,
      hazirlayanId: h.hazirlayan?.id ?? null,
      aciklama: metinDegeri(h.aciklama),
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
            this.offerArrived(d, true);
          } else {
            this.header.markAsPristine();
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
        next: (d) => this.offerArrived(d, false),
        error: (raw: unknown) => {
          this.loadError.set(true);
          const e = apiHatasinaCevir(raw);
          if (!genelGosterilir(e)) this.toast.hata(e.detay);
        },
      });
  }

  /**
   * Kayıtlı teklif geldi: temiz formlar sıfırlanır; kirli formda dokunulan alan korunur, sunucuda da değişen
   * işaretlenir (409 `cakisma` sonrası form SİLİNMEZ). Sonuç = kaydın snapshot'ı.
   */
  private offerArrived(d: CostOfferDetail, saved: boolean): void {
    const previous = this.offer();
    const freshInput = costInputToForm(d.girdi);
    const freshHeader = this.headerOf(d);
    let conflicts = 0;
    if (saved || !this.form.dirty) this.form.reset({ ...freshInput });
    else
      conflicts += sunucuDegerleriniBirlestir(
        this.form,
        { ...freshInput },
        previous ? { ...costInputToForm(previous.girdi) } : { ...freshInput },
        this.t('fiyatTarife.cakismaAlan'),
      ).length;
    if (saved || !this.header.dirty) this.header.reset({ ...freshHeader });
    else
      conflicts += sunucuDegerleriniBirlestir(
        this.header,
        { ...freshHeader },
        previous ? { ...this.headerOf(previous) } : { ...freshHeader },
        this.t('fiyatTarife.cakismaAlan'),
      ).length;
    if (conflicts > 0)
      this.banner.goster({
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
      tarih: gunDegeri(d.teklif.tarih),
      cari: d.teklif.cariId ? { id: d.teklif.cariId, etiket: d.teklif.cariAd ?? '—' } : null,
      hazirlayan: d.teklif.hazirlayanId
        ? { id: d.teklif.hazirlayanId, etiket: this.t('fiyatTarife.maliyet.kayitliPersonel') }
        : null,
      aciklama: d.aciklama,
    };
  }
}
