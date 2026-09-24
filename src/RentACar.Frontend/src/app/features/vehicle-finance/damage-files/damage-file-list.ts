import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listeSorgusuUrlSenkronu } from '@core/veri/liste-sorgusu-url';
import { metinDegeri } from '@features/planlama-ortak/form-yardimcilari';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { Ikon } from '@shared/ikon/ikon';
import { Tablo } from '@shared/tablo/tablo';
import { TabloHucre } from '@shared/tablo/tablo-hucre';

import { damageColumns } from '../finance-columns';
import {
  DAMAGE_LIST,
  DAMAGE_STATUSES,
  type DamageFile,
  type DamageFileRequest,
} from '../finance-model';
import { DAMAGE_FILES, DamageFileStore, recordPath } from '../finance.store';

type DamageStatus = (typeof DAMAGE_STATUSES)[number];
type Transition = 'onaya-gonder' | 'onayla' | 'reddet' | 'kapat';

/**
 * Hasar dosyaları (`/app/hasar`) — Blazor `DamageFileList.razor` paritesi: "Yeni Hasar Dosyası" (kayıt yokken açık),
 * liste, satırda Onaya Gönder / Onayla / Reddet / Kapat — sunucunun `yetkiler` bayraklarından (satır kilidi altında
 * geçiş; onayla + reddet yarışı tek kazanır). MALİ BELGE DEĞİL: tahmini tutar bilgi. OperationsWrite.
 */
@Component({
  selector: 'rc-damage-file-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    AramaSecim,
    FormHatalari,
    Ikon,
    MetinGirdisi,
    ParaGirdisi,
    Secim,
    Tablo,
    TabloHucre,
  ],
  providers: [FetchPolicy, DamageFileStore],
  templateUrl: './damage-file-list.html',
  styleUrl: '../vehicle-finance.scss',
})
export class DamageFileList implements KaydedilmemisDegisiklikSahibi {
  protected readonly store = inject(DamageFileStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();

  protected readonly query = listeSorgusuUrlSenkronu(DAMAGE_LIST);
  protected readonly columns = damageColumns(this.t);
  protected readonly rowId = (r: DamageFile) => r.id;
  protected readonly vehicles = sunucuSecimKaynagi('arac');
  protected readonly customers = sunucuSecimKaynagi('musteri');
  protected readonly busy = signal<string | null>(null);
  /** Kullanıcının aç/kapa tercihi; verilmediyse Blazor gibi kayıt yokken açık. */
  private readonly createToggle = signal<boolean | null>(null);
  protected readonly createOpen = computed(
    () => this.createToggle() ?? this.store.list.veri()?.toplam === 0,
  );

  protected readonly statusOptions: readonly SecenekOgesi<DamageStatus>[] = DAMAGE_STATUSES.map(
    (s) => ({ deger: s, etiket: this.t(`aracFinans.hasar.durumlar.${s}`) }),
  );
  protected readonly filterForm = new FormGroup({
    durum: new FormControl<DamageStatus | null>(null),
  });

  protected readonly form = new FormGroup({
    arac: new FormControl<SecimSecenegi | null>(null, Validators.required),
    cari: new FormControl<SecimSecenegi | null>(null),
    tahminiTutar: new FormControl<string | null>(null),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(1024)),
  });
  protected readonly submission = formGonderimi();

  constructor() {
    const policy = inject(FetchPolicy);
    policy.baglan({
      parametre: this.query.apiParametreleri,
      yukle: (p) => this.store.list.yukle(p),
      sifirla: () => this.store.list.sifirla(),
      sekmeyeDonunce: 'yenile',
    });
    effect(() => {
      const f = this.query.sorgu().filtreler;
      untracked(() => this.filterForm.reset({ durum: f.durum ?? null }));
    });
    sayfaTerkKorumasi(() => this.form.dirty);
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.form.dirty;
  }

  protected statusLabel(s: string): string {
    return (DAMAGE_STATUSES as readonly string[]).includes(s)
      ? this.t(`aracFinans.hasar.durumlar.${s as DamageStatus}`)
      : s;
  }

  protected filter(): void {
    void this.query.degistir({
      sayfa: 1,
      filtreler: { durum: this.filterForm.getRawValue().durum ?? undefined },
    });
  }

  protected toggleCreate(): void {
    this.createToggle.set(!this.createOpen());
  }

  protected create(): void {
    const v = this.form.getRawValue();
    const body: DamageFileRequest = {
      vehicleId: v.arac?.id ?? '',
      cariId: v.cari?.id ?? null,
      tahminiTutar: v.tahminiTutar,
      aciklama: metinDegeri(v.aciklama),
    };
    this.submission.gonder(
      this.form,
      (key) => this.api.post<DamageFile>(DAMAGE_FILES, body, { islemAnahtari: key }),
      {
        esleme: { vehicleId: 'arac', cariId: 'cari' },
        basarili: (f) => {
          this.toast.basari(this.t('aracFinans.hasar.olusturuldu', { no: f.no }));
          this.form.reset();
          this.store.list.yenile();
        },
        hata: (h) => {
          if (h.kod === 'mukerrer') {
            if (h.mevcut?.ayniIcerik) this.form.reset();
            this.store.list.yenile();
          }
        },
      },
    );
  }

  protected transition(row: DamageFile, kind: Transition): void {
    if (this.busy() !== null) return;
    this.busy.set(row.id);
    this.api
      .post<DamageFile>(
        recordPath(DAMAGE_FILES, row.id, `/${kind}`),
        kind === 'onayla' || kind === 'reddet' ? {} : null,
      )
      .pipe(
        finalize(() => this.busy.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (f) => {
          this.toast.basari(
            this.t('aracFinans.hasar.durumDegisti', { no: f.no, durum: this.statusLabel(f.durum) }),
          );
          this.store.list.yenile();
        },
        error: (raw: unknown) => {
          const error = apiHatasinaCevir(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
          this.store.list.yenile();
        },
      });
  }
}
