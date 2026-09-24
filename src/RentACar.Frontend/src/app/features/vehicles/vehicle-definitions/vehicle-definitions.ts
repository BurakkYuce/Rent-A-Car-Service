import { ChangeDetectionStrategy, Component, inject, viewChild } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import type { ApiYolu } from '@core/api/api-istemcisi';
import { sayfaTerkKorumasi } from '@core/form/kaydedilmemis-degisiklik';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { TanimCrud } from '@shared/form/tanim-crud/tanim-crud';
import type { TanimAlani } from '@shared/form/tanim-crud/tanim-kaynagi';

import { ACTIVE, PASSIVE, definitionSource } from './definition-source';

export type DefinitionKind = 'sahip' | 'segment' | 'tip';

const ROOTS: Readonly<Record<DefinitionKind, ApiYolu>> = {
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
  imports: [TranslocoPipe, TanimCrud],
  styleUrl: '../vehicles.scss',
  template: `
    <div class="sayfa">
      <header class="ust">
        <h1>{{ 'arac.tanim.' + kind + '.baslik' | transloco }}</h1>
      </header>
      <p class="aciklama">{{ 'arac.tanim.' + kind + '.aciklama' | transloco }}</p>
      <rc-tanim-crud
        [baslik]="'arac.tanim.' + kind + '.tablo' | transloco"
        [alanlar]="fields"
        [kaynak]="source"
      />
    </div>
  `,
})
export class VehicleDefinitions {
  private readonly t = ceviriFonksiyonu();
  private readonly crud = viewChild(TanimCrud);
  protected readonly kind: DefinitionKind =
    (inject(ActivatedRoute).snapshot.data['tanim'] as DefinitionKind | undefined) ?? 'sahip';

  protected readonly fields: readonly TanimAlani[] = this.buildFields();
  protected readonly source = definitionSource(
    ROOTS[this.kind],
    this.fields.map((f) => f.ad),
  );

  constructor() {
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.crud()?.kaydedilmemisDegisiklikVar() ?? false;
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
