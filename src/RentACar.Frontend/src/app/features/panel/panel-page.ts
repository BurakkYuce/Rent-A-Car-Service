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

import type { PanelReturnRow, PanelSummaryResponse } from '@core/api/ui-tipleri';
import { SubmitLock } from '@core/form/submit-lock';
import { translationFunction } from '@core/i18n/ceviri';
import { SessionService } from '@core/oturum/session-service';
import { tabContext } from '@core/sekme/tab-state';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { FORMAT_PIPES } from '@shared/bicim/bicim-pipe';
import { Icon } from '@shared/ikon/icon';
import type { IkonAdi } from '@shared/ikon/ikon-kaydi';
import { PlateChipComponent } from '@shared/plaka/plaka';
import { StatusSignCardComponent, type FleetStatus } from '@shared/tabela-karti/tabela-karti';

import { PageBand } from '../../kabuk/sayfa-bandi/page-band';
import { ReminderList } from './reminder-list';
import { QUICK_ACTIONS, QuickActions, type HizliIslem } from './quick-actions';
import {
  PANEL_TABS,
  type KpiKarti,
  type PanelTab,
  type VadeKutusu,
  type VadeSatiri,
  dayTitle,
  REFRESH_CHECK_MS,
  pickupActiveTab,
  returnActiveTab,
  bucketRows,
  count,
  resolveTab,
  isRefreshDue,
  isWritableField,
  percent,
} from './panel-modeli';
import { PanelFinance } from './panel-finance';
import { PANEL_COLLECTION_LOCK, PanelCollectionForm } from './panel-collection-form';
import { PanelStore } from './panel.store';

interface Cip {
  readonly sekme: PanelTab;
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
 * - Görünüm (Yol v2 §8 "Panel"): sayfa bandı, 4 tabela kartı; solda Dönüşler + Çıkışlar, sağ sütunda
 *   Hatırlatmalar (vade kademeleri + uyarı rozetleri) ve Hızlı işlemler. Hepsi aynı özet yanıtından.
 */
@Component({
  selector: 'rc-panel-sayfasi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    FetchPolicy,
    PanelStore,
    { provide: PANEL_COLLECTION_LOCK, useFactory: () => new SubmitLock() },
  ],
  imports: [
    RouterLink,
    TranslocoPipe,
    ...FORMAT_PIPES,
    PanelFinance,
    PanelCollectionForm,
    PageBand,
    StatusSignCardComponent,
    PlateChipComponent,
    Icon,
    ReminderList,
    QuickActions,
  ],
  templateUrl: './panel-page.html',
  styleUrl: './panel-page.scss',
})
export class PanelPage {
  private readonly store = inject(PanelStore);
  private readonly policy = inject(FetchPolicy);
  private readonly router = inject(Router);
  private readonly rota = inject(ActivatedRoute);
  private readonly belge = inject(DOCUMENT);
  private readonly sekme = tabContext();
  private readonly t = translationFunction();
  private readonly oturum = inject(SessionService);
  /** Tüm satırların ortak tahsilat kilidi: istek uçarken tablodaki "Tahsil Et"ler ve "Yenile" pasif. */
  protected readonly isCollectionInProgress = inject(PANEL_COLLECTION_LOCK).gonderiliyor;

  protected readonly ozet = this.store.ozet;
  protected readonly veri = this.ozet.veri;
  protected readonly tabs = PANEL_TABS;
  protected readonly count = count;
  /** Filo durum sözlüğü (Yol v2 §1.2): kart kodu → tabela rengi ve ikonu. */
  protected readonly signState: Readonly<Record<KpiKarti['kod'], FleetStatus>> = {
    kirada: 'kirada',
    musait: 'bosta',
    serviste: 'serviste',
    rezervasyon: 'rezerve',
  };
  protected readonly signIcon: Readonly<Record<KpiKarti['kod'], IkonAdi>> = {
    kirada: 'key',
    musait: 'car',
    serviste: 'tool',
    rezervasyon: 'calendar',
  };

  private readonly query = this.rota.snapshot.queryParamMap;
  /** Kullanıcının açık seçimi (ham); `null` = varsayılan kural. */
  protected readonly returnSelected = signal<PanelTab | null>(resolveTab(this.query.get('df')));
  protected readonly pickupSelected = signal<PanelTab | null>(resolveTab(this.query.get('cf')));

  /**
   * Tahsilat formu açık satırın AÇILIŞ ANINDAKİ kopyası; açıkken otomatik tazeleme bekler. Kopya bilinçli:
   * elle "Yenile" ya da sekmeye dönüş yeni anahtar getirse bile form açıldığı anahtarla gönderir — o arada
   * başka biri tahsil ettiyse sunucu 409 `mukerrer` verir (Blazor'daki bayat sekme davranışı); anahtar formun
   * altından sessizce değişip eski tutarla ikinci bir tahsilat yazılamaz.
   */
  protected readonly openCollection = signal<PanelReturnRow | null>(null);

  protected readonly returnTab = computed<PanelTab>(() => {
    const v = this.veri();
    return v ? returnActiveTab(this.returnSelected(), v.donusler) : 'bugun';
  });
  protected readonly pickupTab = computed<PanelTab>(() => pickupActiveTab(this.pickupSelected()));

  protected readonly returnRows = computed(() => {
    const v = this.veri();
    return v ? bucketRows(v.donusler, this.returnTab()) : [];
  });
  protected readonly pickupRows = computed(() => {
    const v = this.veri();
    return v ? bucketRows(v.cikislar, this.pickupTab()) : [];
  });

  protected readonly returnChips = computed(() => this.chips(this.veri()?.donusler));
  protected readonly pickupChips = computed(() => this.chips(this.veri()?.cikislar));

  /** "İşlem" sütunu: sunucu herhangi bir dönüş satırına tahsilat verisi koyduysa (FinanceWrite). */
  protected readonly collectionColumn = computed(() => {
    const d = this.veri()?.donusler;
    return d ? [...d.gecikmis, ...d.bugun, ...d.yarin].some((s) => !!s.tahsilat?.anahtar) : false;
  });

  /**
   * Açık form (kopya) — tazeleme sonrası satır görünen kovada artık yoksa form kapanır; istek uçarken ASLA
   * kapanmaz (sonuç ve bildirim kaybolmasın). Şablon bunu `rentalId` ile izlenen tek elemanlı listeyle çizer:
   * başka satır açılınca form bileşeni YENİDEN oluşur (adversarial F1: bayat tutar başka kiraya gidiyordu).
   */
  protected readonly openCollectionList = computed<readonly PanelReturnRow[]>(() => {
    const open = this.openCollection();
    if (open === null) return [];
    if (this.isCollectionInProgress()) return [open];
    return this.returnRows().some((s) => s.rentalId === open.rentalId) ? [open] : [];
  });

  protected readonly kpiCards = computed<readonly KpiKarti[]>(() => {
    const v = this.veri();
    return v ? this.cards(v) : [];
  });
  protected readonly dueList = computed<readonly VadeSatiri[]>(() => {
    const v = this.veri();
    return v ? this.dueRows(v) : [];
  });
  protected readonly reminderBadges = computed<readonly VadeKutusu[]>(() => {
    const v = this.veri();
    return v ? this.badges(v) : [];
  });

  /** Band alt metni: bugünün tarihi (sunucunun İstanbul günü) · toplam araç. */
  protected readonly bannerSubtext = computed(() => {
    const v = this.veri();
    if (!v) return null;
    return this.t('panel.bant.altMetin', {
      tarih: dayTitle(v.bugun),
      arac: count(v.kpi.toplamArac) ?? 0,
    });
  });

  /** Hızlı işlemler: yalnız kullanıcının açabileceği ekranlar (rota kapısıyla aynı izin). */
  protected readonly quickActions = computed<readonly HizliIslem[]>(() =>
    QUICK_ACTIONS.filter((h) => this.oturum.izinVar(h.izin)).map((h) => ({
      etiket: this.t(h.etiket),
      ikon: h.ikon,
      rota: h.rota,
    })),
  );

  protected readonly dueWarning = computed(() => {
    const due = this.veri()?.vade;
    if (!due) return null;
    const history = count(due.gecmisUyari) ?? 0;
    const upcoming = count(due.yaklasanUyari) ?? 0;
    const complaint = count(due.acikSikayet) ?? 0;
    return history + upcoming > 0
      ? { gecmis: history, yaklasan: upcoming, sikayet: complaint }
      : null;
  });

  private lastLoad = 0;
  /** Yeni anahtarı beklenen açık formun kirası (M-A); panel verisi gelince kopya güncel satırla değişir. */
  private keyPending: string | null = null;

  constructor() {
    this.policy.connect({
      parametre: signal(null),
      yukle: () => {
        this.lastLoad = Date.now();
        this.ozet.yukle(null);
      },
      sifirla: () => this.ozet.reset(),
      // Canlı pano: başka sekmede kira/tahsilat değişmiş olabilir.
      sekmeyeDonunce: 'yenile',
    });

    effect(() => {
      const rows = this.returnRows();
      untracked(() => {
        const pending = this.keyPending;
        if (pending === null || this.ozet.isLoading()) return;
        this.keyPending = null;
        const current = rows.find((s) => s.rentalId === pending);
        if (current?.tahsilat && this.openCollection()?.rentalId === pending)
          this.openCollection.set(current);
      });
    });

    const timer = setInterval(() => this.checkRefresh(), REFRESH_CHECK_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  protected yenile(): void {
    this.policy.yenile();
  }

  protected selectReturn(tab: PanelTab): void {
    this.returnSelected.set(tab);
    if (!this.isCollectionInProgress()) this.openCollection.set(null);
    this.writeToQuery({ df: tab });
  }

  protected selectPickup(tab: PanelTab): void {
    this.pickupSelected.set(tab);
    this.writeToQuery({ cf: tab });
  }

  protected openCollectionAction(row: PanelReturnRow): void {
    if (this.isCollectionInProgress()) return;
    this.openCollection.set(row);
  }

  protected closeCollection(): void {
    if (this.isCollectionInProgress()) return;
    this.openCollection.set(null);
  }

  /**
   * 3. tur M-A: başka bir tahsilat yazılmış, açık formun tutarı YAZILMADI. Form AÇIK kalır; panel yeniden yüklenir
   * ve açık kopya satırın GÜNCEL hâliyle (yeni anahtar) değiştirilir — kullanıcı bakiyeye bakıp bilinçli gönderir.
   */
  protected refreshCollectionKey(): void {
    this.keyPending = this.openCollection()?.rentalId ?? null;
    this.policy.yenile();
  }

  /** 2xx ya da 409 `mukerrer` sonrası: form kapanır, panel yeniden yüklenir (yeni bakiye → yeni anahtar). */
  protected afterCollection(): void {
    this.openCollection.set(null);
    this.policy.yenile();
  }

  protected tabLabel(tab: PanelTab): string {
    return this.t(`panel.sekme.${tab}`);
  }

  private checkRefresh(): void {
    const refresh = isRefreshDue({
      gecen: Date.now() - this.lastLoad,
      belgeGorunur: this.belge.visibilityState !== 'hidden',
      sekmeAktif: this.sekme.aktif(),
      yaziyor: this.openCollection() !== null || isWritableField(this.belge.activeElement),
      mesgul: this.ozet.isLoading() || this.isCollectionInProgress(),
    });
    if (refresh) this.policy.yenile();
  }

  private writeToQuery(parameter: Record<string, string>): void {
    void this.router.navigate([], {
      relativeTo: this.rota,
      queryParams: parameter,
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  private chips(
    buckets:
      | { gecikmis: readonly unknown[]; bugun: readonly unknown[]; yarin: readonly unknown[] }
      | undefined,
  ): readonly Cip[] {
    if (!buckets) return [];
    return [
      { sekme: 'gec', sayi: buckets.gecikmis.length, acil: buckets.gecikmis.length > 0 },
      { sekme: 'bugun', sayi: buckets.bugun.length, acil: false },
      { sekme: 'yarin', sayi: buckets.yarin.length, acil: false },
    ];
  }

  private cards(v: PanelSummaryResponse): readonly KpiKarti[] {
    const k = v.kpi;
    const total = count(k.toplamArac) ?? 0;
    const card = (
      code: KpiKarti['kod'],
      label: string,
      value: number | string,
      sub?: string,
    ): KpiKarti => {
      const y = percent(value, total);
      return {
        kod: code,
        etiket: label,
        sayi: count(value) ?? 0,
        yuzde: y,
        alt: sub ?? this.t('panel.kpi.toplamdan', { yuzde: y }),
      };
    };
    return [
      card('kirada', this.t('panel.kpi.kirada'), k.kirada),
      card('musait', this.t('panel.kpi.musait'), k.musait),
      card('serviste', this.t('panel.kpi.serviste'), k.serviste),
      card(
        'rezervasyon',
        this.t('panel.kpi.acikRezervasyon'),
        k.acikRezervasyon,
        this.t('panel.kpi.bekleyenRezervasyon'),
      ),
    ];
  }

  /** Vade kademeleri (trafik, kasko, muayene) — hepsi vade panosuna gider (F9.3: SPA rotası). */
  private dueRows(v: PanelSummaryResponse): readonly VadeSatiri[] {
    const row = (
      code: VadeSatiri['kod'],
      label: string,
      tier: { yediGun: number | string; otuzGun: number | string; gecmis: number | string },
    ): VadeSatiri => ({
      kod: code,
      etiket: label,
      rota: '/vade',
      yediGun: count(tier.yediGun) ?? 0,
      otuzGun: count(tier.otuzGun) ?? 0,
      gecmis: count(tier.gecmis) ?? 0,
    });
    return [
      row('trafik', this.t('panel.vade.trafik'), v.vade.trafik),
      row('kasko', this.t('panel.vade.kasko'), v.vade.kasko),
      row('muayene', this.t('panel.vade.muayene'), v.vade.muayene),
    ];
  }

  /** KM geçen bakım, site talebi (yalnız modül açıkken), görülmeyen rezervasyon. */
  private badges(v: PanelSummaryResponse): readonly VadeKutusu[] {
    const k = v.kpi;
    const kmElapsed = count(k.kmGecenBakim) ?? 0;
    const unseen = count(k.gorulmeyenRezervasyon) ?? 0;
    const boxes: VadeKutusu[] = [
      {
        sayi: kmElapsed,
        etiket: this.t('panel.vade.kmGecenBakim'),
        rota: '/raporlar/periyodik-servis', // F10.3: SPA rotası
        ton: kmElapsed > 0 ? 'hata' : 'notr',
      },
    ];
    // Site talebi yalnız Web Sitesi modülü açıkken gelir (kapalıyken hep 0 olurdu; kutu gürültü).
    if (k.siteTalebi) {
      const newItem = count(k.siteTalebi.yeni) ?? 0;
      const oldest = count(k.siteTalebi.enEskiGun);
      boxes.push({
        sayi: newItem,
        etiket: this.t('panel.vade.siteTalebi'),
        rota: '/gelen-talepler', // F11.3: SPA rotası (Blazor `?durum=0` = Yeni)
        sorgu: { durum: 'Yeni' },
        ton: newItem > 0 ? ((oldest ?? 0) >= 3 ? 'hata' : 'uyari') : 'notr',
        ipucu:
          oldest !== null
            ? this.t('panel.vade.siteTalebiEnEski', { gun: oldest })
            : this.t('panel.vade.siteTalebiYok'),
      });
    }
    boxes.push({
      sayi: unseen,
      etiket: this.t('panel.vade.gorulmeyenRezervasyon'),
      rota: '/rezervasyonlar', // F5.4: SPA rotası (sunucu yönlendirmesine düşmez)
      ton: unseen > 0 ? 'uyari' : 'notr',
    });
    return boxes;
  }
}
