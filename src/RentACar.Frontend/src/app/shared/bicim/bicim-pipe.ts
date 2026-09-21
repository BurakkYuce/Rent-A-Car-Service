import { Pipe, PipeTransform } from '@angular/core';
import {
  paraBicimle,
  sayiBicimle,
  tarihBicimle,
  tarihSaatBicimle,
  type TarihGirdisi,
} from '@core/bicim/bicim';

/** `{{ tutar | para }}` → `1.234,56 ₺`; `{{ tutar | para: 'USD' }}` → `1.234,56 $`. */
@Pipe({ name: 'para' })
export class ParaPipe implements PipeTransform {
  transform(tutar: number | null | undefined, paraBirimi = 'TRY'): string {
    return paraBicimle(tutar, paraBirimi);
  }
}

/** `{{ adet | sayi }}` → `1.234,5`. */
@Pipe({ name: 'sayi' })
export class SayiPipe implements PipeTransform {
  transform(deger: number | null | undefined, haneler = '1.0-2'): string {
    return sayiBicimle(deger, haneler);
  }
}

/** `{{ tarih | tarih }}` → `26.08.2026`. */
@Pipe({ name: 'tarih' })
export class TarihPipe implements PipeTransform {
  transform(deger: TarihGirdisi): string {
    return tarihBicimle(deger);
  }
}

/** `{{ an | tarihSaat }}` → `26.08.2026 14:05` (İstanbul). */
@Pipe({ name: 'tarihSaat' })
export class TarihSaatPipe implements PipeTransform {
  transform(deger: TarihGirdisi): string {
    return tarihSaatBicimle(deger);
  }
}

export const BICIM_PIPELARI = [ParaPipe, SayiPipe, TarihPipe, TarihSaatPipe] as const;
