import { ChangeDetectionStrategy, Component, inject, viewChild } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import type { ApiPath } from '@core/api/api-istemcisi';
import { pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { translationFunction } from '@core/i18n/ceviri';
import { DefinitionCrud } from '@shared/form/tanim-crud/definition-crud';
import type { TanimAlani } from '@shared/form/tanim-crud/definition-source';

import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { ACTIVE, PASSIVE, definitionSource } from './definition-source';

export type DefinitionKind = 'sahip' | 'segment' | 'tip';

const ROOTS: Readonly<Record<DefinitionKind, ApiPath>> = {
  sahip: '/api/ui/v1/arac-sahipleri',
  segment: '/api/ui/v1/segmentler',
  tip: '/api/ui/v1/arac-tipleri',
};

/**
 * Araç tanımları (`/app/arac-sahipleri`, `/app/segmentler`, `/app/arac-tipleri`; OperationsWrite) — Blazor
 * `VehicleOwnerList` / `VehicleSegmentList` / `VehicleTypeList` paritesi, genel tanım CRUD bileşeniyle:
 * kod (büyük harfe sunucu çevirir, kiracıda benzersiz), ad, türe özgü alanlar ve Aktif/Pasif durumu.
 */
@Component({
  selector: 'rc-vehicle-definitions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, DefinitionCrud, PageBand],
  styleUrl: '../vehicle-screens.scss',
  template: `
    <rc-sayfa-bandi [baslik]="'arac.tanim.' + kind + '.baslik' | transloco" ikon="tag" />
    <div class="rc-sayfa">
      <p class="not">{{ 'arac.tanim.' + kind + '.aciklama' | transloco }}</p>
      <rc-tanim-crud
        [baslik]="'arac.tanim.' + kind + '.tablo' | transloco"
        [alanlar]="fields"
        [kaynak]="source"
      />
    </div>
  `,
})
export class VehicleDefinitions {
  private readonly t = translationFunction();
  private readonly crud = viewChild(DefinitionCrud);
  protected readonly kind: DefinitionKind =
    (inject(ActivatedRoute).snapshot.data['tanim'] as DefinitionKind | undefined) ?? 'sahip';

  protected readonly fields: readonly TanimAlani[] = this.buildFields();
  protected readonly source = definitionSource(
    ROOTS[this.kind],
    this.fields.map((f) => f.ad),
  );

  constructor() {
    pageLeaveGuard(() => this.hasUnsavedChanges());
  }

  hasUnsavedChanges(): boolean {
    return this.crud()?.hasUnsavedChanges() ?? false;
  }

  private buildFields(): readonly TanimAlani[] {
    const l = (key: string) => this.t(`arac.tanim.alan.${key}` as 'arac.tanim.alan.kod');
    const status: TanimAlani = {
      ad: 'durum',
      etiket: l('durum'),
      tur: 'secim',
      secenekler: [
        { deger: ACTIVE, etiket: l('aktif') },
        { deger: PASSIVE, etiket: l('pasif') },
      ],
    };
    const code: TanimAlani = {
      ad: 'kod',
      etiket: l('kod'),
      tur: 'metin',
      zorunlu: true,
      azamiUzunluk: 32,
    };
    const name: TanimAlani = {
      ad: 'ad',
      etiket: l('ad'),
      tur: 'metin',
      zorunlu: true,
      azamiUzunluk: 128,
    };
    switch (this.kind) {
      case 'segment':
        return [
          code,
          name,
          { ad: 'aciklama', etiket: l('aciklama'), tur: 'metin', azamiUzunluk: 512 },
          status,
        ];
      case 'tip':
        return [
          code,
          name,
          { ad: 'marka', etiket: l('marka'), tur: 'metin', azamiUzunluk: 64 },
          { ad: 'vites', etiket: l('vites'), tur: 'metin', azamiUzunluk: 32 },
          { ad: 'yakit', etiket: l('yakit'), tur: 'metin', azamiUzunluk: 32 },
          { ad: 'grup', etiket: l('grup'), tur: 'metin', azamiUzunluk: 64 },
          status,
        ];
      default:
        return [
          code,
          name,
          { ad: 'tur', etiket: l('tur'), tur: 'metin', azamiUzunluk: 32 },
          status,
        ];
    }
  }
}
