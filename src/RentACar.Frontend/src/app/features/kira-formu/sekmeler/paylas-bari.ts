import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { gmailTaslakBaglantisi, whatsappBaglantisi } from '@shared/dis-baglantilar';
import { Alan } from '@shared/form/alan/alan';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { KiraFormuDurumu } from '../kira-formu-durumu';

/**
 * PAYLAŞ barı (Blazor `data-paylas` kartı + `rc-kira-tabs.js` `bindPaylas`): numara/e-posta DÜZENLENEBİLİR
 * (kayıtlı değer ön-doldurma; farklı numaraya/adrese de gönderilebilir). WhatsApp → `wa.me`, e-posta → Gmail
 * taslağı (mailto değil). Hazır özet metin SUNUCUDAN (`paylasim.mesaj`, Blazor metniyle birebir); aktif sözleşme
 * linki varsa mutlak adres sayfanın kendi kökünden kurulup eklenir. PDF eki elle (medya göndermek kimlik ister).
 * Yalnız OperationsWrite (sunucu `paylasim` barını yalnız o izinle doldurur). Kutular ana formun parçası DEĞİL
 * (kirli sayılmaz, gövdeye girmez).
 */
@Component({
  selector: 'rc-kf-paylas-bari',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, TranslocoPipe, Alan, MetinGirdisi],
  template: `
    @if (d.detay.veri()?.paylasim) {
      <section
        class="kf-kart"
        [attr.aria-label]="'kiraFormuParite.paylas.baslik' | transloco"
        data-testid="paylas-bari"
      >
        <h2 class="kf-kart__baslik">{{ 'kiraFormuParite.paylas.baslik' | transloco }}</h2>
        <rc-alan [etiket]="'kiraFormuParite.paylas.numara' | transloco">
          <rc-metin-girdisi [formControl]="tel" tur="tel" yerTutucu="05xx xxx xx xx" />
        </rc-alan>
        <div class="kf-eylemler">
          <button
            type="button"
            class="rc-dugme rc-dugme--kucuk"
            [attr.title]="'kiraFormuParite.paylas.whatsappIpucu' | transloco"
            (click)="whatsapp()"
          >
            {{ 'kiraFormuParite.paylas.whatsapp' | transloco }}
          </button>
        </div>
        <rc-alan [etiket]="'kiraFormuParite.paylas.eposta' | transloco">
          <rc-metin-girdisi [formControl]="eposta" tur="email" yerTutucu="ornek@mail.com" />
        </rc-alan>
        <div class="kf-eylemler">
          <button
            type="button"
            class="rc-dugme rc-dugme--kucuk"
            [attr.title]="'kiraFormuParite.paylas.gmailIpucu' | transloco"
            (click)="gmail()"
          >
            {{ 'kiraFormuParite.paylas.gmail' | transloco }}
          </button>
        </div>
        @if (hata(); as h) {
          <p class="rc-form-mesaji rc-form-mesaji--hata" role="alert">{{ h }}</p>
        }
        <p class="kf-not">{{ 'kiraFormuParite.paylas.not' | transloco }}</p>
      </section>
    }
  `,
})
export class PaylasBari {
  protected readonly d = inject(KiraFormuDurumu);
  private readonly belge = inject(DOCUMENT);
  private readonly t = ceviriFonksiyonu();

  protected readonly tel = new FormControl<string | null>(null);
  protected readonly eposta = new FormControl<string | null>(null);
  protected readonly hata = signal<string | null>(null);

  private readonly bar = computed(() => this.d.detay.veri()?.paylasim ?? null);

  /** Mesaj = sunucunun hazır metni (+ aktif link varsa " Sözleşmeniz: <mutlak adres>"). */
  readonly mesaj = computed(() => {
    const bar = this.bar();
    if (!bar) return '';
    const yol = bar.link?.yol;
    const adres = yol ? `${this.belge.location?.origin ?? ''}${yol}` : null;
    return adres === null
      ? bar.mesaj
      : `${bar.mesaj} ${this.t('kiraFormuParite.paylas.sozlesmeniz', { adres })}`;
  });

  constructor() {
    // Kayıtlı GSM/e-posta ön-doldurma: kullanıcı kutuya dokunmadıysa detay her geldiğinde tazelenir.
    effect(() => {
      const bar = this.bar();
      if (!bar) return;
      if (this.tel.pristine) this.tel.setValue(bar.musteriTel ?? null);
      if (this.eposta.pristine) this.eposta.setValue(bar.musteriEmail ?? null);
    });
  }

  protected whatsapp(): void {
    const adres = whatsappBaglantisi(this.tel.value, this.mesaj());
    if (adres === null) {
      this.hata.set(this.t('kiraFormuParite.paylas.gsmGecersiz'));
      return;
    }
    this.hata.set(null);
    this.ac(adres);
  }

  protected gmail(): void {
    const adres = gmailTaslakBaglantisi(this.eposta.value, this.bar()?.konu ?? '', this.mesaj());
    if (adres === null) {
      this.hata.set(this.t('kiraFormuParite.paylas.epostaGecersiz'));
      return;
    }
    this.hata.set(null);
    this.ac(adres);
  }

  private ac(adres: string): void {
    this.belge.defaultView?.open(adres, '_blank', 'noopener');
  }
}
