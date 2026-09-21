import { ChangeDetectionStrategy, Component, ElementRef, signal, viewChild } from '@angular/core';
import { CdkConnectedOverlay, CdkOverlayOrigin } from '@angular/cdk/overlay';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  type GunMetni,
  anBirlestir,
  anParcala,
  gunBicimle,
  gunCoz,
  saatCoz,
} from '@core/form/tarih-girdisi';
import { Ikon } from '../../ikon/ikon';
import { AyristiranKontrol, kontrolSaglayicilari } from '../kontroller/temel-kontrol';
import { Takvim } from './takvim';

/**
 * Tarih-saat seçici. Değer UTC ANI (`"2026-09-22T11:30:00.000Z"`); kullanıcı İstanbul saatiyle görür
 * ve yazar. Ön doldurma sunucunun anından (ofsetli ISO) yapılır, YEREL saate çevrilip geri UTC
 * sayılmaz — Blazor'daki "hayalet +1 gün" hatasının kaynağı buydu. Tarih ve saat ayrı kutularda;
 * ikisi de doluysa değer bildirilir, biri eksik/bozuksa `tarihGecersiz`/`saatGecersiz`.
 */
@Component({
  selector: 'rc-tarih-saat-secici',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CdkConnectedOverlay, CdkOverlayOrigin, TranslocoPipe, Ikon, Takvim],
  providers: kontrolSaglayicilari(() => TarihSaatSecici, { dogrulayici: true }),
  styles: `
    :host {
      display: flex;
      gap: var(--rc-bosluk-2);
    }
    .gun {
      flex: 1 1 8rem;
    }
    .saat {
      flex: 0 0 5rem;
      text-align: center;
      font-variant-numeric: tabular-nums;
    }
    .panel {
      margin-block: var(--rc-bosluk-1);
    }
  `,
  template: `
    <div class="rc-girdi-kutusu gun" cdkOverlayOrigin #koken="cdkOverlayOrigin">
      <input
        class="rc-girdi"
        type="text"
        inputmode="numeric"
        autocomplete="off"
        [id]="ogeKimligi()"
        [value]="gunMetni()"
        [disabled]="pasif()"
        [attr.placeholder]="'form.tarih.bicim' | transloco"
        [attr.aria-label]="ariaEtiketi() ?? null"
        [attr.aria-invalid]="ariaGecersiz()"
        [attr.aria-describedby]="ariaAciklayan()"
        [attr.aria-required]="ariaZorunlu()"
        (input)="gunYazildi($event)"
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
        (click)="acik.set(!acik())"
      >
        <rc-ikon ad="calendar" [boyut]="14" />
      </button>
    </div>
    <input
      class="rc-girdi saat"
      type="text"
      inputmode="numeric"
      autocomplete="off"
      placeholder="ss:dd"
      [id]="ogeKimligi() + '-saat'"
      [value]="saatMetni()"
      [disabled]="pasif()"
      [attr.aria-label]="'form.tarih.saat' | transloco"
      [attr.aria-invalid]="ariaGecersiz()"
      [attr.aria-describedby]="ariaAciklayan()"
      [attr.aria-required]="ariaZorunlu()"
      (input)="saatYazildi($event)"
      (blur)="birakildi()"
    />
    <ng-template
      cdkConnectedOverlay
      [cdkConnectedOverlayOrigin]="koken"
      [cdkConnectedOverlayOpen]="acik()"
      (overlayOutsideClick)="disTiklama($event)"
      (overlayKeydown)="panelTusu($event)"
      (attach)="takvim()?.odaklan()"
      (detach)="acik.set(false)"
    >
      <div
        class="rc-acilir-panel panel"
        role="dialog"
        [attr.aria-label]="'form.tarih.takvim' | transloco"
      >
        <rc-takvim [secili]="seciliGun()" (gunSecildi)="takvimdenSecildi($event)" />
      </div>
    </ng-template>
  `,
})
export class TarihSaatSecici extends AyristiranKontrol<string> {
  private readonly dugme = viewChild.required<ElementRef<HTMLButtonElement>>('dugme');
  protected readonly takvim = viewChild(Takvim);

  protected readonly gunMetni = signal('');
  protected readonly saatMetni = signal('');
  protected readonly seciliGun = signal<GunMetni | null>(null);
  protected readonly acik = signal(false);

  protected override disaridanYazildi(deger: string | null): void {
    const parca = anParcala(deger);
    this.gunMetni.set(parca ? gunBicimle(parca.gun) : '');
    this.saatMetni.set(parca?.saat ?? '');
    this.seciliGun.set(parca?.gun ?? null);
    this.hataAyarla(null);
  }

  protected gunYazildi(olay: Event): void {
    this.gunMetni.set((olay.target as HTMLInputElement).value);
    this.hesapla();
  }

  protected saatYazildi(olay: Event): void {
    this.saatMetni.set((olay.target as HTMLInputElement).value);
    this.hesapla();
  }

  protected birakildi(): void {
    if (this.ayristirmaHatasi() === null) {
      const parca = anParcala(this.deger());
      if (parca) {
        this.gunMetni.set(gunBicimle(parca.gun));
        this.saatMetni.set(parca.saat);
      }
    }
    this.dokun();
  }

  protected takvimdenSecildi(gun: GunMetni): void {
    this.gunMetni.set(gunBicimle(gun));
    this.acik.set(false);
    this.dugme().nativeElement.focus();
    this.hesapla();
  }

  protected disTiklama(olay: MouseEvent): void {
    if (!this.dugme().nativeElement.contains(olay.target as Node)) this.acik.set(false);
  }

  protected panelTusu(olay: KeyboardEvent): void {
    if (olay.key === 'Escape') {
      olay.preventDefault();
      this.acik.set(false);
      this.dugme().nativeElement.focus();
    }
  }

  private hesapla(): void {
    const gun = gunCoz(this.gunMetni());
    const saat = saatCoz(this.saatMetni());
    this.seciliGun.set(gun === 'gecersiz' ? null : gun);
    if (gun === null && saat === null) {
      this.hataAyarla(null);
      this.bildir(null);
    } else if (gun === null || gun === 'gecersiz') {
      this.hataAyarla({ tarihGecersiz: true });
      this.bildir(null);
    } else if (saat === null || saat === 'gecersiz') {
      this.hataAyarla({ saatGecersiz: true });
      this.bildir(null);
    } else {
      this.hataAyarla(null);
      this.bildir(anBirlestir(gun, saat));
    }
  }
}
