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
  FinansHesapOgesi,
  FinansIslemYaniti,
  KiraListeSatiri,
  TahsilatIstegi,
} from '@core/api/ui-tipleri';
import { paraBicimle } from '@core/bicim/bicim';
import { invariantOndalik } from '@core/form/ondalik';
import {
  TahsilatDenemeKaydi,
  TahsilatDenemesi,
  type TahsilatGonderimi,
  tahsilatMukerrerBildir,
} from '@core/form/tahsilat-denemesi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { TemelStore } from '@core/veri/temel-store';
import { Alan } from '@shared/form/alan/alan';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { Ikon } from '@shared/ikon/ikon';

import { kiraDovizi, sayi } from './kira-sutunlari';

type HesapTuru = 'Kasa' | 'Banka';

let sonrakiNo = 0;

/** Blazor liste formuyla aynı sabitler (FAZ-84 tek-tık hızlı tahsilat; açıklama cari ekstresinde görünür). */
export const TAHSILAT_KANALI = 'Masaüstü';
export const tahsilatAciklamasi = (sozlesmeNo: string) => `Hızlı tahsilat (liste) — ${sozlesmeNo}`;

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
  imports: [ReactiveFormsModule, TranslocoPipe, Alan, FormHatalari, Ikon, ParaGirdisi, Secim],
  template: `
    <section class="rc-bolum" [attr.aria-labelledby]="baslikKimligi">
      <header>
        <h2 [id]="baslikKimligi">
          {{ 'kiraListesi.tahsil.baslik' | transloco: { no: satir().sozlesmeNo } }}
        </h2>
        <p class="panel__aciklama">{{ aciklama() }}</p>
      </header>
      <form
        class="panel__form"
        [formGroup]="form"
        [attr.aria-labelledby]="baslikKimligi"
        (ngSubmit)="gonder()"
      >
        <div class="rc-form-izgara">
          <rc-alan
            [etiket]="'kiraListesi.tahsil.tutar' | transloco"
            [ipucu]="'kiraListesi.tahsil.tutarIpucu' | transloco"
          >
            <rc-para-girdisi formControlName="tutar" [paraBirimi]="doviz()" />
          </rc-alan>
          <rc-alan [etiket]="'kiraListesi.tahsil.hesap' | transloco">
            <rc-secim formControlName="hesap" [secenekler]="hesapTurleri" />
          </rc-alan>
          @if (hesapSecenekleri().length > 0) {
            <rc-alan [etiket]="'kiraListesi.tahsil.hesapId' | transloco">
              <rc-secim
                formControlName="hesapId"
                [secenekler]="hesapSecenekleri()"
                [bosEtiket]="'kiraListesi.tahsil.hesapBelirtilmemis' | transloco"
              />
            </rc-alan>
          }
        </div>
        <rc-form-hatalari [hatalar]="gonderim.genelHatalar()" />
        <div class="rc-form-eylemler">
          <button
            type="submit"
            class="rc-dugme rc-dugme--birincil rc-dugme--kucuk"
            [disabled]="gonderim.gonderiliyor()"
          >
            <rc-ikon ad="cash" [boyut]="14" />
            {{
              gonderim.gonderiliyor()
                ? ('form.gonderiliyor' | transloco)
                : ('kiraListesi.tahsil.kaydet' | transloco)
            }}
          </button>
          <button
            type="button"
            class="rc-dugme rc-dugme--hayalet rc-dugme--kucuk"
            [disabled]="gonderim.gonderiliyor()"
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
export class TahsilPaneli {
  /** Tahsil edilecek satır (anlık görüntü; `tahsilat` dolu olmalı — sayfa yalnız öyleyse açar). */
  readonly satir = input.required<KiraListeSatiri>();
  readonly kapat = output<void>();
  /** 2xx ya da 409 `mukerrer`: işlem sonuçlandı, liste yeniden yüklenmeli (yeni anahtar). */
  readonly sonuclandi = output<void>();
  /**
   * 3. tur M-A: 409 + `mevcut` İÇERİĞİ FARKLI — başka bir tahsilat yazılmış, bu panelin tutarı YAZILMADI. Panel
   * AÇIK KALIR (tutar korunur); sayfa listeyi yeniden yükleyip satırın YENİ anahtarını panele verir.
   */
  readonly anahtarTazele = output<void>();

  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly t = ceviriFonksiyonu();
  private readonly kok = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly gonderim = formGonderimi();
  /** Sonucu bilinmeyen deneme izi (M-C) + L-2. */
  private readonly deneme = new TahsilatDenemesi(inject(TahsilatDenemeKaydi));
  /** Sayfa, istek uçarken başka satırın panelini açmaz (uçan istek iptal edilip sonucu kaybolmasın). */
  readonly gonderiliyor = this.gonderim.gonderiliyor;

  protected readonly baslikKimligi = `rc-tahsil-paneli-${++sonrakiNo}`;

  protected readonly form = new FormGroup({
    tutar: new FormControl<string | null>(null, {
      validators: [Validators.required, (k: AbstractControl) => this.pozitif(k)],
    }),
    hesap: new FormControl<HesapTuru | null>('Kasa', { validators: [Validators.required] }),
    hesapId: new FormControl<string | null>(null),
  });

  protected readonly hesapTurleri: readonly SecenekOgesi<HesapTuru>[] = [
    { deger: 'Kasa', etiket: this.t('kiraListesi.tahsil.kasa') },
    { deger: 'Banka', etiket: this.t('kiraListesi.tahsil.banka') },
  ];

  /** Aktif kasa/banka hesapları (seçim isteğe bağlı; hata sessiz — seçici görünmez, tür yine seçilir). */
  private readonly hesaplar = new TemelStore(() =>
    this.api.get<readonly FinansHesapOgesi[]>('/api/ui/v1/finans/hesaplar', {
      context: istekBaglami({ sessiz: true }),
    }),
  );
  private readonly hesapTuru = toSignal(this.form.controls.hesap.valueChanges, {
    initialValue: this.form.controls.hesap.value,
  });
  /** Türü seçilen türle eşleşen hesaplar (türü belirsiz hesap sunucuda reddedilir → listelenmez). */
  protected readonly hesapSecenekleri = computed<readonly SecenekOgesi<string>[]>(() =>
    (this.hesaplar.veri() ?? [])
      .filter((h) => h.tur !== null && h.tur === this.hesapTuru())
      .map((h) => ({ deger: h.id, etiket: h.etiket })),
  );

  protected readonly doviz = computed(
    () => this.satir().tahsilat?.doviz ?? kiraDovizi(this.satir()),
  );
  protected readonly aciklama = computed(() => {
    const s = this.satir();
    return this.t('kiraListesi.tahsil.aciklama', {
      musteri: s.musteriAd,
      bakiye: paraBicimle(sayi(s.bakiye), kiraDovizi(s)),
    });
  });

  constructor() {
    this.hesaplar.yukle();
    // Tür değişince seçili hesap o türden değilse düşer (Kasa hesabıyla Banka tahsilatı sunucuda red).
    this.form.controls.hesap.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      const id = this.form.controls.hesapId.value;
      if (id !== null && !this.hesapSecenekleri().some((h) => h.deger === id)) {
        this.form.controls.hesapId.setValue(null);
      }
    });
    // Satır değişince (başka satırdan "Tahsil Et") form o satırın önerisiyle sıfırlanır. AYNI kiranın güncel
    // satırı (M-A: yeni anahtar) gelince form KORUNUR — kullanıcının yazdığı yazılmamış tutar kaybolmaz.
    let sonKira: string | null = null;
    effect(() => {
      const s = this.satir();
      untracked(() => {
        if (s.id === sonKira) {
          // L-2: "YAZILMADI" (başka işlem) sonrası dokunulmamış ön-dolu tutar yeni satırın önerisiyle yenilenir.
          const tutar = this.form.controls.tutar;
          if (this.deneme.tutarYenilensinMi(s.tahsilat?.anahtar) && tutar.pristine)
            tutar.setValue(invariantOndalik(s.tahsilat?.varsayilanTutar, { kesir: 2 }));
          return;
        }
        sonKira = s.id;
        this.form.reset({
          tutar: invariantOndalik(s.tahsilat?.varsayilanTutar, { kesir: 2 }),
          hesap: 'Kasa',
          hesapId: null,
        });
      });
    });
    // Açılışta odak önerilen tutarda ve metin SEÇİLİ (para girdisinin varsayılanı): doğrudan yazılan tutar önerinin yerine
    // geçer, sonuna eklenmez (adversarial F3: "1250,50" + "90" → "1250,5090" → 1 kuruş fazla tahsilat).
    afterNextRender(() => this.kok.nativeElement.querySelector<HTMLInputElement>('input')?.focus());
  }

  protected gonder(): void {
    const s = this.satir();
    const tahsilat = s.tahsilat;
    if (tahsilat === null) return;
    const v = this.form.getRawValue();
    let g: TahsilatGonderimi | null = null;
    const govde: TahsilatIstegi = {
      cariId: tahsilat.cariId,
      tutar: v.tutar ?? '',
      hesap: v.hesap,
      kiraId: tahsilat.rentalId,
      doviz: tahsilat.doviz,
      hesapId: v.hesapId,
      kanal: TAHSILAT_KANALI,
      aciklama: tahsilatAciklamasi(s.sozlesmeNo),
      // Sunucunun deterministik anahtarı AYNEN geri gider (istemci üretmez, değiştirmez).
      tahsilatAnahtar: tahsilat.anahtar,
    };
    this.gonderim.gonder(
      this.form,
      (anahtar) => {
        // M-C / 5. tur: belirsiz denemeler gönderimden ÖNCE, ANAHTAR üzerinden (Panel ile ortak kayıt).
        g = this.deneme.basla(tahsilat.anahtar, {
          tutar: govde.tutar,
          doviz: tahsilat.doviz,
          hesap: govde.hesap,
        });
        return this.api.post<FinansIslemYaniti>('/api/ui/v1/finans/tahsilat', govde, {
          islemAnahtari: anahtar,
          // 409 mukerrer: tekrar gönderilmez; toast'u `hata` gösterir (sınıf tekrar bilgisine bağlı). Yeniden
          // yükleme de `hata`'da: kapat + yükle ya da açık tut + yeni anahtar.
          context: istekBaglami({ mukerrerCagiranGosterir: true }),
        });
      },
      {
        deterministikAnahtar: tahsilat.anahtar,
        hata: (h) => {
          if (!g) return;
          const tur = this.deneme.hataGeldi(g, h);
          if (tur === null) return;
          tahsilatMukerrerBildir(this.toast, this.t, tur, h, {
            gonderim: g,
            bayatBaslik: this.t('kiraListesi.tahsil.kayitDegismis'),
            ek: this.t('geriBildirim.mukerrerYenilendi'),
          });
          const tutar = this.form.controls.tutar;
          switch (tur) {
            case 'baskaIslemYazildi':
              if (tutar.pristine && tutar.value !== null)
                this.deneme.tutarYenilemesiIste(tahsilat.anahtar);
              this.anahtarTazele.emit();
              return;
            case 'oncekiDenemeKaydedilmis':
            case 'baskaIslemDenemeYazilmadi':
              tutar.setValue(null);
              this.anahtarTazele.emit();
              return;
            default:
              this.sonuclandi.emit();
          }
        },
        basarili: () => {
          if (g) this.deneme.basarili(g);
          this.toast.basari(
            this.t('kiraListesi.tahsil.basarili', {
              no: s.sozlesmeNo,
              tutar: paraBicimle(sayi(govde.tutar), tahsilat.doviz),
            }),
          );
          this.sonuclandi.emit();
        },
      },
    );
  }

  private pozitif(k: AbstractControl): ValidationErrors | null {
    const deger: unknown = k.value;
    if (typeof deger !== 'string' || deger === '') return null; // boşluğu `required` söyler
    return Number(deger) > 0
      ? null
      : { tutarPozitif: { mesaj: this.t('kiraListesi.tahsil.tutarPozitif') } };
  }
}
