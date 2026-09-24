import { ChangeDetectionStrategy, Component, computed } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { TemelKontrol, kontrolSaglayicilari } from '@shared/form/kontroller/temel-kontrol';

/** Haftanın günleri — numaralandırma .NET `DayOfWeek` ile AYNI (0 = Pazar); Blazor sırası Pzt…Paz. */
export const WEEKDAYS = [1, 2, 3, 4, 5, 6, 0] as const;

/** "1,3,0" ↔ gün kümesi. Boş/null = kısıt yok (tüm günler). Sıra DayOfWeek artan (sunucu sırayı önemsemez). */
export function parseWeekdays(v: string | null): ReadonlySet<number> {
  if (!v) return new Set();
  return new Set(
    v
      .split(',')
      .map((x) => Number(x.trim()))
      .filter((x) => Number.isInteger(x) && x >= 0 && x <= 6),
  );
}

export function formatWeekdays(days: ReadonlySet<number>): string | null {
  if (days.size === 0) return null;
  return [...days].sort((a, b) => a - b).join(',');
}

/**
 * Kira kuralının geçerli gün kısıtı (`haftaGunKisiti`, Blazor "Geçerli günler" onay kutuları). `rc-alan grup`
 * içinde kullanılır; değer "1,2,3" metni ya da `null`.
 */
@Component({
  selector: 'rc-weekday-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe],
  providers: kontrolSaglayicilari(() => WeekdayPicker),
  template: `
    <div
      class="rc-secenek-grubu"
      role="group"
      [attr.aria-labelledby]="alan?.etiketKimligi ?? null"
      [attr.aria-describedby]="ariaAciklayan()"
    >
      @for (d of days; track d) {
        <label class="rc-secenek">
          <input
            type="checkbox"
            [attr.id]="$first ? ogeKimligi() : null"
            [checked]="selected().has(d)"
            [disabled]="pasif()"
            (change)="toggle(d)"
            (blur)="dokun()"
          />
          <span>{{ 'fiyatTarife.gunler.' + d | transloco }}</span>
        </label>
      }
    </div>
  `,
})
export class WeekdayPicker extends TemelKontrol<string> {
  protected readonly days = WEEKDAYS;
  protected readonly selected = computed(() => parseWeekdays(this.deger()));

  protected toggle(day: number): void {
    const next = new Set(this.selected());
    if (next.has(day)) next.delete(day);
    else next.add(day);
    this.bildir(formatWeekdays(next));
  }
}
