import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslocoPipe } from '@jsverse/transloco';
import type { Observable } from 'rxjs';
import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { GonderimKilidi } from '@core/form/gonderim-kilidi';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { BICIM_PIPELARI } from '@shared/bicim/bicim-pipe';
import { KiraFormuDurumu } from '../kira-formu-durumu';

/**
 * Müşteriye verilecek sözleşme LİNKİ (Blazor "Sözleşme Linki" kartı; F4.1 paylaşım uçları). Link
 * tahmin edilemez ve iptal edilebilir; "yeni sürüm" ESKİ adresi öldürür (onay ister). Yalnız
 * OperationsWrite (sunucu `paylasim` barını yalnız o izinle doldurur).
 */
@Component({
  selector: 'rc-kf-paylasim',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, ...BICIM_PIPELARI],
  template: `
    @if (d.gorunenDetay()?.paylasim; as bar) {
      <section class="rc-bolum kf-kart" [attr.aria-label]="'kiraFormu.paylasim.baslik' | transloco">
        <h2 class="kf-kart__baslik">{{ 'kiraFormu.paylasim.baslik' | transloco }}</h2>
        @if (bar.link; as link) {
          <input
            class="rc-girdi"
            readonly
            [value]="adres()"
            [attr.aria-label]="'kiraFormu.paylasim.adres' | transloco"
            data-testid="paylasim-adresi"
          />
          <p class="kf-not">
            {{
              link.erisimSayisi === 0
                ? ('kiraFormu.paylasim.acilmadi' | transloco)
                : ('kiraFormu.paylasim.acildi' | transloco: { sayi: link.erisimSayisi })
            }}
            @if (link.sonErisimUtc) {
              · {{ link.sonErisimUtc | tarihSaat }}
            }
            @if (link.bayat) {
              <span class="rc-rozet rc-rozet--uyari">{{
                'kiraFormu.paylasim.bayat' | transloco
              }}</span>
            }
          </p>
          <div class="kf-eylemler">
            <button type="button" class="rc-dugme rc-dugme--kucuk" (click)="kopyala()">
              {{
                (kopyalandi() ? 'kiraFormu.paylasim.kopyalandi' : 'kiraFormu.paylasim.kopyala')
                  | transloco
              }}
            </button>
            <a class="rc-dugme rc-dugme--kucuk" [href]="link.yol" target="_blank" rel="noopener">{{
              'kiraFormu.paylasim.ac' | transloco
            }}</a>
            <button
              type="button"
              class="rc-dugme rc-dugme--kucuk"
              [disabled]="kilit.gonderiliyor()"
              (click)="yeniSurum()"
            >
              {{ 'kiraFormu.paylasim.yeniSurum' | transloco }}
            </button>
            <button
              type="button"
              class="rc-dugme rc-dugme--kucuk rc-dugme--tehlike"
              [disabled]="kilit.gonderiliyor()"
              (click)="iptal()"
            >
              {{ 'kiraFormu.paylasim.iptal' | transloco }}
            </button>
          </div>
        } @else {
          <p class="kf-not">{{ 'kiraFormu.paylasim.aciklama' | transloco }}</p>
          <div class="kf-eylemler">
            <button
              type="button"
              class="rc-dugme rc-dugme--kucuk"
              [disabled]="kilit.gonderiliyor()"
              (click)="olustur()"
            >
              {{ 'kiraFormu.paylasim.olustur' | transloco }}
            </button>
          </div>
        }
      </section>
    }
  `,
})
export class Paylasim {
  protected readonly d = inject(KiraFormuDurumu);
  private readonly api = inject(ApiIstemcisi);
  private readonly onay = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly belge = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();

  protected readonly kilit = new GonderimKilidi();
  protected readonly kopyalandi = signal(false);
  /** Mutlak adres sayfanın kendi kökünden kurulur (sunucu yalnız göreli yol verir). */
  protected readonly adres = computed(() => {
    const yol = this.d.gorunenDetay()?.paylasim?.link?.yol ?? '';
    return `${this.belge.location?.origin ?? ''}${yol}`;
  });

  private get kok(): `/api/ui/v1/${string}` {
    return `/api/ui/v1/kiralar/${this.d.id ?? ''}/paylasim`;
  }

  protected olustur(): void {
    this.calistir(this.api.post(this.kok, {}));
  }

  protected async yeniSurum(): Promise<void> {
    const tamam = await this.onay.sor({
      baslik: this.t('kiraFormu.paylasim.yeniSurumBaslik'),
      mesaj: this.t('kiraFormu.paylasim.yeniSurumMesaj'),
      tehlikeli: true,
    });
    if (tamam) this.calistir(this.api.post(`${this.kok}/yeni-surum`, {}));
  }

  protected async iptal(): Promise<void> {
    const tamam = await this.onay.sor({
      baslik: this.t('kiraFormu.paylasim.iptalBaslik'),
      mesaj: this.t('kiraFormu.paylasim.iptalMesaj'),
      tehlikeli: true,
    });
    if (tamam) this.calistir(this.api.delete(this.kok));
  }

  protected kopyala(): void {
    void this.belge.defaultView?.navigator.clipboard?.writeText(this.adres()).then(() => {
      this.kopyalandi.set(true);
      setTimeout(() => this.kopyalandi.set(false), 1500);
    });
  }

  private calistir(istek: Observable<unknown>): void {
    this.kilit
      .gonder(() => istek)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.d.yenile(),
        error: (ham: unknown) => {
          const hata = apiHatasinaCevir(ham);
          if (!genelGosterilir(hata)) this.toast.hata(hata.detay);
        },
      });
  }
}
