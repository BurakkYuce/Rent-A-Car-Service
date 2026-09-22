import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import {
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
  type AbstractControl,
  type ValidationErrors,
} from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type {
  FinansHesapOgesi,
  FinansIslemYaniti,
  KiraListeSatiri,
  TahsilatIstegi,
} from '@core/api/ui-tipleri';
import { paraBicimle } from '@core/bicim/bicim';
import { invariantOndalik } from '@core/form/ondalik';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { TemelStore } from '@core/veri/temel-store';
import { Alan } from '@shared/form/alan/alan';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { Ikon } from '@shared/ikon/ikon';

import { kiraDovizi, sayi } from './kira-sutunlari';

type HesapTuru = 'Kasa' | 'Banka';

let sonrakiNo = 0;

/** Blazor liste formuyla aynı sabitler (FAZ-84 tek-tık hızlı tahsilat; açıklama cari ekstresinde görünür). */
export const TAHSILAT_KANALI = 'Masaüstü';
export const tahsilatAciklamasi = (sozlesmeNo: string) => `Hızlı tahsilat (liste) — ${sozlesmeNo}`;

/**
 * Satırdan "Tahsil Et" (Blazor RentalList hızlı tahsilat formu; `POST /api/ui/v1/finans/tahsilat`). PARA:
 *
 * - **Anahtar:** satır DTO'sundaki `tahsilat.anahtar` (sunucunun deterministik `TahsilatAnahtar`'ı:
 *   kira + bakiye + işlem sayısı) gövdede `tahsilatAnahtar` olarak AYNEN geri gider; `Idempotency-Key`
 *   başlığı da aynı değeri taşır (sunucuda deterministik anahtar başlıktan önceliklidir). İstemci anahtar
 *   ÜRETMEZ — iki sekme/iki kullanıcı aynı satırı tahsil ederse ikincisi 409 `mukerrer` olur.
 * - **Kilit:** istek uçarken gönder düğmesi pasif, ikinci gönderim yok sayılır (`formGonderimi`).
 * - **409 `mukerrer`:** yeniden gönderim YOK; interceptor bilgi toast'u gösterir, `sonuclandi` ile sayfa
 *   paneli kapatıp listeyi yeniden yükler (yeni bakiye + yeni anahtar).
 * - **2xx:** başarı toast'u + `sonuclandi` (liste yenilenir → satırın yeni anahtarı gelir).
 * - **Diğer hatalar** (`dogrulama`, `cakisma`, 5xx, ağ): form DEĞERLERİ KORUNUR, panel açık kalır.
 * - Cari, kira ve döviz satırdan (kira dövizi); kur boş → sunucu çözer (TRY=1; döviz: firma kuru → TCMB).
 */
@Component({
  selector: 'rc-kira-tahsil-paneli',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, TranslocoPipe, Alan, FormHatalari, Ikon, ParaGirdisi, Secim],
  template: `
    <section class="panel" [attr.aria-labelledby]="baslikKimligi">
      <header class="panel__ust">
        <h2 [id]="baslikKimligi">
          {{ 'kiraListesi.tahsil.baslik' | transloco: { no: satir().sozlesmeNo } }}
        </h2>
        <p class="panel__aciklama">{{ aciklama() }}</p>
      </header>
      <form
        class="panel__form"
        [formGroup]="form"
        [attr.aria-labelledby]="baslikKimligi"
        (ngSubmit)="gonder()"
      >
        <div class="rc-form-izgara">
          <rc-alan
            [etiket]="'kiraListesi.tahsil.tutar' | transloco"
            [ipucu]="'kiraListesi.tahsil.tutarIpucu' | transloco"
          >
            <rc-para-girdisi formControlName="tutar" [paraBirimi]="doviz()" />
          </rc-alan>
          <rc-alan [etiket]="'kiraListesi.tahsil.hesap' | transloco">
            <rc-secim formControlName="hesap" [secenekler]="hesapTurleri" />
          </rc-alan>
          @if (hesapSecenekleri().length > 0) {
            <rc-alan [etiket]="'kiraListesi.tahsil.hesapId' | transloco">
              <rc-secim
                formControlName="hesapId"
                [secenekler]="hesapSecenekleri()"
                [bosEtiket]="'kiraListesi.tahsil.hesapBelirtilmemis' | transloco"
              />
            </rc-alan>
          }
        </div>
        <rc-form-hatalari [hatalar]="gonderim.genelHatalar()" />
        <div class="panel__eylemler">
          <button
            type="submit"
            class="rc-dugme rc-dugme--birincil rc-dugme--kucuk"
            [disabled]="gonderim.gonderiliyor()"
          >
            <rc-ikon ad="cash" [boyut]="14" />
            {{
              gonderim.gonderiliyor()
                ? ('form.gonderiliyor' | transloco)
                : ('kiraListesi.tahsil.kaydet' | transloco)
            }}
          </button>
          <button
            type="button"
            class="rc-dugme rc-dugme--hayalet rc-dugme--kucuk"
            [disabled]="gonderim.gonderiliyor()"
            (click)="kapat.emit()"
          >
            {{ 'kiraListesi.tahsil.vazgec' | transloco }}
          </button>
        </div>
      </form>
    </section>
  `,
  styles: `
    .panel {
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-3);
      padding: var(--rc-bosluk-3) var(--rc-bosluk-4);
      border: 1px solid var(--rc-kenar);
      border-radius: var(--rc-yaricap-md);
      background-color: var(--rc-yuzey);
    }
    .panel__ust > h2 {
      margin: 0;
      font-size: var(--rc-yazi-md);
    }
    .panel__aciklama {
      margin: var(--rc-bosluk-1) 0 0;
      color: var(--rc-metin-ikincil);
    }
    .panel__form {
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-3);
    }
    .panel__eylemler {
      display: flex;
      flex-wrap: wrap;
      gap: var(--rc-bosluk-2);
    }
  `,
})
export class TahsilPaneli {
  /** Tahsil edilecek satır (anlık görüntü; `tahsilat` dolu olmalı — sayfa yalnız öyleyse açar). */
  readonly satir = input.required<KiraListeSatiri>();
  readonly kapat = output<void>();
  /** 2xx ya da 409 `mukerrer`: işlem sonuçlandı, liste yeniden yüklenmeli (yeni anahtar). */
  readonly sonuclandi = output<void>();

  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly t = ceviriFonksiyonu();
  private readonly kok = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly gonderim = formGonderimi();
  /** Sayfa, istek uçarken başka satırın panelini açmaz (uçan istek iptal edilip sonucu kaybolmasın). */
  readonly gonderiliyor = this.gonderim.gonderiliyor;

  protected readonly baslikKimligi = `rc-tahsil-paneli-${++sonrakiNo}`;

  protected readonly form = new FormGroup({
    tutar: new FormControl<string | null>(null, {
      validators: [Validators.required, (k: AbstractControl) => this.pozitif(k)],
    }),
    hesap: new FormControl<HesapTuru | null>('Kasa', { validators: [Validators.required] }),
    hesapId: new FormControl<string | null>(null),
  });

  protected readonly hesapTurleri: readonly SecenekOgesi<HesapTuru>[] = [
    { deger: 'Kasa', etiket: this.t('kiraListesi.tahsil.kasa') },
    { deger: 'Banka', etiket: this.t('kiraListesi.tahsil.banka') },
  ];

  /** Aktif kasa/banka hesapları (seçim isteğe bağlı; hata sessiz — seçici görünmez, tür yine seçilir). */
  private readonly hesaplar = new TemelStore(() =>
    this.api.get<readonly FinansHesapOgesi[]>('/api/ui/v1/finans/hesaplar', {
      context: istekBaglami({ sessiz: true }),
    }),
  );
  private readonly hesapTuru = toSignal(this.form.controls.hesap.valueChanges, {
    initialValue: this.form.controls.hesap.value,
  });
  /** Türü seçilen türle eşleşen hesaplar (türü belirsiz hesap sunucuda reddedilir → listelenmez). */
  protected readonly hesapSecenekleri = computed<readonly SecenekOgesi<string>[]>(() =>
    (this.hesaplar.veri() ?? [])
      .filter((h) => h.tur !== null && h.tur === this.hesapTuru())
      .map((h) => ({ deger: h.id, etiket: h.etiket })),
  );

  protected readonly doviz = computed(
    () => this.satir().tahsilat?.doviz ?? kiraDovizi(this.satir()),
  );
  protected readonly aciklama = computed(() => {
    const s = this.satir();
    return this.t('kiraListesi.tahsil.aciklama', {
      musteri: s.musteriAd,
      bakiye: paraBicimle(sayi(s.bakiye), kiraDovizi(s)),
    });
  });

  constructor() {
    this.hesaplar.yukle();
    // Tür değişince seçili hesap o türden değilse düşer (Kasa hesabıyla Banka tahsilatı sunucuda red).
    this.form.controls.hesap.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      const id = this.form.controls.hesapId.value;
      if (id !== null && !this.hesapSecenekleri().some((h) => h.deger === id)) {
        this.form.controls.hesapId.setValue(null);
      }
    });
    // Satır değişince (başka satırdan "Tahsil Et") form o satırın önerisiyle sıfırlanır.
    effect(() => {
      const s = this.satir();
      untracked(() =>
        this.form.reset({
          tutar: invariantOndalik(s.tahsilat?.varsayilanTutar, { kesir: 2 }),
          hesap: 'Kasa',
          hesapId: null,
        }),
      );
    });
    afterNextRender(() => this.kok.nativeElement.querySelector<HTMLInputElement>('input')?.focus());
  }

  protected gonder(): void {
    const s = this.satir();
    const tahsilat = s.tahsilat;
    if (tahsilat === null) return;
    const v = this.form.getRawValue();
    const govde: TahsilatIstegi = {
      cariId: tahsilat.cariId,
      tutar: v.tutar ?? '',
      hesap: v.hesap,
      kiraId: tahsilat.rentalId,
      doviz: tahsilat.doviz,
      hesapId: v.hesapId,
      kanal: TAHSILAT_KANALI,
      aciklama: tahsilatAciklamasi(s.sozlesmeNo),
      // Sunucunun deterministik anahtarı AYNEN geri gider (istemci üretmez, değiştirmez).
      tahsilatAnahtar: tahsilat.anahtar,
    };
    this.gonderim.gonder(
      this.form,
      (anahtar) =>
        this.api.post<FinansIslemYaniti>('/api/ui/v1/finans/tahsilat', govde, {
          islemAnahtari: anahtar,
          // 409 mukerrer: tekrar gönderilmez; satır (liste) yeniden yüklenir.
          context: istekBaglami({ mukerrerdeYenile: () => this.sonuclandi.emit() }),
        }),
      {
        deterministikAnahtar: tahsilat.anahtar,
        basarili: () => {
          this.toast.basari(
            this.t('kiraListesi.tahsil.basarili', {
              no: s.sozlesmeNo,
              tutar: paraBicimle(sayi(govde.tutar), tahsilat.doviz),
            }),
          );
          this.sonuclandi.emit();
        },
      },
    );
  }

  private pozitif(k: AbstractControl): ValidationErrors | null {
    const deger: unknown = k.value;
    if (typeof deger !== 'string' || deger === '') return null; // boşluğu `required` söyler
    return Number(deger) > 0
      ? null
      : { tutarPozitif: { mesaj: this.t('kiraListesi.tahsil.tutarPozitif') } };
  }
}
