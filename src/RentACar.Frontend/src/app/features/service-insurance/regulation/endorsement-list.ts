import { ChangeDetectionStrategy, Component, effect, inject, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import type { GunMetni } from '@core/form/tarih-girdisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listeSorgusuUrlSenkronu } from '@core/veri/liste-sorgusu-url';
import { metinDegeri } from '@features/planlama-ortak/form-yardimcilari';
import { Alan } from '@shared/form/alan/alan';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';
import { Ikon } from '@shared/ikon/ikon';
import { Tablo } from '@shared/tablo/tablo';
import { TabloHucre } from '@shared/tablo/tablo-hucre';

import { endorsementColumns } from '../service-insurance-columns';
import { ENDORSEMENT_LIST, type EndorsementRow } from '../service-insurance-model';
import { EndorsementListStore } from '../service-insurance.store';
import { RegulationTabs } from './regulation-tabs';

/**
 * Zeyiller (`/app/regulasyon/zeyiller`, #301) — Blazor `/regulasyon` "Zeyil (Poliçe Ekleri)" tablosu: TÜM poliçelerin
 * zeyilleri tek listede (`GET /regulasyon/zeyiller`). Salt okuma; ekleme ve silme poliçe kaydında (satırdaki poliçe
 * bağlantısı). Kapsam: poliçenin aracının şubesi (sunucu süzer).
 */
@Component({
  selector: 'rc-endorsement-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    Ikon,
    MetinGirdisi,
    RegulationTabs,
    Tablo,
    TabloHucre,
    TarihSecici,
  ],
  providers: [FetchPolicy, EndorsementListStore],
  templateUrl: './endorsement-list.html',
  styleUrl: '../service-insurance.scss',
})
export class EndorsementList {
  protected readonly store = inject(EndorsementListStore);
  private readonly t = ceviriFonksiyonu();

  protected readonly query = listeSorgusuUrlSenkronu(ENDORSEMENT_LIST);
  protected readonly columns = endorsementColumns(this.t);
  protected readonly rowId = (r: EndorsementRow) => r.id;

  protected readonly filterForm = new FormGroup({
    plaka: new FormControl<string | null>(null),
    tipi: new FormControl<string | null>(null),
    bas: new FormControl<GunMetni | null>(null),
    bit: new FormControl<GunMetni | null>(null),
  });

  constructor() {
    inject(FetchPolicy).baglan({
      parametre: this.query.apiParametreleri,
      yukle: (p) => this.store.list.yukle(p),
      sifirla: () => this.store.list.sifirla(),
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
        plaka: metinDegeri(v.plaka) ?? undefined,
        tipi: metinDegeri(v.tipi) ?? undefined,
        bas: v.bas ?? undefined,
        bit: v.bit ?? undefined,
      },
    });
  }

  protected clear(): void {
    void this.query.sifirla();
  }
}
