import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map, of } from 'rxjs';

import { ApiIstemcisi, type ApiYolu } from '@core/api/api-istemcisi';
import type { TabloDuzeniVerisi, TabloDuzeniYaniti } from '@core/api/ui-tipleri';

import { TABLO_SINIRLARI, type TabloDuzeni } from './tablo-modeli';

/**
 * Kullanıcının tablo düzeni deposu. Sunucu tarafı `TabloDuzenleri` (kiracıya RLS'li, kullanıcıya
 * servis guard'lı): kullanıcı kimliği istekten DEĞİL oturumdan gelir, herkes yalnız kendi düzenini
 * okur/yazar. Düzen kişisel veri değildir (sütun kodu/genişlik/sıra) — yine de localStorage'a
 * yazılmaz: cihazlar arası tutarlı olsun ve çıkışta geride iz kalmasın.
 */
export abstract class TabloDuzeniDeposu {
  /** Kayıtlı düzen; yoksa `null`. */
  abstract oku(tabloKodu: string): Observable<TabloDuzeni | null>;
  abstract kaydet(tabloKodu: string, duzen: TabloDuzeni): Observable<void>;
  /** Varsayılana dön (kaydı siler). */
  abstract sifirla(tabloKodu: string): Observable<void>;
}

/**
 * `GET/PUT/DELETE /api/ui/v1/tablo-duzenleri/{tabloKodu}` — `ApiIstemcisi` üstünden (XSRF,
 * `withCredentials`, tipli `ApiHatasi`). PUT upsert'tür (son yazan kazanır), doğası gereği idempotent:
 * `Idempotency-Key` gerekmez.
 */
@Injectable()
export class HttpTabloDuzeniDeposu extends TabloDuzeniDeposu {
  private readonly api = inject(ApiIstemcisi);

  oku(tabloKodu: string): Observable<TabloDuzeni | null> {
    return this.api
      .get<TabloDuzeniYaniti>(adres(tabloKodu))
      .pipe(map((yanit) => (yanit.duzen === null ? null : istemciModeli(yanit.duzen))));
  }

  kaydet(tabloKodu: string, duzen: TabloDuzeni): Observable<void> {
    const govde: TabloDuzeniVerisi = {
      sutunlar: duzen.sutunlar.map((s) => ({ ...s })),
      siralama: duzen.siralama.map((s) => ({ ...s })),
    };
    return this.api.put<TabloDuzeniYaniti>(adres(tabloKodu), govde).pipe(map(() => undefined));
  }

  sifirla(tabloKodu: string): Observable<void> {
    return this.api.delete<unknown>(adres(tabloKodu)).pipe(map(() => undefined));
  }
}

/** Oturumsuz bağlam (birim test, HttpClient sağlanmamış sayfa): düzen yalnız bellekte yaşar. */
export class BellekTabloDuzeniDeposu extends TabloDuzeniDeposu {
  readonly kayitlar = new Map<string, TabloDuzeni>();

  oku(tabloKodu: string): Observable<TabloDuzeni | null> {
    return of(this.kayitlar.get(tabloKodu) ?? null);
  }

  kaydet(tabloKodu: string, duzen: TabloDuzeni): Observable<void> {
    this.kayitlar.set(tabloKodu, duzen);
    return of(undefined);
  }

  sifirla(tabloKodu: string): Observable<void> {
    this.kayitlar.delete(tabloKodu);
    return of(undefined);
  }
}

/**
 * Tablonun kullandığı depo: sağlanmışsa o; değilse HttpClient sağlanmışsa (uygulamada
 * `provideApiIstemcisi`) sunucu, sağlanmamışsa (birim test) bellek.
 */
export function tabloDuzeniDeposuCoz(): TabloDuzeniDeposu {
  const saglanan = inject(TabloDuzeniDeposu, { optional: true });
  if (saglanan !== null) return saglanan;
  return inject(HttpClient, { optional: true }) === null
    ? new BellekTabloDuzeniDeposu()
    : new HttpTabloDuzeniDeposu();
}

/** Sunucu `genislik`'i OpenAPI'de `number | string` (sayı metinden de okunur); istemcide sayı ya da null. */
function istemciModeli(veri: TabloDuzeniVerisi): TabloDuzeni {
  return {
    sutunlar: veri.sutunlar.map((s) => {
      const px = typeof s.genislik === 'string' ? Number(s.genislik) : s.genislik;
      return { kod: s.kod, gorunur: s.gorunur, genislik: Number.isFinite(px) ? px : null };
    }),
    siralama: veri.siralama.map((s) => ({ kod: s.kod, azalan: s.azalan })),
  };
}

function adres(tabloKodu: string): ApiYolu {
  if (!TABLO_SINIRLARI.tabloKoduDeseni.test(tabloKodu) || tabloKodu.length > 64) {
    throw new TypeError(`Geçersiz tablo kodu: "${tabloKodu}".`);
  }
  return `/api/ui/v1/tablo-duzenleri/${tabloKodu}`;
}
