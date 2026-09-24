import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  untracked,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import type { Sayfa } from '@core/api/sayfa';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { sekmeBaglami } from '@core/sekme/sekme-durumu';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listeSorgusuUrlSenkronu } from '@core/veri/liste-sorgusu-url';
import type { StoreDurumu } from '@core/veri/temel-store';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { Alan } from '@shared/form/alan/alan';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { Ikon } from '@shared/ikon/ikon';
import { Tablo } from '@shared/tablo/tablo';
import { TabloHucre } from '@shared/tablo/tablo-hucre';

import { ALLOCATION_PREFILL_KEY, type AllocationPrefill } from '../allocation-prefill';
import { suggestionList } from '../suggestions';
import {
  FLEET_STATUSES,
  FUELS,
  GEARS,
  STATUS_BADGE,
  STATUS_BOARD,
  TRI_STATE,
  VEHICLE_STATUSES,
  toNumber,
  vehicleStatus,
  type FleetStatus,
  type Fuel,
  type Gear,
  type StatusBoardResponse,
  type StatusRow,
  type VehicleStatus,
} from '../vehicle-model';
import { StatusBoardStore, secimSuggestionFetch } from '../vehicle.store';
import { statusColumns } from './status-columns';

/** Canlı pano tazeleme aralığı (Blazor `data-rc-tazele="60"`). */
export const REFRESH_INTERVAL_MS = 60_000;

type TriState = (typeof TRI_STATE)[number];

/**
 * Araç güncel durum panosu (`/app/arac-durum`, OperationsWrite) — Blazor `FleetStatus.razor` paritesi: 15
 * süzgeç, "N araç • kirada • serviste • BAF'ta" sayaçları (filtreye uyan TÜM satırlar, sunucudan), 24 sütun,
 * satırdan "Kirala" (kira formuna araç ön-seçili). Sayfa görünürken 60 sn'de bir kendini tazeler.
 * "Tahsis" SPA BAF ekranının yeni tahsis formunu araç + çıkış KM + şube dolu açar (F6.3; personel orada seçilir).
 * Servis ekranı henüz Blazor'da (F9): bağlantı tam sayfa geçer.
 */
@Component({
  selector: 'rc-vehicle-status-board',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    Ikon,
    MetinGirdisi,
    Secim,
    Tablo,
    TabloHucre,
  ],
  providers: [FetchPolicy, StatusBoardStore],
  templateUrl: './vehicle-status-board.html',
  styleUrl: '../vehicles.scss',
})
export class VehicleStatusBoard {
  protected readonly store = inject(StatusBoardStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);
  private readonly tab = sekmeBaglami();
  private readonly t = ceviriFonksiyonu();

  protected readonly query = listeSorgusuUrlSenkronu(STATUS_BOARD);
  protected readonly columns = statusColumns(this.t);
  protected readonly rowId = (r: StatusRow) => r.vehicleId;

  protected readonly filterForm = new FormGroup({
    q: new FormControl<string | null>(null),
    durum: new FormControl<VehicleStatus | null>(null),
    filoDurum: new FormControl<FleetStatus | null>(null),
    sube: new FormControl<string | null>(null),
    vites: new FormControl<Gear | null>(null),
    yakit: new FormControl<Fuel | null>(null),
    grup: new FormControl<string | null>(null),
    marka: new FormControl<string | null>(null),
    kirada: new FormControl<TriState | null>(null),
    pasifSebep: new FormControl<string | null>(null),
    hgs: new FormControl<string | null>(null),
    gps: new FormControl<string | null>(null),
    karLastigi: new FormControl<TriState | null>(null),
    webRezKapali: new FormControl<TriState | null>(null),
    ofisRezKapali: new FormControl<TriState | null>(null),
  });

  protected readonly statusOptions: readonly SecenekOgesi<VehicleStatus>[] = VEHICLE_STATUSES.map(
    (s) => ({ deger: s, etiket: this.t(`arac.durumlar.${s}`) }),
  );
  protected readonly fleetOptions: readonly SecenekOgesi<FleetStatus>[] = FLEET_STATUSES.map(
    (s) => ({ deger: s, etiket: this.t(`arac.filoDurumlari.${s}`) }),
  );
  protected readonly gearOptions: readonly SecenekOgesi<Gear>[] = GEARS.map((s) => ({
    deger: s,
    etiket: this.t(`arac.vitesler.${s}`),
  }));
  protected readonly fuelOptions: readonly SecenekOgesi<Fuel>[] = FUELS.map((s) => ({
    deger: s,
    etiket: this.t(`arac.yakitlar.${s}`),
  }));
  protected readonly rentedOptions = this.triOptions(
    'arac.durum.yalnizKirada',
    'arac.durum.yalnizBosta',
  );
  protected readonly snowOptions = this.triOptions('arac.durum.takili', 'arac.durum.takiliDegil');
  protected readonly webOptions = this.triOptions('arac.durum.webKapali', 'arac.durum.webAcik');
  protected readonly officeOptions = this.triOptions(
    'arac.durum.ofisKapali',
    'arac.durum.ofisAcik',
  );

  protected readonly branchSuggestions = suggestionList(
    this.filterForm.controls.sube,
    secimSuggestionFetch(this.api, 'sube'),
  );
  /** Pasif sebebi serbest metin (master yok): öneriler görünen satırlardan (Blazor ile aynı sınır). */
  protected readonly reasonSuggestions = computed(() => {
    const rows = this.store.board.veri()?.liste.kayitlar ?? [];
    const seen = new Set<string>();
    for (const r of rows) if (r.pasifSebep?.trim()) seen.add(r.pasifSebep.trim());
    return [...seen].sort((a, b) => a.localeCompare(b, 'tr'));
  });

  /** Tablo motoru `Sayfa<T>` durumu ister: yanıtın `liste` alanı (sayaçlar ayrı). */
  protected readonly tableSource = computed<StoreDurumu<Sayfa<StatusRow>>>(() => {
    const d = this.store.board.durum();
    const page = (l: StatusBoardResponse['liste']): Sayfa<StatusRow> => ({
      kayitlar: l.kayitlar,
      toplam: toNumber(l.toplam) ?? 0,
      sayfaNo: toNumber(l.sayfaNo) ?? 1,
      boyut: toNumber(l.boyut) ?? 0,
    });
    switch (d.tur) {
      case 'hazir':
        return { tur: 'hazir', veri: page(d.veri.liste) };
      case 'yukleniyor':
        return { tur: 'yukleniyor', onceki: d.onceki ? page(d.onceki.liste) : undefined };
      default:
        return d;
    }
  });

  protected readonly counters = computed(() => {
    const b = this.store.board.veri();
    if (!b) return null;
    return this.t('arac.durum.sayaclar', {
      toplam: toNumber(b.liste.toplam) ?? 0,
      kirada: toNumber(b.kirada) ?? 0,
      serviste: toNumber(b.serviste) ?? 0,
      bafta: toNumber(b.bafta) ?? 0,
    });
  });

  constructor() {
    inject(FetchPolicy).baglan({
      parametre: this.query.apiParametreleri,
      yukle: (p) => this.store.board.yukle(p),
      sifirla: () => this.store.board.sifirla(),
      sekmeyeDonunce: 'yenile',
    });
    effect(() => {
      const f = this.query.sorgu().filtreler;
      untracked(() =>
        this.filterForm.reset({
          q: f.q ?? null,
          durum: f.durum ?? null,
          filoDurum: f.filoDurum ?? null,
          sube: f.sube ?? null,
          vites: f.vites ?? null,
          yakit: f.yakit ?? null,
          grup: f.grup ?? null,
          marka: f.marka ?? null,
          kirada: f.kirada ?? null,
          pasifSebep: f.pasifSebep ?? null,
          hgs: f.hgs ?? null,
          gps: f.gps ?? null,
          karLastigi: f.karLastigi ?? null,
          webRezKapali: f.webRezKapali ?? null,
          ofisRezKapali: f.ofisRezKapali ?? null,
        }),
      );
    });
    const timer = setInterval(() => this.refreshIfVisible(), REFRESH_INTERVAL_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        q: v.q ?? undefined,
        durum: v.durum ?? undefined,
        filoDurum: v.filoDurum ?? undefined,
        sube: v.sube ?? undefined,
        vites: v.vites ?? undefined,
        yakit: v.yakit ?? undefined,
        grup: v.grup ?? undefined,
        marka: v.marka ?? undefined,
        kirada: v.kirada ?? undefined,
        pasifSebep: v.pasifSebep ?? undefined,
        hgs: v.hgs ?? undefined,
        gps: v.gps ?? undefined,
        karLastigi: v.karLastigi ?? undefined,
        webRezKapali: v.webRezKapali ?? undefined,
        ofisRezKapali: v.ofisRezKapali ?? undefined,
      },
    });
  }

  protected clear(): void {
    void this.query.sifirla();
  }

  protected openRow(row: StatusRow): void {
    void this.router.navigate(['/araclar', row.vehicleId]);
  }

  protected canRent(row: StatusRow): boolean {
    return !row.kirada && row.durum !== 'Satildi';
  }

  /** "Tahsis" → SPA BAF formu araç + çıkış KM + şube dolu açılır (Blazor satır içi `/baf/create` paritesi). */
  protected allocationState(row: StatusRow): Record<string, AllocationPrefill> {
    return {
      [ALLOCATION_PREFILL_KEY]: {
        vehicleId: row.vehicleId,
        plaka: row.plaka,
        km: toNumber(row.km),
        sube: row.sube,
      },
    };
  }

  protected badge(status: string): string {
    const s = vehicleStatus(status);
    return `rc-rozet ${s ? STATUS_BADGE[s] : ''}`;
  }

  protected statusLabel(status: string): string {
    const s = vehicleStatus(status);
    return s ? this.t(`arac.durumlar.${s}`) : status;
  }

  private refreshIfVisible(): void {
    if (this.document.visibilityState !== 'visible' || !this.tab.aktif()) return;
    if (this.store.board.yukleniyor()) return;
    this.store.board.yenile();
  }

  private triOptions(
    yes:
      | 'arac.durum.yalnizKirada'
      | 'arac.durum.takili'
      | 'arac.durum.webKapali'
      | 'arac.durum.ofisKapali',
    no:
      | 'arac.durum.yalnizBosta'
      | 'arac.durum.takiliDegil'
      | 'arac.durum.webAcik'
      | 'arac.durum.ofisAcik',
  ): readonly SecenekOgesi<TriState>[] {
    return [
      { deger: 'true', etiket: this.t(yes) },
      { deger: 'false', etiket: this.t(no) },
    ];
  }
}
