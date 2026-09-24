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
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { Alan } from '@shared/form/alan/alan';
import { FormHatalari } from '@shared/form/form-hatalari';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { Ikon } from '@shared/ikon/ikon';

import { type FrozenRequest, MoneySubmission, duplicateNotice } from '../money-submission';
import {
  type AccountKind,
  type PolicyDetail,
  type PolicyPaymentRequest,
  type PolicyRow,
  REGULATION,
  num,
  recordPath,
} from '../service-insurance-model';

/**
 * Sigorta ödemesi (Blazor `/regulasyon-odeme/sigorta`) — PARA: prim + zeyil ek prim, Borç Gider[araç] / Alacak
 * Kasa-Banka. Yapısal idempotency (poliçe başına tek ödeme; ödenmiş poliçe → 409 `mukerrer` + `mevcut`). Dövizli
 * poliçede kur boş = sunucu çözer (firma kuru → TCMB; bulunamazsa 400), TRY'de kur alanı yok. Tutar hesabı SUNUCUDA.
 */
@Component({
  selector: 'rc-policy-pay-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, TranslocoPipe, Alan, FormHatalari, Ikon, ParaGirdisi, Secim],
  template: `
    <section class="bolum" aria-labelledby="rc-police-ode-baslik">
      <h2 id="rc-police-ode-baslik">{{ 'servisSigorta.sigorta.ode' | transloco }}</h2>
      <p class="aciklama">{{ 'servisSigorta.sigorta.odeAciklama' | transloco }}</p>
      <p class="sonraki">
        {{ 'servisSigorta.sigorta.odenecekPrim' | transloco: { prim: premiumText() } }}
      </p>
      @if (payment.frozen()) {
        <div class="rc-form-mesaji rc-form-mesaji--uyari" role="alert">
          {{ 'servisSigorta.para.sonucBilinmiyor' | transloco }}
        </div>
      }
      <form class="form" [formGroup]="form" (ngSubmit)="pay()">
        <div class="rc-form-izgara">
          <rc-alan [etiket]="'servisSigorta.para.hesap' | transloco">
            <rc-secim formControlName="hesap" [secenekler]="kindOptions" />
          </rc-alan>
          @if (accountOptions().length > 0) {
            <rc-alan [etiket]="'servisSigorta.para.hesapId' | transloco">
              <rc-secim
                formControlName="hesapId"
                [secenekler]="accountOptions()"
                [bosEtiket]="'servisSigorta.para.hesapBelirtilmemis' | transloco"
              />
            </rc-alan>
          }
          <rc-alan
            [etiket]="'servisSigorta.sigorta.zeyilEkPrim' | transloco"
            [ipucu]="'servisSigorta.sigorta.zeyilEkPrimIpucu' | transloco"
          >
            <rc-para-girdisi formControlName="zeyilEkPrim" [paraBirimi]="policy().doviz" />
          </rc-alan>
          @if (foreign()) {
            <rc-alan
              [etiket]="'servisSigorta.para.kur' | transloco"
              [ipucu]="'servisSigorta.para.kurIpucu' | transloco: { doviz: policy().doviz }"
            >
              <rc-para-girdisi formControlName="kur" [kesir]="6" yerTutucu="" />
            </rc-alan>
          }
        </div>
        <rc-form-hatalari [hatalar]="errors()" />
        <div class="form__eylemler">
          <button
            type="submit"
            class="rc-dugme rc-dugme--birincil rc-dugme--kucuk"
            [disabled]="payment.sending() || refreshing()"
          >
            <rc-ikon ad="cash" [boyut]="14" />
            {{
              payment.sending()
                ? ('form.gonderiliyor' | transloco)
                : payment.frozen()
                  ? ('servisSigorta.para.tekrarDene' | transloco)
                  : ('servisSigorta.sigorta.ode' | transloco)
            }}
          </button>
          @if (payment.frozen() && !payment.sending()) {
            <button
              type="button"
              class="rc-dugme rc-dugme--hayalet rc-dugme--kucuk"
              (click)="abandon()"
            >
              {{ 'servisSigorta.para.vazgec' | transloco }}
            </button>
          }
        </div>
      </form>
    </section>
  `,
})
export class PolicyPayPanel {
  readonly policy = input.required<PolicyRow>();
  readonly accounts = input<readonly FinansHesapOgesi[]>([]);
  readonly refreshing = input(false);
  readonly settled = output<void>();

  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly confirm = inject(OnayServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();

  protected readonly payment = new MoneySubmission<PolicyPaymentRequest>(
    inject(TahsilatDenemeKaydi),
  );
  protected readonly errors = signal<readonly string[]>([]);
  protected readonly form = new FormGroup({
    hesap: new FormControl<AccountKind | null>('Kasa', Validators.required),
    hesapId: new FormControl<string | null>(null),
    zeyilEkPrim: new FormControl<string | null>(null),
    kur: new FormControl<string | null>(null),
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
  protected readonly foreign = computed(() => this.policy().doviz !== 'TRY');
  protected readonly premiumText = computed(() =>
    paraBicimle(num(this.policy().prim), this.policy().doviz),
  );

  constructor() {
    effect(() => {
      const locked = this.payment.frozen() !== null;
      untracked(() => (locked ? this.form.disable() : this.form.enable()));
    });
  }

  hasPendingWork(): boolean {
    return this.payment.frozen() !== null || this.payment.sending() || this.form.dirty;
  }

  protected pay(): void {
    if (this.payment.sending() || this.refreshing()) return;
    this.errors.set([]);
    sunucuHatalariniTemizle(this.form);
    this.form.markAllAsTouched();
    if (this.payment.frozen() === null && this.form.invalid) return;
    const p = this.policy();
    const v = this.form.getRawValue();
    const body: PolicyPaymentRequest = {
      hesap: v.hesap ?? 'Kasa',
      hesapId: v.hesapId,
      zeyilEkPrim: v.zeyilEkPrim,
      kur: this.foreign() ? v.kur : null,
    };
    const copy = this.payment.prepare(`sigorta:${p.id}`, body, {
      tutar: num(p.prim),
      doviz: p.doviz,
      hesap: v.hesap,
    });
    this.payment.started(copy);
    this.api
      .post<PolicyDetail>(recordPath(`${REGULATION}/sigortalar`, p.id, '/odeme'), copy.body, {
        islemAnahtari: copy.key,
        context: istekBaglami({ mukerrerCagiranGosterir: true }),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (d) => {
          this.payment.succeeded();
          this.toast.basari(
            this.t('servisSigorta.sigorta.odendiBildirim', {
              tutar: paraBicimle(num(d.odeme?.tutar ?? null), d.odeme?.doviz ?? p.doviz),
            }),
          );
          this.form.reset({ hesap: 'Kasa' });
          this.settled.emit();
        },
        error: (raw: unknown) => this.failed(copy, raw),
      });
  }

  protected async abandon(): Promise<void> {
    const yes = await this.confirm.sor({
      baslik: this.t('servisSigorta.para.vazgecBaslik'),
      mesaj: this.t('servisSigorta.para.vazgecMesaj'),
    });
    if (!yes) return;
    this.payment.abandon();
    this.settled.emit();
  }

  private failed(copy: FrozenRequest<PolicyPaymentRequest>, raw: unknown): void {
    const error = apiHatasinaCevir(raw);
    const outcome = this.payment.failed(copy, error);
    switch (outcome.kind) {
      case 'uncertain':
        return;
      case 'duplicate': {
        const n = duplicateNotice(outcome.type, error, this.payment.lastSubmission, this.t);
        if (n.tone === 'bilgi') this.toast.bilgi(n.message, { baslik: n.title });
        else this.toast.uyari(n.message, { baslik: n.title });
        this.settled.emit();
        return;
      }
      case 'stale':
        this.settled.emit();
        return;
      default: {
        const unmatched = sunucuHatalariniUygula(this.form, error.alanlar);
        if (unmatched.length > 0) this.errors.set(unmatched);
        else if (error.alanlar === undefined && !genelGosterilir(error))
          this.errors.set([error.detay]);
      }
    }
  }
}
