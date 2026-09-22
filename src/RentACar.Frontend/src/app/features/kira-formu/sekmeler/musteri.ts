import { ChangeDetectionStrategy, Component, inject, output } from '@angular/core';
import { KiraFormuDurumu } from '../kira-formu-durumu';
import { KF_ORTAK } from './ortak';

/**
 * MÜŞTERİ — kanonik müşteri + 2. sürücü (kayıtlı cari ya da misafir) + risk/kefil. Müşteri iletişim,
 * kimlik ve adres bilgisi cari kartındadır (sözleşme ekranı kimlik numarası dökmez; bu API'de yok).
 */
@Component({
  selector: 'rc-kf-musteri',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...KF_ORTAK],
  template: `
    <div [formGroup]="d.form">
      <section class="kf-kart">
        <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.musteriSecimi' | transloco }}</h3>
        <div class="rc-form-izgara">
          <rc-alan
            [etiket]="'kiraFormu.alan.musteri' | transloco"
            [ipucu]="d.yeni ? '' : ('kiraFormu.ipucu.musteriDegismez' | transloco)"
          >
            <rc-arama-secim formControlName="musteri" [kaynak]="d.musteriKaynagi" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.ikinciSurucu' | transloco">
            <rc-arama-secim formControlName="ikinciSurucu" [kaynak]="d.musteriKaynagi" />
          </rc-alan>
        </div>
        @if (d.yeni) {
          <button type="button" class="rc-dugme rc-dugme--hayalet" (click)="yeniMusteriye.emit()">
            {{ 'kiraFormu.musteri.yeniyeGit' | transloco }}
          </button>
        } @else if (d.kira(); as k) {
          <p class="kf-baglantilar">
            <a [href]="'/cariler/' + k.musteriId">{{
              'kiraFormu.baglanti.cariKarti' | transloco
            }}</a>
            <a [href]="'/cariler/' + k.musteriId + '/ekstre'">{{
              'kiraFormu.baglanti.ekstre' | transloco
            }}</a>
          </p>
        }
        <p class="kf-not">{{ 'kiraFormu.not.musteriBilgisi' | transloco }}</p>
      </section>

      <section class="kf-kart">
        <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.misafirSurucu' | transloco }}</h3>
        <div class="rc-form-izgara">
          <rc-alan [etiket]="'kiraFormu.alan.ad' | transloco">
            <rc-metin-girdisi formControlName="ikinciSurucuSerbestAd" [azamiUzunluk]="64" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.soyad' | transloco">
            <rc-metin-girdisi formControlName="ikinciSurucuSerbestSoyad" [azamiUzunluk]="64" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.telefon' | transloco">
            <rc-metin-girdisi
              formControlName="ikinciSurucuSerbestTel"
              tur="tel"
              [azamiUzunluk]="32"
            />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.ehliyetSinifi' | transloco">
            <rc-metin-girdisi
              formControlName="ikinciSurucuSerbestEhliyetSinifi"
              [azamiUzunluk]="16"
              yerTutucu="B"
            />
          </rc-alan>
        </div>
        <p class="kf-not">{{ 'kiraFormu.not.misafirSurucu' | transloco }}</p>
      </section>

      <section class="kf-kart">
        <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.risk' | transloco }}</h3>
        <div class="rc-form-izgara">
          <rc-alan [etiket]="'kiraFormu.alan.findeks' | transloco">
            <rc-sayi-girdisi formControlName="manuelFindexPuan" />
          </rc-alan>
          @if (d.riskOnayGorunur()) {
            <rc-alan [etiket]="'kiraFormu.alan.riskOnay' | transloco" etiketGizli>
              <rc-onay-kutusu formControlName="riskOnay">{{
                'kiraFormu.alan.riskOnay' | transloco
              }}</rc-onay-kutusu>
            </rc-alan>
          }
          <rc-alan [etiket]="'kiraFormu.alan.kefil' | transloco" class="rc-form-izgara__genis">
            <rc-metin-girdisi formControlName="kefilBilgisi" [azamiUzunluk]="512" />
          </rc-alan>
        </div>
        <p class="kf-not">{{ 'kiraFormu.not.findeks' | transloco }}</p>
      </section>
    </div>
  `,
})
export class Musteri {
  protected readonly d = inject(KiraFormuDurumu);
  /** "Yeni müşteri" bloğu Hızlı Giriş'te (formda tek) — sayfa o sekmeye geçip bloğu açar. */
  readonly yeniMusteriye = output<void>();
}
