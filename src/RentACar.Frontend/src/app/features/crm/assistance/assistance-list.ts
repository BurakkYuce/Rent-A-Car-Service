import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  untracked,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { GunMetni } from '@core/form/tarih-girdisi';
import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listeSorgusuUrlSenkronu } from '@core/veri/liste-sorgusu-url';
import { FilterPanelComponent } from '@shared/filtre-paneli/filtre-paneli';
import { PlateChipComponent } from '@shared/plaka/plaka';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import type { SecimSecenegi } from '@shared/form/arama-secim/secim-kaynagi';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { OnayKutusu } from '@shared/form/kontroller/onay-kutusu';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSaatSecici } from '@shared/form/tarih/tarih-saat-secici';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';
import { Tablo } from '@shared/tablo/tablo';
import { TabloHucre } from '@shared/tablo/tablo-hucre';

import { SayfaBandi } from '../../../kabuk/sayfa-bandi/sayfa-bandi';
import { assistanceColumns } from '../crm-columns';
import {
  assistanceHiddenContact,
  assistanceRequest,
  assistanceToForm,
  emptyAssistance,
} from '../crm-forms';
import {
  ASSISTANCE,
  ASSISTANCE_LIST,
  recordPath,
  type Assistance,
  type AssistanceCard,
} from '../crm-model';
import { AssistanceStore, rentalPickSource } from '../crm.store';
import { RecordEditor } from '../record-editor';

/** Blazor tek "Durum" seçimi → uç bayrakları. */
export type AssistanceView = 'acik' | 'kapali' | 'cekici' | 'lastik';

export function viewFilters(v: AssistanceView | null): {
  kapandi?: boolean;
  hareketEdemiyor?: boolean;
  yedekLastik?: boolean;
} {
  switch (v) {
    case 'acik':
      return { kapandi: false };
    case 'kapali':
      return { kapandi: true };
    case 'cekici':
      return { hareketEdemiyor: true };
    case 'lastik':
      return { yedekLastik: true };
    default:
      return {};
  }
}

export function viewOf(f: {
  readonly kapandi?: boolean;
  readonly hareketEdemiyor?: boolean;
  readonly yedekLastik?: boolean;
}): AssistanceView | null {
  if (f.hareketEdemiyor) return 'cekici';
  if (f.yedekLastik) return 'lastik';
  if (f.kapandi === true) return 'kapali';
  if (f.kapandi === false) return 'acik';
  return null;
}

/**
 * Assistans (yol yardım) talepleri (`/app/assistans`) — Blazor `AssistansTalepList` paritesi: süzgeçler (plaka, tarih
 * aralığı, arama, durum), özet (N talep · açık · hareket edemiyor — sunucu sayar), liste, satırda Düzenle / Sil,
 * Yeni/Düzenle formu. Kira seçilirse boş plaka/ad/telefon SÖZLEŞMEDEN kopyalanır (olay tutanağı; elle yazılan değer
 * ezilmez). Ad/telefon bağlı müşteri KVKK ile anonimse sunucudan `null` gelir. OperationsWrite.
 */
@Component({
  selector: 'rc-assistance-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FilterPanelComponent,
    SayfaBandi,
    PlateChipComponent,
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    AramaSecim,
    FormHatalari,
    MetinGirdisi,
    OnayKutusu,
    Secim,
    Tablo,
    TabloHucre,
    TarihSaatSecici,
    TarihSecici,
  ],
  providers: [FetchPolicy, AssistanceStore],
  templateUrl: './assistance-list.html',
  styleUrl: '../crm.scss',
})
export class AssistanceList implements KaydedilmemisDegisiklikSahibi {
  protected readonly store = inject(AssistanceStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly t = ceviriFonksiyonu();

  protected readonly query = listeSorgusuUrlSenkronu(ASSISTANCE_LIST);
  protected readonly columns = assistanceColumns(this.t);
  protected readonly rowId = (r: Assistance) => r.id;
  protected readonly rentals = rentalPickSource();

  protected readonly viewOptions: readonly SecenekOgesi<AssistanceView>[] = (
    ['acik', 'kapali', 'cekici', 'lastik'] as const
  ).map((x) => ({ deger: x, etiket: this.t(`crm.assistans.gorunumler.${x}`) }));

  protected readonly filterForm = new FormGroup({
    plaka: new FormControl<string | null>(null),
    tarihBas: new FormControl<GunMetni | null>(null),
    tarihBit: new FormControl<GunMetni | null>(null),
    ara: new FormControl<string | null>(null),
    gorunum: new FormControl<AssistanceView | null>(null),
  });

  protected readonly form = new FormGroup({
    kira: new FormControl<SecimSecenegi | null>(null),
    plaka: new FormControl<string | null>(null, Validators.maxLength(16)),
    adSoyad: new FormControl<string | null>(null, Validators.maxLength(256)),
    cepTel: new FormControl<string | null>(null, Validators.maxLength(32)),
    zaman: new FormControl<string | null>(null),
    sebep: new FormControl<string | null>(null, Validators.maxLength(512)),
    yedekLastikMi: new FormControl<boolean>(false, { nonNullable: true }),
    aracHareketMi: new FormControl<boolean>(false, { nonNullable: true }),
    kapandi: new FormControl<boolean>(false, { nonNullable: true }),
    mesaj: new FormControl<string | null>(null, [Validators.required, Validators.maxLength(2048)]),
    cozum: new FormControl<string | null>(null, Validators.maxLength(1024)),
    clearName: new FormControl<boolean>(false, { nonNullable: true }),
    clearPhone: new FormControl<boolean>(false, { nonNullable: true }),
  });
  protected readonly submission = formGonderimi();
  /** Düzenlenen kayıtta KVKK ile gizlenen ad/telefon ("Temizle" kutusu yalnız bunlarda). */
  protected readonly hiddenContact = computed(() =>
    assistanceHiddenContact(this.editor.base()?.talep ?? null),
  );

  protected readonly editor = new RecordEditor<Assistance, AssistanceCard>({
    path: ASSISTANCE,
    form: this.form,
    anchor: 'rc-assistans-formu',
    lock: this.submission.kilit,
    empty: emptyAssistance,
    toForm: assistanceToForm,
    rowOf: (c) => c.talep,
    reload: () => this.reload(),
  });

  constructor() {
    const policy = inject(FetchPolicy);
    policy.baglan({
      parametre: this.query.apiParametreleri,
      yukle: (p) => {
        this.store.list.yukle(p);
        this.store.counts.yukle(p);
      },
      sifirla: () => {
        this.store.list.sifirla();
        this.store.counts.sifirla();
      },
      sekmeyeDonunce: 'yenile',
    });
    effect(() => {
      const f = this.query.sorgu().filtreler;
      untracked(() =>
        this.filterForm.reset({
          plaka: f.plaka ?? null,
          tarihBas: (f.tarihBas as GunMetni | undefined) ?? null,
          tarihBit: (f.tarihBit as GunMetni | undefined) ?? null,
          ara: f.ara ?? null,
          gorunum: viewOf(f),
        }),
      );
    });
    sayfaTerkKorumasi(() => this.form.dirty);
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.form.dirty;
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    const flags = viewFilters(v.gorunum);
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        plaka: v.plaka?.trim() || undefined,
        tarihBas: v.tarihBas ?? undefined,
        tarihBit: v.tarihBit ?? undefined,
        ara: v.ara?.trim() || undefined,
        kapandi: flags.kapandi,
        hareketEdemiyor: flags.hareketEdemiyor,
        yedekLastik: flags.yedekLastik,
      },
    });
  }

  protected clear(): void {
    void this.query.sifirla();
  }

  protected edit(row: Assistance): void {
    void this.editor.edit(row, row.id);
  }

  protected remove(row: Assistance): void {
    void this.editor.remove(
      row.id,
      this.t('crm.assistans.silBaslik'),
      this.t('crm.assistans.silMesaj'),
      this.t('crm.assistans.silindi'),
    );
  }

  protected save(): void {
    const id = this.editor.recordId;
    if (this.editor.waiting) return;
    const card = this.editor.base();
    const body = assistanceRequest(
      this.form.getRawValue(),
      id === null || card === null ? null : { surum: card.surum, row: card.talep },
    );
    this.submission.gonder(
      this.form,
      (key) =>
        id === null
          ? this.api.post<AssistanceCard>(ASSISTANCE, body, { islemAnahtari: key })
          : this.api.put<AssistanceCard>(recordPath(ASSISTANCE, id), body, { islemAnahtari: key }),
      {
        esleme: { rentalId: 'kira' },
        basarili: () => {
          this.toast.basari(
            this.t(id === null ? 'crm.assistans.eklendi' : 'crm.assistans.kaydedildi'),
          );
          this.editor.saved();
        },
        hata: (h) => this.editor.failed(h.kod),
      },
    );
  }

  private reload(): void {
    this.store.list.yenile();
    this.store.counts.yenile();
  }
}
