import type { Signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import type { AbstractControl } from '@angular/forms';
import {
  catchError,
  debounceTime,
  distinctUntilChanged,
  map,
  of,
  startWith,
  switchMap,
} from 'rxjs';

import type { SuggestionFetch } from './vehicle.store';

/** Yazarken sunucuya sorma gecikmesi (kira formu `oneriAramasi` ile aynı). */
export const SUGGESTION_DELAY_MS = 250;

/**
 * Seç-veya-yaz (datalist) önerileri: kontrolün metni değiştikçe gecikmeli `q` araması; aynı metin tekrar
 * sorulmaz, son istek kazanır, hata boş öneri (alan serbest metin olarak çalışmaya devam eder). İzin yoksa
 * (`enabled` false) istek atılmaz — 403 bandı çıkmasın. Enjeksiyon bağlamında çağrılır.
 */
export function suggestionList(
  control: AbstractControl,
  fetch: SuggestionFetch,
  enabled: () => boolean = () => true,
): Signal<readonly string[]> {
  return toSignal(
    control.valueChanges.pipe(
      startWith(control.value as unknown),
      map((v) => (typeof v === 'string' ? v.trim() : '')),
      debounceTime(SUGGESTION_DELAY_MS),
      distinctUntilChanged(),
      switchMap((q) => (enabled() ? fetch(q).pipe(catchError(() => of([]))) : of([]))),
    ),
    { initialValue: [] as readonly string[] },
  );
}
