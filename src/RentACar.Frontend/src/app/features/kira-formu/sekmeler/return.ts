import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { formatMoney } from '@core/bicim/bicim';
import { END_REASONS, RentalFormState } from '../rental-form-state';
import { isoCurrency, toNumber } from '../kira-formu-modeli';
import type { ServerNumber } from '../kira-tipleri';
import { KF_SHARED } from './ortak';

/**
 * DÖNÜŞ — açık + teslim edilmiş kirada dönüş işlemi (`POST /kiralar/{id}/donus`) + CANLI önizleme
 * (`GET /kiralar/{id}/donus-hesapla` — ReturnMath; dönüşte kaydedilen hesabın kendisi) + uzatma.
 * Varsayılan gerçek dönüş = beklenen bitiş ANI (yerel saate çevrilip UTC sayılmaz — Blazor'daki
 * "hayalet +1 gün" dersi).
 */
@Component({
  selector: 'rc-kf-donus',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...KF_SHARED],
  template: `
    @if (d.yeni) {
      <p class="kf-not">{{ 'kiraFormu.not.donusKayittanSonra' | transloco }}</p>
    }

    @if (d.kirada() && d.delivered()) {
      <section class="rc-bolum kf-kart">
        <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.donusIslemi' | transloco }}</h3>
        <div class="rc-form-izgara" [formGroup]="d.returnForm" data-testid="donus-formu">
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
            <rc-secim formControlName="bitisSebebi" [secenekler]="reasons" bosEtiket="—" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.teslimAlan' | transloco">
            <rc-arama-secim formControlName="teslimAlanPersonel" [kaynak]="d.staffDataSource" />
          </rc-alan>
          <div class="kf-eylemler">
            <button
              type="button"
              class="rc-dugme rc-dugme--birincil"
              [disabled]="!d.operasyon() || d.returnSubmission.gonderiliyor()"
              (click)="d.doReturn()"
            >
              {{ 'kiraFormu.eylem.donusTamamla' | transloco }}
            </button>
          </div>
        </div>
        <rc-form-hatalari [hatalar]="d.returnSubmission.genelHatalar()" />

        <div class="kf-hesap" [attr.aria-busy]="d.returnPreview.isLoading()">
          <h4 class="kf-kart__baslik">{{ 'kiraFormu.bolum.donusOnizleme' | transloco }}</h4>
          @if (d.returnPreview.veri(); as o) {
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
                  <dd>{{ money(o.fazlaKmBedeli) }}</dd>
                </div>
                <div>
                  <dt>{{ 'kiraFormu.donus.eksikYakit' | transloco }}</dt>
                  <dd>{{ o.eksikYakit }}</dd>
                </div>
                <div>
                  <dt>{{ 'kiraFormu.donus.yakitBedeli' | transloco }}</dt>
                  <dd>{{ money(o.yakitBedeli) }}</dd>
                </div>
                <div>
                  <dt>{{ 'kiraFormu.donus.gecGun' | transloco }}</dt>
                  <dd>{{ o.uzatmaGun }}</dd>
                </div>
                <div>
                  <dt>{{ 'kiraFormu.donus.gecBedel' | transloco }}</dt>
                  <dd>{{ money(o.uzatmaBedeli) }}</dd>
                </div>
                <div class="kf-satirlar__vurgu">
                  <dt>{{ 'kiraFormu.donus.yeniGenelToplam' | transloco }}</dt>
                  <dd>{{ money(o.yeniGenelToplam) }}</dd>
                </div>
                <div>
                  <dt>{{ 'kiraFormu.hesap.kalan' | transloco }}</dt>
                  <dd>{{ money(o.kalan) }}</dd>
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
      <section class="rc-bolum kf-kart">
        <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.uzatma' | transloco }}</h3>
        <p class="kf-not">{{ 'kiraFormu.not.uzatma' | transloco }}</p>
        <div class="rc-form-izgara" [formGroup]="d.extendForm">
          <rc-alan [etiket]="'kiraFormu.alan.yeniBitTar' | transloco">
            <rc-tarih-saat-secici formControlName="yeniBitTar" />
          </rc-alan>
          <div class="kf-eylemler">
            <button
              type="button"
              class="rc-dugme"
              [disabled]="!d.operasyon() || d.extendSubmission.gonderiliyor()"
              (click)="d.extend()"
            >
              {{ 'kiraFormu.eylem.uzat' | transloco }}
            </button>
          </div>
        </div>
        <rc-form-hatalari [hatalar]="d.extendSubmission.genelHatalar()" />
      </section>
    }

    @if (d.kira(); as k) {
      <section class="rc-bolum kf-kart">
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
            <dd>{{ d.visibleDetail()?.teslimAlanPersonelAd ?? '—' }}</dd>
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
      <section class="rc-bolum kf-kart">
        <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.donusBedelleri' | transloco }}</h3>
        <dl class="kf-satirlar">
          <div>
            <dt>{{ 'kiraFormu.donus.fazlaKm' | transloco }}</dt>
            <dd>{{ k.fazlaKm }}</dd>
          </div>
          <div>
            <dt>{{ 'kiraFormu.donus.fazlaKmBedeli' | transloco }}</dt>
            <dd>{{ money(k.fazlaKmBedeli) }}</dd>
          </div>
          <div>
            <dt>{{ 'kiraFormu.donus.eksikYakit' | transloco }}</dt>
            <dd>{{ k.eksikYakit }}</dd>
          </div>
          <div>
            <dt>{{ 'kiraFormu.donus.yakitBedeli' | transloco }}</dt>
            <dd>{{ money(k.yakitBedeli) }}</dd>
          </div>
          <div>
            <dt>{{ 'kiraFormu.donus.gecGun' | transloco }}</dt>
            <dd>{{ k.uzatmaGun }}</dd>
          </div>
          <div>
            <dt>{{ 'kiraFormu.donus.gecBedel' | transloco }}</dt>
            <dd>{{ money(k.uzatmaBedeli) }}</dd>
          </div>
          <div class="kf-satirlar__vurgu">
            <dt>{{ 'kiraFormu.hesap.genelToplam' | transloco }}</dt>
            <dd>{{ money(k.genelToplam) }}</dd>
          </div>
        </dl>
      </section>
    }
  `,
})
export class Return {
  protected readonly d = inject(RentalFormState);
  protected readonly reasons = END_REASONS.map((s) => ({ deger: s, etiket: s }));

  protected money(v: ServerNumber): string {
    return formatMoney(toNumber(v), isoCurrency(this.d.kira()?.doviz)) || '—';
  }
}
