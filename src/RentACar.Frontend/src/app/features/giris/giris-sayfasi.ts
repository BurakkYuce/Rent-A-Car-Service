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
 */
@Component({
  selector: 'rc-giris-sayfasi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [GirisFormu, TranslocoPipe],
  template: `
    <main class="sayfa">
      <div class="kart">
        <header class="ust">
          <p class="marka">{{ 'uygulama.ad' | transloco }}</p>
          <h1>{{ 'oturum.giris.baslik' | transloco }}</h1>
          <p class="aciklama">{{ 'oturum.giris.aciklama' | transloco }}</p>
        </header>
        <rc-giris-formu [bilgi]="bilgi()" (girisYapildi)="girildi($event)" />
      </div>
    </main>
  `,
  styles: `
    .sayfa {
      display: grid;
      place-items: center;
      min-height: 100vh;
      padding: var(--rc-bosluk-6) var(--rc-bosluk-4);
    }
    .kart {
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-4);
      width: min(24rem, 100%);
      padding: var(--rc-bosluk-6);
      border: 1px solid var(--rc-kenar);
      border-radius: var(--rc-yaricap-xl);
      background-color: var(--rc-yuzey);
      box-shadow: var(--rc-golge-2);
    }
    .ust {
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-1);
    }
    .marka {
      color: var(--rc-vurgu-metin);
      font-size: var(--rc-yazi-xs);
      font-weight: var(--rc-agirlik-kalin);
    }
    .aciklama {
      color: var(--rc-metin-ikincil);
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
