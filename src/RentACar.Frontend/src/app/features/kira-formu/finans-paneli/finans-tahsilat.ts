import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { KF_ORTAK } from '../sekmeler/ortak';
import { TEMEL_DOVIZ, dovizSecenekleri } from './finans-modeli';
import { KiraFinansDurumu, type TahsilatFormu } from './kira-finans-durumu';

/**
 * Kira tahsilatı (Nakit = Kasa, Kart/Havale = Banka) — `POST finans/tahsilat`. Cari, kira ve anahtar
 * sunucunun satır kopyasından; formda yalnız tutar, döviz, kur (dövizde; boş = sunucu çözer), hesap,
 * kanal ve not. Düğme gönderim boyunca ve işlem sonrası yeni kayıt gelene dek kapalı.
 */
@Component({
  selector: 'rc-kf-finans-tahsilat',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...KF_ORTAK],
  template: `
    @let t = tf();
    <section class="kf-finans__islem" [attr.aria-labelledby]="baslikId()">
      <h3 class="kf-finans__baslik" [id]="baslikId()">
        {{
          (t.hesap === 'Kasa' ? 'kiraFinans.tahsilat.nakit' : 'kiraFinans.tahsilat.kart')
            | transloco
        }}
      </h3>
      @if (!f.finans()) {
        <p class="kf-not">{{ 'kiraFinans.yetkiYok' | transloco }}</p>
      } @else if (f.iptal()) {
        <p class="kf-not">{{ 'kiraFinans.tahsilat.iptalKira' | transloco }}</p>
      } @else {
        <div class="rc-form-izgara" [formGroup]="t.form">
          <rc-alan [etiket]="'kiraFinans.alan.tutar' | transloco">
            <rc-para-girdisi formControlName="tutar" [paraBirimi]="t.doviz()" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFinans.alan.doviz' | transloco">
            <rc-secim formControlName="doviz" [secenekler]="dovizler()" />
          </rc-alan>
          @if (t.doviz() !== temel) {
            <rc-alan
              [etiket]="'kiraFinans.alan.kur' | transloco"
              [ipucu]="'kiraFinans.ipucu.kur' | transloco"
            >
              <rc-para-girdisi formControlName="kur" [kesir]="6" yerTutucu="" />
            </rc-alan>
          }
          <rc-alan [etiket]="'kiraFinans.alan.hesap' | transloco">
            <rc-secim
              formControlName="hesapId"
              [secenekler]="hesaplar()"
              [bosEtiket]="'kiraFinans.alan.hesapYok' | transloco"
            />
          </rc-alan>
          <rc-alan [etiket]="'kiraFinans.alan.kanal' | transloco">
            <rc-secim formControlName="kanal" [secenekler]="f.kanalSecenekleri" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFinans.alan.odeyen' | transloco" class="rc-form-izgara__genis">
            <rc-metin-girdisi formControlName="aciklama" [azamiUzunluk]="512" />
          </rc-alan>
        </div>
        <rc-form-hatalari [hatalar]="t.gonderim.genelHatalar()" />
        @if (t.uyari(); as uyari) {
          <p
            class="rc-form-mesaji rc-form-mesaji--uyari"
            role="alert"
            [attr.data-testid]="'tahsilat-uyari-' + t.hesap"
          >
            <strong>{{ 'kiraFinans.kayitDegismis' | transloco }}:</strong> {{ uyari }}
          </p>
        }
        @if (t.kopya.tazelemeBekleniyor()) {
          <p class="kf-not" role="status">{{ 'kiraFinans.tahsilat.tazeleniyor' | transloco }}</p>
        }
        <div class="kf-eylemler">
          <button
            type="button"
            class="rc-dugme rc-dugme--birincil"
            [attr.data-testid]="'tahsilat-' + t.hesap"
            [disabled]="!t.kopya.gonderilebilir() || t.gonderim.gonderiliyor()"
            (click)="f.tahsilatYap(t)"
          >
            {{
              (t.gonderim.gonderiliyor()
                ? 'form.gonderiliyor'
                : t.hesap === 'Kasa'
                  ? 'kiraFinans.tahsilat.nakitDugme'
                  : 'kiraFinans.tahsilat.kartDugme'
              ) | transloco
            }}
          </button>
        </div>
      }
    </section>
  `,
})
export class FinansTahsilat {
  readonly tf = input.required<TahsilatFormu>();
  protected readonly f = inject(KiraFinansDurumu);
  protected readonly temel = TEMEL_DOVIZ;

  protected readonly baslikId = computed(() => `kf-finans-tahsilat-${this.tf().hesap}`);
  protected readonly dovizler = computed((): readonly SecenekOgesi<string>[] =>
    dovizSecenekleri(this.tf().kopya.kopya()?.doviz).map((d) => ({ deger: d, etiket: d })),
  );
  protected readonly hesaplar = computed(() => this.f.hesapSecenekleri(this.tf().hesap));
}
