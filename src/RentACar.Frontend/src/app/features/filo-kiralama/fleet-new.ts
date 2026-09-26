import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { type UnsavedChangesOwner, pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { Alan } from '@shared/form/alan/alan';
import { SearchSelection } from '@shared/form/arama-secim/search-selection';
import {
  type SecimSecenegi,
  serverSelectionSource,
} from '@shared/form/arama-secim/selection-source';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { MoneyInput } from '@shared/form/kontroller/money-input';
import { NumberInput } from '@shared/form/kontroller/number-input';
import { DatePicker } from '@shared/form/tarih/date-picker';
import { Icon } from '@shared/ikon/icon';

import { PageBand } from '../../kabuk/sayfa-bandi/page-band';
import { FleetIdentityFields, profileControls } from './fleet-identity-fields';
import {
  type CreateFleetResponse,
  type FiloYeniDegeri,
  createBody,
  newValues,
} from './filo-modeli';

/**
 * Yeni filo sözleşmesi (`/app/filo-kiralama/yeni`) — Blazor "Yeni Sözleşme" formunun tüm alanları.
 * Taksit planı ve genel toplam SUNUCUDA hesaplanır (kayıttan sonra detayda görünür). Kayıt sonrası sekme
 * temiz forma döner, detay açılır.
 */
@Component({
  selector: 'rc-filo-yeni',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    SearchSelection,
    FleetIdentityFields,
    FormErrors,
    Icon,
    MoneyInput,
    NumberInput,
    DatePicker,
    PageBand,
  ],
  templateUrl: './fleet-new.html',
  styleUrl: './filo.scss',
})
export class FleetNew implements UnsavedChangesOwner {
  private readonly api = inject(ApiIstemcisi);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly t = translationFunction();

  protected readonly customers = serverSelectionSource('musteri');
  protected readonly araclar = serverSelectionSource('arac');
  protected readonly submission = formSubmission();

  protected readonly form = new FormGroup({
    musteri: new FormControl<SecimSecenegi | null>(null, Validators.required),
    arac: new FormControl<SecimSecenegi | null>(null, Validators.required),
    basTar: new FormControl<string | null>(null),
    sureAy: new FormControl<number | null>(null, [
      Validators.required,
      Validators.min(1),
      Validators.max(120),
    ]),
    aylikUcret: new FormControl<string | null>(null, Validators.required),
    kdvOrani: new FormControl<number | null>(0.2, [Validators.min(0), Validators.max(1)]),
    damgaVergisi: new FormControl<string | null>(null),
    ...profileControls(),
  });

  constructor() {
    this.form.reset(newValues());
    pageLeaveGuard(() => this.form.dirty);
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty;
  }

  protected kaydet(): void {
    const body = createBody(this.form.getRawValue() as FiloYeniDegeri);
    this.submission.gonder(
      this.form,
      (key) =>
        this.api.post<CreateFleetResponse>('/api/ui/v1/filo-kiralama', body, {
          islemAnahtari: key,
        }),
      {
        esleme: { musteriId: 'musteri', vehicleId: 'arac' },
        basarili: (y) => {
          this.toast.basari(this.t('filoKiralama.olusturuldu', { no: y.no }));
          this.form.reset(newValues());
          void this.router.navigate(['/filo-kiralama', y.id]);
        },
      },
    );
  }
}
