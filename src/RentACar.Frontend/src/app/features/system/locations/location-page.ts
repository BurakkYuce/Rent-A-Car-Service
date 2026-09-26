import { ChangeDetectionStrategy, Component, inject, viewChild } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { type Observable, map } from 'rxjs';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { Schema, SelectionItem } from '@core/api/ui-tipleri';
import { pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { translationFunction } from '@core/i18n/ceviri';
import { DefinitionCrud } from '@shared/form/tanim-crud/definition-crud';
import {
  type TanimAlani,
  type DefinitionSource,
  type DefinitionRow,
  restDefinitionSource,
} from '@shared/form/tanim-crud/definition-source';
import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';

type LocationList = Schema<'SayfaOfLocationDto'>;

const ROOT = '/api/ui/v1/lokasyonlar' as const;
/** Liste ucu sayfalı (`Sayfa<LocationDto>`); ofis sayısı küçük — tek sayfada en çok 200. */
export const LOCATION_LIST_SIZE = 200;

type Translate = (key: CeviriAnahtari) => string;

/**
 * Ofis alanları (`LocationRequest`; sınırlar `LocationLimits`). Haftalık çalışma saatleri formda yok ama `hidden`
 * ile tam PUT'ta aynen geri gider (silinmesin). Şube önerisi kullanıcının şube kapsamındaki şubelerden.
 */
export function locationFields(
  t: Translate,
  branchSuggestions: (q: string) => Observable<readonly string[]>,
): readonly TanimAlani[] {
  const l = (k: string) => t(`sistem.ofis.alan.${k}` as CeviriAnahtari);
  const text = (name: string, max: number, extra: Partial<TanimAlani> = {}): TanimAlani => ({
    ad: name,
    etiket: l(name),
    tur: 'metin',
    azamiUzunluk: max,
    inList: false,
    ...extra,
  });
  return [
    text('kod', 32, { zorunlu: true, inList: true }),
    text('ad', 128, { zorunlu: true, inList: true }),
    {
      ...text('sube', 64, { inList: true }),
      tur: 'datalist',
      suggestions: branchSuggestions,
    },
    text('telefon', 32, { inList: true }),
    text('eposta', 128),
    text('adres', 512),
    text('calismaSaatleri', 64, { placeholder: '09:00-18:00' }),
    { ad: 'teslimUcreti', etiket: l('teslimUcreti'), tur: 'para', inList: false },
    text('ingilizceAd', 128),
    text('bulusmaNoktasi', 128),
    text('iata', 8, { inList: true }),
    text('lokasyonTuru', 64),
    text('binaNo', 32),
    text('ulke', 64),
    text('postaKodu', 16),
    text('mapsKonumu', 256),
    text('dropKarsilamaTuru', 64),
    text('dropCalismaSekli', 64),
    text('ozelMail', 128),
    text('ozelTelefon', 32),
    { ad: 'webSira', etiket: l('webSira'), tur: 'sayi', inList: false },
    { ad: 'webdeGizle', etiket: l('webdeGizle'), tur: 'onay', inList: false },
    text('tarif', 1024, { tur: 'textarea' }),
    text('ekAciklama', 1024, { tur: 'textarea' }),
    { ad: 'haftalikCalismaSaatleri', etiket: '', tur: 'metin', hidden: true },
    {
      ad: 'aktif',
      etiket: t('sistem.ofis.alan.durum'),
      tur: 'secim',
      zorunlu: true,
      defaultValue: true,
      secenekler: [
        { deger: true, etiket: t('sistem.ortak.aktif') },
        { deger: false, etiket: t('sistem.ortak.pasif') },
      ],
    },
  ];
}

/**
 * F11.2b ofisler (Blazor `LocationList`, OperationsWrite): tanım CRUD'u (panel). Güvenlik (F11.1b H1): ofis adı firma
 * içinde benzersiz (başka şubenin ofis adını devralma → 400 `errors[ad]`); yazma şube kapsamından geçer — kapsam dışı
 * ofis 403 (sunucu). Liste tüm ofisleri gösterir (Blazor paritesi); kapsam dışı işlem sunucuda reddedilir.
 */
@Component({
  selector: 'rc-location-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageBand, TranslocoPipe, DefinitionCrud],
  styleUrl: '../system.scss',
  template: `
    <rc-sayfa-bandi [baslik]="'sistem.ofis.baslik' | transloco" ikon="building" />
    <div class="rc-sayfa">
      <p class="not">{{ 'sistem.ofis.aciklama' | transloco }}</p>
      <rc-tanim-crud
        [baslik]="'sistem.ofis.tablo' | transloco"
        [alanlar]="fields"
        [kaynak]="source"
        layout="panel"
      />
    </div>
  `,
})
export class LocationPage {
  private readonly api = inject(ApiIstemcisi);
  private readonly crud = viewChild(DefinitionCrud);

  protected readonly fields = locationFields(translationFunction(), (q) =>
    this.api
      .get<readonly SelectionItem[]>('/api/ui/v1/secim/sube', { parametreler: { q, limit: 20 } })
      .pipe(map((list) => list.map((o) => o.etiket))),
  );
  protected readonly source: DefinitionSource = {
    ...restDefinitionSource(ROOT),
    listele: () =>
      this.api
        .get<LocationList>(ROOT, { parametreler: { boyut: LOCATION_LIST_SIZE, sirala: 'kod' } })
        .pipe(map((p) => p.kayitlar as unknown as readonly DefinitionRow[])),
  };

  constructor() {
    pageLeaveGuard(() => this.hasUnsavedChanges());
  }

  hasUnsavedChanges(): boolean {
    return this.crud()?.hasUnsavedChanges() ?? false;
  }
}
