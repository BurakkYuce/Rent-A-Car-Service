import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  untracked,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import type { Sayfa } from '@core/api/sayfa';
import type { GunMetni } from '@core/form/tarih-girdisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listeSorgusuUrlSenkronu } from '@core/veri/liste-sorgusu-url';
import type { StoreDurumu } from '@core/veri/temel-store';
import { toNumber } from '@features/vehicles/vehicle-model';
import { ParaPipe, SayiPipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { SayiGirdisi } from '@shared/form/kontroller/sayi-girdisi';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';
import { Ikon } from '@shared/ikon/ikon';
import { Tablo } from '@shared/tablo/tablo';

import { segmentColumns } from '../crm-columns';
import { CRM_LIST, type CrmAnalysis as Analysis, type CrmSegmentRow } from '../crm-model';
import { CrmAnalysisStore } from '../crm.store';

/**
 * CRM analiz (`/app/crm`) — Blazor `CrmAnaliz` paritesi: müşteri segment (tarih süzgeci KİRA BAŞLANGICINA; en az kira
 * adedi; rezervasyon kaynağı; çıkış ofisi — seçenekler süzgeçsiz kümeden), özet (müşteri sayısı, toplam ciro, ek
 * hizmet — sunucu toplar), sayfalı segment tablosu ve personel BAF tahsis sayıları. Firma geneli rapor: ViewReports
 * ve şube kapsamsız kullanıcı (şubeli kullanıcıya sunucu 403). Müşteri adı/iletişim KVKK kuralıyla sunucudan.
 */
@Component({
  selector: 'rc-crm-analysis',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    Ikon,
    ParaPipe,
    SayiGirdisi,
    SayiPipe,
    Secim,
    Tablo,
    TarihSecici,
  ],
  providers: [FetchPolicy, CrmAnalysisStore],
  templateUrl: './crm-analysis.html',
  styleUrl: '../crm.scss',
})
export class CrmAnalysis {
  protected readonly store = inject(CrmAnalysisStore);
  private readonly t = ceviriFonksiyonu();

  protected readonly query = listeSorgusuUrlSenkronu(CRM_LIST);
  protected readonly columns = segmentColumns(this.t);
  protected readonly rowId = (r: CrmSegmentRow) => r.cariId;
  protected readonly num = toNumber;

  protected readonly sourceOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    (this.store.options.veri()?.kaynaklar ?? []).map((k) => ({ deger: k, etiket: k })),
  );
  protected readonly officeOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    (this.store.options.veri()?.ofisler ?? []).map((k) => ({ deger: k, etiket: k })),
  );

  /** Segment tablosu: analiz yanıtının sayfası (dört durum korunur — hata ASLA boş liste değil). */
  protected readonly segment = computed<StoreDurumu<Sayfa<CrmSegmentRow>>>(() => {
    const d = this.store.analysis.durum();
    const page = (a: Analysis): Sayfa<CrmSegmentRow> => ({
      kayitlar: a.segment.kayitlar,
      toplam: toNumber(a.segment.toplam) ?? 0,
      sayfaNo: toNumber(a.segment.sayfaNo) ?? 1,
      boyut: toNumber(a.segment.boyut) ?? 50,
    });
    switch (d.tur) {
      case 'hazir':
        return { tur: 'hazir', veri: page(d.veri) };
      case 'yukleniyor':
        return { tur: 'yukleniyor', onceki: d.onceki && page(d.onceki) };
      default:
        return d;
    }
  });

  protected readonly filterForm = new FormGroup({
    tarihBas: new FormControl<GunMetni | null>(null),
    tarihBit: new FormControl<GunMetni | null>(null),
    minKira: new FormControl<number | null>(null),
    kaynak: new FormControl<string | null>(null),
    ofis: new FormControl<string | null>(null),
  });

  constructor() {
    const policy = inject(FetchPolicy);
    policy.baglan({
      parametre: this.query.apiParametreleri,
      yukle: (p) => this.store.analysis.yukle(p),
      sifirla: () => this.store.analysis.sifirla(),
    });
    this.store.options.yukle();
    effect(() => {
      const f = this.query.sorgu().filtreler;
      untracked(() =>
        this.filterForm.reset({
          tarihBas: (f.tarihBas as GunMetni | undefined) ?? null,
          tarihBit: (f.tarihBit as GunMetni | undefined) ?? null,
          minKira: f.minKira ?? null,
          kaynak: f.kaynak ?? null,
          ofis: f.ofis ?? null,
        }),
      );
    });
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        tarihBas: v.tarihBas ?? undefined,
        tarihBit: v.tarihBit ?? undefined,
        minKira: v.minKira ?? undefined,
        kaynak: v.kaynak ?? undefined,
        ofis: v.ofis ?? undefined,
      },
    });
  }

  protected clear(): void {
    void this.query.sifirla();
  }
}
