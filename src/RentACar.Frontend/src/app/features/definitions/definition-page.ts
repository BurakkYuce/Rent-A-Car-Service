import { ChangeDetectionStrategy, Component, inject, viewChild } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { sayfaTerkKorumasi } from '@core/form/kaydedilmemis-degisiklik';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { TanimCrud } from '@shared/form/tanim-crud/tanim-crud';
import { restTanimKaynagi } from '@shared/form/tanim-crud/tanim-kaynagi';

import { type DefinitionKind, definitionConfig, selectionSuggestions } from './definition-catalog';

/**
 * F11.2a genel tanım ekranı (Blazor `BrandList`, `CancelReasonList`, `CountryList`, `CustomerGroupList`,
 * `DepartmentList`, `AccessoryList`, `BankList`, `CurrencyList`, `CustomCodeList`, `ExpenseCategoryList`,
 * `FinancialAccountList`, `DropTanimList` paritesi). Tür rota verisinden (`data.definition`); alanlar
 * `definitionConfig`'te, CRUD + sürüm/409 birleştirme `rc-tanim-crud`'da — ekran başına kod yok.
 */
@Component({
  selector: 'rc-definition-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, TanimCrud],
  styleUrl: './definitions.scss',
  template: `
    <div class="sayfa">
      <h1>{{ 'tanimlar.' + kind + '.baslik' | transloco }}</h1>
      <p class="aciklama">{{ 'tanimlar.' + kind + '.aciklama' | transloco }}</p>
      @if (kind === 'drop') {
        <p class="aciklama">{{ 'tanimlar.drop.aciklama2' | transloco }}</p>
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
  private readonly crud = viewChild(TanimCrud);
  protected readonly kind: DefinitionKind =
    (inject(ActivatedRoute).snapshot.data['definition'] as DefinitionKind | undefined) ?? 'brand';
  protected readonly config = definitionConfig(this.kind, ceviriFonksiyonu(), {
    branch: selectionSuggestions('/api/ui/v1/secim/sube'),
    location: selectionSuggestions('/api/ui/v1/secim/lokasyon'),
  });
  protected readonly source = restTanimKaynagi(this.config.root);

  constructor() {
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.crud()?.kaydedilmemisDegisiklikVar() ?? false;
  }
}
