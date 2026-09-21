import { LocationStrategy, NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { Ikon } from '@shared/ikon/ikon';

import type { MenuKaydi, MenuModeli } from './menu-modeli';

/** Menü durumu: model yoksa yükleniyor/hata kutusu; varsa menü (+ güncellenemedi uyarısı). */
export type YanMenuDurumu = 'yukleniyor' | 'hata' | 'hazir';

/**
 * Yan menü (yoğun ERP): hızlı bağlantılar, grupsuz öğeler ve katlanır gruplar — hepsi sunucunun
 * sırasıyla. Etkin sayfa `aria-current="page"` + görsel işaret; grubu kendiliğinden açılır. Bağlantılar
 * gerçek `href` taşır (orta tık / Ctrl+tık yeni sekme); düz tıklama `sec` olayına gider (SPA → router,
 * Blazor → kaydedilmemiş değişiklik sorusundan sonra tam sayfa).
 */
@Component({
  selector: 'rc-yan-menu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Ikon, NgTemplateOutlet, TranslocoPipe],
  templateUrl: './yan-menu.html',
  styleUrl: './yan-menu.scss',
})
export class YanMenu {
  private readonly konum = inject(LocationStrategy);

  readonly model = input<MenuModeli | null>(null);
  readonly durum = input<YanMenuDurumu>('yukleniyor');
  /** Menü daha önce yüklendi ama son yenileme başarısız (eski menü gösterilir). */
  readonly guncellenemedi = input(false);
  readonly etkin = input<MenuKaydi | null>(null);

  readonly sec = output<MenuKaydi>();
  readonly yenile = output<void>();

  private readonly acikGruplar = signal<ReadonlySet<string>>(new Set());
  protected readonly etkinKimlik = computed(() => this.etkin()?.kimlik ?? null);

  constructor() {
    // Etkin sayfanın grubu kendiliğinden açılır (kullanıcının açtığı diğer gruplar kapanmaz).
    effect(() => {
      const grup = this.etkin()?.grup;
      if (!grup) return;
      untracked(() => {
        if (!this.acikGruplar().has(grup)) this.acikGruplar.update((s) => new Set(s).add(grup));
      });
    });
  }

  protected acikMi(grup: string): boolean {
    return this.acikGruplar().has(grup);
  }

  protected grubuDegistir(grup: string): void {
    this.acikGruplar.update((onceki) => {
      const yeni = new Set(onceki);
      if (!yeni.delete(grup)) yeni.add(grup);
      return yeni;
    });
  }

  protected href(kayit: MenuKaydi): string {
    return kayit.hedef.tur === 'spa'
      ? this.konum.prepareExternalUrl(kayit.hedef.yol)
      : kayit.hedef.adres;
  }

  protected rozet(kayit: MenuKaydi): string | null {
    return rozetMetni(this.sayac(kayit));
  }

  /** Kapalı grubun başlığında içindeki rozetlerin toplamı (açılmadan görünsün). */
  protected grupRozeti(kayitlar: readonly MenuKaydi[]): string | null {
    return rozetMetni(kayitlar.reduce((t, k) => t + this.sayac(k), 0));
  }

  private sayac(kayit: MenuKaydi): number {
    return kayit.rozetKodu ? (this.model()?.rozetler.get(kayit.rozetKodu) ?? 0) : 0;
  }

  /** Düz sol tık uygulama içinde; değiştirici tuşlu ya da orta tık tarayıcıya bırakılır (yeni sekme). */
  protected tikla(olay: MouseEvent, kayit: MenuKaydi): void {
    if (olay.button !== 0 || olay.ctrlKey || olay.metaKey || olay.shiftKey || olay.altKey) return;
    olay.preventDefault();
    this.sec.emit(kayit);
  }
}

function rozetMetni(sayi: number): string | null {
  if (sayi <= 0) return null;
  return sayi > 99 ? '99+' : String(sayi);
}
