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
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi, type SorguParametreleri } from '@core/api/api-istemcisi';
import { invariantOndalik } from '@core/form/ondalik';
import type { GunMetni } from '@core/form/tarih-girdisi';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listeSorgusuUrlSenkronu } from '@core/veri/liste-sorgusu-url';
import { type StoreDurumu, TemelStore } from '@core/veri/temel-store';
import type { Sayfa } from '@core/api/sayfa';
import { metinDegeri } from '@features/planlama-ortak/form-yardimcilari';
import { CustomerLabels } from '@features/vehicle-finance/labels';
import { ParaPipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';
import { Ikon } from '@shared/ikon/ikon';
import { Tablo } from '@shared/tablo/tablo';
import { TabloHucre } from '@shared/tablo/tablo-hucre';
import type { TabloSutunu } from '@shared/tablo/tablo-modeli';

import { COST_OFFERS, type CostOfferList, type CostOfferRow, OFFER_LIST } from './cost-model';

const toNum = (v: number | string | null | undefined): number | null => {
  if (v === null || v === undefined || v === '') return null;
  const n = typeof v === 'number' ? v : Number(v);
  return Number.isFinite(n) ? n : null;
};

/**
 * Kayıtlı maliyet teklifleri (`/app/maliyet-teklifleri`, Blazor `MaliyetTeklifiList.razor`): süzgeç (metin, plaka,
 * cari, tarih, aylık net aralığı), FİLO özet kartları (sunucu `ozet` — araç başı × adet), liste, aç (kayıtlı teklif
 * = hesap ekranı + güncelle) ve onaylı silme. Planlama belgesi: deftere yazmaz. Okuma FinanceWrite ∨ ViewReports,
 * silme/kaydetme FinanceWrite.
 */
@Component({
  selector: 'rc-offer-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    AramaSecim,
    Ikon,
    MetinGirdisi,
    ParaGirdisi,
    ParaPipe,
    Tablo,
    TabloHucre,
    TarihSecici,
  ],
  providers: [FetchPolicy, CustomerLabels],
  templateUrl: './offer-list.html',
  styleUrl: '../pricing.scss',
})
export class OfferList {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly confirm = inject(OnayServisi);
  private readonly session = inject(OturumServisi);
  private readonly customerLabels = inject(CustomerLabels);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();

  protected readonly num = toNum;
  protected readonly query = listeSorgusuUrlSenkronu(OFFER_LIST);
  protected readonly store = new TemelStore(
    (p: SorguParametreleri) => this.api.get<CostOfferList>(COST_OFFERS, { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );
  protected readonly canWrite = computed(() => this.session.izinVar('FinanceWrite'));
  protected readonly customers = sunucuSecimKaynagi('musteri');
  protected readonly busy = signal<string | null>(null);
  protected readonly rowId = (r: CostOfferRow) => r.id;
  protected readonly summary = computed(() => this.store.veri()?.ozet ?? null);
  protected readonly rows = computed<StoreDurumu<Sayfa<CostOfferRow>>>(() => {
    const d = this.store.durum();
    switch (d.tur) {
      case 'hazir':
        return { tur: 'hazir', veri: d.veri.kayitlar };
      case 'yukleniyor':
        return { tur: 'yukleniyor', onceki: d.onceki?.kayitlar };
      default:
        return d;
    }
  });
  protected readonly columns = this.buildColumns();

  protected readonly filterForm = new FormGroup({
    metin: new FormControl<string | null>(null),
    plaka: new FormControl<string | null>(null),
    cari: new FormControl<SecimSecenegi | null>(null),
    bas: new FormControl<GunMetni | null>(null),
    bit: new FormControl<GunMetni | null>(null),
    fiyatMin: new FormControl<string | null>(null),
    fiyatMax: new FormControl<string | null>(null),
  });

  constructor() {
    inject(FetchPolicy).baglan({
      parametre: this.query.apiParametreleri,
      yukle: (p) => this.store.yukle(p),
      sifirla: () => this.store.sifirla(),
      sekmeyeDonunce: 'yenile',
    });
    effect(() => {
      const f = this.query.sorgu().filtreler;
      const cari = this.customerLabels.label(f.cariId);
      untracked(() =>
        this.filterForm.reset({
          metin: f.metin ?? null,
          plaka: f.plaka ?? null,
          cari,
          bas: f.bas ?? null,
          bit: f.bit ?? null,
          fiyatMin: f.fiyatMin === undefined ? null : invariantOndalik(f.fiyatMin, { kesir: 2 }),
          fiyatMax: f.fiyatMax === undefined ? null : invariantOndalik(f.fiyatMax, { kesir: 2 }),
        }),
      );
    });
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    this.customerLabels.remember(v.cari);
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        metin: metinDegeri(v.metin) ?? undefined,
        plaka: metinDegeri(v.plaka) ?? undefined,
        cariId: v.cari?.id ?? undefined,
        bas: v.bas ?? undefined,
        bit: v.bit ?? undefined,
        fiyatMin: toNum(v.fiyatMin) ?? undefined,
        fiyatMax: toNum(v.fiyatMax) ?? undefined,
      },
    });
  }

  protected clear(): void {
    void this.query.sifirla();
  }

  protected async remove(row: CostOfferRow): Promise<void> {
    if (this.busy() !== null) return;
    const yes = await this.confirm.sor({
      baslik: this.t('fiyatTarife.maliyet.silBaslik'),
      mesaj: this.t('fiyatTarife.maliyet.silMesaj', { no: row.kayitNo, baslik: row.baslik }),
      onayEtiketi: this.t('fiyatTarife.sil'),
      tehlikeli: true,
    });
    if (!yes || this.busy() !== null) return;
    this.busy.set(row.id);
    this.api
      .delete<unknown>(`${COST_OFFERS}/${encodeURIComponent(row.id)}`)
      .pipe(
        finalize(() => this.busy.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.toast.basari(this.t('fiyatTarife.maliyet.silindi', { no: row.kayitNo }));
          this.store.yenile();
        },
        error: (raw: unknown) => {
          const e = apiHatasinaCevir(raw);
          if (!genelGosterilir(e)) this.toast.hata(e.detay);
          this.store.yenile();
        },
      });
  }

  private buildColumns(): readonly TabloSutunu<CostOfferRow>[] {
    const h = (k: string) => this.t(`fiyatTarife.maliyet.sutun.${k}` as CeviriAnahtari);
    const money = (k: keyof CostOfferRow, sortable = false): TabloSutunu<CostOfferRow> => ({
      kod: k,
      baslik: h(k),
      deger: (r) => toNum(r[k] as number | string | null),
      tur: 'para',
      sirala: sortable,
      genislik: 130,
    });
    const columns: TabloSutunu<CostOfferRow>[] = [
      {
        kod: 'kayitNo',
        baslik: h('kayitNo'),
        deger: (r) => r.kayitNo,
        sirala: true,
        sabit: true,
        gizlenemez: true,
        genislik: 140,
      },
      {
        kod: 'tarih',
        baslik: h('tarih'),
        deger: (r) => r.tarih,
        tur: 'tarih',
        sirala: true,
        genislik: 110,
      },
      { kod: 'baslik', baslik: h('baslik'), deger: (r) => r.baslik, sirala: true, genislik: 200 },
      {
        kod: 'plaka',
        baslik: h('plaka'),
        deger: (r) => r.plaka ?? '—',
        sirala: true,
        genislik: 100,
      },
      { kod: 'cari', baslik: h('cari'), deger: (r) => r.cariAd ?? '—', genislik: 160 },
      {
        kod: 'aracSayisi',
        baslik: h('aracSayisi'),
        deger: (r) => toNum(r.aracSayisi),
        tur: 'sayi',
        genislik: 80,
      },
      money('teklifAylikNet', true),
      money('teklifKdvli'),
      money('filoTeklifAylikNet'),
      money('filoTeklifKdvli', true),
    ];
    if (this.canWrite())
      columns.push({
        kod: 'islemler',
        baslik: this.t('fiyatTarife.islemler'),
        deger: () => null,
        genislik: 110,
      });
    return columns;
  }
}
