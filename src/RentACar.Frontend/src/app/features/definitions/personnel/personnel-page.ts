import { ChangeDetectionStrategy, Component, viewChild } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { translationFunction } from '@core/i18n/ceviri';
import { DefinitionCrud } from '@shared/form/tanim-crud/definition-crud';

import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
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
  imports: [TranslocoPipe, DefinitionCrud, PageBand],
  styleUrl: '../definitions.scss',
  template: `
    <rc-sayfa-bandi [baslik]="'tanimlar.personnel.baslik' | transloco" ikon="users" />
    <div class="rc-sayfa">
      <p class="not">{{ 'tanimlar.personnel.aciklama' | transloco }}</p>
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
  private readonly crud = viewChild(DefinitionCrud);

  protected readonly fields = personnelFields(translationFunction(), {
    branch: selectionSuggestions('/api/ui/v1/secim/sube'),
  });
  protected readonly source = pagedDefinitionSource('/api/ui/v1/personel', 'kod', {
    toRow: personnelToRow,
    toBody: personnelToBody,
  });

  constructor() {
    pageLeaveGuard(() => this.hasUnsavedChanges());
  }

  hasUnsavedChanges(): boolean {
    return this.crud()?.hasUnsavedChanges() ?? false;
  }
}
