import { DOCUMENT } from '@angular/common';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  Injector,
  linkedSignal,
  signal,
  viewChild,
} from '@angular/core';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import type { MenuYaniti } from '@core/api/ui-tipleri';
import { SayfaTerki } from '@core/form/kaydedilmemis-degisiklik';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { FetchPolicy } from '@core/veri/fetch-policy';
import type { StoreDurumu } from '@core/veri/temel-store';
import { Ikon } from '@shared/ikon/ikon';

import { etkinKayit, type MenuKaydi, menuModeliKur } from './menu/menu-modeli';
import { MenuStore } from './menu/menu-store';
import { YanMenu, type YanMenuDurumu } from './menu/yan-menu';
import { SekmeCubugu } from './sekmeler/sekme-cubugu';
import { SekmeServisi } from './sekmeler/sekme-servisi';
import { UstCubuk } from './ust-cubuk/ust-cubuk';

/** Rozetler bu aralıkla tazelenir (sekme görünürken). */
const MENU_TAZELEME_MS = 5 * 60_000;
/** Çekmece (mobil yan menü) bu genişlikte ve altında. `_kabuk` stilleriyle aynı. */
const MOBIL_SORGU = '(max-width: 900px)';

/**
 * Uygulama kabuğu (F3.2, Blazor `MainLayout` yeniden yazıldı): yan menü `GET /api/ui/v1/menu`'den,
 * üst çubuk, sekmeli çalışma alanı ve Ctrl+K komut paleti. Oturum isteyen tüm sayfalar bunun içinde
 * (`sayfalar.ts`); giriş sayfası dışında.
 *
 * - SPA öğesi router ile; Blazor öğesi TAM SAYFA açılır (`/app` dışı) — önce tüm sekmelerde
 *   kaydedilmemiş değişiklik sorulur (tek soru), onaydan sonra `beforeunload` ikinci kez sormaz.
 * - Mobilde (≤ 900 px) yan menü çekmece: açılınca odak içine, arka plan `inert`, Esc/perde kapatır,
 *   odak menü düğmesine döner.
 */
@Component({
  selector: 'rc-kabuk',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Ikon, RouterOutlet, TranslocoPipe, SekmeCubugu, UstCubuk, YanMenu],
  providers: [FetchPolicy, MenuStore],
  templateUrl: './kabuk.html',
  styleUrl: './kabuk.scss',
  host: { '(document:keydown)': 'kisayol($event)' },
})
export class Kabuk {
  private readonly router = inject(Router);
  private readonly oturum = inject(OturumServisi);
  private readonly terk = inject(SayfaTerki);
  private readonly enjektor = inject(Injector);
  private readonly pencere = inject(DOCUMENT).defaultView;
  private readonly store = inject(MenuStore);
  private readonly politika = inject(FetchPolicy);
  protected readonly sekmeler = inject(SekmeServisi);

  private readonly yanMenu = viewChild.required<ElementRef<HTMLElement>>('yanMenu');
  private readonly icerik = viewChild.required<ElementRef<HTMLElement>>('icerik');
  private readonly ustCubuk = viewChild.required(UstCubuk);

  /** Geçerli SPA yolu (sorgu/fragment yok) — etkin menü öğesi için. */
  private readonly yol = signal(yolunuAl(this.router.url));

  /** Son başarılı menü: yenileme hatasında eski menü kalır (hata BOŞ menü olarak gösterilmez). */
  private readonly sonMenu = linkedSignal<StoreDurumu<MenuYaniti>, MenuYaniti | null>({
    source: this.store.menu.durum,
    computation: (durum, onceki) => {
      if (durum.tur === 'hazir') return durum.veri;
      if (durum.tur === 'bos') return null;
      return onceki?.value ?? null;
    },
  });
  protected readonly model = computed(() => {
    const menu = this.sonMenu();
    return menu ? menuModeliKur(menu) : null;
  });
  protected readonly menuDurumu = computed<YanMenuDurumu>(() => {
    const tur = this.store.menu.tur();
    return tur === 'hata' ? 'hata' : tur === 'hazir' ? 'hazir' : 'yukleniyor';
  });
  protected readonly guncellenemedi = computed(
    () => this.store.menu.tur() === 'hata' && this.model() !== null,
  );
  protected readonly etkin = computed(() => {
    const model = this.model();
    return model ? etkinKayit(model, this.yol()) : null;
  });

  protected readonly mobil = signal(false);
  protected readonly cekmeceAcik = signal(false);
  private paletAcik = false;

  constructor() {
    this.politika.baglan({
      parametre: signal<number>(0),
      yukle: (p) => this.store.menu.yukle(p),
      sifirla: () => this.store.menu.sifirla(),
    });

    const abonelik = this.router.events.subscribe((olay) => {
      if (!(olay instanceof NavigationEnd)) return;
      this.yol.set(yolunuAl(olay.urlAfterRedirects));
      if (this.cekmeceAcik()) this.cekmeceyiKapat(false);
    });

    const zamanlayici = this.pencere?.setInterval(() => {
      if (this.pencere?.document.visibilityState === 'visible') this.politika.yenile();
    }, MENU_TAZELEME_MS);

    const sorgu = this.pencere?.matchMedia?.(MOBIL_SORGU);
    const mobilDegisti = (olay: { matches: boolean }) => {
      this.mobil.set(olay.matches);
      if (!olay.matches) this.cekmeceAcik.set(false);
    };
    if (sorgu) {
      this.mobil.set(sorgu.matches);
      sorgu.addEventListener('change', mobilDegisti);
    }

    inject(DestroyRef).onDestroy(() => {
      abonelik.unsubscribe();
      if (zamanlayici !== undefined) this.pencere?.clearInterval(zamanlayici);
      sorgu?.removeEventListener('change', mobilDegisti);
    });
  }

  protected menuyuYenile(): void {
    this.politika.yenile();
  }

  /** Menü / palet öğesi: SPA → router; Blazor → kirli form sorusu, sonra tam sayfa. */
  async ogeAc(kayit: MenuKaydi): Promise<void> {
    const hedef = kayit.hedef;
    if (hedef.tur === 'spa') {
      await this.router.navigateByUrl(hedef.yol);
      return;
    }
    if (!(await this.sekmeler.ayrilmaOnayi())) return;
    this.terk.tamSayfayaGit(hedef.adres);
  }

  /** Çıkış: tüm sekmelerdeki kaydedilmemiş değişiklik TEK soruyla; onaylanınca guard yeniden sormaz. */
  protected async cikisYap(): Promise<void> {
    if (!(await this.sekmeler.ayrilmaOnayi())) return;
    await this.terk.onayliCalistir(() => this.oturum.cikisYap());
  }

  /** Belge klavyesi: Ctrl+K / ⌘K paleti açar; Esc açık çekmeceyi kapatır (odak menü düğmesine). */
  protected kisayol(olay: KeyboardEvent): void {
    if (olay.key === 'Escape' && this.cekmeceAcik()) {
      this.cekmeceyiKapat(true);
      return;
    }
    const k = olay.key === 'k' || olay.key === 'K' || olay.code === 'KeyK';
    if (!k || !(olay.ctrlKey || olay.metaKey) || olay.altKey || olay.shiftKey) return;
    olay.preventDefault();
    void this.paletiAc();
  }

  /** Ctrl+K paleti: diyalog ve palet bileşeni tembel yüklenir (ilk açılışta). */
  protected async paletiAc(): Promise<void> {
    if (this.paletAcik) return;
    this.paletAcik = true;
    try {
      const { komutPaletiniAc } = await import('./komut-paleti/komut-paleti');
      const secilen = await komutPaletiniAc(this.enjektor, this.model()?.tumu ?? []);
      if (secilen) await this.ogeAc(secilen);
    } finally {
      this.paletAcik = false;
    }
  }

  protected cekmeceyiAc(): void {
    this.cekmeceAcik.set(true);
    afterNextRender(
      () => this.yanMenu().nativeElement.querySelector<HTMLElement>('button, a[href]')?.focus(),
      { injector: this.enjektor },
    );
  }

  protected cekmeceyiKapat(odakGeriVer: boolean): void {
    if (!this.cekmeceAcik()) return;
    this.cekmeceAcik.set(false);
    // İçerik sütunu `inert` iken odaklanamaz: öznitelik kalktıktan (çizimden) sonra.
    if (odakGeriVer) {
      afterNextRender(() => this.ustCubuk().menuDugmesineOdaklan(), { injector: this.enjektor });
    }
  }

  /** Atlama bağlantısı: `<base href>` altında `#icerik` başka belgeye çözülür; odak elle taşınır. */
  protected icerigeGec(olay: Event): void {
    olay.preventDefault();
    this.icerik().nativeElement.focus();
  }

  protected bilesenEtkin(bilesen: unknown): void {
    this.sekmeler.etkinBileseniAyarla(bilesen);
  }
}

function yolunuAl(url: string): string {
  return url.split(/[?#]/, 1)[0] || '/';
}
