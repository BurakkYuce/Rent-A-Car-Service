import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import {
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
  type AbstractControl,
  type ValidationErrors,
} from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type {
  FinanceAccountItem,
  FinanceTransactionResponse,
  RentalListRow,
  CollectionRequest,
} from '@core/api/ui-tipleri';
import { formatMoney } from '@core/bicim/bicim';
import { invariantDecimal } from '@core/form/ondalik';
import {
  CollectionAttemptRecord,
  CollectionAttempt,
  type TahsilatGonderimi,
  reportCollectionDuplicate,
} from '@core/form/collection-attempt';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { requestContext } from '@core/oturum/request-context';
import { TemelStore } from '@core/veri/temel-store';
import { Alan } from '@shared/form/alan/alan';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { MoneyInput } from '@shared/form/kontroller/money-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { Icon } from '@shared/ikon/icon';

import { rentalCurrency, count } from './rental-columns';

type AccountType = 'Kasa' | 'Banka';

let nextNo = 0;

/** Blazor liste formuyla aynı sabitler (FAZ-84 tek-tık hızlı tahsilat; açıklama cari ekstresinde görünür). */
export const COLLECTION_CHANNEL = 'Masaüstü';
export const collectionDescription = (contractNo: string) =>
  `Hızlı tahsilat (liste) — ${contractNo}`;

/**
 * Satırdan "Tahsil Et" (Blazor RentalList hızlı tahsilat formu; `POST /api/ui/v1/finans/tahsilat`). PARA:
 *
 * - **Anahtar:** satır DTO'sundaki `tahsilat.anahtar` (sunucunun deterministik `TahsilatAnahtar`'ı:
 *   kira + bakiye + işlem sayısı) gövdede `tahsilatAnahtar` olarak AYNEN geri gider; `Idempotency-Key`
 *   başlığı da aynı değeri taşır (sunucuda deterministik anahtar başlıktan önceliklidir). İstemci anahtar
 *   ÜRETMEZ — iki sekme/iki kullanıcı aynı satırı tahsil ederse ikincisi 409 `mukerrer` olur.
 * - **Kilit:** istek uçarken gönder düğmesi pasif, ikinci gönderim yok sayılır (`formGonderimi`).
 * - **409 `mukerrer`:** yeniden gönderim YOK (ne aynı ne yeni anahtarla, ne de `tahsilatAnahtar`'sız). Sınıf ve
 *   toast `TahsilatDenemesi` + `tahsilatMukerrerBildir` (sabit panel ve Panel ile TEK kural): kendi birebir tekrarı
 *   ("zaten kaydedildi") ve bayat anahtar → `sonuclandi` (panel kapanır, liste yenilenir); başka işlem yazılmış
 *   (M-A) → panel açık, ön-dolu tutar yeni bakiyeyle yenilenir (L-2); sonucu bilinmeyen denemenin tutarı
 *   değiştirilmiş tekrarı (M-C) → "önceki denemeniz kaydedilmiş", tutar TEMİZLENİR, panel açık (yeni anahtar).
 * - **Yeniden deneme** (ağ/5xx/400 sonrası aynı panelden): gövde DAİMA aynı `tahsilatAnahtar`'ı taşır — anahtarsız
 *   tekrar yok (envanter "SPA sözleşmesi"). İlk deneme sunucuda yazıldıysa ikinci 409 alır, çift yazım olmaz.
 * - **2xx:** başarı toast'u + `sonuclandi` (liste yenilenir → satırın yeni anahtarı gelir).
 * - **Diğer hatalar** (`dogrulama`, `cakisma`, 5xx, ağ): form DEĞERLERİ KORUNUR, panel açık kalır.
 * - Cari, kira ve döviz satırdan (kira dövizi); kur boş → sunucu çözer (TRY=1; döviz: firma kuru → TCMB).
 */
@Component({
  selector: 'rc-kira-tahsil-paneli',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, TranslocoPipe, Alan, FormErrors, Icon, MoneyInput, Selection],
  template: `
    <section class="rc-bolum" [attr.aria-labelledby]="titleId">
      <header>
        <h2 [id]="titleId">
          {{ 'kiraListesi.tahsil.baslik' | transloco: { no: satir().sozlesmeNo } }}
        </h2>
        <p class="panel__aciklama">{{ aciklama() }}</p>
      </header>
      <form
        class="panel__form"
        [formGroup]="form"
        [attr.aria-labelledby]="titleId"
        (ngSubmit)="gonder()"
      >
        <div class="rc-form-izgara">
          <rc-alan
            [etiket]="'kiraListesi.tahsil.tutar' | transloco"
            [ipucu]="'kiraListesi.tahsil.tutarIpucu' | transloco"
          >
            <rc-para-girdisi formControlName="tutar" [paraBirimi]="currency()" />
          </rc-alan>
          <rc-alan [etiket]="'kiraListesi.tahsil.hesap' | transloco">
            <rc-secim formControlName="hesap" [secenekler]="accountTypes" />
          </rc-alan>
          @if (accountOptions().length > 0) {
            <rc-alan [etiket]="'kiraListesi.tahsil.hesapId' | transloco">
              <rc-secim
                formControlName="hesapId"
                [secenekler]="accountOptions()"
                [bosEtiket]="'kiraListesi.tahsil.hesapBelirtilmemis' | transloco"
              />
            </rc-alan>
          }
        </div>
        <rc-form-hatalari [hatalar]="submission.genelHatalar()" />
        <div class="rc-form-eylemler">
          <button
            type="submit"
            class="rc-dugme rc-dugme--birincil rc-dugme--kucuk"
            [disabled]="submission.gonderiliyor()"
          >
            <rc-ikon ad="cash" [boyut]="14" />
            {{
              submission.gonderiliyor()
                ? ('form.gonderiliyor' | transloco)
                : ('kiraListesi.tahsil.kaydet' | transloco)
            }}
          </button>
          <button
            type="button"
            class="rc-dugme rc-dugme--hayalet rc-dugme--kucuk"
            [disabled]="submission.gonderiliyor()"
            (click)="kapat.emit()"
          >
            {{ 'kiraListesi.tahsil.vazgec' | transloco }}
          </button>
        </div>
      </form>
    </section>
  `,
  styles: `
    h2 {
      margin: 0;
      font-size: var(--rc-yazi-lg);
      font-weight: var(--rc-agirlik-kalin);
    }
    .panel__aciklama {
      margin: var(--rc-bosluk-1) 0 0;
      color: var(--rc-metin-ikincil);
    }
    .panel__form {
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-3);
    }
  `,
})
export class CollectPanel {
  /** Tahsil edilecek satır (anlık görüntü; `tahsilat` dolu olmalı — sayfa yalnız öyleyse açar). */
  readonly satir = input.required<RentalListRow>();
  readonly kapat = output<void>();
  /** 2xx ya da 409 `mukerrer`: işlem sonuçlandı, liste yeniden yüklenmeli (yeni anahtar). */
  readonly sonuclandi = output<void>();
  /**
   * 3. tur M-A: 409 + `mevcut` İÇERİĞİ FARKLI — başka bir tahsilat yazılmış, bu panelin tutarı YAZILMADI. Panel
   * AÇIK KALIR (tutar korunur); sayfa listeyi yeniden yükleyip satırın YENİ anahtarını panele verir.
   */
  readonly anahtarTazele = output<void>();

  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastService);
  private readonly t = translationFunction();
  private readonly root = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly submission = formSubmission();
  /** Sonucu bilinmeyen deneme izi (M-C) + L-2. */
  private readonly attempt = new CollectionAttempt(inject(CollectionAttemptRecord));
  /** Sayfa, istek uçarken başka satırın panelini açmaz (uçan istek iptal edilip sonucu kaybolmasın). */
  readonly submitting = this.submission.gonderiliyor;

  protected readonly titleId = `rc-tahsil-paneli-${++nextNo}`;

  protected readonly form = new FormGroup({
    tutar: new FormControl<string | null>(null, {
      validators: [Validators.required, (k: AbstractControl) => this.positive(k)],
    }),
    hesap: new FormControl<AccountType | null>('Kasa', { validators: [Validators.required] }),
    hesapId: new FormControl<string | null>(null),
  });

  protected readonly accountTypes: readonly SecenekOgesi<AccountType>[] = [
    { deger: 'Kasa', etiket: this.t('kiraListesi.tahsil.kasa') },
    { deger: 'Banka', etiket: this.t('kiraListesi.tahsil.banka') },
  ];

  /** Aktif kasa/banka hesapları (seçim isteğe bağlı; hata sessiz — seçici görünmez, tür yine seçilir). */
  private readonly hesaplar = new TemelStore(() =>
    this.api.get<readonly FinanceAccountItem[]>('/api/ui/v1/finans/hesaplar', {
      context: requestContext({ sessiz: true }),
    }),
  );
  private readonly accountType = toSignal(this.form.controls.hesap.valueChanges, {
    initialValue: this.form.controls.hesap.value,
  });
  /** Türü seçilen türle eşleşen hesaplar (türü belirsiz hesap sunucuda reddedilir → listelenmez). */
  protected readonly accountOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    (this.hesaplar.veri() ?? [])
      .filter((h) => h.tur !== null && h.tur === this.accountType())
      .map((h) => ({ deger: h.id, etiket: h.etiket })),
  );

  protected readonly currency = computed(
    () => this.satir().tahsilat?.doviz ?? rentalCurrency(this.satir()),
  );
  protected readonly aciklama = computed(() => {
    const s = this.satir();
    return this.t('kiraListesi.tahsil.aciklama', {
      musteri: s.musteriAd,
      bakiye: formatMoney(count(s.bakiye), rentalCurrency(s)),
    });
  });

  constructor() {
    this.hesaplar.yukle();
    // Tür değişince seçili hesap o türden değilse düşer (Kasa hesabıyla Banka tahsilatı sunucuda red).
    this.form.controls.hesap.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      const id = this.form.controls.hesapId.value;
      if (id !== null && !this.accountOptions().some((h) => h.deger === id)) {
        this.form.controls.hesapId.setValue(null);
      }
    });
    // Satır değişince (başka satırdan "Tahsil Et") form o satırın önerisiyle sıfırlanır. AYNI kiranın güncel
    // satırı (M-A: yeni anahtar) gelince form KORUNUR — kullanıcının yazdığı yazılmamış tutar kaybolmaz.
    let lastRental: string | null = null;
    effect(() => {
      const s = this.satir();
      untracked(() => {
        if (s.id === lastRental) {
          // L-2: "YAZILMADI" (başka işlem) sonrası dokunulmamış ön-dolu tutar yeni satırın önerisiyle yenilenir.
          const amount = this.form.controls.tutar;
          if (this.attempt.shouldRefreshAmount(s.tahsilat?.anahtar) && amount.pristine)
            amount.setValue(invariantDecimal(s.tahsilat?.varsayilanTutar, { kesir: 2 }));
          return;
        }
        lastRental = s.id;
        this.form.reset({
          tutar: invariantDecimal(s.tahsilat?.varsayilanTutar, { kesir: 2 }),
          hesap: 'Kasa',
          hesapId: null,
        });
      });
    });
    // Açılışta odak önerilen tutarda ve metin SEÇİLİ (para girdisinin varsayılanı): doğrudan yazılan tutar önerinin yerine
    // geçer, sonuna eklenmez (adversarial F3: "1250,50" + "90" → "1250,5090" → 1 kuruş fazla tahsilat).
    afterNextRender(() =>
      this.root.nativeElement.querySelector<HTMLInputElement>('input')?.focus(),
    );
  }

  protected gonder(): void {
    const s = this.satir();
    const collection = s.tahsilat;
    if (collection === null) return;
    const v = this.form.getRawValue();
    let g: TahsilatGonderimi | null = null;
    const body: CollectionRequest = {
      cariId: collection.cariId,
      tutar: v.tutar ?? '',
      hesap: v.hesap,
      kiraId: collection.rentalId,
      doviz: collection.doviz,
      hesapId: v.hesapId,
      kanal: COLLECTION_CHANNEL,
      aciklama: collectionDescription(s.sozlesmeNo),
      // Sunucunun deterministik anahtarı AYNEN geri gider (istemci üretmez, değiştirmez).
      tahsilatAnahtar: collection.anahtar,
    };
    this.submission.gonder(
      this.form,
      (key) => {
        // M-C / 5. tur: belirsiz denemeler gönderimden ÖNCE, ANAHTAR üzerinden (Panel ile ortak kayıt).
        g = this.attempt.start(collection.anahtar, {
          tutar: body.tutar,
          doviz: collection.doviz,
          hesap: body.hesap,
        });
        return this.api.post<FinanceTransactionResponse>('/api/ui/v1/finans/tahsilat', body, {
          islemAnahtari: key,
          // 409 mukerrer: tekrar gönderilmez; toast'u `hata` gösterir (sınıf tekrar bilgisine bağlı). Yeniden
          // yükleme de `hata`'da: kapat + yükle ya da açık tut + yeni anahtar.
          context: requestContext({ mukerrerCagiranGosterir: true }),
        });
      },
      {
        deterministikAnahtar: collection.anahtar,
        hata: (h) => {
          if (!g) return;
          const type = this.attempt.errorReceived(g, h);
          if (type === null) return;
          reportCollectionDuplicate(this.toast, this.t, type, h, {
            gonderim: g,
            bayatBaslik: this.t('kiraListesi.tahsil.kayitDegismis'),
            ek: this.t('geriBildirim.mukerrerYenilendi'),
          });
          const amount = this.form.controls.tutar;
          switch (type) {
            case 'baskaIslemYazildi':
              if (amount.pristine && amount.value !== null)
                this.attempt.requestAmountRefresh(collection.anahtar);
              this.anahtarTazele.emit();
              return;
            case 'oncekiDenemeKaydedilmis':
            case 'baskaIslemDenemeYazilmadi':
              amount.setValue(null);
              this.anahtarTazele.emit();
              return;
            default:
              this.sonuclandi.emit();
          }
        },
        basarili: () => {
          if (g) this.attempt.successful(g);
          this.toast.basari(
            this.t('kiraListesi.tahsil.basarili', {
              no: s.sozlesmeNo,
              tutar: formatMoney(count(body.tutar), collection.doviz),
            }),
          );
          this.sonuclandi.emit();
        },
      },
    );
  }

  private positive(k: AbstractControl): ValidationErrors | null {
    const value: unknown = k.value;
    if (typeof value !== 'string' || value === '') return null; // boşluğu `required` söyler
    return Number(value) > 0
      ? null
      : { tutarPozitif: { mesaj: this.t('kiraListesi.tahsil.tutarPozitif') } };
  }
}
