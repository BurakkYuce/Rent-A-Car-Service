import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { Schema } from '@core/api/ui-tipleri';
import { pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ToastService } from '@core/geri-bildirim/toast-service';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { translationFunction } from '@core/i18n/ceviri';
import { TemelStore } from '@core/veri/temel-store';
import { Alan } from '@shared/form/alan/alan';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { TextArea } from '@shared/form/kontroller/text-area';
import { TextInput } from '@shared/form/kontroller/text-input';
import { Checkbox } from '@shared/form/kontroller/checkbox';

import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { mergeServerValues } from '../shared/version-merge';

type MessageTemplate = Schema<'MessageTemplateDto'>;

const ROOT = '/api/ui/v1/mesaj-sablonlari' as const;

/** `SablonDoldur.Anahtarlar` (sunucu listesi). */
export const PLACEHOLDERS = [
  'MusteriAd',
  'Firma',
  'No',
  'Plaka',
  'Arac',
  'CikisTarih',
  'DonusTarih',
  'CikisOfis',
  'DonusOfis',
  'Tutar',
] as const;

const KNOWN_TYPES = [
  'TalepAlindi',
  'RezervasyonOnay',
  'RezervasyonIptal',
  'TeslimHatirlatma',
  'IadeHatirlatma',
  'OdemeHatirlatma',
];

/** Şablon formunun sunucu değerleri (birleştirme tabanı). */
function templateValues(m: MessageTemplate): Record<string, unknown> {
  return {
    konu: m.konu ?? null,
    govde: m.govde === '' ? null : m.govde,
    aktif: m.kayitli ? m.aktif : true,
  };
}

/**
 * F11.2b müşteri mesaj şablonları (Blazor `MesajSablonlari`, ManageUsers): tür × kanal başına tek şablon. PUT tam
 * değiştirme; kayıtlı şablonda `surum` zorunlu, 409 `cakisma` formu silmez (güncel şablon birleşir).
 */
@Component({
  selector: 'rc-message-templates-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PageBand,
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    FormErrors,
    TextArea,
    TextInput,
    Checkbox,
  ],
  styleUrl: '../system.scss',
  templateUrl: './message-templates-page.html',
})
export class MessageTemplatesPage {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = translationFunction();

  protected readonly placeholders = PLACEHOLDERS.map((p) => `{${p}}`);
  protected readonly list = new TemelStore<readonly MessageTemplate[]>(() =>
    this.api.get<readonly MessageTemplate[]>(ROOT),
  );
  protected readonly editing = signal<MessageTemplate | null>(null);
  protected readonly form = new FormGroup({
    konu: new FormControl<string | null>(null, Validators.maxLength(256)),
    govde: new FormControl<string | null>(null, [Validators.required, Validators.maxLength(8192)]),
    aktif: new FormControl<boolean | null>(true),
  });
  protected readonly submit = formSubmission();

  constructor() {
    pageLeaveGuard(() => this.hasUnsavedChanges());
    this.list.yukle();
  }

  hasUnsavedChanges(): boolean {
    return this.editing() !== null && this.form.dirty;
  }

  protected typeLabel(type: string): string {
    return KNOWN_TYPES.includes(type) ? this.t(`sistem.mesaj.tur.${type}` as CeviriAnahtari) : type;
  }

  protected channelLabel(channel: string): string {
    return this.t(channel === 'Eposta' ? 'sistem.mesaj.eposta' : 'sistem.mesaj.sms');
  }

  protected isEditing(m: MessageTemplate): boolean {
    const e = this.editing();
    return e !== null && e.tur === m.tur && e.kanal === m.kanal;
  }

  protected open(m: MessageTemplate): void {
    const subject = this.form.controls.konu;
    subject.setValidators(
      m.kanal === 'Eposta'
        ? [Validators.required, Validators.maxLength(256)]
        : Validators.maxLength(256),
    );
    this.form.reset(templateValues(m));
    this.submit.kilit.yenile();
    this.editing.set(m);
  }

  protected close(): void {
    this.editing.set(null);
    this.form.reset();
  }

  protected save(): void {
    const m = this.editing();
    if (m === null) return;
    const v = this.form.getRawValue();
    const body = {
      konu: m.kanal === 'Eposta' ? v.konu : null,
      govde: v.govde,
      aktif: v.aktif === true,
      surum: m.surum ?? null,
    };
    this.submit.gonder(
      this.form,
      (key) =>
        this.api.put<MessageTemplate>(
          `${ROOT}/${encodeURIComponent(m.tur)}/${encodeURIComponent(m.kanal)}`,
          body,
          { islemAnahtari: key },
        ),
      {
        basarili: () => {
          this.editing.set(null);
          this.toast.basari(this.t('sistem.ortak.kaydedildi'));
          this.list.yenile();
        },
        hata: (h) => {
          if (h.kod === 'cakisma') this.mergeLatest(m);
        },
      },
    );
  }

  private mergeLatest(m: MessageTemplate): void {
    this.api
      .get<readonly MessageTemplate[]>(ROOT)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (rows) => {
          const fresh = rows.find((r) => r.tur === m.tur && r.kanal === m.kanal);
          if (!fresh || !this.isEditing(m)) return;
          mergeServerValues(
            this.form,
            templateValues(m),
            templateValues(fresh),
            this.t('form.tanim.cakismaAlan'),
          );
          this.editing.set(fresh);
        },
        error: () => undefined,
      });
  }
}
