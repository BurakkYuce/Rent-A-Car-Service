import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { sayiya } from '../kira-formu-modeli';
import { KF_ORTAK } from '../sekmeler/ortak';
import { paraGoster } from './finans-modeli';
import { KiraFinansDurumu } from './kira-finans-durumu';

/** Kur bilgileri (`GET secim/kur`, TCMB günün kurları — ulusal veri). Salt okunur. */
@Component({
  selector: 'rc-kf-finans-kurlar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...KF_ORTAK],
  template: `
    @let s = f.kurlar;
    @if (s.tur() === 'hata') {
      <p class="kf-not">{{ 'kiraFinans.kur.okunamadi' | transloco }} {{ s.hata()?.detay }}</p>
    } @else if (s.tur() === 'hazir' && (s.veri() ?? []).length === 0) {
      <p class="kf-not">{{ 'kiraFinans.kur.yok' | transloco }}</p>
    } @else {
      <div
        class="rc-tablo-kap"
        role="region"
        tabindex="0"
        [attr.aria-label]="'kiraFinans.kur.liste' | transloco"
        [attr.aria-busy]="s.yukleniyor()"
      >
        <table class="rc-duz-tablo" [attr.aria-label]="'kiraFinans.kur.liste' | transloco">
          <thead>
            <tr>
              <th scope="col">{{ 'kiraFinans.kur.kod' | transloco }}</th>
              <th scope="col" class="rc-num">{{ 'kiraFinans.kur.alis' | transloco }}</th>
              <th scope="col" class="rc-num">{{ 'kiraFinans.kur.satis' | transloco }}</th>
              <th scope="col">{{ 'kiraFinans.alan.tarih' | transloco }}</th>
            </tr>
          </thead>
          <tbody>
            @for (x of s.veri() ?? []; track x.id) {
              <tr>
                <td>
                  {{ x.etiket }}
                  @if (sayiya(x.birim) !== 1) {
                    ({{ x.birim }})
                  }
                </td>
                <td class="rc-num">{{ sayiya(x.dovizAlis) | sayi: '1.4-4' }}</td>
                <td class="rc-num">{{ sayiya(x.dovizSatis) | sayi: '1.4-4' }}</td>
                <td>{{ x.tarih | tarih }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
})
export class FinansKurlar {
  protected readonly f = inject(KiraFinansDurumu);
  protected readonly sayiya = sayiya;
}

/** Kiraya bağlı cezalar + kira dönemindeki HGS geçişleri (salt okunur; yansıtma Cezalar ekranında). */
@Component({
  selector: 'rc-kf-finans-cezalar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...KF_ORTAK, RouterLink],
  template: `
    @let s = f.cezalar;
    @let v = s.veri();
    @if (s.tur() === 'hata') {
      <p class="kf-not">{{ s.hata()?.detay }}</p>
    }
    <div
      class="rc-tablo-kap"
      role="region"
      tabindex="0"
      [attr.aria-label]="'kiraFinans.ceza.liste' | transloco"
      [attr.aria-busy]="s.yukleniyor()"
    >
      <table class="rc-duz-tablo" [attr.aria-label]="'kiraFinans.ceza.liste' | transloco">
        <thead>
          <tr>
            <th scope="col">{{ 'kiraFinans.ceza.no' | transloco }}</th>
            <th scope="col">{{ 'kiraFinans.alan.tur' | transloco }}</th>
            <th scope="col" class="rc-num">{{ 'kiraFinans.alan.tutar' | transloco }}</th>
            <th scope="col" class="rc-num">{{ 'kiraFinans.ceza.kalan' | transloco }}</th>
            <th scope="col">{{ 'kiraFinans.alan.durum' | transloco }}</th>
          </tr>
        </thead>
        <tbody>
          @for (c of v?.cezalar ?? []; track c.id) {
            <tr>
              <td>{{ c.no }}</td>
              <td>{{ c.cezaTuru }}</td>
              <td class="rc-num">{{ para(c.tutar, null) }}</td>
              <td class="rc-num">{{ para(c.kalan, null) }}</td>
              <td>{{ c.durum }}</td>
            </tr>
          } @empty {
            @if (s.tur() === 'hazir') {
              <tr>
                <td colspan="5" class="rc-bos">{{ 'kiraFinans.ceza.yok' | transloco }}</td>
              </tr>
            }
          }
        </tbody>
      </table>
    </div>
    <div
      class="rc-tablo-kap"
      role="region"
      tabindex="0"
      [attr.aria-label]="'kiraFinans.ceza.hgs' | transloco"
    >
      <table class="rc-duz-tablo" [attr.aria-label]="'kiraFinans.ceza.hgs' | transloco">
        <thead>
          <tr>
            <th scope="col">{{ 'kiraFinans.ceza.zaman' | transloco }}</th>
            <th scope="col">{{ 'kiraFinans.ceza.gise' | transloco }}</th>
            <th scope="col" class="rc-num">{{ 'kiraFinans.alan.tutar' | transloco }}</th>
          </tr>
        </thead>
        <tbody>
          @for (g of v?.hgsGecisleri ?? []; track $index) {
            <tr>
              <td>{{ g.zaman | tarihSaat }}</td>
              <td>{{ g.gecis }}</td>
              <td class="rc-num">{{ para(g.tutar, null) }}</td>
            </tr>
          } @empty {
            @if (s.tur() === 'hazir') {
              <tr>
                <td colspan="3" class="rc-bos">{{ 'kiraFinans.ceza.hgsYok' | transloco }}</td>
              </tr>
            }
          }
        </tbody>
      </table>
    </div>
    <p class="kf-not">
      <a routerLink="/cezalar">{{ 'kiraFinans.ceza.ekran' | transloco }}</a>
      — {{ 'kiraFinans.ceza.not' | transloco }}
    </p>
  `,
})
export class FinansCezalar {
  protected readonly f = inject(KiraFinansDurumu);
  protected readonly para = paraGoster;
}
