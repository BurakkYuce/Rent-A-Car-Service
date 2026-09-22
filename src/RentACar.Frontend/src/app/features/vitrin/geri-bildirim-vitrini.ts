import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { SUNUCU_HATASI } from '@core/form/sunucu-hatalari';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { type ToastDurumu, ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { type BantTuru, UyariBandiServisi } from '@core/geri-bildirim/uyari-bandi-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';

type Kontrol = 'plaka' | 'aciklama';

/**
 * Oturum ve geri bildirim vitrini (F3.3; F3.7 vitrini genişletir). Deneme formu
 * `POST /api/ui/v1/vitrin/kayit`'a gider — bu uç SUNUCUDA YOK; e2e onu Playwright ile sahteler ve
 * kod bazlı davranışı kilitler: doğrulama/çakışma formu korur, oturum düşünce yerinde giriş + aynı
 * istek (aynı `Idempotency-Key`) tekrarlanır, mükerrerde kayıt yeniden yüklenir. Gönderim F3.6
 * `formGonderimi` ile (alan hataları kontrollere, genel hatalar `rc-form-hatalari`'na). Canlıda uç 404 verir.
 */
@Component({
  selector: 'rc-geri-bildirim-vitrini',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, TranslocoPipe, RouterLink, FormHatalari],
  templateUrl: './geri-bildirim-vitrini.html',
  styleUrl: './geri-bildirim-vitrini.scss',
})
export class GeriBildirimVitrini {
  private readonly api = inject(ApiIstemcisi);
  private readonly onay = inject(OnayServisi);
  protected readonly toast = inject(ToastServisi);
  private readonly bant = inject(UyariBandiServisi);
  private readonly t = ceviriFonksiyonu();

  protected readonly form = inject(NonNullableFormBuilder).group({ plaka: [''], aciklama: [''] });
  protected readonly gonderim = formGonderimi();
  protected readonly sonuc = signal<string | null>(null);
  protected readonly yenilemeSayisi = signal(0);
  protected readonly onaySonucu = signal<boolean | null>(null);
  /** Kontrol hataları signal değil: zoneless'ta şablon yanıttan sonra bununla yeniden çizilir. */
  private readonly yanitSayaci = signal(0);
  protected readonly durumlar: readonly ToastDurumu[] = [
    'basari',
    'bilgi',
    'uyari',
    'hata',
    'notr',
    'bekleme',
  ];

  protected readonly bantTurleri: readonly BantTuru[] = ['uyari', 'hata', 'bilgi'];

  protected gonder(): void {
    this.gonderim.gonder(
      this.form,
      (anahtar) =>
        this.api.post<{ id: string }>('/api/ui/v1/vitrin/kayit', this.form.getRawValue(), {
          islemAnahtari: anahtar,
          context: istekBaglami({
            mukerrerdeYenile: () => this.yenilemeSayisi.update((n) => n + 1),
          }),
        }),
      {
        basarili: (yanit) => {
          this.sonuc.set(yanit.id);
          this.toast.basari(this.t('vitrin.kaydedildi'));
        },
        hata: () => this.yanitSayaci.update((n) => n + 1),
      },
    );
  }

  protected alanHatalari(ad: Kontrol): readonly string[] {
    this.yanitSayaci();
    const hatalar: unknown = this.form.controls[ad].errors?.[SUNUCU_HATASI];
    return Array.isArray(hatalar) ? hatalar.filter((h): h is string => typeof h === 'string') : [];
  }

  protected toastGoster(durum: ToastDurumu): void {
    const mesaj = this.t('vitrin.ornekToast');
    if (durum === 'bekleme') {
      const id = this.toast.bekleme(mesaj);
      setTimeout(() => this.toast.bitir(id, 'basari', this.t('vitrin.kaydedildi')), 1500);
    } else {
      this.toast.goster(durum, mesaj);
    }
  }

  protected bantGoster(tur: BantTuru): void {
    this.bant.goster({ tur, mesaj: this.t('vitrin.bantOrnek') });
  }

  protected async onayIste(): Promise<void> {
    this.onaySonucu.set(
      await this.onay.sor({
        baslik: this.t('vitrin.onayBaslik'),
        mesaj: this.t('vitrin.onayMesaj'),
        tehlikeli: true,
      }),
    );
  }
}
