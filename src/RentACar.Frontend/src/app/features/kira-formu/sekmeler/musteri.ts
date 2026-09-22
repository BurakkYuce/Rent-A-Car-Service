import { ChangeDetectionStrategy, Component, computed, inject, output } from '@angular/core';
import { KiraFormuDurumu } from '../kira-formu-durumu';
import { sayiya } from '../kira-formu-modeli';
import { KF_ORTAK } from './ortak';

/**
 * MÜŞTERİ — kanonik müşteri + 2. sürücü (kayıtlı cari ya da misafir) + risk/kefil. Kayıtlı kirada cari
 * özeti SALT OKUNUR (F4.3b `GET /kiralar/{id}/musteri-ozet`): iletişim, adres, ehliyet/pasaport bilgisi, risk
 * limiti, kara liste/uyarı. TC kimlik numarası HİÇ gelmez (Blazor gibi "şifreli — cari kartında"; KVKK en az veri);
 * ehliyet/pasaport numarası yalnız maskeli (son 4 hane — sunucu kuralı; SPA düz numara görmez). Düzenleme cari kartında.
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
        @if (ozet(); as m) {
          @if (m.karaListe || m.uyari) {
            <p class="rc-form-mesaji rc-form-mesaji--hata" role="alert" data-testid="kara-liste">
              <strong>{{ 'kiraFormuParite.musteri.dikkat' | transloco }}</strong>
              @if (m.karaListe) {
                <span class="rc-rozet rc-rozet--hata">{{
                  'kiraFormuParite.musteri.karaListe' | transloco
                }}</span>
                {{ 'kiraFormuParite.musteri.karaListeMesaji' | transloco }}
              }
              @if (m.uyari) {
                {{
                  'kiraFormuParite.musteri.uyariMesaji' | transloco: { neden: m.uyariNedeni ?? '' }
                }}
              }
            </p>
          }
        }
        <p class="kf-not">{{ 'kiraFormu.not.musteriBilgisi' | transloco }}</p>
      </section>

      @if (d.yeni) {
        <p class="kf-not">{{ 'kiraFormuParite.musteri.kayittanSonra' | transloco }}</p>
      } @else if (ozet(); as m) {
        <section class="kf-kart" data-testid="musteri-ozeti">
          <h3 class="kf-kart__baslik">
            {{ 'kiraFormuParite.musteri.iletisimBaslik' | transloco }}
          </h3>
          <dl class="kf-bilgiler">
            <div>
              <dt>{{ 'kiraFormuParite.musteri.cepTel' | transloco }}</dt>
              <dd>{{ m.cepTel || '—' }}</dd>
            </div>
            <div>
              <dt>{{ 'kiraFormuParite.musteri.eposta' | transloco }}</dt>
              <dd>{{ m.email || '—' }}</dd>
            </div>
            <div>
              <dt>{{ 'kiraFormuParite.musteri.tcKimlik' | transloco }}</dt>
              <dd data-testid="tc-kimlik">{{ 'kiraFormuParite.musteri.tcSifreli' | transloco }}</dd>
            </div>
            <div>
              <dt>{{ 'kiraFormuParite.musteri.ehliyetSinifi' | transloco }}</dt>
              <dd>{{ m.ehliyetSinifi || '—' }}</dd>
            </div>
            <div>
              <dt>{{ 'kiraFormuParite.musteri.ehliyetYeri' | transloco }}</dt>
              <dd>{{ m.ehliyetYeri || '—' }}</dd>
            </div>
          </dl>
        </section>
        <section class="kf-kart">
          <h3 class="kf-kart__baslik">{{ 'kiraFormuParite.musteri.adresBaslik' | transloco }}</h3>
          <dl class="kf-bilgiler">
            <div class="rc-form-izgara__genis">
              <dt>{{ 'kiraFormuParite.musteri.adres' | transloco }}</dt>
              <dd>{{ m.adres || '—' }}</dd>
            </div>
            <div>
              <dt>{{ 'kiraFormuParite.musteri.il' | transloco }}</dt>
              <dd>{{ m.il || '—' }}</dd>
            </div>
            <div>
              <dt>{{ 'kiraFormuParite.musteri.ilce' | transloco }}</dt>
              <dd>{{ m.ilce || '—' }}</dd>
            </div>
            <div>
              <dt>{{ 'kiraFormuParite.musteri.musteriTipi' | transloco }}</dt>
              <dd>{{ m.musteriTipi || '—' }}</dd>
            </div>
            <div>
              <dt>{{ 'kiraFormuParite.musteri.ehliyetNo' | transloco }}</dt>
              <dd>{{ m.ehliyetNoMaskeli || '—' }}</dd>
            </div>
            <div>
              <dt>{{ 'kiraFormuParite.musteri.ehliyetTarihi' | transloco }}</dt>
              <dd>{{ (m.ehliyetTarihi | tarih) || '—' }}</dd>
            </div>
            <div>
              <dt>{{ 'kiraFormuParite.musteri.ehliyetUlke' | transloco }}</dt>
              <dd>{{ m.ehliyetUlke || '—' }}</dd>
            </div>
            <div>
              <dt>{{ 'kiraFormuParite.musteri.pasaportNo' | transloco }}</dt>
              <dd>{{ m.pasaportNoMaskeli || '—' }}</dd>
            </div>
            <div>
              <dt>{{ 'kiraFormuParite.musteri.pasaportYeri' | transloco }}</dt>
              <dd>{{ m.pasaportYeri || '—' }}</dd>
            </div>
          </dl>
          <p class="kf-not">{{ 'kiraFormuParite.musteri.maskeNotu' | transloco }}</p>
        </section>
      } @else if (d.musteriOzeti.tur() === 'hata') {
        <p class="kf-not kf-not--uyari" role="status">
          {{ 'kiraFormuParite.musteri.alinamadi' | transloco }}
        </p>
      }

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
          @if (ozet(); as m) {
            <div
              class="kf-bilgi"
              [attr.title]="'kiraFormuParite.musteri.riskLimitiIpucu' | transloco"
            >
              <span>{{ 'kiraFormuParite.musteri.riskLimiti' | transloco }}</span>
              <strong data-testid="risk-limiti">{{ sayi(m.riskLimiti) | para }}</strong>
            </div>
          }
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
  protected readonly sayi = sayiya;
  /** Kayıtlı kiranın cari özeti (yeni kirada yok). */
  protected readonly ozet = computed(() =>
    this.d.yeni ? null : (this.d.musteriOzeti.veri() ?? null),
  );
  /** "Yeni müşteri" bloğu Hızlı Giriş'te (formda tek) — sayfa o sekmeye geçip bloğu açar. */
  readonly yeniMusteriye = output<void>();
}
