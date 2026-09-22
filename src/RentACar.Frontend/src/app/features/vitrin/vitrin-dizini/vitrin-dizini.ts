import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

interface VitrinBaglantisi {
  readonly yol: string;
  readonly ad: string;
  readonly aciklama: string;
  readonly faz: string;
}

/**
 * Vitrin dizini (F3.7): çekirdeğin her parçasına bir bağlantı. Modül fazları (F4–F12) ekranlarını
 * yalnız bu parçalarla kurar; yeni çekirdek parçası buraya ve `e2e/vitrin-sayfalari.ts`'e eklenir
 * (e2e dizinin eksiksiz olduğunu, her sayfanın iki temada axe + 320–1440 px taşma kapısını ve görsel
 * regresyonunu denetler).
 */
@Component({
  selector: 'rc-vitrin-dizini',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  styleUrl: '../vitrin-ortak.scss',
  styles: `
    .kart {
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-1);
      height: 100%;
      padding: var(--rc-bosluk-3);
      border: 1px solid var(--rc-kenar);
      border-radius: var(--rc-yaricap-lg);
      background-color: var(--rc-yuzey);
    }
    .kart a {
      font-weight: var(--rc-agirlik-kalin);
    }
  `,
  template: `
    <div class="sayfa-ust">
      <h1>Vitrin</h1>
      <a routerLink="/">Ana sayfa</a>
    </div>
    <p class="giris">
      Tasarım sistemi ve çekirdek bileşenler. Her sayfa açık ve koyu temada, 320–1440 px genişlikte
      CI'da görsel regresyon, erişilebilirlik (axe) ve yatay taşma kapısından geçer.
    </p>
    <ul class="izgara" aria-label="Vitrin sayfaları">
      @for (b of baglantilar; track b.yol) {
        <li class="kart">
          <a [routerLink]="b.yol">{{ b.ad }}</a>
          <span>{{ b.aciklama }}</span>
          <span class="not">{{ b.faz }}</span>
        </li>
      }
    </ul>
  `,
})
export class VitrinDizini {
  protected readonly baglantilar: readonly VitrinBaglantisi[] = [
    {
      yol: '/vitrin/tokenlar',
      ad: 'Token’lar',
      aciklama: 'Renk, yazı ölçeği, boşluk, köşe ve gölge; açık ve koyu tema.',
      faz: 'F3.1 tasarım sistemi',
    },
    {
      yol: '/vitrin/primitifler',
      ad: 'Primitifler',
      aciklama: 'Düğmeler, durum rozeti, iskelet, boş durum ve ikon kümesi.',
      faz: 'F3.1 tasarım sistemi',
    },
    {
      yol: '/vitrin/form',
      ad: 'Form seti',
      aciklama:
        'Tüm form kontrolleri (para, tarih, aranabilir seçim dahil), sekmeli form ve sabit yan panel.',
      faz: 'F3.6 form seti',
    },
    {
      yol: '/vitrin/tanim',
      ad: 'Tanım CRUD',
      aciklama: 'Alan tanımından liste + ekle/düzenle/sil; sunucu alan hatası.',
      faz: 'F3.6 form seti',
    },
    {
      yol: '/vitrin/tablo',
      ad: 'Tablo motoru',
      aciklama: '49 sütun × 5.000 kayıt: sanal kaydırma, sabit sütun, klavye, kullanıcı düzeni.',
      faz: 'F3.5 tablo motoru',
    },
    {
      yol: '/vitrin/geri-bildirim',
      ad: 'Geri bildirim',
      aciklama: 'Toast, uyarı bandı, onay diyaloğu ve oturum/hata sözleşmesi.',
      faz: 'F3.3 oturum ve geri bildirim',
    },
    {
      yol: '/vitrin/kabuk',
      ad: 'Kabuk',
      aciklama: 'Menü, üst çubuk, Ctrl+K komut paleti, sekmeler ve mobil çekmece.',
      faz: 'F3.2 kabuk',
    },
  ];
}
