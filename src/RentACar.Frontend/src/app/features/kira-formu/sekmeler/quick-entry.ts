import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { formatMoney } from '@core/bicim/bicim';
import { RentalFormState } from '../rental-form-state';
import { isoCurrency, toNumber } from '../kira-formu-modeli';
import type { ServerNumber } from '../kira-tipleri';
import { VehicleCard } from './vehicle-card';
import { CalculationSummary } from './calculation-summary';
import { KF_SHARED } from './ortak';
import { NewCustomer } from './new-customer';

/**
 * HIZLI GİRİŞ — diğer sekmelerin kritik alanlarını tek ekranda toplar (deneyimli operatör buradan kira
 * açar). Alanlar AYNA: kanonik kontrol kendi sekmesinde, buradaki kopya iki yönlü eşitlenir
 * (`form.ayna`); gövdeye yalnız kanonik girer.
 */
@Component({
  selector: 'rc-kf-hizli-giris',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...KF_SHARED, VehicleCard, CalculationSummary, NewCustomer],
  template: `
    <div class="kf-iki-sutun" [formGroup]="d.form.controls.ayna">
      <div class="kf-sutun">
        <section class="rc-bolum kf-kart">
          <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.musteri' | transloco }}</h3>
          <div class="rc-form-izgara">
            <rc-alan [etiket]="'kiraFormu.alan.musteri' | transloco" class="rc-form-izgara__genis">
              <rc-arama-secim
                formControlName="musteri"
                [kaynak]="d.customerDataSource"
                [yerTutucu]="'kiraFormu.alan.musteriAra' | transloco"
              />
            </rc-alan>
            <div class="kf-bilgi">
              <span>{{ 'kiraFormu.alan.durum' | transloco }}</span>
              <strong>{{ 'kiraFormu.durum.' + (d.durum() ?? 'yeni') | transloco }}</strong>
            </div>
            <div class="kf-bilgi">
              <span>{{ 'kiraFormu.alan.sozlesmeNo' | transloco }}</span>
              <strong>{{
                d.kira()?.sozlesmeNo ?? ('kiraFormu.alan.noKayittaVerilir' | transloco)
              }}</strong>
            </div>
          </div>
          @if (d.yeni) {
            <rc-kf-yeni-musteri />
          }
        </section>

        <section class="rc-bolum kf-kart">
          <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.tarihLokasyon' | transloco }}</h3>
          <div class="rc-form-izgara">
            <rc-alan [etiket]="'kiraFormu.alan.basTar' | transloco">
              <rc-tarih-saat-secici formControlName="basTar" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.bitTar' | transloco">
              <rc-tarih-saat-secici formControlName="bitTar" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.cikisOfisi' | transloco">
              <rc-arama-secim formControlName="cikisOfisi" [kaynak]="d.locationDataSource" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.donusOfisi' | transloco">
              <rc-arama-secim formControlName="donusOfisi" [kaynak]="d.locationDataSource" />
            </rc-alan>
          </div>
          <p class="kf-not">{{ 'kiraFormu.not.drop' | transloco }}</p>
        </section>

        <section class="rc-bolum kf-kart">
          <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.arac' | transloco }}</h3>
          <div class="rc-form-izgara">
            <rc-alan [etiket]="'kiraFormu.alan.arac' | transloco" class="rc-form-izgara__genis">
              <rc-arama-secim
                formControlName="arac"
                [kaynak]="d.vehicleSource"
                [yerTutucu]="'kiraFormu.alan.plakaAra' | transloco"
              />
            </rc-alan>
          </div>
          <rc-kf-arac-karti kisa />
        </section>
      </div>

      <div class="kf-sutun">
        <section class="rc-bolum kf-kart">
          <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.fiyat' | transloco }}</h3>
          <div class="rc-form-izgara">
            <rc-alan [etiket]="'kiraFormu.alan.kiralamaTuru' | transloco">
              <rc-secim
                formControlName="kiralamaTuru"
                [secenekler]="d.kiralamaTurleri()"
                bosEtiket="—"
              />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.fiyatTuru' | transloco">
              <rc-secim formControlName="fiyatTuru" [secenekler]="d.fiyatTurleri()" bosEtiket="—" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.doviz' | transloco">
              <rc-secim formControlName="doviz" [secenekler]="d.dovizler()" bosEtiket="—" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.gunlukUcret' | transloco">
              <rc-para-girdisi
                formControlName="gunlukUcret"
                [paraBirimi]="d.rentalCurrency()"
                [yerTutucu]="'kiraFormu.alan.otomatikBos' | transloco"
              />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.kaynak' | transloco" class="rc-form-izgara__genis">
              <rc-metin-girdisi formControlName="kaynak" liste="kf-dl-kaynak" [azamiUzunluk]="64" />
            </rc-alan>
          </div>
        </section>
        <rc-kf-hesap-ozeti kisa />
        <section
          class="rc-bolum kf-kart"
          [attr.aria-label]="'kiraFormuParite.rozet.etiket' | transloco"
        >
          <div class="kf-eylemler" data-testid="hizli-rozetler">
            <span class="rc-rozet">{{
              'kiraFormuParite.rozet.tahsilat' | transloco: { tutar: badges().tahsilat }
            }}</span>
            <span class="rc-rozet">{{
              'kiraFormuParite.rozet.kalan' | transloco: { tutar: badges().kalan }
            }}</span>
            <span class="rc-rozet" data-testid="ceza-rozeti">{{
              'kiraFormuParite.rozet.ceza' | transloco: { tutar: badges().ceza }
            }}</span>
          </div>
        </section>
      </div>
    </div>
  `,
})
export class QuickEntry {
  protected readonly d = inject(RentalFormState);

  /**
   * Alt şerit rozetleri (Blazor Hızlı Giriş): tahsilat, kalan, ceza. Hepsi SUNUCU değeri — kayıtlı kirada
   * sözleşme + `toplamlar.cezaToplam` (F4.3b; ceza kaydı TL), yeni kirada canlı hesabın kalanı. Toplama yok.
   */
  protected readonly badges = computed(() => {
    const k = this.d.kira();
    if (k) {
      const currency = isoCurrency(k.doviz);
      return {
        tahsilat: money(k.tahsilat, currency),
        kalan: money(k.bakiye, currency),
        ceza: money(this.d.visibleDetail()?.toplamlar?.cezaToplam, 'TRY'),
      };
    }
    const h = this.d.hesap.veri();
    return {
      tahsilat: money(0, this.d.rentalCurrency()),
      kalan: h?.ok ? money(h.kalan, isoCurrency(h.doviz)) : '—',
      ceza: money(0, 'TRY'),
    };
  });
}

function money(v: ServerNumber, currency: string): string {
  return formatMoney(toNumber(v), currency) || '—';
}
