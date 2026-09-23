import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import type { PanelDonusSatiri, PanelOzetiYaniti } from '@core/api/ui-tipleri';
import { GonderimKilidi } from '@core/form/gonderim-kilidi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { sekmeBaglami } from '@core/sekme/sekme-durumu';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { BICIM_PIPELARI } from '@shared/bicim/bicim-pipe';

import {
  PANEL_SEKMELERI,
  type KpiKarti,
  type PanelSekme,
  type VadeKutusu,
  TAZELEME_DENETIM_MS,
  cikisEtkinSekme,
  donusEtkinSekme,
  kovaSatirlari,
  sayi,
  sekmeCoz,
  tazelemeZamaniMi,
  yazilabilirAlanMi,
  yuzde,
} from './panel-modeli';
import { PanelFinans } from './panel-finans';
import { PanelKpi } from './panel-kpi';
import { PANEL_TAHSILAT_KILIDI, PanelTahsilatFormu } from './panel-tahsilat-formu';
import { PanelStore } from './panel.store';

interface Cip {
  readonly sekme: PanelSekme;
  readonly sayi: number;
  readonly acil: boolean;
}

/**
 * Panel (F4.5) — Blazor `Home.razor` karşılığı: filo KPI + vade kademeleri, gecikme varsayılanlı dönüş ve
 * çıkış listeleri, finans özeti (yalnız sunucu gönderirse), "Tahsil Et" (yalnız satırda anahtar varsa).
 *
 * - Veri tek uçtan (`PanelStore`); izin kapıları SUNUCUDA — istemci rol/izin süzmez, yanıtta olmayanı çizmez.
 * - Sekme seçimi `?df=` / `?cf=` (Blazor sorgu sözleşmesi; F4.6 yönlendirmesi sorguyu taşır). Seçim yoksa kural
 *   her yüklemede yeniden işler (`donusEtkinSekme`).
 * - 2 dakikada bir tazelenir (Blazor `data-rc-tazele="120"`); kullanıcı bir alana yazarken ya da tahsilat formu
 *   açıkken ertelenir; uygulama sekmesi arkadayken yüklemez, öne gelince yeniler. Meta-refresh yok.
 */
@Component({
  selector: 'rc-panel-sayfasi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    FetchPolicy,
    PanelStore,
    { provide: PANEL_TAHSILAT_KILIDI, useFactory: () => new GonderimKilidi() },
  ],
  imports: [
    RouterLink,
    TranslocoPipe,
    ...BICIM_PIPELARI,
    PanelFinans,
    PanelKpi,
    PanelTahsilatFormu,
  ],
  templateUrl: './panel-sayfasi.html',
  styleUrl: './panel-sayfasi.scss',
})
export class PanelSayfasi {
  private readonly store = inject(PanelStore);
  private readonly politika = inject(FetchPolicy);
  private readonly router = inject(Router);
  private readonly rota = inject(ActivatedRoute);
  private readonly belge = inject(DOCUMENT);
  private readonly sekme = sekmeBaglami();
  private readonly t = ceviriFonksiyonu();
  /** Tüm satırların ortak tahsilat kilidi: istek uçarken tablodaki "Tahsil Et"ler ve "Yenile" pasif. */
  protected readonly tahsilatSuruyor = inject(PANEL_TAHSILAT_KILIDI).gonderiliyor;

  protected readonly ozet = this.store.ozet;
  protected readonly veri = this.ozet.veri;
  protected readonly sekmeler = PANEL_SEKMELERI;
  protected readonly sayi = sayi;

  private readonly sorgu = this.rota.snapshot.queryParamMap;
  /** Kullanıcının açık seçimi (ham); `null` = varsayılan kural. */
  protected readonly donusSecilen = signal<PanelSekme | null>(sekmeCoz(this.sorgu.get('df')));
  protected readonly cikisSecilen = signal<PanelSekme | null>(sekmeCoz(this.sorgu.get('cf')));

  /**
   * Tahsilat formu açık satırın AÇILIŞ ANINDAKİ kopyası; açıkken otomatik tazeleme bekler. Kopya bilinçli:
   * elle "Yenile" ya da sekmeye dönüş yeni anahtar getirse bile form açıldığı anahtarla gönderir — o arada
   * başka biri tahsil ettiyse sunucu 409 `mukerrer` verir (Blazor'daki bayat sekme davranışı); anahtar formun
   * altından sessizce değişip eski tutarla ikinci bir tahsilat yazılamaz.
   */
  protected readonly acikTahsilat = signal<PanelDonusSatiri | null>(null);

  protected readonly donusSekmesi = computed<PanelSekme>(() => {
    const v = this.veri();
    return v ? donusEtkinSekme(this.donusSecilen(), v.donusler) : 'bugun';
  });
  protected readonly cikisSekmesi = computed<PanelSekme>(() =>
    cikisEtkinSekme(this.cikisSecilen()),
  );

  protected readonly donusSatirlari = computed(() => {
    const v = this.veri();
    return v ? kovaSatirlari(v.donusler, this.donusSekmesi()) : [];
  });
  protected readonly cikisSatirlari = computed(() => {
    const v = this.veri();
    return v ? kovaSatirlari(v.cikislar, this.cikisSekmesi()) : [];
  });

  protected readonly donusCipleri = computed(() => this.cipler(this.veri()?.donusler));
  protected readonly cikisCipleri = computed(() => this.cipler(this.veri()?.cikislar));

  /** "İşlem" sütunu: sunucu herhangi bir dönüş satırına tahsilat verisi koyduysa (FinanceWrite). */
  protected readonly tahsilatSutunu = computed(() => {
    const d = this.veri()?.donusler;
    return d ? [...d.gecikmis, ...d.bugun, ...d.yarin].some((s) => !!s.tahsilat?.anahtar) : false;
  });

  /**
   * Açık form (kopya) — tazeleme sonrası satır görünen kovada artık yoksa form kapanır; istek uçarken ASLA
   * kapanmaz (sonuç ve bildirim kaybolmasın). Şablon bunu `rentalId` ile izlenen tek elemanlı listeyle çizer:
   * başka satır açılınca form bileşeni YENİDEN oluşur (adversarial F1: bayat tutar başka kiraya gidiyordu).
   */
  protected readonly acikTahsilatListesi = computed<readonly PanelDonusSatiri[]>(() => {
    const acik = this.acikTahsilat();
    if (acik === null) return [];
    if (this.tahsilatSuruyor()) return [acik];
    return this.donusSatirlari().some((s) => s.rentalId === acik.rentalId) ? [acik] : [];
  });

  protected readonly kpiKartlari = computed<readonly KpiKarti[]>(() => {
    const v = this.veri();
    return v ? this.kartlar(v) : [];
  });

  protected readonly vadeUyarisi = computed(() => {
    const vade = this.veri()?.vade;
    if (!vade) return null;
    const gecmis = sayi(vade.gecmisUyari) ?? 0;
    const yaklasan = sayi(vade.yaklasanUyari) ?? 0;
    const sikayet = sayi(vade.acikSikayet) ?? 0;
    return gecmis + yaklasan > 0 ? { gecmis, yaklasan, sikayet } : null;
  });

  private sonYukleme = 0;
  /** Yeni anahtarı beklenen açık formun kirası (M-A); panel verisi gelince kopya güncel satırla değişir. */
  private anahtarBekleyen: string | null = null;

  constructor() {
    this.politika.baglan({
      parametre: signal(null),
      yukle: () => {
        this.sonYukleme = Date.now();
        this.ozet.yukle(null);
      },
      sifirla: () => this.ozet.sifirla(),
      // Canlı pano: başka sekmede kira/tahsilat değişmiş olabilir.
      sekmeyeDonunce: 'yenile',
    });

    effect(() => {
      const satirlar = this.donusSatirlari();
      untracked(() => {
        const bekleyen = this.anahtarBekleyen;
        if (bekleyen === null || this.ozet.yukleniyor()) return;
        this.anahtarBekleyen = null;
        const guncel = satirlar.find((s) => s.rentalId === bekleyen);
        if (guncel?.tahsilat && this.acikTahsilat()?.rentalId === bekleyen)
          this.acikTahsilat.set(guncel);
      });
    });

    const zamanlayici = setInterval(() => this.tazelemeDenetle(), TAZELEME_DENETIM_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(zamanlayici));
  }

  protected yenile(): void {
    this.politika.yenile();
  }

  protected donusSec(sekme: PanelSekme): void {
    this.donusSecilen.set(sekme);
    if (!this.tahsilatSuruyor()) this.acikTahsilat.set(null);
    this.sorguyaYaz({ df: sekme });
  }

  protected cikisSec(sekme: PanelSekme): void {
    this.cikisSecilen.set(sekme);
    this.sorguyaYaz({ cf: sekme });
  }

  protected tahsilatAc(satir: PanelDonusSatiri): void {
    if (this.tahsilatSuruyor()) return;
    this.acikTahsilat.set(satir);
  }

  protected tahsilatKapat(): void {
    if (this.tahsilatSuruyor()) return;
    this.acikTahsilat.set(null);
  }

  /**
   * 3. tur M-A: başka bir tahsilat yazılmış, açık formun tutarı YAZILMADI. Form AÇIK kalır; panel yeniden yüklenir
   * ve açık kopya satırın GÜNCEL hâliyle (yeni anahtar) değiştirilir — kullanıcı bakiyeye bakıp bilinçli gönderir.
   */
  protected tahsilatAnahtariniTazele(): void {
    this.anahtarBekleyen = this.acikTahsilat()?.rentalId ?? null;
    this.politika.yenile();
  }

  /** 2xx ya da 409 `mukerrer` sonrası: form kapanır, panel yeniden yüklenir (yeni bakiye → yeni anahtar). */
  protected tahsilatSonrasi(): void {
    this.acikTahsilat.set(null);
    this.politika.yenile();
  }

  protected sekmeEtiketi(sekme: PanelSekme): string {
    return this.t(`panel.sekme.${sekme}`);
  }

  private tazelemeDenetle(): void {
    const tazele = tazelemeZamaniMi({
      gecen: Date.now() - this.sonYukleme,
      belgeGorunur: this.belge.visibilityState !== 'hidden',
      sekmeAktif: this.sekme.aktif(),
      yaziyor: this.acikTahsilat() !== null || yazilabilirAlanMi(this.belge.activeElement),
      mesgul: this.ozet.yukleniyor() || this.tahsilatSuruyor(),
    });
    if (tazele) this.politika.yenile();
  }

  private sorguyaYaz(parametre: Record<string, string>): void {
    void this.router.navigate([], {
      relativeTo: this.rota,
      queryParams: parametre,
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  private cipler(
    kovalar:
      | { gecikmis: readonly unknown[]; bugun: readonly unknown[]; yarin: readonly unknown[] }
      | undefined,
  ): readonly Cip[] {
    if (!kovalar) return [];
    return [
      { sekme: 'gec', sayi: kovalar.gecikmis.length, acil: kovalar.gecikmis.length > 0 },
      { sekme: 'bugun', sayi: kovalar.bugun.length, acil: false },
      { sekme: 'yarin', sayi: kovalar.yarin.length, acil: false },
    ];
  }

  private kartlar(v: PanelOzetiYaniti): readonly KpiKarti[] {
    const k = v.kpi;
    const toplam = sayi(k.toplamArac) ?? 0;
    const vadeKutulari = (kademe: {
      yediGun: number | string;
      otuzGun: number | string;
      gecmis: number | string;
    }): VadeKutusu[] => {
      const otuz = sayi(kademe.otuzGun) ?? 0;
      const gecmis = sayi(kademe.gecmis) ?? 0;
      return [
        {
          sayi: sayi(kademe.yediGun) ?? 0,
          etiket: this.t('panel.vade.yediGun'),
          href: '/vade',
          ton: 'notr',
        },
        {
          sayi: otuz,
          etiket: this.t('panel.vade.otuzGun'),
          href: '/vade',
          ton: otuz > 0 ? 'uyari' : 'notr',
        },
        {
          sayi: gecmis,
          etiket: this.t('panel.vade.gecmis'),
          href: '/vade',
          ton: gecmis > 0 ? 'hata' : 'notr',
        },
      ];
    };
    const kart = (
      kod: KpiKarti['kod'],
      etiket: string,
      deger: number | string,
      altBaslik: string | null,
      kutular: VadeKutusu[],
      alt?: string,
    ): KpiKarti => {
      const y = yuzde(deger, toplam);
      return {
        kod,
        etiket,
        sayi: sayi(deger) ?? 0,
        yuzde: y,
        alt: alt ?? this.t('panel.kpi.toplamdan', { yuzde: y }),
        altBaslik,
        kutular,
      };
    };

    const kmGecen = sayi(k.kmGecenBakim) ?? 0;
    const gorulmeyen = sayi(k.gorulmeyenRezervasyon) ?? 0;
    const ekKutular: VadeKutusu[] = [
      {
        sayi: kmGecen,
        etiket: this.t('panel.vade.kmGecenBakim'),
        href: '/raporlar/periyodik-servis',
        ton: kmGecen > 0 ? 'hata' : 'notr',
      },
      {
        sayi: gorulmeyen,
        etiket: this.t('panel.vade.gorulmeyenRezervasyon'),
        rota: '/rezervasyonlar', // F5.4: SPA rotası (sunucu yönlendirmesine düşmez)
        ton: gorulmeyen > 0 ? 'uyari' : 'notr',
      },
    ];
    // Site talebi yalnız Web Sitesi modülü açıkken gelir (kapalıyken hep 0 olurdu; kutu gürültü).
    if (k.siteTalebi) {
      const yeni = sayi(k.siteTalebi.yeni) ?? 0;
      const enEski = sayi(k.siteTalebi.enEskiGun);
      ekKutular.push({
        sayi: yeni,
        etiket: this.t('panel.vade.siteTalebi'),
        href: '/gelen-talepler?durum=0',
        ton: yeni > 0 ? ((enEski ?? 0) >= 3 ? 'hata' : 'uyari') : 'notr',
        ipucu:
          enEski !== null
            ? this.t('panel.vade.siteTalebiEnEski', { gun: enEski })
            : this.t('panel.vade.siteTalebiYok'),
      });
    }

    return [
      kart(
        'kirada',
        this.t('panel.kpi.kirada'),
        k.kirada,
        this.t('panel.vade.trafik'),
        vadeKutulari(v.vade.trafik),
      ),
      kart(
        'musait',
        this.t('panel.kpi.musait'),
        k.musait,
        this.t('panel.vade.kasko'),
        vadeKutulari(v.vade.kasko),
      ),
      kart(
        'serviste',
        this.t('panel.kpi.serviste'),
        k.serviste,
        this.t('panel.vade.muayene'),
        vadeKutulari(v.vade.muayene),
      ),
      kart(
        'rezervasyon',
        this.t('panel.kpi.acikRezervasyon'),
        k.acikRezervasyon,
        null,
        ekKutular,
        this.t('panel.kpi.bekleyenRezervasyon'),
      ),
    ];
  }
}
