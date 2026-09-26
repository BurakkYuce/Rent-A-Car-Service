import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { FORMAT_PIPES } from '@shared/bicim/bicim-pipe';

import type { VadeKutusu, VadeSatiri } from './panel-modeli';

/**
 * Hatırlatmalar (Yol v2 §6 `rc-hatirlatma-listesi`): vade kademeleri matrisi (1 hafta / 30 gün kalan / tarihi
 * geçen; geçen > 0 hata rengi, 30 gün > 0 uyarı rengi) ve altında rozetli uyarı satırları (KM geçen bakım,
 * site talebi, görülmeyen rezervasyon). Veri panel özetinden; bu bileşen hesap yapmaz, yalnız toplam gecikmiş
 * sayısını başlığa yazar.
 */
@Component({
  selector: 'rc-hatirlatma-listesi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslocoPipe, ...FORMAT_PIPES],
  template: `
    <section class="kutu" aria-labelledby="panel-hatirlatmalar">
      <div class="baslik">
        <h2 id="panel-hatirlatmalar">{{ 'panel.hatirlatma.baslik' | transloco }}</h2>
        @if (gecikmis() > 0) {
          <span class="gecikmis">{{
            'panel.hatirlatma.gecikmis' | transloco: { sayi: gecikmis() }
          }}</span>
        }
      </div>
      <table class="matris">
        <caption class="rc-gorunmez">
          {{
            'panel.hatirlatma.matris' | transloco
          }}
        </caption>
        <thead>
          <tr>
            <th scope="col">
              <span class="rc-gorunmez">{{ 'panel.hatirlatma.tur' | transloco }}</span>
            </th>
            <th scope="col" class="rc-num dar">
              <abbr [title]="'panel.vade.yediGun' | transloco">{{
                'panel.hatirlatma.yediGun' | transloco
              }}</abbr>
            </th>
            <th scope="col" class="rc-num dar">
              <abbr [title]="'panel.vade.otuzGun' | transloco">{{
                'panel.hatirlatma.otuzGun' | transloco
              }}</abbr>
            </th>
            <th scope="col" class="rc-num dar">
              <abbr [title]="'panel.vade.gecmis' | transloco">{{
                'panel.hatirlatma.gecmis' | transloco
              }}</abbr>
            </th>
          </tr>
        </thead>
        <tbody>
          @for (s of satirlar(); track s.kod) {
            <tr>
              <th scope="row">
                <a [routerLink]="s.rota">{{ s.etiket }}</a>
              </th>
              <td class="rc-num dar">{{ s.yediGun | sayi: '1.0-0' }}</td>
              <td class="rc-num dar" [class.uyari]="s.otuzGun > 0">
                {{ s.otuzGun | sayi: '1.0-0' }}
              </td>
              <td class="rc-num dar" [class.hata]="s.gecmis > 0">{{ s.gecmis | sayi: '1.0-0' }}</td>
            </tr>
          }
        </tbody>
      </table>
      <ul class="rozetler">
        @for (r of rozetler(); track r.etiket) {
          <li>
            <a
              class="rozet-satiri"
              [routerLink]="r.rota"
              [queryParams]="r.sorgu ?? null"
              [attr.title]="r.ipucu ?? null"
            >
              <span class="rozet-satiri__etiket">{{ r.etiket }}</span>
              <span
                class="rc-rozet"
                [class.rc-rozet--hata]="r.ton === 'hata'"
                [class.rc-rozet--uyari]="r.ton === 'uyari'"
                >{{ r.sayi | sayi: '1.0-0' }}</span
              >
            </a>
          </li>
        }
      </ul>
    </section>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    .kutu {
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-2);
      padding: var(--rc-bosluk-3) var(--rc-bosluk-4);
      border: 1px solid var(--rc-kenar);
      border-radius: var(--rc-yaricap-lg);
      background-color: var(--rc-yuzey);
    }
    .baslik {
      display: flex;
      gap: var(--rc-bosluk-2);
      align-items: baseline;
      justify-content: space-between;
    }
    h2 {
      margin: 0;
      font-size: var(--rc-yazi-md);
      font-weight: var(--rc-agirlik-kalin);
    }
    .gecikmis,
    .hata {
      color: var(--rc-hata-metin);
      font-weight: var(--rc-agirlik-kalin);
    }
    .gecikmis {
      font-size: var(--rc-yazi-xs);
    }
    .uyari {
      color: var(--rc-uyari-metin);
      font-weight: var(--rc-agirlik-kalin);
    }
    .matris {
      width: 100%;
      border-collapse: collapse;
      font-size: var(--rc-yazi-sm);
      font-variant-numeric: tabular-nums;
    }
    .matris th,
    .matris td {
      height: 2.25rem;
      padding: 0 var(--rc-bosluk-1);
      border-bottom: 1px solid var(--rc-kenar);
      text-align: start;
      font-weight: var(--rc-agirlik-normal);
    }
    .matris thead th {
      height: auto;
      padding-bottom: var(--rc-bosluk-1);
      color: var(--rc-metin-soluk);
      font-size: var(--rc-yazi-2xs);
    }
    .matris .dar {
      width: 3rem;
    }
    abbr {
      text-decoration: none;
    }
    a {
      color: var(--rc-metin);
      text-decoration: none;
    }
    a:hover {
      text-decoration: underline;
    }
    .rozetler {
      display: flex;
      flex-direction: column;
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .rozetler li + li {
      border-top: 1px solid var(--rc-kenar);
    }
    .rozet-satiri {
      display: flex;
      gap: var(--rc-bosluk-2);
      align-items: center;
      justify-content: space-between;
      min-height: 2.5rem;
      font-size: var(--rc-yazi-sm);
    }
    .rozet-satiri__etiket {
      min-width: 0;
      overflow-wrap: anywhere;
    }
  `,
})
export class ReminderList {
  readonly satirlar = input.required<readonly VadeSatiri[]>();
  readonly rozetler = input.required<readonly VadeKutusu[]>();

  /** Başlıktaki "n gecikmiş": matris satırlarının tarihi geçen toplamı. */
  protected readonly gecikmis = computed(() => this.satirlar().reduce((t, s) => t + s.gecmis, 0));
}
