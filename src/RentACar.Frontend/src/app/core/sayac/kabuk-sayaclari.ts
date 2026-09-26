import { computed, inject, Injectable, signal } from '@angular/core';

import { OTURUM_BAGLAMI } from '@core/oturum/oturum-baglami';

/** Kira kayıtlı görünümlerinin sayaçları (Yol v2 §5.1, §9 alt kümesi). */
export interface KabukSayaclariDegeri {
  readonly kirada: number;
  readonly geciken: number;
  readonly bugunCikan: number;
  readonly bugunDonecek: number;
}

/**
 * Kenar çubuğundaki kayıtlı görünüm sayaçları. Kaynak: veriyi ZATEN yükleyen ekran (bugün Panel —
 * `GET /api/ui/v1/panel/ozet` yanıtından); kabuk EK İSTEK ATMAZ. Adanmış sayaç ucu (`/api/ui/v1/ozet/sayaclar`,
 * §9) gelince bu servis onu okur; tüketiciler değişmez.
 *
 * Sayaç yoksa (`null`) rozet çizilmez — "0" ile "bilinmiyor" karışmaz. Değer yayımlandığı oturum bağlamına
 * (kiracı|kullanıcı|şube) bağlıdır: bağlam değişince okunmaz, başka oturumun sayısı görünmez.
 */
@Injectable({ providedIn: 'root' })
export class KabukSayaclari {
  private readonly baglam = inject(OTURUM_BAGLAMI);
  private readonly kayit = signal<{
    readonly anahtar: string | null;
    readonly deger: KabukSayaclariDegeri;
  } | null>(null);

  /** Geçerli oturum bağlamında yayımlanmış son sayaçlar; yoksa `null`. */
  readonly sayaclar = computed<KabukSayaclariDegeri | null>(() => {
    const k = this.kayit();
    return k && k.anahtar === this.anahtar() ? k.deger : null;
  });

  yayinla(deger: KabukSayaclariDegeri): void {
    this.kayit.set({ anahtar: this.anahtar(), deger });
  }

  private anahtar(): string | null {
    return this.baglam()?.anahtar ?? null;
  }
}
