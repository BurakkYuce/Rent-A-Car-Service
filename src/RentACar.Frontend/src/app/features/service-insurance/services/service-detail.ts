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

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { paraBicimle } from '@core/bicim/bicim';
import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import { sunucuHatalariniUygula } from '@core/form/sunucu-hatalari';
import { moneySubmission } from '@core/form/money-submission';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { UyariBandiServisi } from '@core/geri-bildirim/uyari-bandi-servisi';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { sekmeBaglami } from '@core/sekme/sekme-durumu';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { sunucuDegerleriniBirlestir } from '@features/planlama-ortak/form-yardimcilari';
import { ParaPipe, SayiPipe, TarihPipe } from '@shared/bicim/bicim-pipe';
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
import { MoneySubmitBar } from '@shared/form/money-submit/money-submit-bar';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';
import { Ikon } from '@shared/ikon/ikon';

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
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    AramaSecim,
    FormHatalari,
    Ikon,
    MetinGirdisi,
    ParaGirdisi,
    ParaPipe,
    SayiGirdisi,
    SayiPipe,
    MoneySubmitBar,
    Secim,
    TarihPipe,
    TarihSecici,
  ],
  providers: [FetchPolicy, ServiceDetailStore],
  templateUrl: './service-detail.html',
  styleUrl: '../service-insurance.scss',
})
export class ServiceDetail implements KaydedilmemisDegisiklikSahibi {
  protected readonly store = inject(ServiceDetailStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly banner = inject(UyariBandiServisi);
  private readonly confirm = inject(OnayServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly tab = sekmeBaglami();
  private readonly t = ceviriFonksiyonu();

  protected readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';
  protected readonly num = num;
  protected readonly planText = planText;
  protected readonly customers = sunucuSecimKaynagi('musteri');
  protected readonly declarationTypes = DECLARATION_TYPES;
  protected readonly busy = signal<Transition | null>(null);
  protected readonly detail = computed(() => this.store.detail.veri());
  protected readonly refreshing = computed(() => this.store.detail.yukleniyor());

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
  });

  // ---- bilgi blokları (tam değiştirme)
  protected readonly infoForm = new FormGroup(
    Object.fromEntries(
      Object.entries(emptyInfoForm()).map(([k, v]) => [k, new FormControl<unknown>(v)]),
    ) as Record<keyof ServiceInfoForm, FormControl<unknown>>,
  );
  protected readonly infoSubmission = formGonderimi();
  /** Formun doldurulduğu kayıt (birleştirme tabanı + `surum`). */
  private base: ServiceRecordDetail | null = null;

  constructor() {
    inject(FetchPolicy).baglan({
      parametre: signal(this.id).asReadonly(),
      yukle: (id) => this.store.detail.yukle(id),
      sifirla: () => this.store.detail.sifirla(),
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
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return (
      this.infoForm.dirty || this.lineForm.dirty || this.reflectForm.dirty || this.hasPendingMoney()
    );
  }

  /** Uçuştaki / sonucu bilinmeyen para işlemi varken özel terk metni (inceleme L2). */
  kaydedilmemisDegisiklikMesaji(): string | null {
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
      const yes = await this.confirm.sor({
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
          const error = apiHatasinaCevir(raw);
          const unmatched = sunucuHatalariniUygula(this.flowForm, error.alanlar);
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
        const cari = this.reflectForm.getRawValue().cari;
        return {
          path: recordPath(SERVICES, d.kayit.id, '/yansit'),
          target: `yansit:${d.kayit.id}`,
          body: { cariId: cari?.id ?? null },
          content: { tutar: num(d.yetkiler.yansitilacakTutar), doviz: 'TRY' },
        };
      },
      success: (fresh) => {
        this.toast.basari(
          this.t('servisSigorta.servis.yansitildiBildirim', {
            tutar: paraBicimle(num(fresh.yansitma?.tutar ?? null), 'TRY'),
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
      const conflicts = sunucuDegerleriniBirlestir(
        this.infoForm,
        { ...fresh },
        { ...baseline },
        this.t('servisSigorta.cakismaAlan'),
      );
      if (conflicts.length > 0)
        this.banner.goster({
          tur: 'uyari',
          mesaj: this.t('servisSigorta.cakismaBant', { sayi: conflicts.length }),
          kod: 'cakisma',
        });
    }
    this.base = d;
  }
}
