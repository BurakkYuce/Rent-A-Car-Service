import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

import { YanMenu } from '../../../kabuk/menu/yan-menu';
import { SayfaBandi } from '../../../kabuk/sayfa-bandi/sayfa-bandi';

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
  imports: [RouterLink, SayfaBandi, YanMenu],
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
      border-radius: var(--rc-yaricap-lg);
      background-color: var(--rc-kenar-cubugu-zemin);
    }
    .bant-ornegi {
      margin: calc(var(--rc-bosluk-4) * -1) calc(var(--rc-bosluk-4) * -1) 0;
    }
    @media (max-width: 30rem) {
      dl {
        grid-template-columns: minmax(0, 1fr);
      }
    }
  `,
  template: `
    <rc-sayfa-bandi
      class="bant-ornegi"
      baslik="Kabuk"
      ikon="layout-sidebar-left-collapse"
      pill="Vitrin · 5 parça"
      altMetin="Lacivert kenar çubuğu, üst çubuk, sekmeler, sayfa bandı"
    >
      <ng-container eylemler>
        <a class="rc-dugme" routerLink="/vitrin">Vitrin</a>
        <button type="button" class="rc-dugme">Görünümü kaydet</button>
        <button type="button" class="rc-dugme">Yazdır</button>
      </ng-container>
      <a birincil class="rc-dugme rc-dugme--birincil" routerLink="/vitrin/tablo">Tablo vitrini</a>
    </rc-sayfa-bandi>
    <p class="giris">
      Bu sayfayı saran menü, üst çubuk ve sekme çubuğu kabuğun canlı parçalarıdır. Sayfa kendi
      <code>&lt;main&gt;</code>’ini açmaz; başlığı sayfa bandının <code>h1</code>’idir (gövdede
      ikinci <code>h1</code> yok). Bandda çerçeveli ikincil eylemler <code>[eylemler]</code>, tek
      dolu birincil <code>[birincil]</code> yuvasına; ≤ 900 px’te ikinciller “…” menüsüne iner.
    </p>

    <section class="rc-bolum" aria-labelledby="parcalar">
      <h2 id="parcalar">Parçalar</h2>
      <dl>
        @for (p of parcalar; track p.ad) {
          <dt>{{ p.ad }}</dt>
          <dd>{{ p.aciklama }}</dd>
        }
      </dl>
    </section>

    <section class="rc-bolum" aria-labelledby="klavye">
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

    <section class="rc-bolum" aria-labelledby="menu-durumlari">
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
      ad: 'Kenar çubuğu',
      aciklama:
        '240 px lacivert (koyu temada asfalt); daralt düğmesiyle 56 px ikon şeridi (rc.kabuk.dar). Kısayol çifti + Kira / + Rezervasyon sunucunun hızlı bağlantılarından (izin süzmesi sunucuda). Akordeon: tek grup açık (rc.menu.acik). Kira grubunda kayıtlı görünümler; sayaçlar Panel verisinden. Altta şube (salt okunur, kapsam kullanıcı tanımından) + kullanıcı kartı + çıkış.',
    },
    {
      ad: 'Üst çubuk',
      aciklama:
        '56 px: menü daralt (≤ 900 px çekmece aç), komut paleti (Ctrl+K), plaka arama (Enter → araç listesi), bildirim zili (menüde bildirim öğesi varsa), tema üçlüsü.',
    },
    {
      ad: 'Sayfa bandı',
      aciklama:
        'rc-sayfa-bandi: ikon + başlık (h1) + bağlam pill’i + ikincil metin; çerçeveli beyaz ikinciller, tek dolu beyaz birincil. Mobilde 44 px, ikinciller “…” menüsünde.',
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
