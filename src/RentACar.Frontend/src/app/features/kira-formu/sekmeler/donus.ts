import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { paraBicimle } from '@core/bicim/bicim';
import { BITIS_SEBEPLERI, KiraFormuDurumu } from '../kira-formu-durumu';
import { isoParaBirimi, sayiya } from '../kira-formu-modeli';
import type { SunucuSayisi } from '../kira-tipleri';
import { KF_ORTAK } from './ortak';

/**
 * DÖNÜŞ — açık + teslim edilmiş kirada dönüş işlemi (`POST /kiralar/{id}/donus`) + CANLI önizleme
 * (`GET /kiralar/{id}/donus-hesapla` — ReturnMath; dönüşte kaydedilen hesabın kendisi) + uzatma.
 * Varsayılan gerçek dönüş = beklenen bitiş ANI (yerel saate çevrilip UTC sayılmaz — Blazor'daki
 * "hayalet +1 gün" dersi).
 */
@Component({
  selector: 'rc-kf-donus',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...KF_ORTAK],
  template: `
    @if (d.yeni) {
      <p class="kf-not">{{ 'kiraFormu.not.donusKayittanSonra' | transloco }}</p>
    }

    @if (d.kirada() && d.teslimEdildi()) {
      <section class="kf-kart">
        <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.donusIslemi' | transloco }}</h3>
        <div class="rc-form-izgara" [formGroup]="d.donusFormu" data-testid="donus-formu">
          <rc-alan [etiket]="'kiraFormu.alan.donusKm' | transloco">
            <rc-sayi-girdisi formControlName="donusKm" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.donusYakit' | transloco">
            <rc-sayi-girdisi formControlName="donusYakit" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.gercekDonus' | transloco">
            <rc-tarih-saat-secici formControlName="gercekDonus" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.kmHediye' | transloco">
            <rc-sayi-girdisi formControlName="kmHediye" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.bitisSebebi' | transloco">
            <rc-secim formControlName="bitisSebebi" [secenekler]="sebepler" bosEtiket="—" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.teslimAlan' | transloco">
            <rc-arama-secim formControlName="teslimAlanPersonel" [kaynak]="d.personelKaynagi" />
          </rc-alan>
          <div class="kf-eylemler">
            <button
              type="button"
              class="rc-dugme rc-dugme--birincil"
              [disabled]="!d.operasyon() || d.donusGonderimi.gonderiliyor()"
              (click)="d.donusYap()"
            >
              {{ 'kiraFormu.eylem.donusTamamla' | transloco }}
            </button>
          </div>
        </div>
        <rc-form-hatalari [hatalar]="d.donusGonderimi.genelHatalar()" />

        <div class="kf-hesap" [attr.aria-busy]="d.donusOnizleme.yukleniyor()">
          <h4 class="kf-kart__baslik">{{ 'kiraFormu.bolum.donusOnizleme' | transloco }}</h4>
          @if (d.donusOnizleme.veri(); as o) {
            @if (o.ok) {
              <dl class="kf-satirlar" data-testid="donus-onizleme">
                <div>
                  <dt>{{ 'kiraFormu.donus.kullanilanKm' | transloco }}</dt>
                  <dd>{{ o.kullanilanKm }}</dd>
                </div>
                <div>
                  <dt>{{ 'kiraFormu.donus.fazlaKm' | transloco }}</dt>
                  <dd>{{ o.fazlaKm }}</dd>
                </div>
                <div>
                  <dt>{{ 'kiraFormu.donus.fazlaKmBedeli' | transloco }}</dt>
                  <dd>{{ para(o.fazlaKmBedeli) }}</dd>
                </div>
                <div>
                  <dt>{{ 'kiraFormu.donus.eksikYakit' | transloco }}</dt>
                  <dd>{{ o.eksikYakit }}</dd>
                </div>
                <div>
                  <dt>{{ 'kiraFormu.donus.yakitBedeli' | transloco }}</dt>
                  <dd>{{ para(o.yakitBedeli) }}</dd>
                </div>
                <div>
                  <dt>{{ 'kiraFormu.donus.gecGun' | transloco }}</dt>
                  <dd>{{ o.uzatmaGun }}</dd>
                </div>
                <div>
                  <dt>{{ 'kiraFormu.donus.gecBedel' | transloco }}</dt>
                  <dd>{{ para(o.uzatmaBedeli) }}</dd>
                </div>
                <div class="kf-satirlar__vurgu">
                  <dt>{{ 'kiraFormu.donus.yeniGenelToplam' | transloco }}</dt>
                  <dd>{{ para(o.yeniGenelToplam) }}</dd>
                </div>
                <div>
                  <dt>{{ 'kiraFormu.hesap.kalan' | transloco }}</dt>
                  <dd>{{ para(o.kalan) }}</dd>
                </div>
              </dl>
            } @else {
              <p class="kf-not kf-not--uyari" role="status">{{ o.hata }}</p>
            }
          } @else {
            <p class="kf-not">{{ 'kiraFormu.not.donusKmGirin' | transloco }}</p>
          }
        </div>
      </section>
    } @else if (d.kirada()) {
      <p class="kf-not">{{ 'kiraFormu.not.onceTeslim' | transloco }}</p>
    }

    @if (d.kirada()) {
      <section class="kf-kart">
        <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.uzatma' | transloco }}</h3>
        <p class="kf-not">{{ 'kiraFormu.not.uzatma' | transloco }}</p>
        <div class="rc-form-izgara" [formGroup]="d.uzatFormu">
          <rc-alan [etiket]="'kiraFormu.alan.yeniBitTar' | transloco">
            <rc-tarih-saat-secici formControlName="yeniBitTar" />
          </rc-alan>
          <div class="kf-eylemler">
            <button
              type="button"
              class="rc-dugme"
              [disabled]="!d.operasyon() || d.uzatGonderimi.gonderiliyor()"
              (click)="d.uzat()"
            >
              {{ 'kiraFormu.eylem.uzat' | transloco }}
            </button>
          </div>
        </div>
        <rc-form-hatalari [hatalar]="d.uzatGonderimi.genelHatalar()" />
      </section>
    }

    @if (d.kira(); as k) {
      <section class="kf-kart">
        <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.donusBilgileri' | transloco }}</h3>
        <dl class="kf-bilgiler">
          <div>
            <dt>{{ 'kiraFormu.alan.durum' | transloco }}</dt>
            <dd>{{ 'kiraFormu.durum.' + k.durum | transloco }}</dd>
          </div>
          <div>
            <dt>{{ 'kiraFormu.alan.bitTar' | transloco }}</dt>
            <dd>{{ k.bitTar | tarihSaat }}</dd>
          </div>
          <div>
            <dt>{{ 'kiraFormu.alan.gercekDonus' | transloco }}</dt>
            <dd>{{ (k.gercekDonusTar | tarihSaat) || '—' }}</dd>
          </div>
          <div>
            <dt>{{ 'kiraFormu.alan.teslimAlan' | transloco }}</dt>
            <dd>{{ d.detay.veri()?.teslimAlanPersonelAd ?? '—' }}</dd>
          </div>
          <div>
            <dt>{{ 'kiraFormu.alan.bitisSebebi' | transloco }}</dt>
            <dd>{{ k.bitisSebebi ?? '—' }}</dd>
          </div>
          <div>
            <dt>{{ 'kiraFormu.alan.donusOfisi' | transloco }}</dt>
            <dd>{{ k.donusOfisi ?? '—' }}</dd>
          </div>
          <div>
            <dt>{{ 'kiraFormu.alan.donusKm' | transloco }}</dt>
            <dd>{{ k.donusKm ?? '—' }}</dd>
          </div>
          <div>
            <dt>{{ 'kiraFormu.alan.donusYakit' | transloco }}</dt>
            <dd>{{ k.donusYakit ?? '—' }}</dd>
          </div>
          <div>
            <dt>{{ 'kiraFormu.alan.kmHediye' | transloco }}</dt>
            <dd>{{ k.kmHediye ?? '—' }}</dd>
          </div>
        </dl>
      </section>
      <section class="kf-kart">
        <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.donusBedelleri' | transloco }}</h3>
        <dl class="kf-satirlar">
          <div>
            <dt>{{ 'kiraFormu.donus.fazlaKm' | transloco }}</dt>
            <dd>{{ k.fazlaKm }}</dd>
          </div>
          <div>
            <dt>{{ 'kiraFormu.donus.fazlaKmBedeli' | transloco }}</dt>
            <dd>{{ para(k.fazlaKmBedeli) }}</dd>
          </div>
          <div>
            <dt>{{ 'kiraFormu.donus.eksikYakit' | transloco }}</dt>
            <dd>{{ k.eksikYakit }}</dd>
          </div>
          <div>
            <dt>{{ 'kiraFormu.donus.yakitBedeli' | transloco }}</dt>
            <dd>{{ para(k.yakitBedeli) }}</dd>
          </div>
          <div>
            <dt>{{ 'kiraFormu.donus.gecGun' | transloco }}</dt>
            <dd>{{ k.uzatmaGun }}</dd>
          </div>
          <div>
            <dt>{{ 'kiraFormu.donus.gecBedel' | transloco }}</dt>
            <dd>{{ para(k.uzatmaBedeli) }}</dd>
          </div>
          <div class="kf-satirlar__vurgu">
            <dt>{{ 'kiraFormu.hesap.genelToplam' | transloco }}</dt>
            <dd>{{ para(k.genelToplam) }}</dd>
          </div>
        </dl>
      </section>
    }
  `,
})
export class Donus {
  protected readonly d = inject(KiraFormuDurumu);
  protected readonly sebepler = BITIS_SEBEPLERI.map((s) => ({ deger: s, etiket: s }));

  protected para(v: SunucuSayisi): string {
    return paraBicimle(sayiya(v), isoParaBirimi(this.d.kira()?.doviz)) || '—';
  }
}
