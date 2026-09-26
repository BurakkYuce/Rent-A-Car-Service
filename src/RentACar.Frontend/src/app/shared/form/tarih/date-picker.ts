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
  type DayText,
  type HazirAralik,
  formatRange,
  parseRange,
  bugun,
  formatDay,
  parseDay,
  compareDays,
  normalizeDay,
  presetRanges,
} from '@core/form/tarih-girdisi';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { Icon } from '../../ikon/icon';
import { ParsingControl, controlProviders } from '../kontroller/base-control';
import { Calendar } from './calendar';

/**
 * Tarih seçici (Revlo date-picker'dan uyarlandı: zoneless, tr, `gg.aa.yyyy`). Değer TAKVİM GÜNÜ
 * metni `"2026-09-22"` — `Date`/UTC dönüşümü yok, kaydet-yeniden aç döngüsünde kayma olmaz.
 * `aralik` modunda değer `{ baslangic, bitis }`, takvimde iki tıklama ya da hazır aralık.
 * Yazılan metin katı ayrıştırılır; anlaşılmazsa `tarihGecersiz`, sınır dışıysa `tarihAralikDisi`.
 */
@Component({
  selector: 'rc-tarih-secici',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CdkConnectedOverlay, CdkOverlayOrigin, TranslocoPipe, Icon, Calendar],
  providers: controlProviders(() => DatePicker, { dogrulayici: true }),
  styleUrl: './date-picker.scss',
  template: `
    <div class="rc-girdi-kutusu" cdkOverlayOrigin #koken="cdkOverlayOrigin">
      <input
        class="rc-girdi"
        type="text"
        inputmode="numeric"
        autocomplete="off"
        [id]="itemId()"
        [value]="metin()"
        [disabled]="pasif()"
        [attr.placeholder]="(aralik() ? 'form.tarih.aralikBicim' : 'form.tarih.bicim') | transloco"
        [attr.aria-label]="ariaEtiketi() ?? null"
        [attr.aria-invalid]="ariaInvalid()"
        [attr.aria-describedby]="ariaDescribedBy()"
        [attr.aria-required]="ariaRequired()"
        (input)="written($event)"
        (blur)="released()"
      />
      <button
        #dugme
        type="button"
        class="rc-girdi-eki"
        aria-haspopup="dialog"
        [attr.aria-expanded]="acik()"
        [attr.aria-label]="'form.tarih.takvimiAc' | transloco"
        [disabled]="pasif()"
        (click)="toggle()"
      >
        <rc-ikon ad="calendar" [boyut]="14" />
      </button>
    </div>
    <ng-template
      cdkConnectedOverlay
      [cdkConnectedOverlayOrigin]="koken"
      [cdkConnectedOverlayOpen]="acik()"
      (overlayOutsideClick)="outsideClick($event)"
      (overlayKeydown)="panelKeydown($event)"
      (attach)="focusCalendar()"
      (detach)="acik.set(false)"
    >
      <div
        class="rc-acilir-panel panel"
        role="dialog"
        [attr.aria-label]="'form.tarih.takvim' | transloco"
      >
        @if (aralik() && hazirlar()) {
          <ul class="hazir" [attr.aria-label]="'form.tarih.hazirBaslik' | transloco">
            @for (h of presetList; track h.kimlik) {
              <li>
                <button
                  type="button"
                  class="rc-dugme rc-dugme--hayalet rc-dugme--kucuk"
                  (click)="selectPreset(h)"
                >
                  {{ presetLabel(h) | transloco }}
                </button>
              </li>
            }
          </ul>
        }
        <rc-takvim
          [secili]="aralik() ? null : singleValue()"
          [aralik]="rangeValue()"
          [bekleyen]="pending()"
          [enAz]="enAz()"
          [enCok]="enCok()"
          (gunSecildi)="selectedFromCalendar($event)"
        />
      </div>
    </ng-template>
  `,
})
export class DatePicker extends ParsingControl<DayText | GunAraligi> {
  readonly aralik = input(false, { transform: booleanAttribute });
  readonly hazirlar = input(true, { transform: booleanAttribute });
  readonly enAz = input<DayText | null>(null);
  readonly enCok = input<DayText | null>(null);

  private readonly dugme = viewChild.required<ElementRef<HTMLButtonElement>>('dugme');
  private readonly takvim = viewChild(Calendar);

  protected readonly metin = signal('');
  protected readonly acik = signal(false);
  protected readonly pending = signal<DayText | null>(null);
  protected readonly presetList = presetRanges(bugun());

  protected readonly singleValue = computed(() => {
    const d = this.deger();
    return typeof d === 'string' ? d : null;
  });
  protected readonly rangeValue = computed(() => {
    const d = this.deger();
    return d !== null && typeof d === 'object' ? d : null;
  });

  protected override writtenExternally(value: DayText | GunAraligi | null): void {
    const normal = this.normalize(value);
    this.deger.set(normal);
    this.metin.set(this.bicimle(normal));
    this.setError(null);
  }

  protected written(evt: Event): void {
    const written = (evt.target as HTMLInputElement).value;
    this.metin.set(written);
    const resolution = this.aralik() ? parseRange(written) : parseDay(written);
    if (resolution === 'gecersiz') {
      this.setError({ tarihGecersiz: true });
      this.notify(null);
      return;
    }
    this.setError(this.limitError(resolution));
    this.notify(resolution);
  }

  protected released(): void {
    if (this.parseError() === null) this.metin.set(this.bicimle(this.deger()));
    this.touch();
  }

  protected toggle(): void {
    if (this.acik()) {
      this.close();
    } else {
      this.pending.set(null);
      this.acik.set(true);
    }
  }

  protected focusCalendar(): void {
    // Overlay bağlandıktan sonra ızgara çizilmiş olur.
    this.takvim()?.focusOn();
  }

  protected selectedFromCalendar(day: DayText): void {
    if (!this.aralik()) {
      this.selectValue(day);
      return;
    }
    const first = this.pending();
    if (first === null) {
      this.pending.set(day);
      return;
    }
    const [start, end] = compareDays(day, first) < 0 ? [day, first] : [first, day];
    this.selectValue({ baslangic: start, bitis: end });
  }

  protected selectPreset(h: HazirAralik): void {
    this.selectValue(h.aralik);
  }

  protected presetLabel(h: HazirAralik): CeviriAnahtari {
    return `form.tarih.hazir.${h.kimlik}`;
  }

  protected outsideClick(evt: MouseEvent): void {
    if (!this.dugme().nativeElement.contains(evt.target as Node)) this.close();
  }

  protected panelKeydown(evt: KeyboardEvent): void {
    if (evt.key === 'Escape') {
      evt.preventDefault();
      this.close();
    }
  }

  private selectValue(value: DayText | GunAraligi): void {
    this.metin.set(this.bicimle(value));
    this.setError(this.limitError(value));
    this.notify(value);
    this.touch();
    this.close();
  }

  private close(): void {
    this.acik.set(false);
    this.pending.set(null);
    this.dugme().nativeElement.focus();
  }

  private limitError(value: DayText | GunAraligi | null): ValidationErrors | null {
    if (value === null) return null;
    const [start, bit] =
      typeof value === 'string' ? [value, value] : [value.baslangic, value.bitis];
    if (compareDays(bit, start) < 0) return { tarihSirasi: true };
    const enAz = this.enAz();
    const enCok = this.enCok();
    if ((enAz && compareDays(start, enAz) < 0) || (enCok && compareDays(bit, enCok) > 0)) {
      return { tarihAralikDisi: true };
    }
    return null;
  }

  private normalize(value: unknown): DayText | GunAraligi | null {
    if (this.aralik()) {
      if (typeof value !== 'object' || value === null) return null;
      const d = value as Partial<Record<'baslangic' | 'bitis', unknown>>;
      const start = normalizeDay(d.baslangic);
      const end = normalizeDay(d.bitis);
      return start && end ? { baslangic: start, bitis: end } : null;
    }
    return normalizeDay(value);
  }

  private bicimle(value: DayText | GunAraligi | null): string {
    if (value === null) return '';
    return typeof value === 'string' ? formatDay(value) : formatRange(value);
  }
}
