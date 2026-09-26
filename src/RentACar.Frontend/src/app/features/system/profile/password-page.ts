import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { Alan } from '@shared/form/alan/alan';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { TextInput } from '@shared/form/kontroller/text-input';
import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';

const PASSWORD_MAX = 128;

/**
 * F11.2b kendi parolası (Blazor `SifreDegistir`; oturum yeter, kimlik sunucuda oturumdan, giriş hız sınırı). Mevcut
 * parola `current-password`, yenileri `new-password`; değerler yalnız bellekte, başarıdan sonra silinir.
 */
@Component({
  selector: 'rc-password-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageBand, ReactiveFormsModule, TranslocoPipe, Alan, FormErrors, TextInput],
  styleUrl: '../system.scss',
  template: `
    <rc-sayfa-bandi [baslik]="'sistem.profil.baslik' | transloco" ikon="key" />
    <div class="rc-sayfa">
      <p class="not">{{ 'sistem.profil.aciklama' | transloco }}</p>
      <rc-form-hatalari [hatalar]="submit.genelHatalar()" />
      <div class="rc-form-izgara" [formGroup]="form">
        <rc-alan [etiket]="'sistem.profil.eski' | transloco">
          <rc-metin-girdisi
            formControlName="eskiSifre"
            tur="password"
            otomatikTamamlama="current-password"
            [azamiUzunluk]="128"
          />
        </rc-alan>
        <rc-alan [etiket]="'sistem.profil.yeni' | transloco">
          <rc-metin-girdisi
            formControlName="yeniSifre"
            tur="password"
            otomatikTamamlama="new-password"
            [azamiUzunluk]="128"
          />
        </rc-alan>
        <rc-alan [etiket]="'sistem.profil.tekrar' | transloco">
          <rc-metin-girdisi
            formControlName="yeniSifreTekrar"
            tur="password"
            otomatikTamamlama="new-password"
            [azamiUzunluk]="128"
          />
        </rc-alan>
        <div class="eylemler">
          <button
            type="button"
            class="rc-dugme rc-dugme--birincil"
            [disabled]="submit.gonderiliyor()"
            (click)="save()"
          >
            {{ 'sistem.profil.kaydet' | transloco }}
          </button>
        </div>
      </div>
    </div>
  `,
})
export class PasswordPage {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastService);
  private readonly t = translationFunction();

  protected readonly form = new FormGroup({
    eskiSifre: new FormControl<string | null>(null, [
      Validators.required,
      Validators.maxLength(PASSWORD_MAX),
    ]),
    yeniSifre: new FormControl<string | null>(null, [
      Validators.required,
      Validators.maxLength(PASSWORD_MAX),
    ]),
    yeniSifreTekrar: new FormControl<string | null>(null, [
      Validators.required,
      Validators.maxLength(PASSWORD_MAX),
    ]),
  });
  protected readonly submit = formSubmission();

  constructor() {
    pageLeaveGuard(() => this.form.dirty);
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty;
  }

  protected save(): void {
    const v = this.form.getRawValue();
    this.submit.gonder(
      this.form,
      (key) => this.api.post('/api/ui/v1/profil/sifre', v, { islemAnahtari: key }),
      {
        basarili: () => {
          this.form.reset();
          this.toast.basari(this.t('sistem.profil.degisti'));
        },
      },
    );
  }
}
