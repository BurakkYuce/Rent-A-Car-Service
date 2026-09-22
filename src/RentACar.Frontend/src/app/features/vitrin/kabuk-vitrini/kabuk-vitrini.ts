import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

import { YanMenu } from '../../../kabuk/menu/yan-menu';

interface KabukParcasi {
  readonly ad: string;
  readonly aciklama: string;
}

/**
 * Kabuk vitrini (F3.2). Kabuğun kendisi bu sayfayı sarar (menü, üst çubuk, sekme çubuğu canlı); burada
 * parçaların sözleşmesi ve menünün canlıda nadir görülen durumları (yükleniyor, hata) çizilir. Menü
 * modeli VERİLMEZ: model kimlikleri (`rc-menu-*`) canlı menüyle çakışırdı.
 */
@Component({
  selector: 'rc-kabuk-vitrini',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, YanMenu],
  styleUrl: '../vitrin-ortak.scss',
  styles: `
    dl {
      display: grid;
      grid-template-columns: minmax(0, 12rem) minmax(0, 1fr);
      gap: var(--rc-bosluk-2) var(--rc-bosluk-4);
      margin: 0;
    }
    dt {
      font-weight: var(--rc-agirlik-kalin);
    }
    dd {
      margin: 0;
      color: var(--rc-metin-ikincil);
    }
    .menu-ornegi {
      max-width: 15rem;
      border: 1px solid var(--rc-kenar);
      border-radius: var(--rc-yaricap-lg);
    }
    @media (max-width: 30rem) {
      dl {
        grid-template-columns: minmax(0, 1fr);
      }
    }
  `,
  template: `
    <div class="sayfa-ust">
      <h1>Kabuk</h1>
      <a routerLink="/vitrin">Vitrin</a>
    </div>
    <p class="giris">
      Bu sayfayı saran menü, üst çubuk ve sekme çubuğu kabuğun canlı parçalarıdır. Sayfa kendi
      <code>&lt;main&gt;</code>’ini açmaz; başlığı <code>h1</code>.
    </p>

    <section class="bolum" aria-labelledby="parcalar">
      <h2 id="parcalar">Parçalar</h2>
      <dl>
        @for (p of parcalar; track p.ad) {
          <dt>{{ p.ad }}</dt>
          <dd>{{ p.aciklama }}</dd>
        }
      </dl>
    </section>

    <section class="bolum" aria-labelledby="klavye">
      <h2 id="klavye">Klavye</h2>
      <dl>
        <dt><kbd>Ctrl</kbd> + <kbd>K</kbd> / <kbd>⌘</kbd> + <kbd>K</kbd></dt>
        <dd>Komut paleti: Türkçe-gevşek arama (İş = iş = is), ↑ ↓ seç, Enter aç, Esc kapat.</dd>
        <dt><kbd>Tab</kbd> (sayfa başında)</dt>
        <dd>“İçeriğe geç” bağlantısı.</dd>
        <dt><kbd>Esc</kbd></dt>
        <dd>Mobil çekmeceyi kapatır; odak menü düğmesine döner.</dd>
      </dl>
    </section>

    <section class="bolum" aria-labelledby="menu-durumlari">
      <h2 id="menu-durumlari">Menü durumları</h2>
      <div class="satir">
        <div class="menu-ornegi"><rc-yan-menu [durum]="'yukleniyor'" /></div>
        <div class="menu-ornegi"><rc-yan-menu [durum]="'hata'" /></div>
      </div>
    </section>
  `,
})
export class KabukVitrini {
  protected readonly parcalar: readonly KabukParcasi[] = [
    {
      ad: 'Yan menü',
      aciklama:
        'GET /api/ui/v1/menu’den; istemci süzmez. SPA öğesi router’la, Blazor öğesi tam sayfa açılır (önce tüm sekmelerdeki kaydedilmemiş değişiklik sorulur). Etkin sayfa aria-current.',
    },
    {
      ad: 'Üst çubuk',
      aciklama:
        'Menü düğmesi (≤ 900 px), komut paleti, tema seçimi (sistem/açık/koyu), kimlik, çıkış.',
    },
    {
      ad: 'Sekme çubuğu',
      aciklama:
        'Her sayfa bir sekme; geçişte bileşen yaşar. En fazla 10 sekme; tarayıcıda yalnız rota + id saklanır.',
    },
    {
      ad: 'Mobil çekmece',
      aciklama: '≤ 900 px’te yan menü çekmecedir; açıkken içerik inert, perde ve Esc kapatır.',
    },
    {
      ad: 'Uyarı bandı ve toast',
      aciklama: 'Uygulama kökünde; bkz. Geri bildirim vitrini.',
    },
  ];
}
