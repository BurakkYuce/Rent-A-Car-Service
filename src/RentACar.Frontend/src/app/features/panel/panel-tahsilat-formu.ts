import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  InjectionToken,
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

import type { ApiHatasi } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { GonderimKilidi } from '@core/form/gonderim-kilidi';
import type { FinansHesapOgesi, TahsilatBilgisi } from '@core/api/ui-tipleri';
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
 * Panelin TEK tahsilat kilidi (sayfa sağlar). Tüm satırların formu aynı kilidi kullanır: istek uçarken ne başka
 * satırın formu gönderebilir ne de tablodaki "Tahsil Et" düğmeleri basılabilir (sayfa `gonderiliyor()`'u okur).
 */
export const PANEL_TAHSILAT_KILIDI = new InjectionToken<GonderimKilidi>('PANEL_TAHSILAT_KILIDI');

/**
 * Panel "Tahsil Et" formu (Blazor Home.razor hızlı tahsilat karşılığı): tutar (bakiye ön dolu, düzenlenebilir),
 * hesap türü (Kasa/Banka) ve isteğe bağlı somut kasa/banka hesabı. `POST /api/ui/v1/finans/tahsilat`.
 *
 * PARA KURALLARI (roadmap F4.5, idempotency envanteri E01):
 * - Anahtar sunucunun deterministik `tahsilatAnahtar`'ı; gövdede AYNEN geri gider, istemci anahtarı üretilmez
 *   (`GonderimKilidi` deterministik dalı).
 * - Gönderim boyunca kilit (sayfa düzeyi `PANEL_TAHSILAT_KILIDI`): düğme pasif, çift tık tek istek.
 * - Form bir SATIRA aittir: sayfa onu `rentalId` anahtarıyla oluşturur; başka satırın "Tahsil Et"i yeni örnek
 *   açar (tutar, hesap, hata — hiçbir durum satırlar arasında taşınmaz). Adversarial F1: aynı örnek korunduğunda
 *   A'nın tutarı B'nin kirasına yazılıyordu.
 * - 409 `mukerrer`: otomatik yeniden gönderim YOK; `mukerrer` çıktısı panel yeniden yüklenir, form kapanır. Sunucu
 *   anahtarı yeniden hesaplar: bayat anahtar (ekran açıldıktan sonra kirada tahsilat/ters kayıt/bakiye değişti)
 *   de 409 `mukerrer` döner. Bu yüzden genel "Mükerrer işlem" bildirimi KULLANILMAZ (operatör parayı kaydedildi
 *   sanmasın): istek `sessiz`, uyarıda sunucunun `detail`'ı AYNEN + nötr başlık gösterilir.
 * - `sessiz` yüzünden genel bant/toast'a düşmeyen kodlar (yetki, çok istek, 5xx, ağ) formun hata kutusunda
 *   gösterilir; 5xx/ağda "kaydedilmemiş olabilir, kontrol edin" metni (sessiz başarısızlık yok).
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
          <!-- Para girdisi varsayılanı: ön dolu öneri odakta (otomatik ya da Tab) tümüyle seçili; yazılan onun yerine geçer. -->
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
      <rc-form-hatalari [hatalar]="hatalar()" />
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

  readonly bilgi = input.required<TahsilatBilgisi>();
  readonly plaka = input.required<string>();
  readonly belgeNo = input.required<string>();

  /** 2xx: tahsilat yazıldı → panel tazelenir (yeni anahtar). */
  readonly tamamlandi = output();
  /** 409 `mukerrer` (çift gönderim ya da bayat anahtar): panel yeniden yüklenir; yeniden gönderim YOK. */
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

  protected readonly gonderim = formGonderimi(
    inject(PANEL_TAHSILAT_KILIDI, { optional: true }) ?? new GonderimKilidi(),
  );
  /** `sessiz` istekte genel bant/toast'a düşmeyen hatalar (yetki, çok istek, 5xx, ağ) formda. */
  private readonly ekHatalar = signal<readonly string[]>([]);
  protected readonly hatalar = computed(() => [
    ...this.gonderim.genelHatalar(),
    ...this.ekHatalar(),
  ]);

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
    // Adversarial F3: ön dolu tutarda imleç sonda kalınca "90" yazan kullanıcı "1250,5090" üretiyordu. Odakta tüm
    // metni `rc-para-girdisi` (varsayılan) seçer (otomatik odak da Tab da); fazla hane artık yuvarlanmaz, alan
    // hatası olur (#260).
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
    if (!this.gonderim.gonderiliyor()) this.ekHatalar.set([]);
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
          // Sessiz: genel "Mükerrer işlem" toast'u yerine sunucunun detail'ı (bkz. `hataIsle`).
          { context: istekBaglami({ sessiz: true }) },
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
        hata: (hata) => this.hataIsle(hata),
      },
    );
  }

  /**
   * `dogrulama` / `cakisma` / vazgeçilen `oturum_yok` formGonderimi'nde alanlara ya da forma yazılır; burada
   * yalnız `sessiz` yüzünden genel katmanın göstermediği kodlar ele alınır.
   */
  private hataIsle(hata: ApiHatasi): void {
    switch (hata.kod) {
      case 'mukerrer':
        // Sunucunun detail'ı AYNEN: bayat anahtarda "kayıt değişti, yeniden yükleyip tekrar deneyin", gerçek çift
        // gönderimde "zaten kaydedilmiş". Başlık nötr — "kaydedildi" izlenimi vermez.
        this.toast.uyari(`${hata.detay} ${this.t('panel.tahsilat.mukerrerYenilendi')}`, {
          baslik: this.t('panel.tahsilat.mukerrerBaslik'),
        });
        this.mukerrer.emit();
        return;
      case 'sunucu':
        this.ekHatalar.set([this.t('geriBildirim.sunucuHatasi')]);
        return;
      case 'ag':
        this.ekHatalar.set([this.t('geriBildirim.agHatasi')]);
        return;
      case 'yetki_yok':
      case 'pilot_degil':
      case 'cok_istek':
        this.ekHatalar.set([hata.detay]);
        return;
      default:
        return;
    }
  }

  private pozitifTutar(c: AbstractControl): ValidationErrors | null {
    const kanonik = invariantOndalik(c.value as string | number | null, { kesir: 2 });
    if (kanonik === null) return null; // boş/biçimsiz: required ya da paraGecersiz söyler
    return Number(kanonik) > 0
      ? null
      : { pozitifTutar: { mesaj: this.t('panel.tahsilat.pozitif') } };
  }
}
