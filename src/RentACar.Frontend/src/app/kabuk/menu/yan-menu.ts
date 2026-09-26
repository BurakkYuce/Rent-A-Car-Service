import { DOCUMENT, LocationStrategy, NgTemplateOutlet } from '@angular/common';
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

import { ShellCounters } from '@core/sayac/shell-counters';
import { Icon } from '@shared/ikon/icon';

import { type KayitliGorunum, rentalViews, isRentalList, shortcutPair } from './kisayollar';
import { groupIcon, itemIcon } from './menu-ikonlari';
import type { MenuKaydi, MenuModeli } from './menu-modeli';

/** Menü durumu: model yoksa yükleniyor/hata kutusu; varsa menü (+ güncellenemedi uyarısı). */
export type SideMenuState = 'yukleniyor' | 'hata' | 'hazir';

/** Açık grup (akordeon) kalıcı anahtarı (Yol v2 §5.1). */
export const MENU_OPEN_KEY = 'rc.menu.acik';

/**
 * Yan menü (Yol v2 §5.1, lacivert kenar çubuğu): kısayol çifti (`+ Kira` / `+ Rezervasyon`), grupsuz öğeler ve
 * ikonlu akordeon gruplar — hepsi sunucunun sırasıyla. Aynı anda TEK grup açık; açık grup `rc.menu.acik`'te
 * kalıcı; etkin sayfanın grubu kendiliğinden açılır. Kira grubunda SPA kira listesi kayıtlı görünümlerle
 * (sayaçlı) açılır. Etkin sayfa `aria-current="page"`. Bağlantılar gerçek `href` taşır (orta tık / Ctrl+tık
 * yeni sekme); düz tıklama `sec` olayına gider (SPA → router, Blazor → kaydedilmemiş değişiklik sorusu).
 *
 * `dar`: 56 px ikon şeridi — etiketler görsel olarak gizlenir (erişilebilir ad kalır), gruba tıklamak menüyü
 * genişletir (`genislet`) ve grubu açar.
 */
@Component({
  selector: 'rc-yan-menu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, NgTemplateOutlet, TranslocoPipe],
  templateUrl: './yan-menu.html',
  styleUrl: './yan-menu.scss',
  host: { '[class.dar]': 'dar()' },
})
export class YanMenu {
  private readonly location = inject(LocationStrategy);
  private readonly store = getStore(inject(DOCUMENT));
  private readonly counters = inject(ShellCounters).counters;

  readonly model = input<MenuModeli | null>(null);
  readonly durum = input<SideMenuState>('yukleniyor');
  /** Menü daha önce yüklendi ama son yenileme başarısız (eski menü gösterilir). */
  readonly guncellenemedi = input(false);
  readonly etkin = input<MenuKaydi | null>(null);
  /** Geçerli adresin `gorunum` sorgu değeri (kayıtlı görünüm işareti). */
  readonly gorunum = input<string | null>(null);
  readonly dar = input(false);

  readonly sec = output<MenuKaydi>();
  readonly yenile = output<void>();
  readonly genislet = output<void>();

  private readonly openGroup = signal<string | null>(this.readStore());
  protected readonly activeId = computed(() => this.etkin()?.kimlik ?? null);
  /** Hızlı bağlantı grubu ("Kısa Yollar") menüde grup olarak çizilmez; açılacak grup yok. */
  private readonly activeGroup = computed(() => {
    const active = this.etkin();
    return active && !active.hizli ? active.grup || null : null;
  });
  protected readonly shortcuts = computed(() => {
    const model = this.model();
    return model ? shortcutPair(model) : { kira: null, rezervasyon: null };
  });

  protected readonly groupIcon = groupIcon;
  protected readonly itemIcon = itemIcon;
  protected readonly isRentalList = isRentalList;

  constructor() {
    // Etkin sayfanın grubu açılır (akordeon: diğeri kapanır). Yalnız etkin GRUP değişince — kullanıcının
    // elle açtığı grup menü tazelemesinde geri kapanmaz.
    effect(() => {
      const group = this.activeGroup();
      if (group) untracked(() => this.openGroupAction(group));
    });
  }

  protected isOpen(group: string): boolean {
    return !this.dar() && this.openGroup() === group;
  }

  protected toggleGroup(group: string): void {
    if (this.dar()) {
      this.genislet.emit();
      this.openGroupAction(group);
      return;
    }
    this.writeOpenGroup(this.openGroup() === group ? null : group);
  }

  protected views(rentals: MenuKaydi): KayitliGorunum[] {
    return rentalViews(rentals);
  }

  /** Kira listesindeyken işaretli görünüm: sorgudaki kod, yoksa "Tüm sözleşmeler". */
  protected isViewActive(g: KayitliGorunum, rentals: MenuKaydi): boolean {
    return this.activeId() === rentals.kimlik && (this.gorunum() ?? null) === g.kod;
  }

  protected viewCounter(g: KayitliGorunum): string | null {
    const s = this.counters();
    return g.sayac && s ? badgeText(s[g.sayac], true) : null;
  }

  protected href(record: MenuKaydi): string {
    return record.hedef.tur === 'spa'
      ? this.location.prepareExternalUrl(record.hedef.yol)
      : record.hedef.adres;
  }

  protected rozet(record: MenuKaydi): string | null {
    return badgeText(this.sayac(record));
  }

  /** Kapalı grubun başlığında içindeki rozetlerin toplamı (açılmadan görünsün). */
  protected groupBadge(records: readonly MenuKaydi[]): string | null {
    return badgeText(records.reduce((t, k) => t + this.sayac(k), 0));
  }

  private sayac(record: MenuKaydi): number {
    return record.rozetKodu ? (this.model()?.rozetler.get(record.rozetKodu) ?? 0) : 0;
  }

  /** Düz sol tık uygulama içinde; değiştirici tuşlu ya da orta tık tarayıcıya bırakılır (yeni sekme). */
  protected click(evt: MouseEvent, record: MenuKaydi): void {
    if (evt.button !== 0 || evt.ctrlKey || evt.metaKey || evt.shiftKey || evt.altKey) return;
    evt.preventDefault();
    this.sec.emit(record);
  }

  private openGroupAction(group: string): void {
    if (this.openGroup() !== group) this.writeOpenGroup(group);
  }

  private writeOpenGroup(group: string | null): void {
    this.openGroup.set(group);
    try {
      if (group === null) this.store?.removeItem(MENU_OPEN_KEY);
      else this.store?.setItem(MENU_OPEN_KEY, group);
    } catch {
      // Depo kapalı (gizli pencere, kota): yalnız bu oturumda hatırlanır.
    }
  }

  private readStore(): string | null {
    try {
      return this.store?.getItem(MENU_OPEN_KEY) || null;
    } catch {
      return null;
    }
  }
}

function getStore(document: Document): Storage | null {
  try {
    return document.defaultView?.localStorage ?? null;
  } catch {
    return null;
  }
}

function badgeText(count: number, showZero = false): string | null {
  if (!Number.isFinite(count) || count < 0 || (count === 0 && !showZero)) return null;
  return count > 99 ? '99+' : String(count);
}
