import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi, type SorguParametreleri } from '@core/api/api-istemcisi';
import type { Sayfa } from '@core/api/sayfa';
import type { Sema } from '@core/api/ui-tipleri';
import { yeniIslemAnahtari } from '@core/form/gonderim-kilidi';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listeTanimi } from '@core/veri/liste-sorgusu';
import { listeSorgusuUrlSenkronu } from '@core/veri/liste-sorgusu-url';
import { type StoreDurumu, TemelStore } from '@core/veri/temel-store';
import { metinDegeri } from '@features/planlama-ortak/form-yardimcilari';
import { Alan } from '@shared/form/alan/alan';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { Ikon } from '@shared/ikon/ikon';
import { Tablo } from '@shared/tablo/tablo';
import type { TabloSutunu } from '@shared/tablo/tablo-modeli';

import { APPROVAL_STATES } from '../catalog/catalog-configs';

type ImportResult = Sema<'RateImportResult'>;
type MatrixRow = Sema<'RateMatrixDto'>;

/** Ekran yanıtı: sayfa çekirdek `Sayfa<T>` biçiminde okunur (tablo motoru sözleşmesi). */
interface ImportView {
  readonly satirlar: Sayfa<MatrixRow>;
  readonly bekleyen: number | string;
  readonly onayli: number | string;
  readonly silinecek: number | string | null;
}

const ROOT = '/api/ui/v1/tarife-aktar';

export const IMPORT_LIST = listeTanimi({
  filtreler: {
    kanal: { tur: 'metin', enFazla: 64 },
    sube: { tur: 'metin', enFazla: 64 },
    durum: { tur: 'secim', degerler: APPROVAL_STATES },
  },
  siralanabilir: ['kod', 'kanal', 'sube', 'aracGrupKod', 'onayDurumu', 'basTar'],
  varsayilanSirala: 'kod',
});

const toNum = (v: number | string | null | undefined): number | null => {
  if (v === null || v === undefined || v === '') return null;
  const n = typeof v === 'number' ? v : Number(v);
  return Number.isFinite(n) ? n : null;
};

/**
 * Silme kutusu YALNIZ "kanal seçili, başka daraltma yok" hâlinde (Blazor adversarial M1): silme kanalın TÜM
 * şubelerindeki BEKLEYEN satırları kapsar, ekrandaki şube/onay süzgecine uymaz. Onay süzgeci "Bekliyor" ise ekran
 * silinecek kümeyi gösterir → serbest.
 */
export function channelDeleteVisible(f: {
  kanal?: string;
  sube?: string;
  durum?: string;
}): boolean {
  return !!f.kanal && !f.sube && (!f.durum || f.durum === 'Bekliyor');
}

/**
 * Tarife içe aktar (`/app/tarife-aktar`, Blazor `TarifeAktar.razor`, yalnız ManageUsers): CSV/Excel → tarife matrisi
 * satırları HEP `Bekliyor` girer (dosyadaki onay kolonları sunucuda yok sayılır; motor onaysızı kullanmaz). Canlı
 * liste (kanal / şube / onay süzgeci) + bekleyen/onaylı sayısı + kanalın BEKLEYEN satırlarını toplu silme (sayı
 * sunucudan: silmeyle aynı saf yüklem). Onay tarife matrisi ekranından verilir.
 */
@Component({
  selector: 'rc-rate-import',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, TranslocoPipe, Alan, Ikon, MetinGirdisi, Secim, Tablo],
  providers: [FetchPolicy],
  templateUrl: './rate-import.html',
  styleUrl: '../pricing.scss',
})
export class RateImport {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly confirm = inject(OnayServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();
  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');

  protected readonly query = listeSorgusuUrlSenkronu(IMPORT_LIST);
  protected readonly store = new TemelStore(
    (p: SorguParametreleri) => this.api.get<ImportView>(ROOT, { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );
  protected readonly rows = computed<StoreDurumu<Sayfa<MatrixRow>>>(() => {
    const d = this.store.durum();
    switch (d.tur) {
      case 'hazir':
        return { tur: 'hazir', veri: d.veri.satirlar };
      case 'yukleniyor':
        return { tur: 'yukleniyor', onceki: d.onceki?.satirlar };
      default:
        return d;
    }
  });
  protected readonly view = computed(() => this.store.veri());
  /** Toplu silmenin SİLECEĞİ satır sayısı (sunucu; ekran süzgecinden bağımsız). */
  protected readonly toDelete = computed(() => toNum(this.view()?.silinecek) ?? 0);
  /**
   * Silme yalnız GÜNCEL yanıtla: süzgeç değişip liste yüklenirken ekrandaki sayı ESKİ kanalındır — düğme kapalı
   * (inceleme L1: onay yeni kanal için eski sayıyı gösteriyordu).
   */
  protected readonly deleteReady = computed(() => this.store.tur() === 'hazir');
  protected readonly deleteVisible = computed(() =>
    channelDeleteVisible(this.query.sorgu().filtreler),
  );
  protected readonly channel = computed(() => this.query.sorgu().filtreler.kanal ?? null);
  protected readonly rowId = (r: MatrixRow) => r.id;
  protected readonly columns = this.buildColumns();
  protected readonly statusOptions: readonly SecenekOgesi<string>[] = APPROVAL_STATES.map((s) => ({
    deger: s,
    etiket: this.t(`fiyatTarife.onayDurumlari.${s}` as CeviriAnahtari),
  }));

  protected readonly file = signal<File | null>(null);
  protected readonly uploading = signal(false);
  protected readonly deleting = signal(false);
  protected readonly lastResult = signal<ImportResult | null>(null);
  protected readonly uploadError = signal<string | null>(null);
  /** Yükleme işlem anahtarı: sonucu bilinmeyen tekrar AYNI anahtarla (sunucu bugün kullanmaz; ileriye dönük). */
  private uploadKey: string | null = null;

  protected readonly filterForm = new FormGroup({
    kanal: new FormControl<string | null>(null),
    sube: new FormControl<string | null>(null),
    durum: new FormControl<string | null>(null),
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
      untracked(() =>
        this.filterForm.reset({
          kanal: f.kanal ?? null,
          sube: f.sube ?? null,
          durum: f.durum ?? null,
        }),
      );
    });
  }

  protected picked(): void {
    const f = this.fileInput()?.nativeElement.files?.[0] ?? null;
    this.file.set(f);
    this.uploadKey = null;
    this.uploadError.set(null);
  }

  protected upload(): void {
    const f = this.file();
    if (!f || this.uploading()) return;
    const body = new FormData();
    body.append('dosya', f, f.name);
    this.uploadKey ??= yeniIslemAnahtari();
    this.uploading.set(true);
    this.uploadError.set(null);
    this.api
      .post<ImportResult>(`${ROOT}/yukle`, body, { islemAnahtari: this.uploadKey })
      .pipe(
        finalize(() => this.uploading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (r) => {
          this.uploadKey = null;
          this.lastResult.set(r);
          this.file.set(null);
          const el = this.fileInput()?.nativeElement;
          if (el) el.value = '';
          this.toast.basari(this.t('fiyatTarife.aktar.yuklendi', { eklenen: r.eklenen }));
          this.store.yenile();
        },
        error: (raw: unknown) => {
          const e = apiHatasinaCevir(raw);
          const messages = e.alanlar ? Object.values(e.alanlar).flat() : [];
          if (messages.length > 0) this.uploadError.set(messages.join(' '));
          else if (!genelGosterilir(e)) this.uploadError.set(e.detay);
        },
      });
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        kanal: metinDegeri(v.kanal) ?? undefined,
        sube: metinDegeri(v.sube) ?? undefined,
        durum: (v.durum as (typeof APPROVAL_STATES)[number] | null) ?? undefined,
      },
    });
  }

  protected clear(): void {
    void this.query.sifirla();
  }

  protected onlyChannel(): void {
    void this.query.degistir({ sayfa: 1, filtreler: { kanal: this.channel() ?? undefined } });
  }

  protected async deleteChannel(): Promise<void> {
    const kanal = this.channel();
    const count = this.toDelete();
    if (!kanal || this.deleting() || !this.deleteReady() || count === 0) return;
    const yes = await this.confirm.sor({
      baslik: this.t('fiyatTarife.aktar.silBaslik'),
      mesaj: this.t('fiyatTarife.aktar.silMesaj', { kanal, adet: count }),
      onayEtiketi: this.t('fiyatTarife.sil'),
      tehlikeli: true,
    });
    if (!yes || this.deleting() || !this.deleteReady() || this.channel() !== kanal) return;
    this.deleting.set(true);
    this.api
      .post<{ silinen: number }>(`${ROOT}/kanal-sil`, { kanal })
      .pipe(
        finalize(() => this.deleting.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (r) => {
          this.toast.basari(this.t('fiyatTarife.aktar.silindi', { adet: r.silinen, kanal }));
          this.store.yenile();
        },
        error: (raw: unknown) => {
          const e = apiHatasinaCevir(raw);
          if (!genelGosterilir(e)) this.toast.hata(e.detay);
          this.store.yenile();
        },
      });
  }

  private buildColumns(): readonly TabloSutunu<MatrixRow>[] {
    const h = (k: string) => this.t(`fiyatTarife.alan.${k}` as CeviriAnahtari);
    return [
      {
        kod: 'kod',
        baslik: h('kod'),
        deger: (r) => r.kod,
        sirala: true,
        sabit: true,
        gizlenemez: true,
        genislik: 130,
      },
      { kod: 'ad', baslik: h('ad'), deger: (r) => r.ad, genislik: 160 },
      {
        kod: 'kanal',
        baslik: h('kanal'),
        deger: (r) => r.kanal ?? '—',
        sirala: true,
        genislik: 110,
      },
      { kod: 'sube', baslik: h('sube'), deger: (r) => r.sube ?? '—', sirala: true, genislik: 110 },
      {
        kod: 'aracGrupKod',
        baslik: h('aracGrupKod'),
        deger: (r) => r.aracGrupKod ?? '—',
        sirala: true,
        genislik: 100,
      },
      {
        kod: 'basTar',
        baslik: h('basTar'),
        deger: (r) => r.basTar,
        tur: 'tarih',
        sirala: true,
        genislik: 110,
      },
      { kod: 'bitTar', baslik: h('bitTar'), deger: (r) => r.bitTar, tur: 'tarih', genislik: 110 },
      {
        kod: 'gun1',
        baslik: h('gun1'),
        deger: (r) => toNum(r.gun1),
        tur: 'para',
        paraBirimi: (r) => r.paraBirimi ?? 'TRY',
        genislik: 110,
      },
      {
        kod: 'gun7',
        baslik: h('gun7'),
        deger: (r) => toNum(r.gun7),
        tur: 'para',
        paraBirimi: (r) => r.paraBirimi ?? 'TRY',
        genislik: 110,
      },
      {
        kod: 'onayDurumu',
        baslik: h('onayDurumu'),
        deger: (r) => this.t(`fiyatTarife.onayDurumlari.${r.onayDurumu}` as CeviriAnahtari),
        sirala: true,
        genislik: 110,
      },
      {
        kod: 'aktif',
        baslik: h('aktif'),
        deger: (r) => this.t(r.aktif ? 'fiyatTarife.evet' : 'fiyatTarife.hayir'),
        genislik: 80,
      },
    ];
  }
}
