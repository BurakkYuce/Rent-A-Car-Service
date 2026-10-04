import { Pipe, PipeTransform } from '@angular/core';
import {
  formatMoney,
  sayiBicimle,
  tarihBicimle,
  formatDateTime,
  type DateInput,
} from '@core/bicim/bicim';
import { enumLabel } from '@core/bicim/enum-label';

/** `{{ tutar | para }}` → `1.234,56 ₺`; `{{ tutar | para: 'USD' }}` → `1.234,56 $`. */
@Pipe({ name: 'para' })
export class MoneyPipe implements PipeTransform {
  transform(amount: number | null | undefined, currency = 'TRY'): string {
    return formatMoney(amount, currency);
  }
}

/** `{{ adet | sayi }}` → `1.234,5`. */
@Pipe({ name: 'sayi' })
export class NumberPipe implements PipeTransform {
  transform(value: number | null | undefined, digits = '1.0-2'): string {
    return sayiBicimle(value, digits);
  }
}

/** `{{ tarih | tarih }}` → `26.08.2026`. */
@Pipe({ name: 'tarih' })
export class DatePipe implements PipeTransform {
  transform(value: DateInput): string {
    return tarihBicimle(value);
  }
}

/** `{{ an | tarihSaat }}` → `26.08.2026 14:05` (İstanbul). */
@Pipe({ name: 'tarihSaat' })
export class DateTimePipe implements PipeTransform {
  transform(value: DateInput): string {
    return formatDateTime(value);
  }
}

/** `{{ kod | etiket }}` → sunucu kodunun Türkçe etiketi (`Planlandi` → `Planlandı`); bilinmeyen kod olduğu gibi. */
@Pipe({ name: 'etiket' })
export class EnumLabelPipe implements PipeTransform {
  transform(code: string | null | undefined): string {
    return enumLabel(code);
  }
}

export const FORMAT_PIPES = [MoneyPipe, NumberPipe, DatePipe, DateTimePipe, EnumLabelPipe] as const;
