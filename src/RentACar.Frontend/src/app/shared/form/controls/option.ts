/** Seçim/radyo seçeneği. `deger` herhangi bir tip olabilir (enum, sayı, kimlik). */
export interface SecenekOgesi<T> {
  readonly deger: T;
  readonly etiket: string;
  readonly pasif?: boolean;
}

export type Comparer<T> = (a: T | null, b: T | null) => boolean;

export const sameValue: Comparer<unknown> = (a, b) => Object.is(a, b);
