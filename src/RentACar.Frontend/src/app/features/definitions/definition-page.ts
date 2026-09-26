import { ChangeDetectionStrategy, Component, inject, viewChild } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { translationFunction } from '@core/i18n/ceviri';
import { DefinitionCrud } from '@shared/form/tanim-crud/definition-crud';
import { restDefinitionSource } from '@shared/form/tanim-crud/definition-source';

import { PageBand } from '../../kabuk/sayfa-bandi/page-band';
import { type DefinitionKind, definitionConfig, selectionSuggestions } from './definition-catalog';
import { pagedDefinitionSource } from './paged-source';

/**
 * F11.2a genel tanım ekranı (Blazor `BrandList`, `CancelReasonList`, `CountryList`, `CustomerGroupList`,
 * `DepartmentList`, `AccessoryList`, `BankList`, `CurrencyList`, `CustomCodeList`, `ExpenseCategoryList`,
 * `FinancialAccountList`, `DropTanimList`; F11.2c: `PaymentTypeList`, `FuelKindList`, `TransmissionTypeList`,
 * `VehicleColorList`, `HesapKoduList`, `InsuranceCompanyList`, `KdvRateList`, `PenaltyTypeList`, `BelgeSablonList`
 * paritesi). Tür rota verisinden (`data.definition`); alanlar
 * `definitionConfig`'te, CRUD + sürüm/409 birleştirme `rc-tanim-crud`'da — ekran başına kod yok.
 */
@Component({
  selector: 'rc-definition-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, DefinitionCrud, PageBand],
  styleUrl: './definitions.scss',
  template: `
    <rc-sayfa-bandi [baslik]="'tanimlar.' + kind + '.baslik' | transloco" ikon="tag" />
    <div class="rc-sayfa">
      <p class="not">{{ 'tanimlar.' + kind + '.aciklama' | transloco }}</p>
      @if (kind === 'drop') {
        <p class="not">{{ 'tanimlar.drop.aciklama2' | transloco }}</p>
      }
      <rc-tanim-crud
        [baslik]="'tanimlar.' + kind + '.tablo' | transloco"
        [alanlar]="config.fields"
        [kaynak]="source"
        [layout]="config.layout"
      />
    </div>
  `,
})
export class DefinitionPage {
  private readonly crud = viewChild(DefinitionCrud);
  protected readonly kind: DefinitionKind =
    (inject(ActivatedRoute).snapshot.data['definition'] as DefinitionKind | undefined) ?? 'brand';
  protected readonly config = definitionConfig(this.kind, translationFunction(), {
    branch: selectionSuggestions('/api/ui/v1/secim/sube'),
    location: selectionSuggestions('/api/ui/v1/secim/lokasyon'),
  });
  protected readonly source =
    this.config.pagedSort === undefined
      ? restDefinitionSource(this.config.root)
      : pagedDefinitionSource(this.config.root, this.config.pagedSort);

  constructor() {
    pageLeaveGuard(() => this.hasUnsavedChanges());
  }

  hasUnsavedChanges(): boolean {
    return this.crud()?.hasUnsavedChanges() ?? false;
  }
}
