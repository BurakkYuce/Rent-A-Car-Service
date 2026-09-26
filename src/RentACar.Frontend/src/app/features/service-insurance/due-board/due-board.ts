import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  untracked,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { translationFunction } from '@core/i18n/ceviri';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listQueryUrlSync } from '@core/veri/liste-sorgusu-url';
import type { StoreState } from '@core/veri/temel-store';
import type { Sayfa } from '@core/api/sayfa';
import { textValue } from '@features/planlama-ortak/form-yardimcilari';
import { Alan } from '@shared/form/alan/alan';
import { TextInput } from '@shared/form/kontroller/text-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { Table } from '@shared/tablo/table';
import { TableCell } from '@shared/tablo/table-cell';

import { RegulationTabs } from '../regulation/regulation-tabs';
import { dueColumns } from '../service-insurance-columns';
import { DUE_BUCKETS, DUE_LIST, type DueBucket, type DueItem } from '../service-insurance-model';
import { DueBoardStore } from '../service-insurance.store';
import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { FilterPanelComponent } from '@shared/filtre-paneli/filtre-paneli';
import { PlateChipComponent } from '@shared/plaka/plaka';

/**
 * Vade uyarıları (`/app/vade`) — Blazor `VadePanosu.razor`: sigorta / MTV / muayene bitiş takibi; kova özeti
 * (Geçmiş, ≤ 7 gün, ≤ 30 gün, İleri) + plaka / tür / kova süzgeci + dışa aktarma (sunucu `/listeler/export/vade`).
 * ARACIN şubesi süzer. Okuma OperationsWrite ∨ FinanceWrite ∨ ViewReports.
 */
@Component({
  selector: 'rc-due-board',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PlateChipComponent,
    FilterPanelComponent,
    PageBand,
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    TextInput,
    RegulationTabs,
    Selection,
    Table,
    TableCell,
  ],
  providers: [FetchPolicy, DueBoardStore],
  templateUrl: './due-board.html',
  styleUrl: '../service-insurance.scss',
})
export class DueBoardPage {
  protected readonly store = inject(DueBoardStore);
  private readonly t = translationFunction();

  protected readonly query = listQueryUrlSync(DUE_LIST);
  protected readonly columns = dueColumns(this.t, (b) => this.bucketLabel(b));
  protected readonly rowId = (r: DueItem) => `${r.vehicleId}:${r.tur}:${r.bitis}`;
  protected readonly bucketOptions: readonly SecenekOgesi<DueBucket>[] = DUE_BUCKETS.map((b) => ({
    deger: b,
    etiket: this.bucketLabel(b),
  }));

  /** Tablo kaynağı: panodaki sayfa (`kalemler`) aynı dört durumla. */
  protected readonly items = computed<StoreState<Sayfa<DueItem>>>(() => {
    const d = this.store.board.durum();
    switch (d.tur) {
      case 'hazir':
        return { tur: 'hazir', veri: d.veri.kalemler };
      case 'yukleniyor':
        return { tur: 'yukleniyor', onceki: d.onceki?.kalemler };
      default:
        return d;
    }
  });
  protected readonly summary = computed(() => this.store.board.veri()?.ozet ?? null);

  /** Dışa aktarma sunucu ucundan (istemcide dosya üretilmez; Blazor bağlantılarıyla aynı — süzgeçsiz tüm liste). */
  protected readonly exportLinks = [
    { label: 'Excel', href: '/listeler/export/vade' },
    { label: 'CSV', href: '/listeler/export/vade?format=csv' },
    { label: 'PDF', href: '/listeler/export/vade?format=pdf' },
  ] as const;

  protected readonly filterForm = new FormGroup({
    plaka: new FormControl<string | null>(null),
    tur: new FormControl<string | null>(null),
    kova: new FormControl<DueBucket | null>(null),
  });

  constructor() {
    inject(FetchPolicy).connect({
      parametre: this.query.apiParametreleri,
      yukle: (p) => this.store.board.yukle(p),
      sifirla: () => this.store.board.reset(),
      sekmeyeDonunce: 'yenile',
    });
    effect(() => {
      const f = this.query.sorgu().filtreler;
      untracked(() =>
        this.filterForm.reset({ plaka: f.plaka ?? null, tur: f.tur ?? null, kova: f.kova ?? null }),
      );
    });
  }

  protected bucketLabel(b: string): string {
    return (DUE_BUCKETS as readonly string[]).includes(b)
      ? this.t(`servisSigorta.vade.kovalar.${b}` as CeviriAnahtari)
      : b;
  }

  protected bucketTone(b: string): string {
    return b === 'Gecmis' || b === 'YediGun'
      ? 'rc-rozet--hata'
      : b === 'OtuzGun'
        ? 'rc-rozet--uyari'
        : '';
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        plaka: textValue(v.plaka) ?? undefined,
        tur: textValue(v.tur) ?? undefined,
        kova: v.kova ?? undefined,
      },
    });
  }

  /** Filtre panelinin "Temizle"si: form boşalır, liste süzgeçsiz yeniden istenir. */
  protected clear(): void {
    this.filterForm.reset({ plaka: null, tur: null, kova: null });
    this.filter();
  }

  protected selectBucket(b: DueBucket | null): void {
    void this.query.degistir({
      sayfa: 1,
      filtreler: { ...this.query.sorgu().filtreler, kova: b ?? undefined },
    });
  }
}
