import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { toNumber } from '../kira-formu-modeli';
import { KF_SHARED } from '../sekmeler/ortak';
import { BASE_CURRENCY, currencyCode, currencyOptions, displayMoney } from './finans-modeli';
import { RentalFinanceState, type TahsilatFormu } from './rental-finance-state';

/**
 * Kira tahsilatı (Nakit = Kasa, Kart/Havale = Banka) — `POST finans/tahsilat`. Cari, kira ve anahtar
 * sunucunun satır kopyasından; formda yalnız tutar, döviz, kur (dövizde; boş = sunucu çözer), hesap,
 * kanal ve not. Düğme gönderim boyunca ve işlem sonrası yeni kayıt gelene dek kapalı — iki form (Nakit, Kart/Havale)
 * aynı anahtarı taşıdığı için ötekinin gönderimi/tazelemesi de düğmeyi kapatır (`tahsilatMesgul`).
 */
@Component({
  selector: 'rc-kf-finans-tahsilat',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...KF_SHARED],
  template: `
    @let t = tf();
    <section
      class="kf-finans__islem"
      [attr.aria-labelledby]="titleId()"
      [attr.data-testid]="'tahsilat-formu-' + t.hesap"
    >
      <h3 class="kf-finans__baslik" [id]="titleId()">
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
            <rc-secim formControlName="kanal" [secenekler]="f.channelOptions" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFinans.alan.odeyen' | transloco" class="rc-form-izgara__genis">
            <rc-metin-girdisi formControlName="aciklama" [azamiUzunluk]="512" />
          </rc-alan>
        </div>
        @if (balanceWarning(); as u) {
          <p
            class="kf-not kf-not--uyari"
            role="note"
            [attr.data-testid]="'tahsilat-bakiye-' + t.hesap"
          >
            {{ u.anahtar | transloco: { kalan: u.kalan } }}
          </p>
        }
        <rc-form-hatalari [hatalar]="t.gonderim.genelHatalar()" />
        @if (f.collectionLoadFailed()) {
          <div class="kf-finans__satir" role="alert">
            <p class="kf-not kf-not--uyari">
              {{ 'kiraFinans.tahsilat.yuklenemedi' | transloco }}
            </p>
            <button
              type="button"
              class="rc-dugme rc-dugme--kucuk"
              [attr.data-testid]="'tahsilat-yeniden-yukle-' + t.hesap"
              (click)="f.yenile()"
            >
              {{ 'kiraFinans.tahsilat.yenidenYukle' | transloco }}
            </button>
          </div>
        } @else if (f.isCollectionRefreshing()) {
          <p class="kf-not" role="status">{{ 'kiraFinans.tahsilat.tazeleniyor' | transloco }}</p>
        }
        <div class="kf-eylemler">
          <button
            type="button"
            class="rc-dugme rc-dugme--birincil"
            [attr.data-testid]="'tahsilat-' + t.hesap"
            [disabled]="!t.kopya.canSubmit() || f.isCollectionBusy()"
            (click)="f.doCollection(t)"
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
export class FinanceCollection {
  readonly tf = input.required<TahsilatFormu>();
  protected readonly f = inject(RentalFinanceState);
  protected readonly temel = BASE_CURRENCY;

  protected readonly titleId = computed(() => `kf-finans-tahsilat-${this.tf().hesap}`);
  protected readonly dovizler = computed((): readonly SecenekOgesi<string>[] =>
    currencyOptions(this.tf().kopya.kopya()?.doviz).map((d) => ({ deger: d, etiket: d })),
  );
  protected readonly hesaplar = computed(() => this.f.accountOptions(this.tf().hesap));

  /**
   * Adversarial L6 (Blazor "Kalan" rozeti paritesi): kalan bakiye yoksa ya da tutar kalanı aşıyorsa uyarı. Yalnız
   * GÖSTERİM — gönderimi engellemez (ön/fazla tahsilat meşru); döviz farklıysa karşılaştırılmaz.
   */
  protected readonly balanceWarning = computed(
    (): { anahtar: CeviriAnahtari; kalan: string } | null => {
      const k = this.f.kira();
      const balance = toNumber(k?.bakiye);
      if (!k || balance === null) return null;
      const remaining = displayMoney(k.bakiye, k.doviz);
      if (balance <= 0) return { anahtar: 'kiraFinans.tahsilat.bakiyeYok', kalan: remaining };
      const t = this.tf();
      const amount = toNumber(t.tutar());
      return amount !== null && t.doviz() === currencyCode(k.doviz) && amount > balance
        ? { anahtar: 'kiraFinans.tahsilat.kalaniAsiyor', kalan: remaining }
        : null;
    },
  );
}
