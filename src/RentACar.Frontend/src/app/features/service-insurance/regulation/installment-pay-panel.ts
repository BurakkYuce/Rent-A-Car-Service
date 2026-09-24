import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { FinansHesapOgesi } from '@core/api/ui-tipleri';
import { paraBicimle } from '@core/bicim/bicim';
import { sunucuHatalariniTemizle, sunucuHatalariniUygula } from '@core/form/sunucu-hatalari';
import { TahsilatDenemeKaydi } from '@core/form/tahsilat-denemesi';
import type { GunMetni } from '@core/form/tarih-girdisi';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { anDegeri, gunDegeri, metinDegeri } from '@features/planlama-ortak/form-yardimcilari';
import { Alan } from '@shared/form/alan/alan';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';
import { Ikon } from '@shared/ikon/ikon';

import {
  type FrozenRequest,
  MoneySubmission,
  duplicateNotice,
  setLocked,
} from '../money-submission';
import {
  type AccountKind,
  type InstallmentPaymentRequest,
  type InstallmentPaymentResult,
  REGULATION,
  num,
  recordPath,
} from '../service-insurance-model';

export type InstallmentKind = 'mtv' | 'muayene';

/** Donmuş gövde → form değerleri (ekranda gönderilenin aynısı görünsün). */
function bodyToForm(b: InstallmentPaymentRequest) {
  return {
    hesap: (b.hesap as AccountKind | null) ?? 'Kasa',
    hesapId: b.hesapId ?? null,
    tutar: b.tutar === null || b.tutar === undefined ? null : String(b.tutar),
    ceza: b.ceza === null || b.ceza === undefined ? null : String(b.ceza),
    odemeTarihi: gunDegeri(b.odemeTarihi ?? null),
    evrakNo: b.evrakNo ?? null,
    islemYapan: b.islemYapan ?? null,
    kasaKodu: b.kasaKodu ?? null,
    hesapNo: b.hesapNo ?? null,
    aciklama: b.aciklama ?? null,
  };
}

/**
 * MTV / muayene kısmi ödemesi (Blazor `/regulasyon-odeme/mtv|muayene`) — PARA: gider + dengeli defter (Borç
 * Gider[araç] / Alacak Kasa-Banka). Tutar boş → kalanın (muayenede kalan + ceza) tamamı; tutar ve ceza
 * `rc-para-girdisi` ile. `beklenenKalan` = ekranın gördüğü kalan: başka sekme/kullanıcı arada ödediyse sunucu 409
 * `cakisma` döner, form SİLİNMEZ, kayıt yenilenir. Anahtar/donmuş kopya kuralları {@link MoneySubmission}'da.
 */
@Component({
  selector: 'rc-installment-pay-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    FormHatalari,
    Ikon,
    MetinGirdisi,
    ParaGirdisi,
    Secim,
    TarihSecici,
  ],
  templateUrl: './installment-pay-panel.html',
})
export class InstallmentPayPanel {
  readonly kind = input.required<InstallmentKind>();
  readonly recordId = input.required<string>();
  /** Sunucunun kalanı (ekranın gördüğü). */
  readonly remaining = input.required<number>();
  readonly accounts = input<readonly FinansHesapOgesi[]>([]);
  /** Kayıt yenileniyor: düğme pasif (bayat kalanla gönderim olmasın). */
  readonly refreshing = input(false);
  /** 2xx ya da kesin 409: kayıt yeniden okunmalı. */
  readonly settled = output<void>();

  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly confirm = inject(OnayServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();

  protected readonly payment = new MoneySubmission<InstallmentPaymentRequest>(
    inject(TahsilatDenemeKaydi),
  );
  protected readonly errors = signal<readonly string[]>([]);

  protected readonly form = new FormGroup({
    hesap: new FormControl<AccountKind | null>('Kasa', Validators.required),
    hesapId: new FormControl<string | null>(null),
    tutar: new FormControl<string | null>(null),
    ceza: new FormControl<string | null>(null),
    odemeTarihi: new FormControl<GunMetni | null>(null),
    evrakNo: new FormControl<string | null>(null, Validators.maxLength(64)),
    islemYapan: new FormControl<string | null>(null, Validators.maxLength(128)),
    kasaKodu: new FormControl<string | null>(null, Validators.maxLength(64)),
    hesapNo: new FormControl<string | null>(null, Validators.maxLength(64)),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });

  protected readonly kindOptions: readonly SecenekOgesi<AccountKind>[] = [
    { deger: 'Kasa', etiket: this.t('servisSigorta.para.kasa') },
    { deger: 'Banka', etiket: this.t('servisSigorta.para.banka') },
  ];
  private readonly accountKind = toSignal(this.form.controls.hesap.valueChanges, {
    initialValue: this.form.controls.hesap.value,
  });
  protected readonly accountOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    this.accounts()
      .filter((h) => h.tur !== null && h.tur === this.accountKind())
      .map((h) => ({ deger: h.id, etiket: h.etiket })),
  );
  protected readonly remainingText = computed(() => paraBicimle(this.remaining(), 'TRY'));

  constructor() {
    this.form.controls.hesap.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      const id = this.form.controls.hesapId.value;
      if (id !== null && !this.accountOptions().some((h) => h.deger === id))
        this.form.controls.hesapId.setValue(null);
    });
    // İstek uçarken ve donmuş kopya varken form kilitli (inceleme M1): ekrandaki değer gönderilenle aynı kalır;
    // donmuş kopyanın gövdesi forma geri yazılır — "tekrar gönder" ekranda görüneni gönderir.
    effect(() => {
      const frozen = this.payment.frozen();
      const locked = frozen !== null || this.payment.sending();
      untracked(() => {
        if (frozen) this.form.patchValue(bodyToForm(frozen.body), { emitEvent: false });
        setLocked(this.form, locked);
      });
    });
    // `mukerrer` sonrası kilit: kullanıcı açıkça yeni bir tutar yazınca kalkar (inceleme M4).
    this.form.controls.tutar.valueChanges.pipe(takeUntilDestroyed()).subscribe((v) => {
      if (v !== null && v !== '') this.needsAmount.set(false);
    });
  }

  /** `mukerrer` sonrası: boş tutar "kalanın tamamını öde" demek — yeni ödeme ancak açıkça girilen tutarla. */
  protected readonly needsAmount = signal(false);

  /** Sonucu bilinmeyen (donmuş) ödeme ya da doldurulmuş form — sayfa terk koruması sorar. */
  hasPendingWork(): boolean {
    return this.hasPendingPayment() || this.form.dirty;
  }

  /** Sonucu bilinmeyen ya da uçuştaki ödeme (sayfa terkinde özel uyarı). */
  hasPendingPayment(): boolean {
    return this.payment.frozen() !== null || this.payment.sending();
  }

  protected pay(): void {
    if (this.payment.sending() || this.refreshing()) return;
    this.errors.set([]);
    sunucuHatalariniTemizle(this.form);
    this.form.markAllAsTouched();
    if (this.payment.frozen() === null && this.form.invalid) return;
    const v = this.form.getRawValue();
    if (
      this.payment.frozen() === null &&
      this.needsAmount() &&
      (v.tutar === null || v.tutar === '')
    ) {
      this.form.controls.tutar.markAsTouched();
      return; // panelde kalıcı "tutarı açıkça girin" uyarısı var
    }
    const body: InstallmentPaymentRequest = {
      hesap: v.hesap ?? 'Kasa',
      hesapId: v.hesapId,
      tutar: v.tutar,
      ceza: this.kind() === 'muayene' ? v.ceza : null,
      odemeTarihi: anDegeri(v.odemeTarihi, null),
      beklenenKalan: this.remaining(),
      evrakNo: metinDegeri(v.evrakNo),
      islemYapan: metinDegeri(v.islemYapan),
      kasaKodu: metinDegeri(v.kasaKodu),
      hesapNo: metinDegeri(v.hesapNo),
      aciklama: metinDegeri(v.aciklama),
    };
    const copy = this.payment.prepare(`${this.kind()}:${this.recordId()}`, body, {
      tutar: v.tutar ?? this.remaining(),
      doviz: 'TRY',
      hesap: v.hesap,
    });
    const path = recordPath(
      this.kind() === 'mtv' ? `${REGULATION}/mtv` : `${REGULATION}/muayeneler`,
      this.recordId(),
      '/odeme',
    );
    this.payment.started(copy);
    this.api
      .post<InstallmentPaymentResult>(path, copy.body, {
        islemAnahtari: copy.key,
        context: istekBaglami({ mukerrerCagiranGosterir: true }),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => {
          this.payment.succeeded();
          this.toast.basari(
            this.t('servisSigorta.odeme.odendi', {
              sira: r.sira,
              tutar: paraBicimle(num(r.tutar), 'TRY'),
              kalan: paraBicimle(num(r.kalan), 'TRY'),
            }),
          );
          this.resetKeepingAccount();
          this.needsAmount.set(false);
          this.settled.emit();
        },
        error: (raw: unknown) => this.failed(copy, raw),
      });
  }

  /** Donmuş denemeden bilinçli vazgeçiş (onaylı): yeni deneme yeni anahtarla, kalan kontrolü sunucuda. */
  protected async abandon(): Promise<void> {
    const yes = await this.confirm.sor({
      baslik: this.t('servisSigorta.para.vazgecBaslik'),
      mesaj: this.t('servisSigorta.para.vazgecMesaj'),
    });
    if (!yes) return;
    this.payment.abandon();
    this.settled.emit();
  }

  private resetKeepingAccount(): void {
    const { hesap, hesapId } = this.form.getRawValue();
    this.form.reset({ hesap: hesap ?? 'Kasa', hesapId });
  }

  private failed(copy: FrozenRequest<InstallmentPaymentRequest>, raw: unknown): void {
    const error = apiHatasinaCevir(raw);
    const outcome = this.payment.failed(copy, error);
    switch (outcome.kind) {
      case 'uncertain':
        return; // interceptor toast'u + panelde kalıcı "sonucu bilinmiyor" bandı
      case 'duplicate': {
        const n = duplicateNotice(outcome.type, error, this.payment.lastSubmission, this.t);
        if (n.tone === 'bilgi') this.toast.bilgi(n.message, { baslik: n.title });
        else this.toast.uyari(n.message, { baslik: n.title });
        // Başarı yolundaki gibi TAM sıfırlama (ceza dahil; hesap korunur) + açık tutar kilidi (inceleme M4): aksi
        // halde boş tutar "kalanın tamamı" olarak planlanmamış ikinci ödemeyi ve cezayı ikinci kez yazardı.
        this.resetKeepingAccount();
        this.needsAmount.set(true);
        this.settled.emit();
        return;
      }
      case 'stale':
        this.settled.emit(); // alansız cakisma bandı interceptor'da; form SİLİNMEZ
        return;
      default: {
        setLocked(this.form, false); // önce aç: sonra açmak alan hatalarını silerdi
        const unmatched = sunucuHatalariniUygula(this.form, error.alanlar);
        if (unmatched.length > 0) this.errors.set(unmatched);
        else if (error.alanlar === undefined && !genelGosterilir(error))
          this.errors.set([error.detay]);
      }
    }
  }
}
