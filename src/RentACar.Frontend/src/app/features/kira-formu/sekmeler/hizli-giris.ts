import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { paraBicimle } from '@core/bicim/bicim';
import { KiraFormuDurumu } from '../kira-formu-durumu';
import { isoParaBirimi, sayiya } from '../kira-formu-modeli';
import type { SunucuSayisi } from '../kira-tipleri';
import { AracKarti } from './arac-karti';
import { HesapOzeti } from './hesap-ozeti';
import { KF_ORTAK } from './ortak';
import { YeniMusteri } from './yeni-musteri';

/**
 * HIZLI GİRİŞ — diğer sekmelerin kritik alanlarını tek ekranda toplar (deneyimli operatör buradan kira
 * açar). Alanlar AYNA: kanonik kontrol kendi sekmesinde, buradaki kopya iki yönlü eşitlenir
 * (`form.ayna`); gövdeye yalnız kanonik girer.
 */
@Component({
  selector: 'rc-kf-hizli-giris',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...KF_ORTAK, AracKarti, HesapOzeti, YeniMusteri],
  template: `
    <div class="kf-iki-sutun" [formGroup]="d.form.controls.ayna">
      <div class="kf-sutun">
        <section class="kf-kart">
          <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.musteri' | transloco }}</h3>
          <div class="rc-form-izgara">
            <rc-alan [etiket]="'kiraFormu.alan.musteri' | transloco" class="rc-form-izgara__genis">
              <rc-arama-secim
                formControlName="musteri"
                [kaynak]="d.musteriKaynagi"
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

        <section class="kf-kart">
          <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.tarihLokasyon' | transloco }}</h3>
          <div class="rc-form-izgara">
            <rc-alan [etiket]="'kiraFormu.alan.basTar' | transloco">
              <rc-tarih-saat-secici formControlName="basTar" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.bitTar' | transloco">
              <rc-tarih-saat-secici formControlName="bitTar" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.cikisOfisi' | transloco">
              <rc-arama-secim formControlName="cikisOfisi" [kaynak]="d.lokasyonKaynagi" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.donusOfisi' | transloco">
              <rc-arama-secim formControlName="donusOfisi" [kaynak]="d.lokasyonKaynagi" />
            </rc-alan>
          </div>
          <p class="kf-not">{{ 'kiraFormu.not.drop' | transloco }}</p>
        </section>

        <section class="kf-kart">
          <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.arac' | transloco }}</h3>
          <div class="rc-form-izgara">
            <rc-alan [etiket]="'kiraFormu.alan.arac' | transloco" class="rc-form-izgara__genis">
              <rc-arama-secim
                formControlName="arac"
                [kaynak]="d.aracKaynagi"
                [yerTutucu]="'kiraFormu.alan.plakaAra' | transloco"
              />
            </rc-alan>
          </div>
          <rc-kf-arac-karti kisa />
        </section>
      </div>

      <div class="kf-sutun">
        <section class="kf-kart">
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
                [paraBirimi]="d.kiraDovizi()"
                [yerTutucu]="'kiraFormu.alan.otomatikBos' | transloco"
              />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.kaynak' | transloco" class="rc-form-izgara__genis">
              <rc-metin-girdisi formControlName="kaynak" liste="kf-dl-kaynak" [azamiUzunluk]="64" />
            </rc-alan>
          </div>
        </section>
        <rc-kf-hesap-ozeti kisa />
        <section class="kf-kart" [attr.aria-label]="'kiraFormuParite.rozet.etiket' | transloco">
          <div class="kf-eylemler" data-testid="hizli-rozetler">
            <span class="rc-rozet">{{
              'kiraFormuParite.rozet.tahsilat' | transloco: { tutar: rozetler().tahsilat }
            }}</span>
            <span class="rc-rozet">{{
              'kiraFormuParite.rozet.kalan' | transloco: { tutar: rozetler().kalan }
            }}</span>
            <span class="rc-rozet" data-testid="ceza-rozeti">{{
              'kiraFormuParite.rozet.ceza' | transloco: { tutar: rozetler().ceza }
            }}</span>
          </div>
        </section>
      </div>
    </div>
  `,
})
export class HizliGiris {
  protected readonly d = inject(KiraFormuDurumu);

  /**
   * Alt şerit rozetleri (Blazor Hızlı Giriş): tahsilat, kalan, ceza. Hepsi SUNUCU değeri — kayıtlı kirada
   * sözleşme + `toplamlar.cezaToplam` (F4.3b; ceza kaydı TL), yeni kirada canlı hesabın kalanı. Toplama yok.
   */
  protected readonly rozetler = computed(() => {
    const k = this.d.kira();
    if (k) {
      const doviz = isoParaBirimi(k.doviz);
      return {
        tahsilat: para(k.tahsilat, doviz),
        kalan: para(k.bakiye, doviz),
        ceza: para(this.d.detay.veri()?.toplamlar?.cezaToplam, 'TRY'),
      };
    }
    const h = this.d.hesap.veri();
    return {
      tahsilat: para(0, this.d.kiraDovizi()),
      kalan: h?.ok ? para(h.kalan, isoParaBirimi(h.doviz)) : '—',
      ceza: para(0, 'TRY'),
    };
  });
}

function para(v: SunucuSayisi, doviz: string): string {
  return paraBicimle(sayiya(v), doviz) || '—';
}
