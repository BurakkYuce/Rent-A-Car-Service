import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  input,
  OnInit,
  output,
  signal,
} from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { loginErrorMessage } from '@core/oturum/giris-hatasi';
import { SessionService } from '@core/oturum/session-service';
import type { Ben } from '@core/oturum/oturum-tipleri';

/**
 * Firma + kullanıcı + parola formu. Giriş sayfası ve yerinde yeniden giriş diyaloğu aynı formu kullanır.
 * - `kilitli`: firma ve kullanıcı salt-okunur (diyalog: aynı kullanıcı yeniden girer; başka kimlik
 *   önceki kullanıcının formunu göndermesin).
 * - Hata mesajı `role="alert"`; `dogrulama` her zaman genel metin ("Firma, kullanıcı veya parola hatalı").
 * - Gönderim kilidi: istek sürerken düğme `aria-disabled` (DOM `disabled` DEĞİL — odak düğmede kalır,
 *   diyalog kapanınca odak geri dönebilir), ikinci gönderim yok sayılır. Hatada parola temizlenir, diğerleri kalır.
 */
@Component({
  selector: 'rc-giris-formu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, TranslocoPipe],
  templateUrl: './login-form.html',
  styles: `
    :host {
      display: block;
    }
    form {
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-3);
    }
    .eylemler {
      display: flex;
      flex-wrap: wrap;
      justify-content: flex-end;
      gap: var(--rc-bosluk-2);
      padding-top: var(--rc-bosluk-1);
    }
    .eylemler .rc-dugme--birincil {
      min-width: 8rem;
    }
  `,
})
export class LoginForm implements OnInit {
  private readonly oturum = inject(SessionService);
  private readonly root = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Alan kimliklerinin öneki (sayfa ve diyalog aynı anda açık olabilir). */
  readonly onek = input('rc-giris');
  readonly firma = input<string | null>(null);
  readonly kullanici = input<string | null>(null);
  readonly kilitli = input(false);
  /** Harici bilgi mesajı (ör. `?neden=kiraci_kapali`). */
  readonly bilgi = input<CeviriAnahtari | null>(null);
  readonly gonderEtiketi = input<CeviriAnahtari>('oturum.giris.gonder');

  readonly girisYapildi = output<Ben>();

  protected readonly form = inject(NonNullableFormBuilder).group({
    firma: ['', Validators.required],
    kullanici: ['', Validators.required],
    sifre: ['', Validators.required],
  });
  protected readonly submitting = signal(false);
  protected readonly hata = signal<CeviriAnahtari | null>(null);

  ngOnInit(): void {
    this.form.patchValue({ firma: this.firma() ?? '', kullanici: this.kullanici() ?? '' });
  }

  protected async gonder(): Promise<void> {
    if (this.submitting()) return;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.root.nativeElement.querySelector<HTMLElement>('.ng-invalid:not(form)')?.focus();
      return;
    }
    this.submitting.set(true);
    this.hata.set(null);
    try {
      const ben = await this.oturum.login(this.form.getRawValue());
      this.girisYapildi.emit(ben);
    } catch (error: unknown) {
      this.hata.set(loginErrorMessage(error));
      this.form.controls.sifre.reset();
      this.root.nativeElement.querySelector<HTMLElement>(`#${this.onek()}-sifre`)?.focus();
    } finally {
      this.submitting.set(false);
    }
  }

  protected invalid(name: 'firma' | 'kullanici' | 'sifre'): boolean {
    const check = this.form.controls[name];
    return check.invalid && check.touched;
  }
}
