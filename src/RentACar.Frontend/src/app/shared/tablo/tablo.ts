import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterEveryRender,
  afterNextRender,
  computed,
  contentChildren,
  effect,
  inject,
  input,
  linkedSignal,
  model,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  createTable,
  functionalUpdate,
  getCoreRowModel,
  type ColumnDef,
  type ColumnSizingInfoState,
  type Header,
  type Updater,
} from '@tanstack/table-core';
import {
  Virtualizer,
  elementScroll,
  observeElementOffset,
  observeElementRect,
} from '@tanstack/virtual-core';
import { take } from 'rxjs';

import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { BICIM_PIPELARI } from '@shared/bicim/bicim-pipe';
import { BosDurum } from '@shared/bos-durum/bos-durum';
import { Ikon } from '@shared/ikon/ikon';

import { disaAktarmaAdresi, type DisaAktarma, type DisaAktarmaBicimi } from './disa-aktarma';
import {
  duzenAnahtari,
  duzeniBirlestir,
  enAzGenislik,
  genislikAyarla,
  gizlenebilirMi,
  gorunurlukAyarla,
  siralamaAyarla,
  sutunuKaydir,
  sutunuYerlestir,
  tanstackDurumu,
  varsayilanDuzen,
} from './tablo-duzeni';
import { tabloDuzeniDeposuCoz } from './tablo-duzeni-deposu';
import { TabloHucre, type TabloHucreBaglami } from './tablo-hucre';
import { kaynakCoz } from './tablo-kaynagi';
import { hucreGezin, konumKirp, type HucreKonumu } from './tablo-klavye';
import {
  SECIM_SUTUNU,
  SECIM_SUTUNU_GENISLIGI,
  TABLO_SINIRLARI,
  type TabloDuzeni,
  type TabloKaynagi,
  type TabloSutunu,
} from './tablo-modeli';
import { TabloSayfalama } from './tablo-sayfalama';
import { TabloSutunSecici, type SutunSeciciOgesi } from './tablo-sutun-secici';
import { ariaSiralama, siralamaCoz, siralamaMetni, sonrakiSiralama } from './tablo-siralama';

/** Şablonun çizdiği görünür sütun. */
interface GorunurSutun<T> {
  readonly kod: string;
  readonly tanim: TabloSutunu<T> | null; // null = seçim sütunu
  readonly baslik: Header<T, unknown>;
  readonly genislik: number;
  /** Sabitse sol konumu (px), değilse null. */
  readonly sol: number | null;
  readonly sonSabit: boolean;
  readonly hiza: 'bas' | 'son' | 'orta';
  readonly siralanabilir: boolean;
  readonly tasinabilir: boolean;
}

interface CizilenSatir<T> {
  readonly satir: T;
  /** Sayfadaki sıra (0 tabanlı). */
  readonly index: number;
  readonly kimlik: string;
}

const KAYIT_GECIKMESI_MS = 600;
const KLAVYE_GENISLIK_ADIMI = 16;
const ISKELET_SATIRI = 8;
const BOS_BOYUTLAMA: ColumnSizingInfoState = {
  columnSizingStart: [],
  deltaOffset: null,
  deltaPercentage: null,
  isResizingColumn: false,
  startOffset: null,
  startSize: null,
};

/**
 * Tablo motoru (F3.5): yoğun satır (32 px), sabit başlık + sabit ilk sütun(lar), para sağa yaslı ve
 * tr biçimli, sütun göster/gizle/sırala/boyutla (kullanıcıya kayıtlı, `TabloDuzenleri`), sunucu
 * sayfalama/sıralama (F3.4 liste sorgusu), sanal satırlar, satır seçimi, klavye gezinmesi (APG
 * Data Grid) ve dört AYRI durum (boş-istek/yükleniyor/hazır/hata).
 *
 * TanStack `table-core` (sütun durumu + boyutlama) ve `virtual-core` (satır sanallaştırma) doğrudan,
 * çerçeve bağdaştırıcısız kullanılır (bağdaştırıcılar deneysel/zone'a bağlı). Revlo'dan farklar:
 * düzen otele değil kullanıcıya bağlı, zoneless, 48 → 32 px satır, klavye gezinmesi eklendi.
 */
@Component({
  selector: 'rc-tablo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgTemplateOutlet,
    TranslocoPipe,
    Ikon,
    BosDurum,
    TabloSayfalama,
    TabloSutunSecici,
    ...BICIM_PIPELARI,
  ],
  templateUrl: './tablo.html',
  styleUrl: './tablo.scss',
  // Izgara olayları kökte dinlenir (hücreler `data-hucre` taşır; araç çubuğu olayları yok sayılır).
  host: {
    '(keydown)': 'klavye($event)',
    '(focusin)': 'odakGeldi($event)',
  },
})
export class Tablo<T> {
  // ------------------------------------------------------------------ girdiler / çıktılar

  /** Erişilebilir ad (`aria-label`). */
  readonly etiket = input.required<string>();
  readonly sutunlar = input.required<readonly TabloSutunu<T>[]>();
  /** F3.4 `TemelStore` durumu (`store.liste.durum()`). */
  readonly kaynak = input.required<TabloKaynagi<T>>();
  /** Satırın kalıcı kimliği (seçim ve DOM izleme). */
  readonly satirKimligi = input.required<(satir: T) => string>();
  /** Kullanıcı düzeninin anahtarı (`kiralar.liste`); null → düzen kaydedilmez. */
  readonly tabloKodu = input<string | null>(null);
  /** Geçerli sunucu sıralaması (F3.4 `sorgu().sirala`). */
  readonly sirala = input<string | null>(null);
  /** Sayfanın varsayılan sıralaması (`ListeTanimi.varsayilanSirala`): kayıtlı tercih yalnız bu durumdayken uygulanır. */
  readonly varsayilanSirala = input<string | null>(null);
  readonly boyutSecenekleri = input<readonly number[]>([25, 50, 100, 200]);
  readonly secilebilir = input(false);
  /** Seçili satır kimlikleri (sayfalar arası korunur). `[(secim)]`. */
  readonly secim = model<readonly string[]>([]);
  readonly disaAktarma = input<DisaAktarma | null>(null);
  /** Bu satır sayısının üstünde satırlar sanallaştırılır. */
  readonly sanalEsik = input(60);

  readonly siralaDegisti = output<string | null>();
  readonly sayfaDegisti = output<number>();
  readonly boyutDegisti = output<number>();
  /** Enter ya da çift tıklama. */
  readonly satirAc = output<T>();
  readonly yenidenDene = output<void>();

  // ------------------------------------------------------------------ iç durum

  private readonly ceviri = ceviriFonksiyonu();
  private readonly depo = tabloDuzeniDeposuCoz();
  private readonly yikim = inject(DestroyRef);
  private readonly kaydirici = viewChild<ElementRef<HTMLDivElement>>('kaydirici');
  private readonly hucreSablonlari = contentChildren<TabloHucre<T>>(TabloHucre);

  protected readonly SECIM = SECIM_SUTUNU;
  protected readonly iskeletSatirlari = Array.from({ length: ISKELET_SATIRI }, (_, i) => i);

  protected readonly hal = computed(() => kaynakCoz(this.kaynak()));
  protected readonly satirlar = computed(() => this.hal().satirlar);

  /** Kullanıcı düzeni; tanımlar değişince kayıtlıyla yeniden uzlaşır. */
  private readonly duzen = linkedSignal<readonly TabloSutunu<T>[], TabloDuzeni>({
    source: this.sutunlar,
    computation: (sutunlar, onceki) => duzeniBirlestir(sutunlar, onceki?.value ?? null),
  });
  private readonly boyutlama = signal<ColumnSizingInfoState>(BOS_BOYUTLAMA);
  private kaydedilenAnahtar: string | null = null;
  private kayitZamanlayici: ReturnType<typeof setTimeout> | null = null;
  protected readonly kayitHatasi = signal(false);
  protected readonly duyuru = signal('');

  protected readonly siralama = computed(() => siralamaCoz(this.sutunlar(), this.sirala()));

  private readonly tanstackSutunlari = computed<ColumnDef<T, unknown>[]>(() => {
    const tanimlar: ColumnDef<T, unknown>[] = this.sutunlar().map((s) => ({
      id: s.kod,
      accessorFn: (satir: T) => s.deger(satir),
      header: s.baslik,
      minSize: enAzGenislik(s),
      maxSize: TABLO_SINIRLARI.enFazlaGenislik,
      enableHiding: gizlenebilirMi(s),
      enableResizing: true,
    }));
    if (!this.secilebilir()) return tanimlar;
    return [
      {
        id: SECIM_SUTUNU,
        size: SECIM_SUTUNU_GENISLIGI,
        minSize: SECIM_SUTUNU_GENISLIGI,
        maxSize: SECIM_SUTUNU_GENISLIGI,
        enableResizing: false,
        enableHiding: false,
      },
      ...tanimlar,
    ];
  });

  private readonly tanstack = createTable<T>({
    data: [],
    columns: [],
    state: {},
    onStateChange: () => undefined,
    renderFallbackValue: null,
    getCoreRowModel: getCoreRowModel(),
  });

  /** TanStack tablosu (her okumada güncel seçeneklerle; nesne aynı, bu yüzden `equal` yok). */
  private readonly tablo = computed(
    () => {
      const durum = tanstackDurumu(this.sutunlar(), this.duzen(), this.secilebilir());
      this.tanstack.setOptions((onceki) => ({
        ...onceki,
        columns: this.tanstackSutunlari(),
        state: { ...this.tanstack.initialState, ...durum, columnSizingInfo: this.boyutlama() },
        enableColumnResizing: true,
        columnResizeMode: 'onChange',
        manualSorting: true,
        manualPagination: true,
        manualFiltering: true,
        onColumnSizingChange: (u: Updater<Record<string, number>>) =>
          this.genislikDegisti(functionalUpdate(u, durum.columnSizing)),
        onColumnSizingInfoChange: (u: Updater<ColumnSizingInfoState>) =>
          this.boyutlamaDegisti(functionalUpdate(u, this.boyutlama())),
      }));
      return this.tanstack;
    },
    { equal: () => false },
  );

  protected readonly gorunurSutunlar = computed<readonly GorunurSutun<T>[]>(() => {
    const tablo = this.tablo();
    const tanim = new Map(this.sutunlar().map((s) => [s.kod, s] as const));
    const basliklar = tablo.getHeaderGroups()[0]?.headers ?? [];
    return basliklar.map((baslik) => {
      const kolon = baslik.column;
      const s = tanim.get(kolon.id) ?? null;
      const sabit = kolon.getIsPinned() === 'left';
      return {
        kod: kolon.id,
        tanim: s,
        baslik,
        genislik: kolon.getSize(),
        sol: sabit ? kolon.getStart('left') : null,
        sonSabit: sabit && kolon.getIsLastColumn('left'),
        hiza:
          s === null
            ? 'orta'
            : (s.hizala ?? (s.tur === 'para' || s.tur === 'sayi' ? 'son' : 'bas')),
        siralanabilir: s !== null && s.sirala !== undefined && s.sirala !== false,
        tasinabilir: s !== null && !s.sabit,
      };
    });
  });

  protected readonly toplamGenislik = computed(() =>
    this.gorunurSutunlar().reduce((t, s) => t + s.genislik, 0),
  );
  protected readonly sabitGenislik = computed(() =>
    this.gorunurSutunlar()
      .filter((s) => s.sol !== null)
      .reduce((t, s) => t + s.genislik, 0),
  );

  protected readonly seciciOgeleri = computed<readonly SutunSeciciOgesi[]>(() => {
    const tanim = new Map(this.sutunlar().map((s) => [s.kod, s] as const));
    const liste = this.duzen().sutunlar;
    return liste.map((d, i) => {
      const s = tanim.get(d.kod);
      const tasinabilir = s !== undefined && !s.sabit;
      const onceki = tanim.get(liste[i - 1]?.kod ?? '');
      const sonraki = tanim.get(liste[i + 1]?.kod ?? '');
      return {
        kod: d.kod,
        baslik: s?.baslik ?? d.kod,
        gorunur: d.gorunur,
        gizlenebilir: s !== undefined && gizlenebilirMi(s),
        oncekiyeTasinabilir: tasinabilir && onceki !== undefined && !onceki.sabit,
        sonrakiyeTasinabilir: tasinabilir && sonraki !== undefined,
      };
    });
  });

  private readonly sablonlar = computed(
    () => new Map(this.hucreSablonlari().map((h) => [h.kod(), h.sablon] as const)),
  );

  // ------------------------------------------------------------------ sanallaştırma

  private readonly satirPx = signal(32);
  private readonly baslikPx = signal(32);
  private readonly sanalSurum = signal(0);
  private sanal: Virtualizer<HTMLDivElement, HTMLTableRowElement> | null = null;
  protected readonly sanalMi = computed(() => this.satirlar().length > this.sanalEsik());

  protected readonly cizim = computed(() => {
    this.sanalSurum();
    const satirlar = this.satirlar();
    const kimlik = this.satirKimligi();
    const olustur = (index: number): CizilenSatir<T> => ({
      satir: satirlar[index],
      index,
      kimlik: kimlik(satirlar[index]),
    });
    const sanal = this.sanal;
    if (!this.sanalMi()) return { satirlar: satirlar.map((_, i) => olustur(i)), ust: 0, alt: 0 };
    if (sanal === null || sanal.options.count !== satirlar.length) {
      // Sanallaştırıcı henüz güncellenmedi (aynı tikteki effect'te güncellenir): tüm sayfayı değil
      // ilk ekranı çiz, geri kalanı boşlukla tut — 200 × 49 hücre tek karede çizilmesin.
      const ilk = Math.min(satirlar.length, 40);
      return {
        satirlar: satirlar.slice(0, ilk).map((_, i) => olustur(i)),
        ust: 0,
        alt: (satirlar.length - ilk) * this.satirPx(),
      };
    }
    const ogeler = sanal.getVirtualItems();
    if (ogeler.length === 0) return { satirlar: [], ust: 0, alt: 0 };
    const kenar = sanal.options.scrollMargin;
    const ilk = ogeler[0];
    const son = ogeler[ogeler.length - 1];
    return {
      satirlar: ogeler.filter((o) => o.index < satirlar.length).map((o) => olustur(o.index)),
      ust: Math.max(0, ilk.start - kenar),
      alt: Math.max(0, sanal.getTotalSize() - (son.end - kenar)),
    };
  });

  // ------------------------------------------------------------------ seçim

  protected readonly seciliKume = computed(() => new Set(this.secim()));
  protected readonly sayfaSecimi = computed<'hepsi' | 'bazi' | 'yok'>(() => {
    const kume = this.seciliKume();
    const kimlik = this.satirKimligi();
    const satirlar = this.satirlar();
    const secili = satirlar.filter((s) => kume.has(kimlik(s))).length;
    if (secili === 0) return 'yok';
    return secili === satirlar.length ? 'hepsi' : 'bazi';
  });

  // ------------------------------------------------------------------ klavye

  /** Aktif hücre: satır 0 başlık, 1..n veri (sayfadaki sıra + 1). */
  protected readonly aktif = signal<HucreKonumu>({ satir: 0, sutun: 0 });
  private odakBekliyor = false;
  /** Sekme durağı: aktif satır sanal pencerenin dışındaysa başlıktaki aynı sütun (Tab ızgarayı kaçırmasın). */
  protected readonly sekmeDuragi = computed<HucreKonumu>(() => {
    const k = konumKirp(this.aktif(), this.izgaraBoyutu());
    if (k.satir === 0) return k;
    const cizili = this.cizim().satirlar.some((c) => c.index === k.satir - 1);
    return cizili ? k : { satir: 0, sutun: k.sutun };
  });
  private readonly izgaraBoyutu = computed(() => ({
    satirSayisi: this.satirlar().length + 1,
    sutunSayisi: this.gorunurSutunlar().length,
    sayfaAdimi: Math.max(
      1,
      Math.floor(
        ((this.kaydirici()?.nativeElement.clientHeight || 320) - this.baslikPx()) / this.satirPx(),
      ),
    ),
  }));

  /** `aria-rowindex` tabanı: sunucu sayfalamasında önceki sayfaların satırları. */
  protected readonly satirOfseti = computed(() => {
    const s = this.hal().sayfa;
    return s === null ? 0 : (s.sayfa - 1) * s.boyut;
  });
  protected readonly toplamSatir = computed(
    () => this.hal().sayfa?.toplam ?? this.satirlar().length,
  );

  constructor() {
    // Kullanıcı düzeni: tablo kodu gelince bir kez okunur.
    effect(() => {
      const kod = this.tabloKodu();
      untracked(() => this.duzenYukle(kod));
    });

    // Yeni sayfa/sıralama: dikey kaydırma başa döner, aktif hücre ızgarada kalır.
    let oncekiSorgu = '';
    effect(() => {
      const sorgu = `${this.hal().sayfa?.sayfa ?? ''}|${this.sirala() ?? ''}`;
      const boyut = this.izgaraBoyutu();
      untracked(() => {
        this.aktif.update((k) => konumKirp(k, boyut));
        if (sorgu === oncekiSorgu) return;
        oncekiSorgu = sorgu;
        const el = this.kaydirici()?.nativeElement;
        if (el !== undefined && el.scrollTop > 0) el.scrollTop = 0;
      });
    });

    // Satır sayısı/yüksekliği değişince sanallaştırıcı güncellenir.
    effect(() => {
      const sayi = this.satirlar().length;
      const sanalMi = this.sanalMi();
      const px = this.satirPx();
      const baslik = this.baslikPx();
      untracked(() => {
        if (this.sanal === null) return;
        this.sanal.setOptions(this.sanalSecenekleri(sayi, sanalMi, px, baslik));
        this.sanal._willUpdate();
        this.sanalSurum.update((n) => n + 1);
      });
    });

    afterNextRender(() => {
      const el = this.kaydirici()?.nativeElement;
      if (el === undefined) return;
      this.satirPx.set(this.cssPx(el, '--rc-tablo-satir', 32));
      this.baslikPx.set(
        el.querySelector('thead')?.getBoundingClientRect().height || this.satirPx(),
      );
      this.sanal = new Virtualizer(
        this.sanalSecenekleri(
          this.satirlar().length,
          this.sanalMi(),
          this.satirPx(),
          this.baslikPx(),
        ),
      );
      const temizle = this.sanal._didMount();
      this.sanal._willUpdate();
      this.sanalSurum.update((n) => n + 1);
      this.yikim.onDestroy(temizle);
    });

    afterEveryRender(() => {
      if (this.odakBekliyor) this.odakla();
    });

    this.yikim.onDestroy(() => this.bekleyenKaydiGonder());
  }

  // ------------------------------------------------------------------ şablon yardımcıları

  protected sablon(kod: string) {
    return this.sablonlar().get(kod) ?? null;
  }

  protected baglam(satir: T, sutun: TabloSutunu<T>): TabloHucreBaglami<T> {
    return { $implicit: satir, deger: sutun.deger(satir) };
  }

  protected sayiDegeri(sutun: TabloSutunu<T>, satir: T): number | null {
    const d = sutun.deger(satir);
    return typeof d === 'number' && Number.isFinite(d) ? d : null;
  }

  protected tarihDegeri(sutun: TabloSutunu<T>, satir: T): Date | string | number | null {
    const d = sutun.deger(satir);
    return d instanceof Date || typeof d === 'string' || typeof d === 'number' ? d : null;
  }

  protected metinDegeri(sutun: TabloSutunu<T>, satir: T): string {
    const d = sutun.deger(satir);
    return d === null || d === undefined ? '' : String(d);
  }

  protected paraBirimi(sutun: TabloSutunu<T>, satir: T): string {
    const b = sutun.paraBirimi;
    return typeof b === 'function' ? b(satir) : (b ?? 'TRY');
  }

  protected ariaSirala(kod: string) {
    return ariaSiralama(this.siralama(), kod);
  }

  protected sekmeSirasi(satir: number, sutun: number): 0 | -1 {
    const d = this.sekmeDuragi();
    return d.satir === satir && d.sutun === sutun ? 0 : -1;
  }

  protected disaAktarmaAdresi(bicim: DisaAktarmaBicimi): string {
    const d = this.disaAktarma();
    return d === null ? '' : disaAktarmaAdresi(d, bicim);
  }

  // ------------------------------------------------------------------ sıralama

  protected siralaTikla(kod: string): void {
    const yeni = sonrakiSiralama(this.sutunlar(), this.siralama(), kod);
    this.siralaDegisti.emit(siralamaMetni(this.sutunlar(), yeni));
    this.duzenDegistir(siralamaAyarla(this.duzen(), yeni));
    const s = this.sutunlar().find((x) => x.kod === kod);
    this.duyuru.set(
      yeni === null
        ? this.ceviri('tablo.duyuru.siralamaYok')
        : this.ceviri(yeni.azalan ? 'tablo.duyuru.azalan' : 'tablo.duyuru.artan', {
            baslik: s?.baslik ?? kod,
          }),
    );
  }

  // ------------------------------------------------------------------ sütun düzeni

  protected gorunurlukDegistir(kod: string, gorunur: boolean): void {
    this.duzenDegistir(gorunurlukAyarla(this.sutunlar(), this.duzen(), kod, gorunur));
  }

  protected sutunKaydir(kod: string, yon: -1 | 1): void {
    this.duzenDegistir(sutunuKaydir(this.sutunlar(), this.duzen(), kod, yon));
  }

  protected varsayilanaDon(): void {
    this.zamanlayiciyiDurdur();
    const varsayilan = varsayilanDuzen(this.sutunlar());
    this.duzen.set(varsayilan);
    this.kaydedilenAnahtar = duzenAnahtari(varsayilan);
    this.kayitHatasi.set(false);
    if (this.sirala() !== this.varsayilanSirala()) this.siralaDegisti.emit(null);
    const kod = this.tabloKodu();
    if (kod !== null) {
      this.depo
        .sifirla(kod)
        .pipe(take(1))
        .subscribe({ error: () => this.kayitHatasi.set(true) });
    }
  }

  private genislikDegisti(yeni: Record<string, number>): void {
    let duzen = this.duzen();
    // Yalnız GERÇEKTEN değişen sütun yazılır; diğerleri `null` (tanım varsayılanı) kalır.
    const mevcut = tanstackDurumu(this.sutunlar(), duzen, false).columnSizing;
    for (const [kod, px] of Object.entries(yeni)) {
      if (kod !== SECIM_SUTUNU && kod in mevcut && mevcut[kod] !== px) {
        duzen = genislikAyarla(this.sutunlar(), duzen, kod, px);
      }
    }
    this.duzenDegistir(duzen);
    this.tablo();
  }

  private boyutlamaDegisti(bilgi: ColumnSizingInfoState): void {
    this.boyutlama.set(bilgi);
    this.tablo(); // TanStack durumu hemen eşitlensin: sonraki fare hareketi güncel başlangıcı görsün
  }

  protected boyutlamaBasla(olay: MouseEvent | TouchEvent, baslik: Header<T, unknown>): void {
    olay.stopPropagation();
    baslik.getResizeHandler()(olay);
  }

  protected genislikSifirla(kod: string): void {
    const s = this.sutunlar().find((x) => x.kod === kod);
    if (s === undefined) return;
    const duzen = this.duzen();
    this.duzenDegistir({
      ...duzen,
      sutunlar: duzen.sutunlar.map((d) => (d.kod === kod ? { ...d, genislik: null } : d)),
    });
  }

  // Sürükle-bırak (başlık) --------------------------------------------------------------
  private surukleen: string | null = null;

  protected surukleBasla(olay: DragEvent, kod: string): void {
    if (this.boyutlama().isResizingColumn !== false) {
      olay.preventDefault();
      return;
    }
    this.surukleen = kod;
    olay.dataTransfer?.setData('text/plain', kod);
    if (olay.dataTransfer) olay.dataTransfer.effectAllowed = 'move';
  }

  protected surukleUzerinde(olay: DragEvent, sutun: GorunurSutun<T>): void {
    if (this.surukleen !== null && sutun.tasinabilir && sutun.kod !== this.surukleen) {
      olay.preventDefault();
    }
  }

  protected birak(olay: DragEvent, sutun: GorunurSutun<T>): void {
    const kod = this.surukleen;
    this.surukleen = null;
    if (kod === null || !sutun.tasinabilir) return;
    olay.preventDefault();
    const hucre = olay.currentTarget as HTMLElement;
    const kutu = hucre.getBoundingClientRect();
    const konum = olay.clientX < kutu.left + kutu.width / 2 ? 'once' : 'sonra';
    this.duzenDegistir(sutunuYerlestir(this.sutunlar(), this.duzen(), kod, sutun.kod, konum));
  }

  protected surukleBitti(): void {
    this.surukleen = null;
  }

  // ------------------------------------------------------------------ seçim

  protected satirSecimi(kimlik: string, secili: boolean): void {
    const mevcut = this.secim();
    if (secili === mevcut.includes(kimlik)) return;
    this.secim.set(secili ? [...mevcut, kimlik] : mevcut.filter((k) => k !== kimlik));
  }

  protected sayfayiSec(secili: boolean): void {
    const kimlik = this.satirKimligi();
    const sayfadakiler = this.satirlar().map((s) => kimlik(s));
    const kume = new Set(this.secim());
    for (const k of sayfadakiler) {
      if (secili) kume.add(k);
      else kume.delete(k);
    }
    this.secim.set([...kume]);
  }

  // ------------------------------------------------------------------ klavye / odak

  protected odakGeldi(olay: FocusEvent): void {
    const konum = this.hucreKonumu(olay.target);
    if (konum !== null) this.aktif.set(konum);
  }

  protected klavye(olay: KeyboardEvent): void {
    const hedef = olay.target as HTMLElement;
    const konum = this.hucreKonumu(hedef);
    if (konum === null) return;
    const sutun = this.gorunurSutunlar()[konum.sutun];

    // Başlıkta Alt+←/→ genişlik, Alt+Shift+←/→ sıra.
    if (
      konum.satir === 0 &&
      olay.altKey &&
      (olay.key === 'ArrowLeft' || olay.key === 'ArrowRight')
    ) {
      olay.preventDefault();
      const yon = olay.key === 'ArrowLeft' ? -1 : 1;
      if (sutun?.tanim) {
        if (olay.shiftKey) this.klavyeIleTasi(sutun, yon);
        else this.klavyeIleBoyutla(sutun, yon);
      }
      return;
    }

    const yeni = hucreGezin(konum, olay, this.izgaraBoyutu());
    if (yeni !== null) {
      olay.preventDefault();
      this.odakTasi(yeni);
      return;
    }

    if (konum.satir === 0) return; // başlık: Enter/Space düğmenin kendi davranışı (sıralama)
    const satir = this.satirlar()[konum.satir - 1];
    if (satir === undefined) return;
    const denetim = hedef instanceof HTMLInputElement || hedef instanceof HTMLButtonElement;
    if (olay.key === 'Enter' && !denetim) {
      olay.preventDefault();
      this.satirAc.emit(satir);
    } else if (olay.key === ' ' && this.secilebilir() && !denetim) {
      olay.preventDefault();
      const kimlik = this.satirKimligi()(satir);
      this.satirSecimi(kimlik, !this.seciliKume().has(kimlik));
    }
  }

  protected ciftTik(satir: T): void {
    this.satirAc.emit(satir);
  }

  private klavyeIleBoyutla(sutun: GorunurSutun<T>, yon: -1 | 1): void {
    const tanim = sutun.tanim;
    if (tanim === null) return;
    const px = sutun.genislik + yon * KLAVYE_GENISLIK_ADIMI;
    this.duzenDegistir(genislikAyarla(this.sutunlar(), this.duzen(), tanim.kod, px));
    const yeni = this.duzen().sutunlar.find((d) => d.kod === tanim.kod)?.genislik ?? px;
    this.duyuru.set(this.ceviri('tablo.duyuru.genislik', { baslik: tanim.baslik, px: yeni }));
  }

  private klavyeIleTasi(sutun: GorunurSutun<T>, yon: -1 | 1): void {
    const tanim = sutun.tanim;
    if (tanim === null || tanim.sabit) return;
    // Görünür komşunun ötesine geç (gizli sütunlar arada kalabilir).
    let duzen = this.duzen();
    const gorunurKodlar = () => duzen.sutunlar.filter((d) => d.gorunur).map((d) => d.kod);
    const onceki = gorunurKodlar().indexOf(tanim.kod);
    for (let adim = duzen.sutunlar.length; adim > 0; adim--) {
      const sonraki = sutunuKaydir(this.sutunlar(), duzen, tanim.kod, yon);
      if (sonraki === duzen) break;
      duzen = sonraki;
      if (gorunurKodlar().indexOf(tanim.kod) !== onceki) break;
    }
    this.duzenDegistir(duzen);
    const yeniSira = this.gorunurSutunlar().findIndex((s) => s.kod === tanim.kod);
    if (yeniSira >= 0) this.odakTasi({ satir: 0, sutun: yeniSira });
    this.duyuru.set(
      this.ceviri('tablo.duyuru.tasindi', { baslik: tanim.baslik, sira: yeniSira + 1 }),
    );
  }

  private odakTasi(konum: HucreKonumu): void {
    this.aktif.set(konum);
    if (konum.satir > 0 && this.sanal !== null && this.sanalMi()) {
      const cizili = this.cizim().satirlar.some((c) => c.index === konum.satir - 1);
      if (!cizili) this.sanal.scrollToIndex(konum.satir - 1, { align: 'auto' });
    }
    this.odakBekliyor = true;
    this.odakla();
  }

  private odakla(): void {
    const { satir, sutun } = this.aktif();
    const hucre = this.kaydirici()?.nativeElement.querySelector<HTMLElement>(
      `[data-hucre="${satir}:${sutun}"]`,
    );
    if (hucre === null || hucre === undefined) return; // sanal satır henüz çizilmedi; sonraki çizimde denenir
    this.odakBekliyor = false;
    const hedef = hucre.querySelector<HTMLElement>('[data-odak]') ?? hucre;
    if (document.activeElement !== hedef) hedef.focus();
  }

  private hucreKonumu(hedef: EventTarget | null): HucreKonumu | null {
    if (!(hedef instanceof HTMLElement)) return null;
    const hucre = hedef.closest<HTMLElement>('[data-hucre]');
    const [satir, sutun] = (hucre?.dataset['hucre'] ?? '').split(':').map(Number);
    return Number.isInteger(satir) && Number.isInteger(sutun) ? { satir, sutun } : null;
  }

  // ------------------------------------------------------------------ kalıcılık

  private duzenYukle(kod: string | null): void {
    this.kaydedilenAnahtar = duzenAnahtari(this.duzen());
    if (kod === null) return;
    this.depo
      .oku(kod)
      .pipe(take(1))
      .subscribe({
        next: (kayitli) => {
          if (kayitli === null) return;
          const duzen = duzeniBirlestir(this.sutunlar(), kayitli);
          this.duzen.set(duzen);
          this.kaydedilenAnahtar = duzenAnahtari(duzen);
          // Kayıtlı sıralama tercihi yalnız sayfa varsayılandayken (URL açık sıralama taşımıyorsa).
          const tercih = siralamaMetni(this.sutunlar(), duzen.siralama[0] ?? null);
          if (
            tercih !== null &&
            this.sirala() === this.varsayilanSirala() &&
            tercih !== this.sirala()
          ) {
            this.siralaDegisti.emit(tercih);
          }
        },
        // Okunamayan düzen tabloyu durdurmaz: varsayılanla devam.
        error: () => undefined,
      });
  }

  private duzenDegistir(yeni: TabloDuzeni): void {
    if (yeni === this.duzen()) return;
    this.duzen.set(yeni);
    if (this.tabloKodu() === null) return;
    this.zamanlayiciyiDurdur();
    this.kayitZamanlayici = setTimeout(() => this.bekleyenKaydiGonder(), KAYIT_GECIKMESI_MS);
  }

  private bekleyenKaydiGonder(): void {
    this.zamanlayiciyiDurdur();
    const kod = this.tabloKodu();
    const duzen = this.duzen();
    const anahtar = duzenAnahtari(duzen);
    if (kod === null || anahtar === this.kaydedilenAnahtar) return;
    // Boyutlama sürerken yazma; bırakınca yazılır.
    if (this.boyutlama().isResizingColumn !== false) {
      this.kayitZamanlayici = setTimeout(() => this.bekleyenKaydiGonder(), KAYIT_GECIKMESI_MS);
      return;
    }
    this.kaydedilenAnahtar = anahtar;
    this.depo
      .kaydet(kod, duzen)
      .pipe(take(1))
      .subscribe({
        next: () => this.kayitHatasi.set(false),
        error: () => {
          this.kaydedilenAnahtar = null;
          this.kayitHatasi.set(true);
        },
      });
  }

  private zamanlayiciyiDurdur(): void {
    if (this.kayitZamanlayici !== null) clearTimeout(this.kayitZamanlayici);
    this.kayitZamanlayici = null;
  }

  // ------------------------------------------------------------------ iç yardımcılar

  private sanalSecenekleri(sayi: number, etkin: boolean, satirPx: number, baslikPx: number) {
    return {
      count: sayi,
      enabled: etkin,
      getScrollElement: () => this.kaydirici()?.nativeElement ?? null,
      estimateSize: () => satirPx,
      scrollMargin: baslikPx,
      scrollPaddingStart: baslikPx,
      overscan: 10,
      getItemKey: (i: number) => {
        const satir = this.satirlar()[i];
        return satir === undefined ? i : this.satirKimligi()(satir);
      },
      scrollToFn: elementScroll,
      observeElementRect,
      observeElementOffset,
      onChange: () => this.sanalSurum.update((n) => n + 1),
    };
  }

  private cssPx(el: HTMLElement, ad: string, varsayilan: number): number {
    const deger = getComputedStyle(el).getPropertyValue(ad).trim();
    const sayi = parseFloat(deger);
    if (!Number.isFinite(sayi)) return varsayilan;
    if (deger.endsWith('rem')) {
      return sayi * (parseFloat(getComputedStyle(document.documentElement).fontSize) || 16);
    }
    return deger.endsWith('px') ? sayi : varsayilan;
  }
}
