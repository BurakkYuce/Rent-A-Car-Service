import { HttpErrorResponse } from '@angular/common/http';
import { DOCUMENT } from '@angular/common';
import { ErrorHandler, inject, Injectable, Injector } from '@angular/core';

import { ApiHatasi } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { IstemciHataIstegi } from '@core/api/ui-tipleri';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { ParcaHatasiServisi, parcaYuklemeHatasiMi } from '@core/surum/parca-hatasi';
import { SurumServisi } from '@core/surum/surum-servisi';

/** `POST /api/ui/v1/istemci-hata` gövdesi (sunucu sınırları: gövde ≤ 4 KB, alanlar kırpılır). */
export type IstemciHataRaporu = IstemciHataIstegi;

export const RAPOR_SINIRLARI = { mesaj: 500, yigin: 2500, url: 300, surum: 64 } as const;
/** Sayfa ömrü boyunca en çok bu kadar rapor (hata döngüsü sunucuyu doldurmasın). */
export const EN_FAZLA_RAPOR = 10;

function kirp(metin: string, sinir: number): string {
  return metin.length > sinir ? metin.slice(0, sinir) : metin;
}

/**
 * Yakalanmamış istemci hatasını sunucuya raporlar (backend WARNING loglar; firma/kullanıcıyla).
 * Yalnız oturum açıkken (uç kimlik ister), sayfa başına {@link EN_FAZLA_RAPOR}, aynı mesaj bir kez.
 * URL'den yalnız YOL gider — sorgu dizesi (arama metni, müşteri adı) KVKK gereği gönderilmez.
 * HTTP hataları raporlanmaz (sunucu zaten loglar).
 */
@Injectable({ providedIn: 'root' })
export class IstemciHataRaporlayici {
  private readonly api = inject(ApiIstemcisi);
  private readonly oturum = inject(OturumServisi);
  private readonly surum = inject(SurumServisi);
  private readonly konum = inject(DOCUMENT).location;
  private readonly gorulen = new Set<string>();

  raporla(hata: unknown): boolean {
    if (hata instanceof HttpErrorResponse || hata instanceof ApiHatasi) return false;
    if (!this.oturum.girisYapildi() || this.gorulen.size >= EN_FAZLA_RAPOR) return false;

    const mesaj = kirp(
      (hata instanceof Error ? `${hata.name}: ${hata.message}` : String(hata)) || 'Bilinmeyen hata',
      RAPOR_SINIRLARI.mesaj,
    );
    if (this.gorulen.has(mesaj)) return false;
    this.gorulen.add(mesaj);

    const yigin =
      hata instanceof Error && hata.stack ? kirp(hata.stack, RAPOR_SINIRLARI.yigin) : undefined;
    const rapor: IstemciHataRaporu = {
      mesaj,
      yigin: yigin ?? null,
      url: kirp(this.konum.pathname, RAPOR_SINIRLARI.url),
      surum: kirp(this.surum.mevcut ?? 'gelistirme', RAPOR_SINIRLARI.surum),
    };
    this.api
      .post<unknown>('/api/ui/v1/istemci-hata', rapor, {
        context: istekBaglami({ sessiz: true, yenidenGirisYok: true }),
      })
      .subscribe({ error: () => undefined });
    return true;
  }
}

/**
 * Uygulama `ErrorHandler`'ı: konsola yazar; tembel parça yüklenemediyse kontrollü yenileme, değilse
 * sunucuya rapor. Servisler ilk hatada çözülür (ErrorHandler açılışta çok erken kurulur).
 */
@Injectable()
export class RcHataIsleyici implements ErrorHandler {
  private readonly enjektor = inject(Injector);

  handleError(hata: unknown): void {
    console.error(hata);
    try {
      if (parcaYuklemeHatasiMi(hata)) {
        this.enjektor.get(ParcaHatasiServisi).isle();
        return;
      }
      this.enjektor.get(IstemciHataRaporlayici).raporla(hata);
    } catch (ic: unknown) {
      console.error(ic);
    }
  }
}
