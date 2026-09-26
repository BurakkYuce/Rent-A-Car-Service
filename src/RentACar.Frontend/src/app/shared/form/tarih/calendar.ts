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
  type DayText,
  monthStart,
  monthTitle,
  monthGrid,
  bugun,
  addDays,
  compareDays,
  dayLongName,
  weekStart,
  weekdayNames,
} from '@core/form/tarih-girdisi';
import { Icon } from '../../ikon/icon';

interface Hucre {
  readonly gun: DayText;
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
  imports: [TranslocoPipe, Icon],
  styleUrl: './calendar.scss',
  template: `
    <div class="baslik">
      <button
        type="button"
        class="rc-dugme rc-dugme--hayalet rc-dugme--ikon rc-dugme--kucuk"
        [attr.aria-label]="'form.tarih.oncekiAy' | transloco"
        (click)="switchMonth(-1)"
      >
        <rc-ikon ad="chevron-left" [boyut]="14" />
      </button>
      <span class="ay" aria-live="polite">{{ baslik() }}</span>
      <button
        type="button"
        class="rc-dugme rc-dugme--hayalet rc-dugme--ikon rc-dugme--kucuk"
        [attr.aria-label]="'form.tarih.sonrakiAy' | transloco"
        (click)="switchMonth(1)"
      >
        <rc-ikon ad="chevron-right" [boyut]="14" />
      </button>
    </div>
    <table role="grid" [attr.aria-label]="baslik()">
      <thead>
        <tr>
          @for (ad of dayNames; track $index) {
            <th scope="col" [attr.abbr]="ad">{{ ad }}</th>
          }
        </tr>
      </thead>
      <tbody>
        @for (hafta of weeks(); track $index) {
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
                  (click)="select(h.gun)"
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
export class Calendar {
  readonly secili = input<DayText | null>(null);
  readonly aralik = input<GunAraligi | null>(null);
  /** Aralık seçiminde ilk tıklanan gün (bitiş bekleniyor). */
  readonly bekleyen = input<DayText | null>(null);
  readonly enAz = input<DayText | null>(null);
  readonly enCok = input<DayText | null>(null);
  readonly gunSecildi = output<DayText>();

  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly todayDay = bugun();
  protected readonly dayNames = weekdayNames();

  /** Klavye odağındaki gün; seçim değişince ona döner. */
  protected readonly odak = linkedSignal<DayText>(
    () => this.bekleyen() ?? this.secili() ?? this.aralik()?.baslangic ?? this.todayDay,
  );
  private readonly visibleMonth = computed(() => monthStart(this.odak()));
  protected readonly baslik = computed(() => monthTitle(this.visibleMonth()));

  protected readonly weeks = computed((): Hucre[][] => {
    const selected = this.secili();
    const pending = this.bekleyen();
    const range = this.aralik();
    const enAz = this.enAz();
    const enCok = this.enCok();
    const cells = monthGrid(this.visibleMonth()).map(({ gun: day, ayDisi: outsideMonth }) => ({
      gun: day,
      ayDisi: outsideMonth,
      sayi: Number(day.slice(8)),
      secili:
        day === selected ||
        day === pending ||
        (!pending && !!range && (day === range.baslangic || day === range.bitis)),
      aralikta:
        !pending &&
        !!range &&
        compareDays(day, range.baslangic) > 0 &&
        compareDays(day, range.bitis) < 0,
      bugun: day === this.todayDay,
      pasif:
        (enAz !== null && compareDays(day, enAz) < 0) ||
        (enCok !== null && compareDays(day, enCok) > 0),
      etiket: dayLongName(day),
    }));
    return Array.from({ length: 6 }, (_, i) => cells.slice(i * 7, i * 7 + 7));
  });

  /** Açılışta ızgaraya odaklanmak için (diyalog açan bileşen çağırır). */
  focusOn(): void {
    this.focusDay(this.odak());
  }

  protected switchMonth(count: number): void {
    this.odak.set(monthStart(this.odak(), count));
  }

  protected select(day: DayText): void {
    this.odak.set(day);
    this.gunSecildi.emit(day);
  }

  protected tus(evt: KeyboardEvent): void {
    const focus = this.odak();
    const target = ((): DayText | null => {
      switch (evt.key) {
        case 'ArrowLeft':
          return addDays(focus, -1);
        case 'ArrowRight':
          return addDays(focus, 1);
        case 'ArrowUp':
          return addDays(focus, -7);
        case 'ArrowDown':
          return addDays(focus, 7);
        case 'Home':
          return weekStart(focus);
        case 'End':
          return addDays(weekStart(focus), 6);
        case 'PageUp':
          return shiftMonth(focus, -1);
        case 'PageDown':
          return shiftMonth(focus, 1);
        default:
          return null;
      }
    })();
    if (target === null) return;
    evt.preventDefault();
    this.focusDay(target);
  }

  private focusDay(day: DayText): void {
    this.odak.set(day);
    afterNextRender(
      () =>
        this.element.nativeElement.querySelector<HTMLButtonElement>(`[data-gun="${day}"]`)?.focus(),
      { injector: this.injector },
    );
  }
}

/** Aynı gün numarası, komşu ay (31 Ocak → 28/29 Şubat). */
function shiftMonth(day: DayText, count: number): DayText {
  const targetMonth = monthStart(day, count);
  const lastDay = addDays(monthStart(targetMonth, 1), -1);
  const candidate = `${targetMonth.slice(0, 8)}${day.slice(8)}`;
  return compareDays(candidate, lastDay) > 0 || !/^\d{4}-\d{2}-\d{2}$/.test(candidate)
    ? lastDay
    : candidate;
}
