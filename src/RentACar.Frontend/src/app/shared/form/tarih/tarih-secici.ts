import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  booleanAttribute,
  computed,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { CdkConnectedOverlay, CdkOverlayOrigin } from '@angular/cdk/overlay';
import { TranslocoPipe } from '@jsverse/transloco';
import type { ValidationErrors } from '@angular/forms';
import {
  type GunAraligi,
  type GunMetni,
  type HazirAralik,
  aralikBicimle,
  aralikCoz,
  bugun,
  gunBicimle,
  gunCoz,
  gunKiyasla,
  gunNormalize,
  hazirAraliklar,
} from '@core/form/tarih-girdisi';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { Ikon } from '../../ikon/ikon';
import { AyristiranKontrol, kontrolSaglayicilari } from '../kontroller/temel-kontrol';
import { Takvim } from './takvim';

/**
 * Tarih seçici (Revlo date-picker'dan uyarlandı: zoneless, tr, `gg.aa.yyyy`). Değer TAKVİM GÜNÜ
 * metni `"2026-09-22"` — `Date`/UTC dönüşümü yok, kaydet-yeniden aç döngüsünde kayma olmaz.
 * `aralik` modunda değer `{ baslangic, bitis }`, takvimde iki tıklama ya da hazır aralık.
 * Yazılan metin katı ayrıştırılır; anlaşılmazsa `tarihGecersiz`, sınır dışıysa `tarihAralikDisi`.
 */
@Component({
  selector: 'rc-tarih-secici',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CdkConnectedOverlay, CdkOverlayOrigin, TranslocoPipe, Ikon, Takvim],
  providers: kontrolSaglayicilari(() => TarihSecici, { dogrulayici: true }),
  styleUrl: './tarih-secici.scss',
  template: `
    <div class="rc-girdi-kutusu" cdkOverlayOrigin #koken="cdkOverlayOrigin">
      <input
        class="rc-girdi"
        type="text"
        inputmode="numeric"
        autocomplete="off"
        [id]="ogeKimligi()"
        [value]="metin()"
        [disabled]="pasif()"
        [attr.placeholder]="(aralik() ? 'form.tarih.aralikBicim' : 'form.tarih.bicim') | transloco"
        [attr.aria-label]="ariaEtiketi() ?? null"
        [attr.aria-invalid]="ariaGecersiz()"
        [attr.aria-describedby]="ariaAciklayan()"
        [attr.aria-required]="ariaZorunlu()"
        (input)="yazildi($event)"
        (blur)="birakildi()"
      />
      <button
        #dugme
        type="button"
        class="rc-girdi-eki"
        aria-haspopup="dialog"
        [attr.aria-expanded]="acik()"
        [attr.aria-label]="'form.tarih.takvimiAc' | transloco"
        [disabled]="pasif()"
        (click)="acKapa()"
      >
        <rc-ikon ad="calendar" [boyut]="14" />
      </button>
    </div>
    <ng-template
      cdkConnectedOverlay
      [cdkConnectedOverlayOrigin]="koken"
      [cdkConnectedOverlayOpen]="acik()"
      (overlayOutsideClick)="disTiklama($event)"
      (overlayKeydown)="panelTusu($event)"
      (attach)="takvimeOdaklan()"
      (detach)="acik.set(false)"
    >
      <div
        class="rc-acilir-panel panel"
        role="dialog"
        [attr.aria-label]="'form.tarih.takvim' | transloco"
      >
        @if (aralik() && hazirlar()) {
          <ul class="hazir" [attr.aria-label]="'form.tarih.hazirBaslik' | transloco">
            @for (h of hazirListe; track h.kimlik) {
              <li>
                <button
                  type="button"
                  class="rc-dugme rc-dugme--hayalet rc-dugme--kucuk"
                  (click)="hazirSec(h)"
                >
                  {{ hazirEtiketi(h) | transloco }}
                </button>
              </li>
            }
          </ul>
        }
        <rc-takvim
          [secili]="aralik() ? null : tekDeger()"
          [aralik]="aralikDegeri()"
          [bekleyen]="bekleyen()"
          [enAz]="enAz()"
          [enCok]="enCok()"
          (gunSecildi)="takvimdenSecildi($event)"
        />
      </div>
    </ng-template>
  `,
})
export class TarihSecici extends AyristiranKontrol<GunMetni | GunAraligi> {
  readonly aralik = input(false, { transform: booleanAttribute });
  readonly hazirlar = input(true, { transform: booleanAttribute });
  readonly enAz = input<GunMetni | null>(null);
  readonly enCok = input<GunMetni | null>(null);

  private readonly dugme = viewChild.required<ElementRef<HTMLButtonElement>>('dugme');
  private readonly takvim = viewChild(Takvim);

  protected readonly metin = signal('');
  protected readonly acik = signal(false);
  protected readonly bekleyen = signal<GunMetni | null>(null);
  protected readonly hazirListe = hazirAraliklar(bugun());

  protected readonly tekDeger = computed(() => {
    const d = this.deger();
    return typeof d === 'string' ? d : null;
  });
  protected readonly aralikDegeri = computed(() => {
    const d = this.deger();
    return d !== null && typeof d === 'object' ? d : null;
  });

  protected override disaridanYazildi(deger: GunMetni | GunAraligi | null): void {
    const normal = this.normalize(deger);
    this.deger.set(normal);
    this.metin.set(this.bicimle(normal));
    this.hataAyarla(null);
  }

  protected yazildi(olay: Event): void {
    const yazilan = (olay.target as HTMLInputElement).value;
    this.metin.set(yazilan);
    const cozum = this.aralik() ? aralikCoz(yazilan) : gunCoz(yazilan);
    if (cozum === 'gecersiz') {
      this.hataAyarla({ tarihGecersiz: true });
      this.bildir(null);
      return;
    }
    this.hataAyarla(this.sinirHatasi(cozum));
    this.bildir(cozum);
  }

  protected birakildi(): void {
    if (this.ayristirmaHatasi() === null) this.metin.set(this.bicimle(this.deger()));
    this.dokun();
  }

  protected acKapa(): void {
    if (this.acik()) {
      this.kapat();
    } else {
      this.bekleyen.set(null);
      this.acik.set(true);
    }
  }

  protected takvimeOdaklan(): void {
    // Overlay bağlandıktan sonra ızgara çizilmiş olur.
    this.takvim()?.odaklan();
  }

  protected takvimdenSecildi(gun: GunMetni): void {
    if (!this.aralik()) {
      this.degerSec(gun);
      return;
    }
    const ilk = this.bekleyen();
    if (ilk === null) {
      this.bekleyen.set(gun);
      return;
    }
    const [baslangic, bitis] = gunKiyasla(gun, ilk) < 0 ? [gun, ilk] : [ilk, gun];
    this.degerSec({ baslangic, bitis });
  }

  protected hazirSec(h: HazirAralik): void {
    this.degerSec(h.aralik);
  }

  protected hazirEtiketi(h: HazirAralik): CeviriAnahtari {
    return `form.tarih.hazir.${h.kimlik}`;
  }

  protected disTiklama(olay: MouseEvent): void {
    if (!this.dugme().nativeElement.contains(olay.target as Node)) this.kapat();
  }

  protected panelTusu(olay: KeyboardEvent): void {
    if (olay.key === 'Escape') {
      olay.preventDefault();
      this.kapat();
    }
  }

  private degerSec(deger: GunMetni | GunAraligi): void {
    this.metin.set(this.bicimle(deger));
    this.hataAyarla(this.sinirHatasi(deger));
    this.bildir(deger);
    this.dokun();
    this.kapat();
  }

  private kapat(): void {
    this.acik.set(false);
    this.bekleyen.set(null);
    this.dugme().nativeElement.focus();
  }

  private sinirHatasi(deger: GunMetni | GunAraligi | null): ValidationErrors | null {
    if (deger === null) return null;
    const [bas, bit] = typeof deger === 'string' ? [deger, deger] : [deger.baslangic, deger.bitis];
    if (gunKiyasla(bit, bas) < 0) return { tarihSirasi: true };
    const enAz = this.enAz();
    const enCok = this.enCok();
    if ((enAz && gunKiyasla(bas, enAz) < 0) || (enCok && gunKiyasla(bit, enCok) > 0)) {
      return { tarihAralikDisi: true };
    }
    return null;
  }

  private normalize(deger: unknown): GunMetni | GunAraligi | null {
    if (this.aralik()) {
      if (typeof deger !== 'object' || deger === null) return null;
      const d = deger as Partial<Record<'baslangic' | 'bitis', unknown>>;
      const baslangic = gunNormalize(d.baslangic);
      const bitis = gunNormalize(d.bitis);
      return baslangic && bitis ? { baslangic, bitis } : null;
    }
    return gunNormalize(deger);
  }

  private bicimle(deger: GunMetni | GunAraligi | null): string {
    if (deger === null) return '';
    return typeof deger === 'string' ? gunBicimle(deger) : aralikBicimle(deger);
  }
}
