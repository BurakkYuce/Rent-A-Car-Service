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
        <div class="giris-kutusu">
          <header class="giris-basligi">
            <h1>{{ 'oturum.giris.baslik' | transloco }}</h1>
            <p class="not">{{ 'oturum.giris.aciklama' | transloco }}</p>
          </header>
          <rc-giris-formu [bilgi]="bilgi()" (girisYapildi)="girildi($event)" />
        </div>
      </section>
    </main>
  `,
  styleUrl: './giris-sayfasi.scss',
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
