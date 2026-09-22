import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  inject,
  input,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { paraBicimle } from '@core/bicim/bicim';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { KiraFormuDurumu } from '../kira-formu-durumu';
import { isoParaBirimi, sayiya } from '../kira-formu-modeli';
import type { SunucuSayisi } from '../kira-tipleri';

/**
 * Tutar özeti. Yeni kirada CANLI sunucu hesabı (`GET /kiralar/hesapla` — KiraHesapService → fiyat
 * motoru); kayıtlı kirada sözleşmenin kayıtlı değerleri. Burada hiçbir tutar HESAPLANMAZ.
 */
@Component({
  selector: 'rc-kf-hesap-ozeti',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe],
  template: `
    <section class="kf-kart kf-hesap" [attr.aria-busy]="d.hesap.yukleniyor()">
      <h3 class="kf-kart__baslik">
        {{ (d.yeni ? 'kiraFormu.hesap.canli' : 'kiraFormu.hesap.kayitli') | transloco }}
      </h3>
      @if (d.yeni) {
        @if (d.hesap.veri(); as h) {
          @if (h.ok) {
            <dl class="kf-satirlar" data-testid="canli-hesap">
              <div>
                <dt>{{ 'kiraFormu.hesap.gun' | transloco }}</dt>
                <dd>{{ h.gun }}</dd>
              </div>
              @if (!kisa()) {
                <div>
                  <dt>{{ 'kiraFormu.hesap.efektifGunluk' | transloco }}</dt>
                  <dd>{{ para(h.gunlukUcret, h.doviz) }}</dd>
                </div>
              }
              <div>
                <dt>{{ 'kiraFormu.hesap.net' | transloco }}</dt>
                <dd>{{ para(h.net, h.doviz) }}</dd>
              </div>
              <div>
                <dt>{{ 'kiraFormu.hesap.kdv' | transloco }}</dt>
                <dd>{{ para(h.kdv, h.doviz) }}</dd>
              </div>
              <div>
                <dt>{{ 'kiraFormu.hesap.ekHizmet' | transloco }}</dt>
                <dd>{{ para(h.ekHizmetToplam, h.doviz) }}</dd>
              </div>
              <div class="kf-satirlar__vurgu">
                <dt>{{ 'kiraFormu.hesap.genelToplam' | transloco }}</dt>
                <dd>{{ para(h.genelToplam, h.doviz) }}</dd>
              </div>
              @if (h.genelToplamTl !== null) {
                <div>
                  <dt>{{ 'kiraFormu.hesap.tlKarsiligi' | transloco }}</dt>
                  <dd>{{ para(h.genelToplamTl, 'TRY') }}</dd>
                </div>
              }
              <div class="kf-satirlar__vurgu">
                <dt>{{ 'kiraFormu.hesap.kalan' | transloco }}</dt>
                <dd>{{ para(h.kalan, h.doviz) }}</dd>
              </div>
            </dl>
            @if (dokum().length > 0) {
              <p class="kf-not">{{ dokum().join(' · ') }}</p>
            }
            @for (n of h.notlar ?? []; track $index) {
              <p class="kf-not">{{ n }}</p>
            }
          } @else {
            <p class="kf-not kf-not--uyari" role="status">{{ h.hata }}</p>
          }
        } @else if (d.hesap.tur() === 'hata') {
          <p class="kf-not kf-not--uyari" role="status">
            {{ 'kiraFormu.hesap.alinamadi' | transloco }}
          </p>
        } @else {
          <p class="kf-not">{{ 'kiraFormu.hesap.tarihGirin' | transloco }}</p>
        }
      } @else if (d.kira(); as k) {
        <dl class="kf-satirlar">
          <div>
            <dt>{{ 'kiraFormu.hesap.gun' | transloco }}</dt>
            <dd>{{ k.gun }}</dd>
          </div>
          <div>
            <dt>{{ 'kiraFormu.hesap.baz' | transloco }}</dt>
            <dd>{{ para(k.tutar, k.doviz) }}</dd>
          </div>
          @if (!kisa()) {
            <div>
              <dt>{{ 'kiraFormu.hesap.fazlaKm' | transloco }}</dt>
              <dd>{{ para(k.fazlaKmBedeli, k.doviz) }}</dd>
            </div>
            <div>
              <dt>{{ 'kiraFormu.hesap.yakit' | transloco }}</dt>
              <dd>{{ para(k.yakitBedeli, k.doviz) }}</dd>
            </div>
            <div>
              <dt>{{ 'kiraFormu.hesap.uzatma' | transloco }}</dt>
              <dd>{{ para(k.uzatmaBedeli, k.doviz) }}</dd>
            </div>
          }
          <div class="kf-satirlar__vurgu">
            <dt>{{ 'kiraFormu.hesap.genelToplam' | transloco }}</dt>
            <dd>{{ para(k.genelToplam, k.doviz) }}</dd>
          </div>
          <div>
            <dt>{{ 'kiraFormu.hesap.tahsilat' | transloco }}</dt>
            <dd>{{ para(k.tahsilat, k.doviz) }}</dd>
          </div>
          <div class="kf-satirlar__vurgu">
            <dt>{{ 'kiraFormu.hesap.bakiye' | transloco }}</dt>
            <dd>{{ para(k.bakiye, k.doviz) }}</dd>
          </div>
        </dl>
        @if (!kisa()) {
          <p class="kf-not">{{ 'kiraFormu.hesap.farkNotu' | transloco }}</p>
        }
      }
    </section>
  `,
})
export class HesapOzeti {
  protected readonly d = inject(KiraFormuDurumu);
  private readonly t = ceviriFonksiyonu();
  /** Hızlı Giriş'teki kısa görünüm (bazı satırlar gizli). */
  readonly kisa = input(false, { transform: booleanAttribute });

  /** Motor dökümü (Otomatik tarife bileşenleri — yalnız bilgi). */
  protected readonly dokum = computed(() => {
    const h = this.d.hesap.veri();
    if (!h?.ok) return [];
    const satir: string[] = [];
    if (h.hediyeGun !== null)
      satir.push(this.t('kiraFormu.hesap.hediyeGun', { deger: h.hediyeGun }));
    if (h.faturalananGun !== null) {
      satir.push(this.t('kiraFormu.hesap.faturalananGun', { deger: h.faturalananGun }));
    }
    if (h.iskontoTutar !== null) {
      satir.push(this.t('kiraFormu.hesap.iskonto', { deger: this.para(h.iskontoTutar, h.doviz) }));
    }
    if (h.haftaSonuFark !== null) {
      satir.push(
        this.t('kiraFormu.hesap.haftaSonu', { deger: this.para(h.haftaSonuFark, h.doviz) }),
      );
    }
    return satir;
  });

  protected para(v: SunucuSayisi, doviz: string | null | undefined): string {
    return paraBicimle(sayiya(v), isoParaBirimi(doviz)) || '—';
  }
}
