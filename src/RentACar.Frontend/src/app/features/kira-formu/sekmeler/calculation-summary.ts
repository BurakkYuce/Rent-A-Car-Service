import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  inject,
  input,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { formatMoney } from '@core/bicim/bicim';
import { translationFunction } from '@core/i18n/ceviri';
import { RentalFormState } from '../rental-form-state';
import { isoCurrency, toNumber } from '../kira-formu-modeli';
import type { ServerNumber } from '../kira-tipleri';

/**
 * Tutar özeti. Yeni kirada CANLI sunucu hesabı (`GET /kiralar/hesapla` — KiraHesapService → fiyat
 * motoru); kayıtlı kirada sözleşmenin kayıtlı değerleri + sunucunun hesapladığı `toplamlar` (F4.3b: ek hizmet
 * tutarı). Burada hiçbir tutar HESAPLANMAZ / TOPLANMAZ.
 */
@Component({
  selector: 'rc-kf-hesap-ozeti',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe],
  template: `
    <section class="rc-bolum kf-kart kf-hesap" [attr.aria-busy]="d.hesap.isLoading()">
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
                  <dd>{{ money(h.gunlukUcret, h.doviz) }}</dd>
                </div>
              }
              <div>
                <dt>{{ 'kiraFormu.hesap.net' | transloco }}</dt>
                <dd>{{ money(h.net, h.doviz) }}</dd>
              </div>
              <div>
                <dt>{{ 'kiraFormu.hesap.kdv' | transloco }}</dt>
                <dd>{{ money(h.kdv, h.doviz) }}</dd>
              </div>
              <div>
                <dt>{{ 'kiraFormu.hesap.ekHizmet' | transloco }}</dt>
                <dd>{{ money(h.ekHizmetToplam, h.doviz) }}</dd>
              </div>
              <div class="kf-satirlar__vurgu">
                <dt>{{ 'kiraFormu.hesap.genelToplam' | transloco }}</dt>
                <dd>{{ money(h.genelToplam, h.doviz) }}</dd>
              </div>
              @if (h.genelToplamTl !== null) {
                <div>
                  <dt>{{ 'kiraFormu.hesap.tlKarsiligi' | transloco }}</dt>
                  <dd>{{ money(h.genelToplamTl, 'TRY') }}</dd>
                </div>
              }
              <div class="kf-satirlar__vurgu">
                <dt>{{ 'kiraFormu.hesap.kalan' | transloco }}</dt>
                <dd>{{ money(h.kalan, h.doviz) }}</dd>
              </div>
            </dl>
            @if (breakdown().length > 0) {
              <p class="kf-not">{{ breakdown().join(' · ') }}</p>
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
            <dd>{{ money(k.tutar, k.doviz) }}</dd>
          </div>
          <div>
            <dt>{{ 'kiraFormu.hesap.ekHizmet' | transloco }}</dt>
            <dd data-testid="kayitli-ek-hizmet">
              {{ money(d.visibleDetail()?.toplamlar?.ekHizmetToplam, k.doviz) }}
            </dd>
          </div>
          @if (!kisa()) {
            <div>
              <dt>{{ 'kiraFormu.hesap.fazlaKm' | transloco }}</dt>
              <dd>{{ money(k.fazlaKmBedeli, k.doviz) }}</dd>
            </div>
            <div>
              <dt>{{ 'kiraFormu.hesap.yakit' | transloco }}</dt>
              <dd>{{ money(k.yakitBedeli, k.doviz) }}</dd>
            </div>
            <div>
              <dt>{{ 'kiraFormu.hesap.uzatma' | transloco }}</dt>
              <dd>{{ money(k.uzatmaBedeli, k.doviz) }}</dd>
            </div>
          }
          <div class="kf-satirlar__vurgu">
            <dt>{{ 'kiraFormu.hesap.genelToplam' | transloco }}</dt>
            <dd>{{ money(k.genelToplam, k.doviz) }}</dd>
          </div>
          <div>
            <dt>{{ 'kiraFormu.hesap.tahsilat' | transloco }}</dt>
            <dd>{{ money(k.tahsilat, k.doviz) }}</dd>
          </div>
          <div class="kf-satirlar__vurgu">
            <dt>{{ 'kiraFormu.hesap.bakiye' | transloco }}</dt>
            <dd>{{ money(k.bakiye, k.doviz) }}</dd>
          </div>
        </dl>
        @if (!kisa()) {
          <p class="kf-not">{{ 'kiraFormu.hesap.farkNotu' | transloco }}</p>
        }
      }
    </section>
  `,
})
export class CalculationSummary {
  protected readonly d = inject(RentalFormState);
  private readonly t = translationFunction();
  /** Hızlı Giriş'teki kısa görünüm (bazı satırlar gizli). */
  readonly kisa = input(false, { transform: booleanAttribute });

  /** Motor dökümü (Otomatik tarife bileşenleri — yalnız bilgi). */
  protected readonly breakdown = computed(() => {
    const h = this.d.hesap.veri();
    if (!h?.ok) return [];
    const row: string[] = [];
    if (h.hediyeGun !== null) row.push(this.t('kiraFormu.hesap.hediyeGun', { deger: h.hediyeGun }));
    if (h.faturalananGun !== null) {
      row.push(this.t('kiraFormu.hesap.faturalananGun', { deger: h.faturalananGun }));
    }
    if (h.iskontoTutar !== null) {
      row.push(this.t('kiraFormu.hesap.iskonto', { deger: this.money(h.iskontoTutar, h.doviz) }));
    }
    if (h.haftaSonuFark !== null) {
      row.push(
        this.t('kiraFormu.hesap.haftaSonu', { deger: this.money(h.haftaSonuFark, h.doviz) }),
      );
    }
    return row;
  });

  protected money(v: ServerNumber, currency: string | null | undefined): string {
    return formatMoney(toNumber(v), isoCurrency(currency)) || '—';
  }
}
