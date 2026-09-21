import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  linkedSignal,
  output,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  type GunAraligi,
  type GunMetni,
  ayBasi,
  ayBasligi,
  ayIzgarasi,
  bugun,
  gunEkle,
  gunKiyasla,
  gunUzunAdi,
  haftaBasi,
  haftaGunuAdlari,
} from '@core/form/tarih-girdisi';
import { Ikon } from '../../ikon/ikon';

interface Hucre {
  readonly gun: GunMetni;
  readonly sayi: number;
  readonly ayDisi: boolean;
  readonly secili: boolean;
  readonly aralikta: boolean;
  readonly bugun: boolean;
  readonly pasif: boolean;
  readonly etiket: string;
}

/**
 * Ay takvimi (Revlo date-picker ızgarasından; animasyon, işaretçi, hafta numarası, çoklu mod atıldı).
 * Izgara `role="grid"`, dolaşan tabindex: ←/→ gün, ↑/↓ hafta, PageUp/PageDown ay, Home/End hafta
 * başı/sonu, Enter/Boşluk seçer. Değerler takvim günü metni — saat dilimine girmez.
 */
@Component({
  selector: 'rc-takvim',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, Ikon],
  styleUrl: './takvim.scss',
  template: `
    <div class="baslik">
      <button
        type="button"
        class="rc-dugme rc-dugme--hayalet rc-dugme--ikon rc-dugme--kucuk"
        [attr.aria-label]="'form.tarih.oncekiAy' | transloco"
        (click)="ayGec(-1)"
      >
        <rc-ikon ad="chevron-left" [boyut]="14" />
      </button>
      <span class="ay" aria-live="polite">{{ baslik() }}</span>
      <button
        type="button"
        class="rc-dugme rc-dugme--hayalet rc-dugme--ikon rc-dugme--kucuk"
        [attr.aria-label]="'form.tarih.sonrakiAy' | transloco"
        (click)="ayGec(1)"
      >
        <rc-ikon ad="chevron-right" [boyut]="14" />
      </button>
    </div>
    <table role="grid" [attr.aria-label]="baslik()">
      <thead>
        <tr>
          @for (ad of gunAdlari; track $index) {
            <th scope="col" [attr.abbr]="ad">{{ ad }}</th>
          }
        </tr>
      </thead>
      <tbody>
        @for (hafta of haftalar(); track $index) {
          <tr>
            @for (h of hafta; track h.gun) {
              <td role="gridcell" [attr.aria-selected]="h.secili">
                <button
                  type="button"
                  class="gun"
                  [attr.data-gun]="h.gun"
                  [class.gun--disi]="h.ayDisi"
                  [class.gun--secili]="h.secili"
                  [class.gun--aralik]="h.aralikta"
                  [class.gun--bugun]="h.bugun"
                  [attr.aria-current]="h.bugun ? 'date' : null"
                  [attr.aria-label]="h.etiket"
                  [tabindex]="h.gun === odak() ? 0 : -1"
                  [disabled]="h.pasif"
                  (click)="sec(h.gun)"
                  (keydown)="tus($event)"
                >
                  {{ h.sayi }}
                </button>
              </td>
            }
          </tr>
        }
      </tbody>
    </table>
  `,
})
export class Takvim {
  readonly secili = input<GunMetni | null>(null);
  readonly aralik = input<GunAraligi | null>(null);
  /** Aralık seçiminde ilk tıklanan gün (bitiş bekleniyor). */
  readonly bekleyen = input<GunMetni | null>(null);
  readonly enAz = input<GunMetni | null>(null);
  readonly enCok = input<GunMetni | null>(null);
  readonly gunSecildi = output<GunMetni>();

  private readonly eleman = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly bugunGun = bugun();
  protected readonly gunAdlari = haftaGunuAdlari();

  /** Klavye odağındaki gün; seçim değişince ona döner. */
  protected readonly odak = linkedSignal<GunMetni>(
    () => this.bekleyen() ?? this.secili() ?? this.aralik()?.baslangic ?? this.bugunGun,
  );
  private readonly gorunenAy = computed(() => ayBasi(this.odak()));
  protected readonly baslik = computed(() => ayBasligi(this.gorunenAy()));

  protected readonly haftalar = computed((): Hucre[][] => {
    const secili = this.secili();
    const bekleyen = this.bekleyen();
    const aralik = this.aralik();
    const enAz = this.enAz();
    const enCok = this.enCok();
    const hucreler = ayIzgarasi(this.gorunenAy()).map(({ gun, ayDisi }) => ({
      gun,
      ayDisi,
      sayi: Number(gun.slice(8)),
      secili:
        gun === secili ||
        gun === bekleyen ||
        (!bekleyen && !!aralik && (gun === aralik.baslangic || gun === aralik.bitis)),
      aralikta:
        !bekleyen &&
        !!aralik &&
        gunKiyasla(gun, aralik.baslangic) > 0 &&
        gunKiyasla(gun, aralik.bitis) < 0,
      bugun: gun === this.bugunGun,
      pasif:
        (enAz !== null && gunKiyasla(gun, enAz) < 0) ||
        (enCok !== null && gunKiyasla(gun, enCok) > 0),
      etiket: gunUzunAdi(gun),
    }));
    return Array.from({ length: 6 }, (_, i) => hucreler.slice(i * 7, i * 7 + 7));
  });

  /** Açılışta ızgaraya odaklanmak için (diyalog açan bileşen çağırır). */
  odaklan(): void {
    this.gunOdakla(this.odak());
  }

  protected ayGec(adet: number): void {
    this.odak.set(ayBasi(this.odak(), adet));
  }

  protected sec(gun: GunMetni): void {
    this.odak.set(gun);
    this.gunSecildi.emit(gun);
  }

  protected tus(olay: KeyboardEvent): void {
    const odak = this.odak();
    const hedef = ((): GunMetni | null => {
      switch (olay.key) {
        case 'ArrowLeft':
          return gunEkle(odak, -1);
        case 'ArrowRight':
          return gunEkle(odak, 1);
        case 'ArrowUp':
          return gunEkle(odak, -7);
        case 'ArrowDown':
          return gunEkle(odak, 7);
        case 'Home':
          return haftaBasi(odak);
        case 'End':
          return gunEkle(haftaBasi(odak), 6);
        case 'PageUp':
          return ayKaydir(odak, -1);
        case 'PageDown':
          return ayKaydir(odak, 1);
        default:
          return null;
      }
    })();
    if (hedef === null) return;
    olay.preventDefault();
    this.gunOdakla(hedef);
  }

  private gunOdakla(gun: GunMetni): void {
    this.odak.set(gun);
    afterNextRender(
      () =>
        this.eleman.nativeElement.querySelector<HTMLButtonElement>(`[data-gun="${gun}"]`)?.focus(),
      { injector: this.injector },
    );
  }
}

/** Aynı gün numarası, komşu ay (31 Ocak → 28/29 Şubat). */
function ayKaydir(gun: GunMetni, adet: number): GunMetni {
  const hedefAy = ayBasi(gun, adet);
  const sonGun = gunEkle(ayBasi(hedefAy, 1), -1);
  const aday = `${hedefAy.slice(0, 8)}${gun.slice(8)}`;
  return gunKiyasla(aday, sonGun) > 0 || !/^\d{4}-\d{2}-\d{2}$/.test(aday) ? sonGun : aday;
}
