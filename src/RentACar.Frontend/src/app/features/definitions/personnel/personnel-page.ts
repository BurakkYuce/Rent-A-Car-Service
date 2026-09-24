import { ChangeDetectionStrategy, Component, viewChild } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { sayfaTerkKorumasi } from '@core/form/kaydedilmemis-degisiklik';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { TanimCrud } from '@shared/form/tanim-crud/tanim-crud';

import { selectionSuggestions } from '../definition-catalog';
import { pagedDefinitionSource } from '../paged-source';
import { personnelFields, personnelToBody, personnelToRow } from './personnel-model';

/**
 * F11.2d personel (Blazor `PersonelList`, ManageUsers): tanım CRUD'u (panel). Liste PII'sız (TC ve maaş yok);
 * düzenleme açılırken tekil kayıt okunur (maaş yalnız orada). TC yazma-yalnız (bkz. `personnelFields`).
 */
@Component({
  selector: 'rc-personnel-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, TanimCrud],
  styleUrl: '../definitions.scss',
  template: `
    <div class="sayfa">
      <h1>{{ 'tanimlar.personnel.baslik' | transloco }}</h1>
      <p class="aciklama">{{ 'tanimlar.personnel.aciklama' | transloco }}</p>
      <rc-tanim-crud
        layout="panel"
        [baslik]="'tanimlar.personnel.tablo' | transloco"
        [alanlar]="fields"
        [kaynak]="source"
      />
    </div>
  `,
})
export class PersonnelPage {
  private readonly crud = viewChild(TanimCrud);

  protected readonly fields = personnelFields(ceviriFonksiyonu(), {
    branch: selectionSuggestions('/api/ui/v1/secim/sube'),
  });
  protected readonly source = pagedDefinitionSource('/api/ui/v1/personel', 'kod', {
    toRow: personnelToRow,
    toBody: personnelToBody,
  });

  constructor() {
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.crud()?.kaydedilmemisDegisiklikVar() ?? false;
  }
}
