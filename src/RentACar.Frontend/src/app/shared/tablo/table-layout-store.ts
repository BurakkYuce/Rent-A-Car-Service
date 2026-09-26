import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map, of } from 'rxjs';

import { ApiIstemcisi, type ApiPath } from '@core/api/api-istemcisi';
import type { TableLayoutData, TableLayoutResponse } from '@core/api/ui-tipleri';

import { TABLE_LIMITS, type TabloDuzeni } from './tablo-modeli';

/**
 * Kullanıcının tablo düzeni deposu. Sunucu tarafı `TabloDuzenleri` (kiracıya RLS'li, kullanıcıya
 * servis guard'lı): kullanıcı kimliği istekten DEĞİL oturumdan gelir, herkes yalnız kendi düzenini
 * okur/yazar. Düzen kişisel veri değildir (sütun kodu/genişlik/sıra) — yine de localStorage'a
 * yazılmaz: cihazlar arası tutarlı olsun ve çıkışta geride iz kalmasın.
 */
export abstract class TableLayoutStore {
  /** Kayıtlı düzen; yoksa `null`. */
  abstract read(tableCode: string): Observable<TabloDuzeni | null>;
  abstract kaydet(tableCode: string, layout: TabloDuzeni): Observable<void>;
  /** Varsayılana dön (kaydı siler). */
  abstract reset(tableCode: string): Observable<void>;
}

/**
 * `GET/PUT/DELETE /api/ui/v1/tablo-duzenleri/{tabloKodu}` — `ApiIstemcisi` üstünden (XSRF,
 * `withCredentials`, tipli `ApiHatasi`). PUT upsert'tür (son yazan kazanır), doğası gereği idempotent:
 * `Idempotency-Key` gerekmez.
 */
@Injectable()
export class HttpTableLayoutStore extends TableLayoutStore {
  private readonly api = inject(ApiIstemcisi);

  read(tableCode: string): Observable<TabloDuzeni | null> {
    return this.api
      .get<TableLayoutResponse>(adres(tableCode))
      .pipe(map((response) => (response.duzen === null ? null : clientModel(response.duzen))));
  }

  kaydet(tableCode: string, layout: TabloDuzeni): Observable<void> {
    const body: TableLayoutData = {
      sutunlar: layout.sutunlar.map((s) => ({ ...s })),
      siralama: layout.siralama.map((s) => ({ ...s })),
    };
    return this.api.put<TableLayoutResponse>(adres(tableCode), body).pipe(map(() => undefined));
  }

  reset(tableCode: string): Observable<void> {
    return this.api.delete<unknown>(adres(tableCode)).pipe(map(() => undefined));
  }
}

/** Oturumsuz bağlam (birim test, HttpClient sağlanmamış sayfa): düzen yalnız bellekte yaşar. */
export class InMemoryTableLayoutStore extends TableLayoutStore {
  readonly records = new Map<string, TabloDuzeni>();

  read(tableCode: string): Observable<TabloDuzeni | null> {
    return of(this.records.get(tableCode) ?? null);
  }

  kaydet(tableCode: string, layout: TabloDuzeni): Observable<void> {
    this.records.set(tableCode, layout);
    return of(undefined);
  }

  reset(tableCode: string): Observable<void> {
    this.records.delete(tableCode);
    return of(undefined);
  }
}

/**
 * Tablonun kullandığı depo: sağlanmışsa o; değilse HttpClient sağlanmışsa (uygulamada
 * `provideApiIstemcisi`) sunucu, sağlanmamışsa (birim test) bellek.
 */
export function resolveTableLayoutStore(): TableLayoutStore {
  const provided = inject(TableLayoutStore, { optional: true });
  if (provided !== null) return provided;
  return inject(HttpClient, { optional: true }) === null
    ? new InMemoryTableLayoutStore()
    : new HttpTableLayoutStore();
}

/** Sunucu `genislik`'i OpenAPI'de `number | string` (sayı metinden de okunur); istemcide sayı ya da null. */
function clientModel(data: TableLayoutData): TabloDuzeni {
  return {
    sutunlar: data.sutunlar.map((s) => {
      const px = typeof s.genislik === 'string' ? Number(s.genislik) : s.genislik;
      return { kod: s.kod, gorunur: s.gorunur, genislik: Number.isFinite(px) ? px : null };
    }),
    siralama: data.siralama.map((s) => ({ kod: s.kod, azalan: s.azalan })),
  };
}

function adres(tableCode: string): ApiPath {
  if (!TABLE_LIMITS.tabloKoduDeseni.test(tableCode) || tableCode.length > 64) {
    throw new TypeError(`Geçersiz tablo kodu: "${tableCode}".`);
  }
  return `/api/ui/v1/tablo-duzenleri/${tableCode}`;
}
