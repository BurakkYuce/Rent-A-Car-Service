import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import { Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import type { Ben } from '@core/oturum/oturum-tipleri';
import type { YenidenGirisVerisi } from '@core/oturum/relogin-service';
import { LoginForm } from '@shared/giris-formu/login-form';
import { Icon } from '@shared/ikon/icon';

/**
 * Yerinde yeniden giriş (oturum düştü). Sayfa ve form arkada OLDUĞU GİBİ kalır; giriş başarılı olunca
 * diyalog `true` ile kapanır ve interceptor bekleyen isteği tekrarlar. Esc ya da "Vazgeç" → `false`
 * (istek `oturum_yok` ile düşer, form yine yerinde). Firma/kullanıcı bilinen oturumdan kilitli gelir.
 */
@Component({
  selector: 'rc-yeniden-giris-diyalogu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LoginForm, TranslocoPipe, Icon],
  template: `
    <div class="rc-diyalog">
      <header class="rc-diyalog__ust">
        <rc-ikon ad="alert-circle" [boyut]="20" />
        <h2 id="rc-yeniden-giris-baslik">{{ 'oturum.yenidenGiris.baslik' | transloco }}</h2>
      </header>
      <p id="rc-yeniden-giris-aciklama" class="rc-diyalog__govde">
        {{ 'oturum.yenidenGiris.aciklama' | transloco }}
      </p>
      <rc-giris-formu
        onek="rc-yeniden-giris"
        [firma]="veri.firma"
        [kullanici]="veri.kullanici"
        [kilitli]="veri.kullanici !== null"
        gonderEtiketi="oturum.yenidenGiris.gonder"
        (girisYapildi)="entered($event)"
      >
        <button type="button" class="rc-dugme" (click)="ref.close(false)">
          {{ 'oturum.yenidenGiris.vazgec' | transloco }}
        </button>
      </rc-giris-formu>
    </div>
  `,
})
export class YenidenGirisDiyalogu {
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
  protected readonly veri = inject<YenidenGirisVerisi>(DIALOG_DATA);
  private readonly router = inject(Router);

  constructor() {
    // disableClose: perde kapatmaz; Esc açıkça "vazgeç".
    const subscription = this.ref.keydownEvents.subscribe((evt) => {
      if (evt.key === 'Escape') {
        evt.preventDefault();
        this.ref.close(false);
      }
    });
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
  }

  protected entered(ben: Ben): void {
    if (this.veri.kullaniciId !== null && ben.kullanici.id !== this.veri.kullaniciId) {
      // Başka kimlik: önceki kullanıcının isteği bu kimlikle GÖNDERİLMEZ; ana sayfaya.
      this.ref.close(false);
      void this.router.navigateByUrl('/');
      return;
    }
    this.ref.close(true);
  }
}
