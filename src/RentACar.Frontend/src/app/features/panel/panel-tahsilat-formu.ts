import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  OnInit,
  afterNextRender,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import {
  type AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  type ValidationErrors,
  Validators,
} from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { FinansHesapOgesi, PanelTahsilatBilgisi } from '@core/api/ui-tipleri';
import { paraBicimle } from '@core/bicim/bicim';
import { invariantOndalik } from '@core/form/ondalik';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { Alan } from '@shared/form/alan/alan';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';

import { type HesapTuru, sayi, tahsilatGovdesi } from './panel-modeli';

/**
 * Panel "Tahsil Et" formu (Blazor Home.razor hızlı tahsilat karşılığı): tutar (bakiye ön dolu, düzenlenebilir),
 * hesap türü (Kasa/Banka) ve isteğe bağlı somut kasa/banka hesabı. `POST /api/ui/v1/finans/tahsilat`.
 *
 * PARA KURALLARI (roadmap F4.5, idempotency envanteri E01):
 * - Anahtar sunucunun deterministik `tahsilatAnahtar`'ı; gövdede AYNEN geri gider, istemci anahtarı üretilmez
 *   (`GonderimKilidi` deterministik dalı).
 * - Gönderim boyunca kilit: düğme pasif, çift tık tek istek.
 * - 409 `mukerrer`: otomatik yeniden gönderim YOK. Interceptor `mukerrer` çıktısını tetikler (panel yeniden
 *   yüklenir, form kapanır) ve bilgi toast'u gösterir.
 * - 2xx: `tamamlandi` → panel tazelenir; satırın yeni bakiyesi ve işlem sayısıyla YENİ anahtar gelir.
 */
@Component({
  selector: 'rc-panel-tahsilat-formu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, TranslocoPipe, Alan, FormHatalari, ParaGirdisi, Secim],
  template: `
    <form
      class="tahsilat"
      [formGroup]="form"
      (ngSubmit)="gonder()"
      [attr.aria-labelledby]="baslikKimligi"
      novalidate
    >
      <h3 class="tahsilat__baslik" [id]="baslikKimligi">
        {{ 'panel.tahsilat.baslik' | transloco: { plaka: plaka(), no: belgeNo() } }}
      </h3>
      <div class="tahsilat__alanlar">
        <rc-alan
          [etiket]="'panel.tahsilat.tutar' | transloco"
          [ipucu]="'panel.tahsilat.tutarIpucu' | transloco"
        >
          <rc-para-girdisi formControlName="tutar" [paraBirimi]="bilgi().doviz" />
        </rc-alan>
        <rc-alan [etiket]="'panel.tahsilat.hesap' | transloco">
          <rc-secim formControlName="hesap" [secenekler]="hesapTurleri" />
        </rc-alan>
        @if (hesapSecenekleri().length > 0) {
          <rc-alan [etiket]="'panel.tahsilat.hesapId' | transloco">
            <rc-secim
              formControlName="hesapId"
              [secenekler]="hesapSecenekleri()"
              [bosEtiket]="'panel.tahsilat.hesapBelirtilmemis' | transloco"
            />
          </rc-alan>
        }
      </div>
      <rc-form-hatalari [hatalar]="gonderim.genelHatalar()" />
      <div class="tahsilat__eylemler">
        <button
          type="submit"
          class="rc-dugme rc-dugme--birincil"
          [disabled]="gonderim.gonderiliyor()"
          [attr.aria-busy]="gonderim.gonderiliyor()"
        >
          {{
            (gonderim.gonderiliyor() ? 'panel.tahsilat.gonderiliyor' : 'panel.tahsilat.gonder')
              | transloco
          }}
        </button>
        <button
          type="button"
          class="rc-dugme"
          [disabled]="gonderim.gonderiliyor()"
          (click)="vazgecildi.emit()"
        >
          {{ 'panel.tahsilat.vazgec' | transloco }}
        </button>
      </div>
    </form>
  `,
  styles: `
    :host {
      display: block;
    }
    .tahsilat {
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-3);
      padding: var(--rc-bosluk-3);
      border: 1px solid var(--rc-vurgu-metin);
      border-radius: var(--rc-yaricap-md);
      background-color: var(--rc-yuzey-alt);
    }
    .tahsilat__baslik {
      margin: 0;
      font-size: var(--rc-yazi-md);
      font-weight: var(--rc-agirlik-kalin);
      overflow-wrap: anywhere;
    }
    .tahsilat__alanlar {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 12rem), 1fr));
      gap: var(--rc-bosluk-3);
    }
    .tahsilat__eylemler {
      display: flex;
      flex-wrap: wrap;
      gap: var(--rc-bosluk-2);
    }
  `,
})
export class PanelTahsilatFormu implements OnInit {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();

  readonly bilgi = input.required<PanelTahsilatBilgisi>();
  readonly plaka = input.required<string>();
  readonly belgeNo = input.required<string>();

  /** 2xx: tahsilat yazıldı → panel tazelenir (yeni anahtar). */
  readonly tamamlandi = output();
  /** 409 `mukerrer`: kayıt zaten var → panel yeniden yüklenir; yeniden gönderim YOK. */
  readonly mukerrer = output();
  readonly vazgecildi = output();

  private static sayac = 0;
  protected readonly baslikKimligi = `rc-panel-tahsilat-${++PanelTahsilatFormu.sayac}`;

  protected readonly hesapTurleri: readonly SecenekOgesi<HesapTuru>[] = [
    { deger: 'Kasa', etiket: this.t('panel.tahsilat.kasa') },
    { deger: 'Banka', etiket: this.t('panel.tahsilat.banka') },
  ];

  protected readonly form = new FormGroup({
    tutar: new FormControl<string | number | null>(null, [
      Validators.required,
      (c: AbstractControl) => this.pozitifTutar(c),
    ]),
    hesap: new FormControl<HesapTuru | null>('Kasa', Validators.required),
    hesapId: new FormControl<string | null>(null),
  });

  protected readonly gonderim = formGonderimi();

  private readonly hesaplar = signal<readonly FinansHesapOgesi[]>([]);
  private readonly secilenTur = toSignal(this.form.controls.hesap.valueChanges, {
    initialValue: this.form.controls.hesap.value,
  });
  /** Somut hesap listesi seçilen türe göre (Blazor HesapSecici; boş = belirtilmemiş, isteğe bağlı). */
  protected readonly hesapSecenekleri = computed<readonly SecenekOgesi<string>[]>(() => {
    const tur = this.secilenTur();
    return this.hesaplar()
      .filter((h) => h.tur === tur)
      .map((h) => ({ deger: h.id, etiket: h.etiket }));
  });

  constructor() {
    const kok = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    afterNextRender(() => kok.querySelector<HTMLInputElement>('input')?.focus());
    // Tür değişince başka türün hesabı seçili kalmasın (sunucu da reddeder; burada sessizce temizlenir).
    this.form.controls.hesap.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this.form.controls.hesapId.setValue(null);
    });
  }

  ngOnInit(): void {
    this.form.controls.tutar.setValue(this.bilgi().varsayilanTutar);
    // Hesap listesi isteğe bağlı: alınamazsa alan görünmez, tahsilat türle yine yapılır (sessiz).
    this.api
      .get<FinansHesapOgesi[]>('/api/ui/v1/finans/hesaplar', {
        context: istekBaglami({ sessiz: true }),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (liste) => this.hesaplar.set(liste), error: () => undefined });
  }

  protected gonder(): void {
    const bilgi = this.bilgi();
    const deger = this.form.getRawValue();
    const tutar = invariantOndalik(deger.tutar, { kesir: 2 });
    this.gonderim.gonder(
      this.form,
      () =>
        this.api.post<unknown>(
          '/api/ui/v1/finans/tahsilat',
          tahsilatGovdesi(
            bilgi,
            { tutar: tutar ?? '', hesap: deger.hesap ?? 'Kasa', hesapId: deger.hesapId },
            this.t('panel.tahsilat.aciklama', { plaka: this.plaka() }),
          ),
          { context: istekBaglami({ mukerrerdeYenile: () => this.mukerrer.emit() }) },
        ),
      {
        deterministikAnahtar: bilgi.anahtar,
        basarili: () => {
          this.toast.basari(
            this.t('panel.tahsilat.basarili', {
              tutar: paraBicimle(sayi(tutar), bilgi.doviz),
              plaka: this.plaka(),
            }),
          );
          this.tamamlandi.emit();
        },
      },
    );
  }

  private pozitifTutar(c: AbstractControl): ValidationErrors | null {
    const kanonik = invariantOndalik(c.value as string | number | null, { kesir: 2 });
    if (kanonik === null) return null; // boş/biçimsiz: required ya da paraGecersiz söyler
    return Number(kanonik) > 0
      ? null
      : { pozitifTutar: { mesaj: this.t('panel.tahsilat.pozitif') } };
  }
}
