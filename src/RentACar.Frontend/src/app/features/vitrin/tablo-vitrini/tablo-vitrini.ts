import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { FetchPolicy } from '@core/veri/fetch-policy';
import { listeSorgusuUrlSenkronu } from '@core/veri/liste-sorgusu-url';
import type { DisaAktarma } from '@shared/tablo/disa-aktarma';
import { Tablo } from '@shared/tablo/tablo';
import { TabloHucre } from '@shared/tablo/tablo-hucre';

import { ARAC_SUTUNLARI, SENARYOLAR, type AracSatiri, type Senaryo } from './arac-verisi';
import { ARAC_VITRIN_LISTESI, TabloVitriniStore } from './tablo-vitrini.store';

// Yol v2 §1.2 filo durum sözlüğü: kirada yeşil, boşta nötr, serviste sarı, rezerve lacivert.
const DURUM_ROZETI: Readonly<Record<string, string>> = {
  Müsait: 'rc-rozet--notr',
  Kirada: 'rc-rozet--basari',
  Serviste: 'rc-rozet--uyari',
  Rezerve: 'rc-rozet--vurgu',
};

/**
 * Tablo motoru vitrini (`/app/vitrin/tablo`): 49 sütun × 5.000 kayıt, sunucu sayfalama/sıralama
 * (F3.4 URL senkronu + `TemelStore` + `FetchPolicy`), kullanıcı düzeni ve dört durum. Özellik
 * ekranları için örnek kablolama budur.
 */
@Component({
  selector: 'rc-tablo-vitrini',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, Tablo, TabloHucre],
  providers: [FetchPolicy, TabloVitriniStore],
  templateUrl: './tablo-vitrini.html',
  styleUrl: './tablo-vitrini.scss',
})
export class TabloVitrini {
  protected readonly store = inject(TabloVitriniStore);
  protected readonly liste = listeSorgusuUrlSenkronu(ARAC_VITRIN_LISTESI);
  protected readonly sutunlar = ARAC_SUTUNLARI;
  protected readonly varsayilanSirala = ARAC_VITRIN_LISTESI.varsayilanSirala;
  protected readonly senaryolar = SENARYOLAR;
  protected readonly kimlik = (a: AracSatiri) => a.id;
  /** Vitrin: "bugünün işi" krem satır vurgusu (her 7. araç; gerçek ekranda bugün dönen/çıkan kira). */
  protected readonly satirSinifi = (a: AracSatiri) =>
    Number(a.id.slice(-5)) % 7 === 0 ? 'rc-satir-bugun' : null;
  protected readonly secim = signal<readonly string[]>([]);
  protected readonly acilan = signal<AracSatiri | null>(null);

  protected readonly senaryo = computed<Senaryo>(
    () => this.liste.sorgu().filtreler.senaryo ?? 'normal',
  );

  /** Dışa aktarma sunucu ucuyla: ekrandaki filtre + sıralama taşınır, sayfa taşınmaz. */
  protected readonly disaAktarma = computed<DisaAktarma>(() => ({
    yol: '/listeler/export/araclar',
    parametreler: this.liste.apiParametreleri(),
    bicimler: ['excel', 'csv', 'pdf'],
  }));

  constructor() {
    inject(FetchPolicy).baglan({
      parametre: this.liste.apiParametreleri,
      yukle: (p) => this.store.liste.yukle(p),
      sifirla: () => this.store.liste.sifirla(),
    });
  }

  protected senaryoSec(senaryo: Senaryo): void {
    void this.liste.degistir({
      filtreler: { senaryo: senaryo === 'normal' ? undefined : senaryo },
    });
  }

  protected rozet(durum: unknown): string {
    return `rc-rozet ${DURUM_ROZETI[String(durum)] ?? ''}`;
  }
}
