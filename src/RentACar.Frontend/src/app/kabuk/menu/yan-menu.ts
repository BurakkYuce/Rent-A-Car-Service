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

import { KabukSayaclari } from '@core/sayac/kabuk-sayaclari';
import { Ikon } from '@shared/ikon/ikon';

import { type KayitliGorunum, kiraGorunumleri, kiraListesiMi, kisayolCifti } from './kisayollar';
import { grupIkonu, ogeIkonu } from './menu-ikonlari';
import type { MenuKaydi, MenuModeli } from './menu-modeli';

/** Menü durumu: model yoksa yükleniyor/hata kutusu; varsa menü (+ güncellenemedi uyarısı). */
export type YanMenuDurumu = 'yukleniyor' | 'hata' | 'hazir';

/** Açık grup (akordeon) kalıcı anahtarı (Yol v2 §5.1). */
export const MENU_ACIK_ANAHTARI = 'rc.menu.acik';

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
  imports: [Ikon, NgTemplateOutlet, TranslocoPipe],
  templateUrl: './yan-menu.html',
  styleUrl: './yan-menu.scss',
  host: { '[class.dar]': 'dar()' },
})
export class YanMenu {
  private readonly konum = inject(LocationStrategy);
  private readonly depo = depoAl(inject(DOCUMENT));
  private readonly sayaclar = inject(KabukSayaclari).sayaclar;

  readonly model = input<MenuModeli | null>(null);
  readonly durum = input<YanMenuDurumu>('yukleniyor');
  /** Menü daha önce yüklendi ama son yenileme başarısız (eski menü gösterilir). */
  readonly guncellenemedi = input(false);
  readonly etkin = input<MenuKaydi | null>(null);
  /** Geçerli adresin `gorunum` sorgu değeri (kayıtlı görünüm işareti). */
  readonly gorunum = input<string | null>(null);
  readonly dar = input(false);

  readonly sec = output<MenuKaydi>();
  readonly yenile = output<void>();
  readonly genislet = output<void>();

  private readonly acikGrup = signal<string | null>(this.depoOku());
  protected readonly etkinKimlik = computed(() => this.etkin()?.kimlik ?? null);
  /** Hızlı bağlantı grubu ("Kısa Yollar") menüde grup olarak çizilmez; açılacak grup yok. */
  private readonly etkinGrup = computed(() => {
    const etkin = this.etkin();
    return etkin && !etkin.hizli ? etkin.grup || null : null;
  });
  protected readonly kisayollar = computed(() => {
    const model = this.model();
    return model ? kisayolCifti(model) : { kira: null, rezervasyon: null };
  });

  protected readonly grupIkonu = grupIkonu;
  protected readonly ogeIkonu = ogeIkonu;
  protected readonly kiraListesiMi = kiraListesiMi;

  constructor() {
    // Etkin sayfanın grubu açılır (akordeon: diğeri kapanır). Yalnız etkin GRUP değişince — kullanıcının
    // elle açtığı grup menü tazelemesinde geri kapanmaz.
    effect(() => {
      const grup = this.etkinGrup();
      if (grup) untracked(() => this.grubuAc(grup));
    });
  }

  protected acikMi(grup: string): boolean {
    return !this.dar() && this.acikGrup() === grup;
  }

  protected grubuDegistir(grup: string): void {
    if (this.dar()) {
      this.genislet.emit();
      this.grubuAc(grup);
      return;
    }
    this.acikGrupYaz(this.acikGrup() === grup ? null : grup);
  }

  protected gorunumler(kiralar: MenuKaydi): KayitliGorunum[] {
    return kiraGorunumleri(kiralar);
  }

  /** Kira listesindeyken işaretli görünüm: sorgudaki kod, yoksa "Tüm sözleşmeler". */
  protected gorunumEtkin(g: KayitliGorunum, kiralar: MenuKaydi): boolean {
    return this.etkinKimlik() === kiralar.kimlik && (this.gorunum() ?? null) === g.kod;
  }

  protected gorunumSayaci(g: KayitliGorunum): string | null {
    const s = this.sayaclar();
    return g.sayac && s ? rozetMetni(s[g.sayac], true) : null;
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

  private grubuAc(grup: string): void {
    if (this.acikGrup() !== grup) this.acikGrupYaz(grup);
  }

  private acikGrupYaz(grup: string | null): void {
    this.acikGrup.set(grup);
    try {
      if (grup === null) this.depo?.removeItem(MENU_ACIK_ANAHTARI);
      else this.depo?.setItem(MENU_ACIK_ANAHTARI, grup);
    } catch {
      // Depo kapalı (gizli pencere, kota): yalnız bu oturumda hatırlanır.
    }
  }

  private depoOku(): string | null {
    try {
      return this.depo?.getItem(MENU_ACIK_ANAHTARI) || null;
    } catch {
      return null;
    }
  }
}

function depoAl(belge: Document): Storage | null {
  try {
    return belge.defaultView?.localStorage ?? null;
  } catch {
    return null;
  }
}

function rozetMetni(sayi: number, sifirGoster = false): string | null {
  if (!Number.isFinite(sayi) || sayi < 0 || (sayi === 0 && !sifirGoster)) return null;
  return sayi > 99 ? '99+' : String(sayi);
}
