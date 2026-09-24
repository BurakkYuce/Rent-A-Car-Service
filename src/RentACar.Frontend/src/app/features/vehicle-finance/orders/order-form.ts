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
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { UyariBandiServisi } from '@core/geri-bildirim/uyari-bandi-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { trKucukHarf } from '@core/metin/tr-normalize';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { sekmeBaglami } from '@core/sekme/sekme-durumu';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { sunucuDegerleriniBirlestir } from '@features/planlama-ortak/form-yardimcilari';
import { suggestionList } from '@features/vehicles/suggestions';
import { toNumber } from '@features/vehicles/vehicle-model';
import { secimSuggestionFetch, vehicleSuggestionFetch } from '@features/vehicles/vehicle.store';
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

import { ORDER_CURRENCIES, type OrderDetail } from '../finance-model';
import { ORDERS, OrderFormStore, recordPath } from '../finance.store';
import { type OrderFormValue, emptyOrder, orderRequest, orderToForm } from './order-form-model';

type Transition = 'onayla' | 'teslim-al' | 'iptal';

/**
 * Araç siparişi (`/app/arac-siparis/yeni`, `/app/arac-siparis/:id`) — Blazor sipariş formunun TÜM alanları + durum
 * geçişleri. Düğmeler istemci izninden DEĞİL sunucunun `yetkiler` bayraklarından (servisin tek geçiş tablosu). PUT
 * tam değiştirmedir (`surum`); 409 `cakisma` → güncel kayıt KİRLİ forma birleşir. Deftere yazmaz.
 */
@Component({
  selector: 'rc-order-form',
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
  providers: [FetchPolicy, OrderFormStore],
  templateUrl: './order-form.html',
  styleUrl: '../vehicle-finance.scss',
})
export class OrderForm implements KaydedilmemisDegisiklikSahibi {
  protected readonly store = inject(OrderFormStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly router = inject(Router);
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly banner = inject(UyariBandiServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly tab = sekmeBaglami();
  private readonly t = ceviriFonksiyonu();

  protected readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id');
  protected readonly isNew = this.id === null;
  protected readonly base = signal<OrderDetail | null>(null);
  protected readonly transitioning = signal(false);
  protected readonly num = toNumber;
  protected readonly customers = sunucuSecimKaynagi('musteri');

  protected readonly form = new FormGroup({
    tedarikci: new FormControl<string | null>(null, [
      Validators.required,
      Validators.maxLength(200),
    ]),
    tedarikciCari: new FormControl<SecimSecenegi | null>(null),
    dosyaNo: new FormControl<string | null>(null, Validators.maxLength(64)),
    siparisTarihi: new FormControl<string | null>(null),
    imzaTarih: new FormControl<string | null>(null),
    beklenenTeslim: new FormControl<string | null>(null),
    satisTemsilci: new FormControl<string | null>(null, Validators.maxLength(128)),
    ozelTemsilci: new FormControl<string | null>(null, Validators.maxLength(128)),
    marka: new FormControl<string | null>(null, Validators.maxLength(100)),
    tip: new FormControl<string | null>(null, Validators.maxLength(100)),
    grup: new FormControl<string | null>(null, Validators.maxLength(100)),
    versiyon: new FormControl<string | null>(null, Validators.maxLength(100)),
    renk: new FormControl<string | null>(null, Validators.maxLength(64)),
    icRenk: new FormControl<string | null>(null, Validators.maxLength(64)),
    kaynakTip: new FormControl<string | null>(null, Validators.maxLength(32)),
    satisTipi: new FormControl<string | null>(null, Validators.maxLength(32)),
    opsiyon: new FormControl<string | null>(null, Validators.maxLength(512)),
    tsbKayitNo: new FormControl<string | null>(null, Validators.maxLength(64)),
    krediId: new FormControl<string | null>(null),
    adet: new FormControl<number | null>(1, [
      Validators.required,
      Validators.min(1),
      Validators.max(10000),
    ]),
    birimFiyat: new FormControl<string | null>(null, Validators.required),
    piyasaFiyat: new FormControl<string | null>(null),
    opsFiyat: new FormControl<string | null>(null),
    filoFiyat: new FormControl<string | null>(null),
    doviz: new FormControl<string | null>('TRY'),
    kur: new FormControl<number | null>(null),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  protected readonly submission = formGonderimi();

  protected readonly currency = toSignal(this.form.controls.doviz.valueChanges, {
    initialValue: this.form.controls.doviz.value,
  });
  protected readonly currencyOptions: readonly SecenekOgesi<string>[] = ORDER_CURRENCIES.map(
    (c) => ({ deger: c, etiket: c }),
  );
  protected readonly loanOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    (this.store.loans.veri()?.kayitlar ?? []).map((k) => ({
      deger: k.id,
      etiket: `${k.no} — ${k.bankaAdi}`,
    })),
  );
  protected readonly supplierSuggestions = computed(() => {
    const seen = new Set<string>();
    const out: string[] = [];
    for (const s of this.store.suppliers.veri()?.kayitlar ?? []) {
      const name = s.tedarikci.trim();
      const key = trKucukHarf(name);
      if (name !== '' && !seen.has(key)) {
        seen.add(key);
        out.push(name);
      }
    }
    return out.sort((a, b) => a.localeCompare(b, 'tr'));
  });
  protected readonly brandSuggestions = suggestionList(
    this.form.controls.marka,
    vehicleSuggestionFetch(this.api, 'marka'),
  );
  protected readonly typeSuggestions = suggestionList(
    this.form.controls.tip,
    vehicleSuggestionFetch(this.api, 'tip', () => ({ marka: this.form.controls.marka.value })),
  );
  protected readonly groupSuggestions = suggestionList(
    this.form.controls.grup,
    secimSuggestionFetch(this.api, 'arac-grubu'),
  );
  /** Blazor datalist'leriyle aynı sabit öneriler (serbest metin de kabul). */
  protected readonly sourceTypes = ['ÖzMal', 'Kiralık', 'Filo'] as const;
  protected readonly saleTypes = ['Sıfır', '2. El'] as const;

  /** Düzenlenebilir mi: yeni kayıtta evet; kayıtta sunucunun `yetkiler.duzenle` bayrağı. */
  protected readonly editable = computed(
    () => this.isNew || (this.base()?.yetkiler.duzenle ?? false),
  );

  constructor() {
    const policy = inject(FetchPolicy);
    policy.baglan({
      parametre: signal(0).asReadonly(),
      yukle: () => {
        this.store.loans.yukle();
        this.store.suppliers.yukle();
      },
    });
    if (this.id !== null) {
      policy.baglan({
        parametre: signal(this.id).asReadonly(),
        yukle: (id) => this.store.order.yukle(id),
        sifirla: () => this.store.order.sifirla(),
      });
      effect(() => {
        const d = this.store.order.durum();
        if (d.tur === 'hazir') untracked(() => this.orderArrived(d.veri));
      });
    } else {
      this.form.reset({ ...emptyOrder() });
    }
    effect(() => {
      const editable = this.editable();
      untracked(() =>
        editable ? this.form.enable({ emitEvent: false }) : this.form.disable({ emitEvent: false }),
      );
    });
    sayfaTerkKorumasi(() => this.form.dirty);
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.form.dirty;
  }

  protected save(): void {
    if (!this.editable()) return;
    const base = this.base();
    if (!this.isNew && (base === null || this.store.order.yukleniyor())) return;
    const body = orderRequest(this.form.getRawValue() as OrderFormValue, base);
    const id = this.id;
    this.submission.gonder(
      this.form,
      (key) =>
        id === null
          ? this.api.post<{ id: string; no: string }>(ORDERS, body, { islemAnahtari: key })
          : this.api.put<OrderDetail>(recordPath(ORDERS, id), body, { islemAnahtari: key }),
      {
        esleme: { tedarikciCariId: 'tedarikciCari' },
        basarili: (r) => {
          if (id === null) {
            this.toast.basari(this.t('aracFinans.siparis.olusturuldu', { no: r.no }));
            void this.router.navigate(['/arac-siparis', r.id], { replaceUrl: true });
            return;
          }
          const d = r as OrderDetail;
          this.toast.basari(this.t('aracFinans.siparis.kaydedildi', { no: d.no }));
          this.base.set(d);
          this.form.reset({ ...orderToForm(d) });
        },
        hata: (h) => {
          if (h.kod === 'cakisma') this.store.order.yenile();
          if (h.kod === 'mukerrer' && id === null && h.mevcut?.ayniIcerik) {
            this.form.markAsPristine();
            void this.router.navigate(['/arac-siparis', h.mevcut.id], { replaceUrl: true });
          }
        },
      },
    );
  }

  protected async transition(kind: Transition): Promise<void> {
    const d = this.base();
    if (!d || this.transitioning()) return;
    if (kind === 'iptal') {
      const yes = await this.confirm.sor({
        baslik: this.t('aracFinans.siparis.iptalBaslik'),
        mesaj: this.t(
          d.durum === 'Onaylandi'
            ? 'aracFinans.siparis.iptalMesajOnayli'
            : 'aracFinans.siparis.iptalMesaj',
          { no: d.no },
        ),
        onayEtiketi: this.t('aracFinans.siparis.iptal'),
        tehlikeli: true,
      });
      if (!yes || this.transitioning()) return;
    }
    this.transitioning.set(true);
    this.api
      .post<OrderDetail>(recordPath(ORDERS, d.id, `/${kind}`), null)
      .pipe(
        finalize(() => this.transitioning.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (fresh) => {
          this.toast.basari(this.t('aracFinans.siparis.durumDegisti', { no: fresh.no }));
          this.orderArrived(fresh);
        },
        error: (raw: unknown) => {
          const error = apiHatasinaCevir(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
          this.store.order.yenile();
        },
      });
  }

  protected statusLabel(s: string): string {
    return this.t(`aracFinans.siparis.durumlar.${s}` as 'aracFinans.siparis.durumlar.Bekliyor');
  }

  /** Temiz form sunucu hâline sıfırlanır; kirli formda dokunulan alanlar korunur (çakışan işaretlenir). */
  private orderArrived(d: OrderDetail): void {
    this.tab.etiketAyarla(this.t('aracFinans.siparis.sekmeEtiketi', { no: d.no }));
    const previous = this.base();
    if (!this.form.dirty || previous === null) {
      this.form.reset({ ...orderToForm(d) });
    } else {
      const conflicts = sunucuDegerleriniBirlestir(
        this.form,
        { ...orderToForm(d) },
        { ...orderToForm(previous) },
        this.t('aracFinans.cakismaAlan'),
      );
      if (conflicts.length > 0)
        this.banner.goster({
          tur: 'uyari',
          mesaj: this.t('aracFinans.cakismaBant', { sayi: conflicts.length }),
          kod: 'cakisma',
        });
    }
    this.base.set(d);
  }
}
