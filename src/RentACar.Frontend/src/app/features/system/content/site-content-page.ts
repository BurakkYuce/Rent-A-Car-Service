import { ChangeDetectionStrategy, Component, computed, inject, viewChildren } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { map } from 'rxjs';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { Sema } from '@core/api/ui-tipleri';
import { sayfaTerkKorumasi } from '@core/form/kaydedilmemis-degisiklik';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { TanimCrud } from '@shared/form/tanim-crud/tanim-crud';
import {
  type TanimAlani,
  type TanimKaynagi,
  type TanimSatiri,
  restTanimKaynagi,
} from '@shared/form/tanim-crud/tanim-kaynagi';
import { SayfaBandi } from '../../../kabuk/sayfa-bandi/sayfa-bandi';

type PageList = Sema<'SayfaOfPageRowDto'>;

const ROOT = '/api/ui/v1/site-icerik' as const;

type Translate = (key: CeviriAnahtari) => string;

function publishedField(t: Translate): TanimAlani {
  return {
    ad: 'yayinda',
    etiket: t('sistem.icerik.yayinda'),
    tur: 'secim',
    zorunlu: true,
    defaultValue: false,
    secenekler: [
      { deger: true, etiket: t('sistem.icerik.yayinDurum') },
      { deger: false, etiket: t('sistem.icerik.taslak') },
    ],
  };
}

/** Sayfa alanları (`SiteIcerikService`: başlık ≤ 200, gövde ≤ 20.000, özet ≤ 300; adres boşsa başlıktan). */
export function pageFields(t: Translate): readonly TanimAlani[] {
  return [
    {
      ad: 'baslik',
      etiket: t('sistem.icerik.baslikAlan'),
      tur: 'metin',
      zorunlu: true,
      azamiUzunluk: 200,
    },
    { ad: 'slug', etiket: t('sistem.icerik.adres'), tur: 'metin', azamiUzunluk: 200 },
    { ad: 'sira', etiket: t('sistem.icerik.sira'), tur: 'sayi', defaultValue: 0 },
    publishedField(t),
    {
      ad: 'metaAciklama',
      etiket: t('sistem.icerik.meta'),
      tur: 'metin',
      azamiUzunluk: 300,
      inList: false,
    },
    {
      ad: 'govde',
      etiket: t('sistem.icerik.govde'),
      tur: 'textarea',
      zorunlu: true,
      azamiUzunluk: 20000,
      inList: false,
    },
  ];
}

/** SSS alanları (soru ≤ 300, cevap ≤ 4.000). */
export function faqFields(t: Translate): readonly TanimAlani[] {
  return [
    { ad: 'soru', etiket: t('sistem.icerik.soru'), tur: 'metin', zorunlu: true, azamiUzunluk: 300 },
    {
      ad: 'cevap',
      etiket: t('sistem.icerik.cevap'),
      tur: 'textarea',
      zorunlu: true,
      azamiUzunluk: 4000,
    },
    { ad: 'sira', etiket: t('sistem.icerik.sira'), tur: 'sayi', defaultValue: 0 },
    publishedField(t),
  ];
}

/**
 * F11.2b site içeriği (Blazor `SiteIcerikYonetim`; OperationsWrite + web sitesi modülü): içerik sayfaları ve SSS, iki
 * tanım CRUD'u (sürüm/409 birleştirme çekirdekte). Metinler DÜZ METİNDİR — site onları kodlayarak basar; ekran da HTML
 * olarak yorumlamaz.
 */
@Component({
  selector: 'rc-site-content-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SayfaBandi, TranslocoPipe, TanimCrud],
  styleUrl: '../system.scss',
  template: `
    <rc-sayfa-bandi [baslik]="'sistem.icerik.baslik' | transloco" ikon="world" />
    <div class="rc-sayfa">
      @if (!module()) {
        <p class="bos">{{ 'sistem.ortak.modulYok' | transloco }}</p>
      } @else {
        <p class="not">{{ 'sistem.icerik.aciklama' | transloco }}</p>
        <rc-tanim-crud
          [baslik]="'sistem.icerik.sayfalar' | transloco"
          [alanlar]="pages"
          [kaynak]="pageSource"
          layout="panel"
        />
        <rc-tanim-crud
          [baslik]="'sistem.icerik.sss' | transloco"
          [alanlar]="faqs"
          [kaynak]="faqSource"
          layout="panel"
        />
      }
    </div>
  `,
})
export class SiteContentPage {
  private readonly api = inject(ApiIstemcisi);
  private readonly session = inject(OturumServisi);
  private readonly cruds = viewChildren(TanimCrud);
  private readonly t = ceviriFonksiyonu();

  protected readonly module = computed(() => this.session.ben()?.moduller.webSitesi === true);
  protected readonly pages = pageFields(this.t);
  protected readonly faqs = faqFields(this.t);
  protected readonly pageSource: TanimKaynagi = {
    ...restTanimKaynagi(`${ROOT}/sayfalar`),
    listele: () =>
      this.api
        .get<PageList>(`${ROOT}/sayfalar`, { parametreler: { boyut: 200, sirala: 'sira' } })
        .pipe(map((p) => p.kayitlar as unknown as readonly TanimSatiri[])),
  };
  protected readonly faqSource = restTanimKaynagi(`${ROOT}/sss`);

  constructor() {
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.cruds().some((c) => c.kaydedilmemisDegisiklikVar());
  }
}
