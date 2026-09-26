import { ChangeDetectionStrategy, Component, effect, inject, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import type { DayText } from '@core/form/tarih-girdisi';
import { translationFunction } from '@core/i18n/ceviri';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listQueryUrlSync } from '@core/veri/liste-sorgusu-url';
import { textValue } from '@features/planlama-ortak/form-yardimcilari';
import { Alan } from '@shared/form/alan/alan';
import { TextInput } from '@shared/form/kontroller/text-input';
import { DatePicker } from '@shared/form/tarih/date-picker';
import { Table } from '@shared/tablo/table';
import { TableCell } from '@shared/tablo/table-cell';

import { endorsementColumns } from '../service-insurance-columns';
import { ENDORSEMENT_LIST, type EndorsementRow } from '../service-insurance-model';
import { EndorsementListStore } from '../service-insurance.store';
import { RegulationTabs } from './regulation-tabs';
import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { FilterPanelComponent } from '@shared/filtre-paneli/filtre-paneli';
import { PlateChipComponent } from '@shared/plaka/plaka';

/**
 * Zeyiller (`/app/regulasyon/zeyiller`, #301) — Blazor `/regulasyon` "Zeyil (Poliçe Ekleri)" tablosu: TÜM poliçelerin
 * zeyilleri tek listede (`GET /regulasyon/zeyiller`). Salt okuma; ekleme ve silme poliçe kaydında (satırdaki poliçe
 * bağlantısı). Kapsam: poliçenin aracının şubesi (sunucu süzer).
 */
@Component({
  selector: 'rc-endorsement-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PlateChipComponent,
    FilterPanelComponent,
    PageBand,
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    TextInput,
    RegulationTabs,
    Table,
    TableCell,
    DatePicker,
  ],
  providers: [FetchPolicy, EndorsementListStore],
  templateUrl: './endorsement-list.html',
  styleUrl: '../service-insurance.scss',
})
export class EndorsementList {
  protected readonly store = inject(EndorsementListStore);
  private readonly t = translationFunction();

  protected readonly query = listQueryUrlSync(ENDORSEMENT_LIST);
  protected readonly columns = endorsementColumns(this.t);
  protected readonly rowId = (r: EndorsementRow) => r.id;

  protected readonly filterForm = new FormGroup({
    plaka: new FormControl<string | null>(null),
    tipi: new FormControl<string | null>(null),
    bas: new FormControl<DayText | null>(null),
    bit: new FormControl<DayText | null>(null),
  });

  constructor() {
    inject(FetchPolicy).connect({
      parametre: this.query.apiParametreleri,
      yukle: (p) => this.store.list.yukle(p),
      sifirla: () => this.store.list.reset(),
      sekmeyeDonunce: 'yenile',
    });
    effect(() => {
      const f = this.query.sorgu().filtreler;
      untracked(() =>
        this.filterForm.reset({
          plaka: f.plaka ?? null,
          tipi: f.tipi ?? null,
          bas: f.bas ?? null,
          bit: f.bit ?? null,
        }),
      );
    });
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        plaka: textValue(v.plaka) ?? undefined,
        tipi: textValue(v.tipi) ?? undefined,
        bas: v.bas ?? undefined,
        bit: v.bit ?? undefined,
      },
    });
  }

  protected clear(): void {
    void this.query.sifirla();
  }
}
