import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  InjectionToken,
  OnInit,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import {
  type AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  type ValidationErrors,
  Validators,
} from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import type { ApiHatasi } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { SubmitLock } from '@core/form/submit-lock';
import {
  CollectionAttemptRecord,
  CollectionAttempt,
  type TahsilatGonderimi,
  reportCollectionDuplicate,
} from '@core/form/collection-attempt';
import type { FinanceAccountItem, CollectionInfo } from '@core/api/ui-tipleri';
import { formatMoney } from '@core/bicim/bicim';
import { invariantDecimal } from '@core/form/ondalik';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { requestContext } from '@core/oturum/request-context';
import { Alan } from '@shared/form/alan/alan';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { MoneyInput } from '@shared/form/kontroller/money-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';

import { type AccountType, count, collectionBody } from './panel-modeli';

/**
 * Panelin TEK tahsilat kilidi (sayfa sağlar). Tüm satırların formu aynı kilidi kullanır: istek uçarken ne başka
 * satırın formu gönderebilir ne de tablodaki "Tahsil Et" düğmeleri basılabilir (sayfa `gonderiliyor()`'u okur).
 */
export const PANEL_COLLECTION_LOCK = new InjectionToken<SubmitLock>('PANEL_TAHSILAT_KILIDI');

/**
 * Panel "Tahsil Et" formu (Blazor Home.razor hızlı tahsilat karşılığı): tutar (bakiye ön dolu, düzenlenebilir),
 * hesap türü (Kasa/Banka) ve isteğe bağlı somut kasa/banka hesabı. `POST /api/ui/v1/finans/tahsilat`.
 *
 * PARA KURALLARI (roadmap F4.5, idempotency envanteri E01):
 * - Anahtar sunucunun deterministik `tahsilatAnahtar`'ı; gövdede AYNEN geri gider, istemci anahtarı üretilmez
 *   (`GonderimKilidi` deterministik dalı).
 * - Gönderim boyunca kilit (sayfa düzeyi `PANEL_TAHSILAT_KILIDI`): düğme pasif, çift tık tek istek.
 * - Form bir SATIRA aittir: sayfa onu `rentalId` anahtarıyla oluşturur; başka satırın "Tahsil Et"i yeni örnek
 *   açar (tutar, hesap, hata — hiçbir durum satırlar arasında taşınmaz). Adversarial F1: aynı örnek korunduğunda
 *   A'nın tutarı B'nin kirasına yazılıyordu.
 * - 409 `mukerrer` sınıfı/toast'u `TahsilatDenemesi` + `tahsilatMukerrerBildir` (sabit panel, kira listesiyle TEK
 *   kural): başka işlem yazılmış (M-A) → form açık, ön-dolu tutar yeni bakiyeyle yenilenir (L-2); sonucu bilinmeyen
 *   denemenin tutarı değiştirilmiş tekrarı (M-C) → "önceki denemeniz kaydedilmiş", tutar TEMİZLENİR, form açık.
 * - 409 `mukerrer`: otomatik yeniden gönderim YOK; `mukerrer` çıktısı panel yeniden yüklenir, form kapanır. Sunucu
 *   anahtarı yeniden hesaplar: bayat anahtar (ekran açıldıktan sonra kirada tahsilat/ters kayıt/bakiye değişti)
 *   de 409 `mukerrer` döner. Bu yüzden genel "Mükerrer işlem" bildirimi KULLANILMAZ (operatör parayı kaydedildi
 *   sanmasın): istek `sessiz`, uyarıda sunucunun `detail`'ı AYNEN + nötr başlık gösterilir.
 * - `sessiz` yüzünden genel bant/toast'a düşmeyen kodlar (yetki, çok istek, 5xx, ağ) formun hata kutusunda
 *   gösterilir; 5xx/ağda "kaydedilmemiş olabilir, kontrol edin" metni (sessiz başarısızlık yok).
 * - 2xx: `tamamlandi` → panel tazelenir; satırın yeni bakiyesi ve işlem sayısıyla YENİ anahtar gelir.
 */
@Component({
  selector: 'rc-panel-tahsilat-formu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, TranslocoPipe, Alan, FormErrors, MoneyInput, Selection],
  template: `
    <form
      class="tahsilat"
      [formGroup]="form"
      (ngSubmit)="gonder()"
      [attr.aria-labelledby]="titleId"
      novalidate
    >
      <h3 class="tahsilat__baslik" [id]="titleId">
        {{ 'panel.tahsilat.baslik' | transloco: { plaka: plaka(), no: belgeNo() } }}
      </h3>
      <div class="tahsilat__alanlar">
        <rc-alan
          [etiket]="'panel.tahsilat.tutar' | transloco"
          [ipucu]="'panel.tahsilat.tutarIpucu' | transloco"
        >
          <!-- Para girdisi varsayılanı: ön dolu öneri odakta (otomatik ya da Tab) tümüyle seçili; yazılan onun yerine geçer. -->
          <rc-para-girdisi formControlName="tutar" [paraBirimi]="bilgi().doviz" />
        </rc-alan>
        <rc-alan [etiket]="'panel.tahsilat.hesap' | transloco">
          <rc-secim formControlName="hesap" [secenekler]="accountTypes" />
        </rc-alan>
        @if (accountOptions().length > 0) {
          <rc-alan [etiket]="'panel.tahsilat.hesapId' | transloco">
            <rc-secim
              formControlName="hesapId"
              [secenekler]="accountOptions()"
              [bosEtiket]="'panel.tahsilat.hesapBelirtilmemis' | transloco"
            />
          </rc-alan>
        }
      </div>
      <rc-form-hatalari [hatalar]="hatalar()" />
      <div class="tahsilat__eylemler">
        <button
          type="submit"
          class="rc-dugme rc-dugme--birincil"
          [disabled]="submission.gonderiliyor()"
          [attr.aria-busy]="submission.gonderiliyor()"
        >
          {{
            (submission.gonderiliyor() ? 'panel.tahsilat.gonderiliyor' : 'panel.tahsilat.gonder')
              | transloco
          }}
        </button>
        <button
          type="button"
          class="rc-dugme"
          [disabled]="submission.gonderiliyor()"
          (click)="vazgecildi.emit()"
        >
          {{ 'panel.tahsilat.vazgec' | transloco }}
        </button>
      </div>
    </form>
  `,
  styles: `
    :host {
      display: block;
    }
    .tahsilat {
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-3);
      padding: var(--rc-bosluk-3);
      border: 1px solid var(--rc-vurgu-metin);
      border-radius: var(--rc-yaricap-md);
      background-color: var(--rc-yuzey-alt);
    }
    .tahsilat__baslik {
      margin: 0;
      font-size: var(--rc-yazi-md);
      font-weight: var(--rc-agirlik-kalin);
      overflow-wrap: anywhere;
    }
    .tahsilat__alanlar {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 12rem), 1fr));
      gap: var(--rc-bosluk-3);
    }
    .tahsilat__eylemler {
      display: flex;
      flex-wrap: wrap;
      gap: var(--rc-bosluk-2);
    }
  `,
})
export class PanelCollectionForm implements OnInit {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = translationFunction();

  readonly bilgi = input.required<CollectionInfo>();
  readonly plaka = input.required<string>();
  readonly belgeNo = input.required<string>();

  /** 2xx: tahsilat yazıldı → panel tazelenir (yeni anahtar). */
  readonly tamamlandi = output();
  /** 409 `mukerrer` (çift gönderim ya da bayat anahtar): panel yeniden yüklenir; yeniden gönderim YOK. */
  readonly mukerrer = output();
  /**
   * 3. tur M-A: 409 + `mevcut` İÇERİĞİ FARKLI — başka bir tahsilat yazılmış, bu formun tutarı YAZILMADI. Form
   * AÇIK KALIR (tutar korunur); sayfa paneli yeniden yükleyip satırın YENİ anahtarını forma verir.
   */
  readonly anahtarTazele = output();
  readonly vazgecildi = output();

  private static sayac = 0;
  protected readonly titleId = `rc-panel-tahsilat-${++PanelCollectionForm.sayac}`;

  protected readonly accountTypes: readonly SecenekOgesi<AccountType>[] = [
    { deger: 'Kasa', etiket: this.t('panel.tahsilat.kasa') },
    { deger: 'Banka', etiket: this.t('panel.tahsilat.banka') },
  ];

  protected readonly form = new FormGroup({
    tutar: new FormControl<string | number | null>(null, [
      Validators.required,
      (c: AbstractControl) => this.positiveAmount(c),
    ]),
    hesap: new FormControl<AccountType | null>('Kasa', Validators.required),
    hesapId: new FormControl<string | null>(null),
  });

  protected readonly submission = formSubmission(
    inject(PANEL_COLLECTION_LOCK, { optional: true }) ?? new SubmitLock(),
  );
  /** Sonucu bilinmeyen deneme izi (M-C) + L-2. */
  private readonly attempt = new CollectionAttempt(inject(CollectionAttemptRecord));
  /** Son gönderimin fotoğrafı (belirsiz denemeler + gönderilen içerik; M-C mesajı). */
  private lastSubmission: TahsilatGonderimi | null = null;
  /** `sessiz` istekte genel bant/toast'a düşmeyen hatalar (yetki, çok istek, 5xx, ağ) formda. */
  private readonly extraErrors = signal<readonly string[]>([]);
  protected readonly hatalar = computed(() => [
    ...this.submission.genelHatalar(),
    ...this.extraErrors(),
  ]);

  private readonly hesaplar = signal<readonly FinanceAccountItem[]>([]);
  private readonly selectedType = toSignal(this.form.controls.hesap.valueChanges, {
    initialValue: this.form.controls.hesap.value,
  });
  /** Somut hesap listesi seçilen türe göre (Blazor HesapSecici; boş = belirtilmemiş, isteğe bağlı). */
  protected readonly accountOptions = computed<readonly SecenekOgesi<string>[]>(() => {
    const type = this.selectedType();
    return this.hesaplar()
      .filter((h) => h.tur === type)
      .map((h) => ({ deger: h.id, etiket: h.etiket }));
  });

  constructor() {
    const root = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    // Adversarial F3: ön dolu tutarda imleç sonda kalınca "90" yazan kullanıcı "1250,5090" üretiyordu. Odakta tüm
    // metni `rc-para-girdisi` (varsayılan) seçer (otomatik odak da Tab da); fazla hane artık yuvarlanmaz, alan
    // hatası olur (#260).
    afterNextRender(() => root.querySelector<HTMLInputElement>('input')?.focus());
    // Tür değişince başka türün hesabı seçili kalmasın (sunucu da reddeder; burada sessizce temizlenir).
    this.form.controls.hesap.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this.form.controls.hesapId.setValue(null);
    });
    // L-2: "YAZILMADI" (başka işlem) sonrası sayfa satırın GÜNCEL hâlini (yeni anahtar) verir; dokunulmamış ön-dolu
    // tutar yeni bakiyeyle yenilenir, kullanıcının yazdığı tutar korunur.
    effect(() => {
      const b = this.bilgi();
      untracked(() => {
        const amount = this.form.controls.tutar;
        if (this.attempt.shouldRefreshAmount(b.anahtar) && amount.pristine)
          amount.setValue(b.varsayilanTutar);
      });
    });
  }

  ngOnInit(): void {
    this.form.controls.tutar.setValue(this.bilgi().varsayilanTutar);
    // Hesap listesi isteğe bağlı: alınamazsa alan görünmez, tahsilat türle yine yapılır (sessiz).
    this.api
      .get<FinanceAccountItem[]>('/api/ui/v1/finans/hesaplar', {
        context: requestContext({ sessiz: true }),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (list) => this.hesaplar.set(list), error: () => undefined });
  }

  protected gonder(): void {
    const info = this.bilgi();
    const value = this.form.getRawValue();
    const amount = invariantDecimal(value.tutar, { kesir: 2 });
    if (!this.submission.gonderiliyor()) this.extraErrors.set([]);
    this.submission.gonder(
      this.form,
      () => {
        // M-C / 5. tur: belirsiz denemeler gönderimden ÖNCE, ANAHTAR üzerinden (kira listesiyle ortak kayıt).
        this.lastSubmission = this.attempt.start(info.anahtar, {
          tutar: amount,
          doviz: info.doviz,
          hesap: value.hesap ?? 'Kasa',
        });
        return this.api.post<unknown>(
          '/api/ui/v1/finans/tahsilat',
          collectionBody(
            info,
            { tutar: amount ?? '', hesap: value.hesap ?? 'Kasa', hesapId: value.hesapId },
            this.t('panel.tahsilat.aciklama', { plaka: this.plaka() }),
          ),
          // Sessiz: genel "Mükerrer işlem" toast'u yerine sunucunun detail'ı (bkz. `hataIsle`).
          { context: requestContext({ sessiz: true }) },
        );
      },
      {
        deterministikAnahtar: info.anahtar,
        basarili: () => {
          if (this.lastSubmission) this.attempt.successful(this.lastSubmission);
          this.toast.basari(
            this.t('panel.tahsilat.basarili', {
              tutar: formatMoney(count(amount), info.doviz),
              plaka: this.plaka(),
            }),
          );
          this.tamamlandi.emit();
        },
        hata: (error) => this.handleError(error),
      },
    );
  }

  /**
   * `dogrulama` / `cakisma` / vazgeçilen `oturum_yok` formGonderimi'nde alanlara ya da forma yazılır; burada
   * yalnız `sessiz` yüzünden genel katmanın göstermediği kodlar ele alınır.
   */
  private handleError(error: ApiHatasi): void {
    const g = this.lastSubmission;
    const type = g ? this.attempt.errorReceived(g, error) : null;
    if (g && type !== null) {
      // Sunucunun detail'ı (M-C dışında) AYNEN; başlık sınıfa göre. `mevcut` doluysa işlem ZATEN yazıldı; yoksa
      // bayat anahtar: "kayıt değişti, tutarı yeniden girin" — nötr başlık, "kaydedildi" izlenimi vermez.
      reportCollectionDuplicate(this.toast, this.t, type, error, {
        gonderim: g,
        bayatBaslik: this.t('panel.tahsilat.mukerrerBaslik'),
        ek: this.t('panel.tahsilat.mukerrerYenilendi'),
      });
      const amount = this.form.controls.tutar;
      switch (type) {
        case 'baskaIslemYazildi':
          // BU tutar yazılmadı → form korunur, yeni anahtar gelir; ön-dolu tutar yeni bakiyeyle yenilenir (L-2).
          if (amount.pristine && amount.value !== null)
            this.attempt.requestAmountRefresh(g.anahtar);
          this.anahtarTazele.emit();
          return;
        case 'oncekiDenemeKaydedilmis':
        case 'baskaIslemDenemeYazilmadi':
          amount.setValue(null);
          this.anahtarTazele.emit();
          return;
        default:
          this.mukerrer.emit();
          return;
      }
    }
    switch (error.kod) {
      case 'sunucu':
        this.extraErrors.set([this.t('geriBildirim.sunucuHatasi')]);
        return;
      case 'ag':
        this.extraErrors.set([this.t('geriBildirim.agHatasi')]);
        return;
      case 'yetki_yok':
      case 'pilot_degil':
      case 'cok_istek':
        this.extraErrors.set([error.detay]);
        return;
      default:
        return;
    }
  }

  private positiveAmount(c: AbstractControl): ValidationErrors | null {
    const canonical = invariantDecimal(c.value as string | number | null, { kesir: 2 });
    if (canonical === null) return null; // boş/biçimsiz: required ya da paraGecersiz söyler
    return Number(canonical) > 0
      ? null
      : { pozitifTutar: { mesaj: this.t('panel.tahsilat.pozitif') } };
  }
}
