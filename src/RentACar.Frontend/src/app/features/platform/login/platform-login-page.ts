import {
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';

import {
  loginErrorMessage,
  PLATFORM_HOME_ROUTE,
  PlatformSessionService,
} from '../platform-session';

/** `?neden=` → info line on the login page. */
const REASON_MESSAGE: Readonly<Record<string, CeviriAnahtari>> = {
  cikis: 'platform.giris.cikisYapildi',
  oturum: 'platform.giris.oturumDustu',
};

/**
 * `/app/platform/giris` — platform operator login (Blazor `PlatformLogin` parity). Separate from the
 * tenant login: no company field, own session endpoint. The error is ALWAYS generic ("user name or
 * password wrong"); the password is cleared on error, the user name kept. Bandless (Yol v2 §5.5): the card
 * is in-page paper — 1 px border, no shadow (shadows only on layers).
 */
@Component({
  selector: 'rc-platform-login-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, TranslocoPipe],
  template: `
    <main class="page">
      <div class="card">
        <header class="head">
          <p class="brand">
            {{ 'uygulama.ad' | transloco }} · {{ 'platform.kabuk.platform' | transloco }}
          </p>
          <h1>{{ 'platform.giris.baslik' | transloco }}</h1>
          <p class="muted">{{ 'platform.giris.aciklama' | transloco }}</p>
        </header>
        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          @if (error(); as key) {
            <p class="rc-form-mesaji rc-form-mesaji--hata" role="alert">{{ key | transloco }}</p>
          } @else if (info(); as key) {
            <p class="rc-form-mesaji rc-form-mesaji--uyari" role="status">{{ key | transloco }}</p>
          }
          <div class="rc-alan">
            <label class="rc-alan__etiket" for="rc-platform-kullanici">{{
              'platform.giris.kullanici' | transloco
            }}</label>
            <input
              class="rc-girdi"
              id="rc-platform-kullanici"
              formControlName="kullanici"
              autocomplete="username"
              autocapitalize="none"
              spellcheck="false"
              [attr.aria-invalid]="invalid('kullanici')"
            />
            @if (invalid('kullanici')) {
              <p class="rc-alan__hata">{{ 'platform.giris.zorunlu' | transloco }}</p>
            }
          </div>
          <div class="rc-alan">
            <label class="rc-alan__etiket" for="rc-platform-sifre">{{
              'platform.giris.sifre' | transloco
            }}</label>
            <input
              class="rc-girdi"
              id="rc-platform-sifre"
              type="password"
              formControlName="sifre"
              autocomplete="current-password"
              [attr.aria-invalid]="invalid('sifre')"
            />
            @if (invalid('sifre')) {
              <p class="rc-alan__hata">{{ 'platform.giris.zorunlu' | transloco }}</p>
            }
          </div>
          <div class="submit">
            <button
              type="submit"
              class="rc-dugme rc-dugme--birincil"
              [attr.aria-disabled]="sending()"
              [attr.aria-busy]="sending()"
            >
              {{
                (sending() ? 'platform.giris.gonderiliyor' : 'platform.giris.gonder') | transloco
              }}
            </button>
          </div>
        </form>
      </div>
    </main>
  `,
  styles: `
    .page {
      display: grid;
      place-items: center;
      min-height: 100vh;
      padding: var(--rc-bosluk-6) var(--rc-bosluk-4);
      background-color: var(--rc-zemin);
    }
    .card {
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-4);
      width: min(24rem, 100%);
      padding: var(--rc-bosluk-6);
      border: 1px solid var(--rc-kenar);
      border-radius: var(--rc-yaricap-xl);
      background-color: var(--rc-yuzey);
    }
    .head,
    form {
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-3);
    }
    .brand {
      color: var(--rc-vurgu-metin);
      font-size: var(--rc-yazi-xs);
      font-weight: var(--rc-agirlik-kalin);
    }
    .muted {
      color: var(--rc-metin-ikincil);
    }
    .submit {
      display: flex;
      justify-content: flex-end;
    }
  `,
})
export class PlatformLoginPage {
  private readonly session = inject(PlatformSessionService);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly params = toSignal(inject(ActivatedRoute).queryParamMap, { requireSync: true });

  protected readonly form = inject(NonNullableFormBuilder).group({
    kullanici: ['', Validators.required],
    sifre: ['', Validators.required],
  });
  protected readonly sending = signal(false);
  protected readonly error = signal<CeviriAnahtari | null>(null);
  protected readonly info = computed<CeviriAnahtari | null>(() => {
    const reason = this.params().get('neden');
    return (reason && REASON_MESSAGE[reason]) || null;
  });

  protected invalid(name: 'kullanici' | 'sifre'): boolean {
    const control = this.form.controls[name];
    return control.invalid && control.touched;
  }

  protected async submit(): Promise<void> {
    if (this.sending()) return;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.host.nativeElement.querySelector<HTMLElement>('.ng-invalid:not(form)')?.focus();
      return;
    }
    this.sending.set(true);
    this.error.set(null);
    try {
      const { kullanici, sifre } = this.form.getRawValue();
      await this.session.signIn(kullanici.trim(), sifre);
      await this.router.navigateByUrl(PLATFORM_HOME_ROUTE, { replaceUrl: true });
    } catch (e: unknown) {
      this.error.set(loginErrorMessage(e));
      this.form.controls.sifre.reset();
      this.host.nativeElement.querySelector<HTMLElement>('#rc-platform-sifre')?.focus();
    } finally {
      this.sending.set(false);
    }
  }
}
