import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { TAM_SAYFA_GEZINMESI } from '@core/form/kaydedilmemis-degisiklik';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { girisSonrasiHedef } from '@core/oturum/giris-hatasi';
import type { Ben } from '@core/oturum/oturum-tipleri';
import { GirisFormu } from '@shared/giris-formu/giris-formu';

/** `?neden=` → giriş sayfasındaki bilgi mesajı (interceptor ve çıkış bunu yazar). */
const NEDEN_MESAJI: Readonly<Record<string, CeviriAnahtari>> = {
  kiraci_kapali: 'oturum.giris.hata.kiraciKapali',
  cikis: 'oturum.giris.cikisYapildi',
};

/**
 * `/app/giris` — TEK giriş (F4.6: Blazor `/login` buraya yönlenir; aynı kimlik doğrulaması ve cookie).
 * Başarılı girişte `girisSonrasiHedef`: pilot firma → `returnUrl` (yeni arayüz) ya da Panel; pilot olmayan
 * firma ya da Blazor dönüşü → tam sayfa geçiş (sunucu `/login` kapısı dönüşü doğrular).
 * Görünüm Yol v2 §5.5 (sol lacivert tanıtım paneli, sağ kağıt form).
 */
@Component({
  selector: 'rc-giris-sayfasi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [GirisFormu, TranslocoPipe],
  template: `
    <main class="giris">
      <section class="tanitim" aria-labelledby="rc-giris-marka">
        <div class="marka">
          <span class="logo" aria-hidden="true">RA</span>
          <span class="marka__ad" id="rc-giris-marka">{{ 'uygulama.ad' | transloco }}</span>
        </div>
        <p class="tanitim__cumle">{{ 'oturum.giris.tanitim' | transloco }}</p>
      </section>
      <section class="form-alani">
        <div class="kart">
          <header class="ust">
            <h1>{{ 'oturum.giris.baslik' | transloco }}</h1>
            <p class="aciklama">{{ 'oturum.giris.aciklama' | transloco }}</p>
          </header>
          <rc-giris-formu [bilgi]="bilgi()" (girisYapildi)="girildi($event)" />
        </div>
      </section>
    </main>
  `,
  // Yol v2 §5.5: sol lacivert panel (logo + tek cümle), sağ kağıt zeminde form. Plaka motifi yok.
  // Lacivert panel kenar çubuğu tokenlarını kullanır (koyu temada asfalt varyantı aynı katmandan).
  styles: `
    .giris {
      display: grid;
      grid-template-columns: minmax(0, 5fr) minmax(0, 7fr);
      min-height: 100vh;
      min-height: 100dvh;
      background-color: var(--rc-zemin);
    }
    .tanitim {
      display: flex;
      flex-direction: column;
      justify-content: space-between;
      gap: var(--rc-bosluk-8);
      padding: var(--rc-bosluk-8) var(--rc-bosluk-8) var(--rc-bosluk-12);
      background-color: var(--rc-kenar-cubugu-zemin);
      color: var(--rc-kenar-cubugu-metin);
    }
    .marka {
      display: flex;
      align-items: center;
      gap: var(--rc-bosluk-3);
    }
    .logo {
      display: inline-grid;
      place-items: center;
      width: 2.5rem;
      height: 2.5rem;
      border-radius: var(--rc-yaricap-md);
      background-color: var(--rc-kenar-cubugu-rozet-zemin);
      color: var(--rc-kenar-cubugu-rozet-metin);
      font-size: var(--rc-yazi-md);
      font-weight: var(--rc-agirlik-cok-kalin);
    }
    .marka__ad {
      font-size: var(--rc-yazi-lg);
      font-weight: var(--rc-agirlik-kalin);
    }
    .tanitim__cumle {
      max-width: 22rem;
      color: var(--rc-kenar-cubugu-metin);
      font-size: var(--rc-yazi-2xl);
      font-weight: var(--rc-agirlik-orta);
      line-height: var(--rc-satir-sik);
    }
    .form-alani {
      display: grid;
      place-items: center;
      padding: var(--rc-bosluk-8) var(--rc-bosluk-4);
    }
    .kart {
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-5);
      width: min(24rem, 100%);
    }
    .ust {
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-1);
    }
    .ust h1 {
      font-size: var(--rc-yazi-2xl);
    }
    .aciklama {
      color: var(--rc-metin-ikincil);
    }
    @media (max-width: 900px) {
      .giris {
        grid-template-columns: minmax(0, 1fr);
        grid-template-rows: auto 1fr;
      }
      .tanitim {
        gap: var(--rc-bosluk-2);
        padding: var(--rc-bosluk-4);
      }
      .tanitim__cumle {
        font-size: var(--rc-yazi-sm);
        color: var(--rc-kenar-cubugu-ikincil);
      }
      .form-alani {
        place-items: start center;
        padding-block: var(--rc-bosluk-6);
      }
    }
  `,
})
export class GirisSayfasi {
  private readonly router = inject(Router);
  private readonly gezin = inject(TAM_SAYFA_GEZINMESI);
  private readonly parametreler = toSignal(inject(ActivatedRoute).queryParamMap, {
    requireSync: true,
  });

  protected readonly bilgi = computed<CeviriAnahtari | null>(() => {
    const neden = this.parametreler().get('neden');
    return (neden && NEDEN_MESAJI[neden]) || null;
  });

  protected girildi(ben: Ben): void {
    const hedef = girisSonrasiHedef(ben.pilot, this.parametreler().get('returnUrl'));
    if (hedef.tur === 'spa') void this.router.navigateByUrl(hedef.yol, { replaceUrl: true });
    else this.gezin(hedef.adres);
  }
}
