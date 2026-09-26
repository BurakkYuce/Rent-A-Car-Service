import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { FULL_PAGE_NAVIGATION } from '@core/form/kaydedilmemis-degisiklik';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { postLoginTarget } from '@core/oturum/giris-hatasi';
import type { Ben } from '@core/oturum/oturum-tipleri';
import { LoginForm } from '@shared/giris-formu/login-form';

/** `?neden=` → giriş sayfasındaki bilgi mesajı (interceptor ve çıkış bunu yazar). */
const REASON_MESSAGE: Readonly<Record<string, CeviriAnahtari>> = {
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
  imports: [LoginForm, TranslocoPipe],
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
        <div class="giris-kutusu">
          <header class="giris-basligi">
            <h1>{{ 'oturum.giris.baslik' | transloco }}</h1>
            <p class="not">{{ 'oturum.giris.aciklama' | transloco }}</p>
          </header>
          <rc-giris-formu [bilgi]="bilgi()" (girisYapildi)="entered($event)" />
        </div>
      </section>
    </main>
  `,
  styleUrl: './login-page.scss',
})
export class LoginPage {
  private readonly router = inject(Router);
  private readonly navigate = inject(FULL_PAGE_NAVIGATION);
  private readonly parameters = toSignal(inject(ActivatedRoute).queryParamMap, {
    requireSync: true,
  });

  protected readonly bilgi = computed<CeviriAnahtari | null>(() => {
    const reason = this.parameters().get('neden');
    return (reason && REASON_MESSAGE[reason]) || null;
  });

  protected entered(ben: Ben): void {
    const target = postLoginTarget(ben.pilot, this.parameters().get('returnUrl'));
    if (target.tur === 'spa') void this.router.navigateByUrl(target.yol, { replaceUrl: true });
    else this.navigate(target.adres);
  }
}
