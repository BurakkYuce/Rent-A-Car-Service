import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { paraBicimle } from '@core/bicim/bicim';
import { SUNUCU_HATASI } from '@core/form/sunucu-hatalari';
import { KiraFormuDurumu } from '../kira-formu-durumu';
import { sayiya } from '../kira-formu-modeli';
import type { SunucuSayisi } from '../kira-tipleri';
import { KF_ORTAK } from './ortak';

/**
 * EK HİZMETLER. Yeni kira: seçilen tanımlar kira KAYDIYLA eklenir (tanım fiyat anlık görüntüsü —
 * serbest fiyat yok; satır tutarları canlı hesaptan). Kayıtlı kira: kalemler + ekle/sil (F4.1 uçları).
 * Ekleme ANAHTARSIZ: çift gönderim iki kalem yazar → düğme istek boyunca kilitli.
 */
@Component({
  selector: 'rc-kf-ek-hizmet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...KF_ORTAK],
  template: `
    @if (d.yeni) {
      <section class="kf-kart" [formGroup]="d.form">
        <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.ekHizmetSecimi' | transloco }}</h3>
        <div class="rc-form-izgara">
          <rc-alan [etiket]="'kiraFormu.ekHizmet.ekle' | transloco" class="rc-form-izgara__genis">
            <rc-arama-secim [formControl]="d.ekHizmetSecici" [kaynak]="d.ekHizmetKaynagi" />
          </rc-alan>
        </div>
        @if (sunucuHatalari().length > 0) {
          <div class="rc-form-hatalari" role="alert" aria-invalid="true" tabindex="-1">
            @for (m of sunucuHatalari(); track $index) {
              <p>{{ m }}</p>
            }
          </div>
        }
        <div
          class="kf-tablo-kutusu"
          formArrayName="ekHizmetler"
          role="region"
          tabindex="0"
          [attr.aria-label]="'kiraFormu.bolum.ekHizmetSecimi' | transloco"
        >
          <table class="kf-tablo" [attr.aria-label]="'kiraFormu.bolum.ekHizmetler' | transloco">
            <thead>
              <tr>
                <th scope="col">{{ 'kiraFormu.ekHizmet.hizmet' | transloco }}</th>
                <th scope="col">{{ 'kiraFormu.ekHizmet.miktar' | transloco }}</th>
                <th scope="col" class="num">{{ 'kiraFormu.ekHizmet.net' | transloco }}</th>
                <th scope="col" class="num">{{ 'kiraFormu.ekHizmet.kdv' | transloco }}</th>
                <th scope="col" class="num">{{ 'kiraFormu.ekHizmet.toplam' | transloco }}</th>
                <th scope="col">
                  <span class="rc-gorunmez">{{ 'kiraFormu.eylem.sil' | transloco }}</span>
                </th>
              </tr>
            </thead>
            <tbody>
              @for (satir of satirlar(); track satir.value.tanim?.id; let i = $index) {
                @let kalem = hesapKalemi(satir.value.tanim?.id);
                <tr [formGroupName]="i">
                  <td>{{ satir.value.tanim?.etiket }}</td>
                  <td class="kf-miktar">
                    <rc-sayi-girdisi
                      formControlName="miktar"
                      [kesir]="2"
                      [ariaEtiketi]="
                        ('kiraFormu.ekHizmet.miktar' | transloco) + ' ' + satir.value.tanim?.etiket
                      "
                    />
                  </td>
                  <td class="num">{{ para(kalem?.net) }}</td>
                  <td class="num">{{ para(kalem?.kdv) }}</td>
                  <td class="num">{{ para(kalem?.toplam) }}</td>
                  <td>
                    <button
                      type="button"
                      class="rc-dugme rc-dugme--kucuk rc-dugme--hayalet"
                      [disabled]="!d.operasyon()"
                      (click)="d.ekHizmetSatiriSil(i)"
                    >
                      {{ 'kiraFormu.eylem.cikar' | transloco }}
                    </button>
                  </td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="6" class="kf-bos">
                    {{ 'kiraFormu.ekHizmet.secilmedi' | transloco }}
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <p class="kf-not">
          {{ 'kiraFormu.ekHizmet.toplamEtiket' | transloco }}:
          <strong>{{ para(d.hesap.veri()?.ekHizmetToplam) }}</strong> —
          {{ 'kiraFormu.not.ekHizmetYeni' | transloco }}
        </p>
      </section>
    } @else {
      <section class="kf-kart">
        <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.ekHizmetler' | transloco }}</h3>
        <div
          class="kf-tablo-kutusu"
          role="region"
          tabindex="0"
          [attr.aria-label]="'kiraFormu.bolum.ekHizmetler' | transloco"
        >
          <table class="kf-tablo" [attr.aria-label]="'kiraFormu.bolum.ekHizmetler' | transloco">
            <thead>
              <tr>
                <th scope="col">{{ 'kiraFormu.ekHizmet.hizmet' | transloco }}</th>
                <th scope="col" class="num">{{ 'kiraFormu.ekHizmet.miktar' | transloco }}</th>
                <th scope="col" class="num">{{ 'kiraFormu.ekHizmet.birimNet' | transloco }}</th>
                <th scope="col" class="num">{{ 'kiraFormu.ekHizmet.kdvOrani' | transloco }}</th>
                <th scope="col" class="num">{{ 'kiraFormu.ekHizmet.net' | transloco }}</th>
                <th scope="col" class="num">{{ 'kiraFormu.ekHizmet.kdv' | transloco }}</th>
                <th scope="col" class="num">{{ 'kiraFormu.ekHizmet.toplam' | transloco }}</th>
                <th scope="col">
                  <span class="rc-gorunmez">{{ 'kiraFormu.eylem.sil' | transloco }}</span>
                </th>
              </tr>
            </thead>
            <tbody>
              @for (k of d.detay.veri()?.ekHizmetler ?? []; track k.id) {
                <tr>
                  <td>{{ k.ad }}</td>
                  <td class="num">{{ sayi(k.miktar) | sayi }}</td>
                  <td class="num">{{ para(k.birimNetFiyat) }}</td>
                  <td class="num">{{ sayi(k.kdvOrani) | percent: '1.0-2' }}</td>
                  <td class="num">{{ para(k.netTutar) }}</td>
                  <td class="num">{{ para(k.kdvTutar) }}</td>
                  <td class="num">{{ para(k.toplam) }}</td>
                  <td>
                    @if (!d.iptal()) {
                      <button
                        type="button"
                        class="rc-dugme rc-dugme--kucuk rc-dugme--hayalet"
                        [disabled]="!d.operasyon() || d.ekHizmetSilKilidi.gonderiliyor()"
                        [attr.aria-label]="('kiraFormu.eylem.sil' | transloco) + ' ' + k.ad"
                        (click)="d.ekHizmetSil(k)"
                      >
                        {{ 'kiraFormu.eylem.sil' | transloco }}
                      </button>
                    }
                  </td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="8" class="kf-bos">{{ 'kiraFormu.ekHizmet.yok' | transloco }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        @if (!d.iptal()) {
          <div
            class="rc-form-izgara"
            [formGroup]="d.ekHizmetEkleFormu"
            data-testid="ek-hizmet-ekle"
          >
            <rc-alan [etiket]="'kiraFormu.ekHizmet.hizmet' | transloco">
              <rc-arama-secim formControlName="tanim" [kaynak]="d.ekHizmetKaynagi" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.ekHizmet.miktar' | transloco">
              <rc-sayi-girdisi formControlName="miktar" [kesir]="2" />
            </rc-alan>
            <div class="kf-eylemler">
              <button
                type="button"
                class="rc-dugme rc-dugme--birincil"
                [disabled]="!d.operasyon() || d.ekHizmetEkleGonderimi.gonderiliyor()"
                (click)="d.ekHizmetEkle()"
              >
                {{
                  (d.ekHizmetEkleGonderimi.gonderiliyor()
                    ? 'form.gonderiliyor'
                    : 'kiraFormu.ekHizmet.ekleDugme'
                  ) | transloco
                }}
              </button>
            </div>
          </div>
          <rc-form-hatalari [hatalar]="d.ekHizmetEkleGonderimi.genelHatalar()" />
          <p class="kf-not">{{ 'kiraFormu.not.ekHizmetKayitli' | transloco }}</p>
        }
      </section>
    }
  `,
})
export class EkHizmet {
  protected readonly d = inject(KiraFormuDurumu);
  protected readonly sayi = sayiya;

  protected readonly satirlar = computed(() => {
    this.d.ekSatirSurumu();
    return [...this.d.form.controls.ekHizmetler.controls];
  });

  /** Dizi düzeyindeki sunucu hatası (`ekHizmetler` alanı) — tabloya bağlı görünür. */
  protected readonly sunucuHatalari = computed((): readonly string[] => {
    this.d.ekSatirSurumu();
    this.d.kayit.gonderiliyor();
    const hata: unknown = this.d.form.controls.ekHizmetler.errors?.[SUNUCU_HATASI];
    return Array.isArray(hata) ? (hata as string[]) : [];
  });

  protected hesapKalemi(tanimId: string | undefined) {
    return this.d.hesap.veri()?.ekKalemler.find((k) => k.tanimId === tanimId);
  }

  protected para(v: SunucuSayisi): string {
    return paraBicimle(sayiya(v), this.d.kiraDovizi()) || '—';
  }
}
