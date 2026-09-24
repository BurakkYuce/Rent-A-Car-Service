import { ChangeDetectionStrategy, Component, effect, inject, untracked } from '@angular/core';
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
import { suggestionList } from '@features/vehicles/suggestions';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { SayiGirdisi } from '@shared/form/kontroller/sayi-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';
import { Ikon } from '@shared/ikon/ikon';
import { Tablo } from '@shared/tablo/tablo';
import { TabloHucre } from '@shared/tablo/tablo-hucre';

import { complaintColumns } from '../crm-columns';
import { complaintRequest, complaintToForm, emptyComplaint } from '../crm-forms';
import {
  COMPLAINTS,
  COMPLAINT_CHANNELS,
  COMPLAINT_LIST,
  COMPLAINT_PLACES,
  COMPLAINT_STATUSES,
  recordPath,
  type Complaint,
  type ComplaintCard,
} from '../crm-model';
import { ComplaintStore, officeSuggestionFetch, rentalPickSource } from '../crm.store';
import { CustomerFilterLabel } from '../customer-filter-label';
import { RecordEditor } from '../record-editor';

type Status = (typeof COMPLAINT_STATUSES)[number];
type Place = (typeof COMPLAINT_PLACES)[number];

/**
 * Müşteri şikayetleri (`/app/sikayetler`) — Blazor `SikayetList` paritesi: süzgeçler (müşteri, çıkış ofisi, yer, kanal,
 * durum, arama), özet (N şikayet · M açık — sunucu sayar), liste (plaka/sözleşme SÖZLEŞMEDEN okunur), satırda
 * Düzenle / Sil (onaylı), Yeni/Düzenle formu. Teslim alan/eden personel şikayetin KENDİ alanı. Müşteri adı/telefonu
 * KVKK kuralıyla sunucudan. PUT `surum`; 409 `cakisma` formu silmez. OperationsWrite.
 */
@Component({
  selector: 'rc-complaint-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    AramaSecim,
    FormHatalari,
    Ikon,
    MetinGirdisi,
    SayiGirdisi,
    Secim,
    Tablo,
    TabloHucre,
    TarihSecici,
  ],
  providers: [FetchPolicy, ComplaintStore, CustomerFilterLabel],
  templateUrl: './complaint-list.html',
  styleUrl: '../crm.scss',
})
export class ComplaintList implements KaydedilmemisDegisiklikSahibi {
  protected readonly store = inject(ComplaintStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly labels = inject(CustomerFilterLabel);
  private readonly t = ceviriFonksiyonu();

  protected readonly query = listeSorgusuUrlSenkronu(COMPLAINT_LIST);
  protected readonly columns = complaintColumns(this.t);
  protected readonly rowId = (r: Complaint) => r.id;
  protected readonly customers = sunucuSecimKaynagi('musteri');
  protected readonly staff = sunucuSecimKaynagi('personel');
  protected readonly rentals = rentalPickSource();
  protected readonly channels = COMPLAINT_CHANNELS;

  protected readonly statusOptions: readonly SecenekOgesi<string>[] = COMPLAINT_STATUSES.map(
    (x) => ({ deger: x, etiket: this.t(`crm.sikayet.durumlar.${x}`) }),
  );
  protected readonly placeOptions: readonly SecenekOgesi<string>[] = COMPLAINT_PLACES.map((x) => ({
    deger: x,
    etiket: this.t(`crm.sikayet.yerler.${x}`),
  }));

  protected readonly filterForm = new FormGroup({
    cari: new FormControl<SecimSecenegi | null>(null),
    ofis: new FormControl<string | null>(null),
    yer: new FormControl<string | null>(null),
    kanal: new FormControl<string | null>(null),
    durum: new FormControl<string | null>(null),
    ara: new FormControl<string | null>(null),
  });

  protected readonly form = new FormGroup({
    cari: new FormControl<SecimSecenegi | null>(null),
    kira: new FormControl<SecimSecenegi | null>(null),
    sikayetYeri: new FormControl<string | null>(null),
    sikayetKanali: new FormControl<string | null>(null, Validators.maxLength(64)),
    cikisOfisi: new FormControl<string | null>(null, Validators.maxLength(128)),
    teslimAlan: new FormControl<SecimSecenegi | null>(null),
    teslimEden: new FormControl<SecimSecenegi | null>(null),
    puan: new FormControl<number | null>(null, [Validators.min(1), Validators.max(5)]),
    tarih: new FormControl<GunMetni | null>(null),
    konu: new FormControl<string | null>(null, [Validators.required, Validators.maxLength(256)]),
    detay: new FormControl<string | null>(null, Validators.maxLength(2048)),
    durum: new FormControl<string | null>('Acik', Validators.required),
    cozum: new FormControl<string | null>(null, Validators.maxLength(2048)),
  });
  protected readonly submission = formGonderimi();
  protected readonly offices = suggestionList(
    this.form.controls.cikisOfisi,
    officeSuggestionFetch(this.api),
  );
  protected readonly filterOffices = suggestionList(
    this.filterForm.controls.ofis,
    officeSuggestionFetch(this.api),
  );

  protected readonly editor = new RecordEditor<Complaint, ComplaintCard>({
    path: COMPLAINTS,
    form: this.form,
    anchor: 'rc-sikayet-formu',
    lock: this.submission.kilit,
    empty: emptyComplaint,
    toForm: complaintToForm,
    rowOf: (c) => c.sikayet,
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
      const label = this.labels.label(f.cariId);
      untracked(() =>
        this.filterForm.reset({
          cari: label,
          ofis: f.ofis ?? null,
          yer: f.yer ?? null,
          kanal: f.kanal ?? null,
          durum: f.durum ?? null,
          ara: f.ara ?? null,
        }),
      );
    });
    sayfaTerkKorumasi(() => this.form.dirty);
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.form.dirty;
  }

  protected statusLabel(s: string): string {
    return (COMPLAINT_STATUSES as readonly string[]).includes(s)
      ? this.t(`crm.sikayet.durumlar.${s as Status}`)
      : s;
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    this.labels.remember(v.cari);
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        cariId: v.cari?.id ?? undefined,
        ofis: v.ofis?.trim() || undefined,
        yer: (v.yer as Place | null) ?? undefined,
        kanal: v.kanal?.trim() || undefined,
        durum: (v.durum as Status | null) ?? undefined,
        ara: v.ara?.trim() || undefined,
      },
    });
  }

  protected clear(): void {
    void this.query.sifirla();
  }

  protected edit(row: Complaint): void {
    void this.editor.edit(row, row.id);
  }

  protected remove(row: Complaint): void {
    void this.editor.remove(
      row.id,
      this.t('crm.sikayet.silBaslik'),
      this.t('crm.sikayet.silMesaj'),
      this.t('crm.sikayet.silindi'),
    );
  }

  protected save(): void {
    const id = this.editor.recordId;
    if (this.editor.waiting) return;
    const card = this.editor.base();
    const body = complaintRequest(
      this.form.getRawValue(),
      id === null || card === null ? null : { tarih: card.sikayet.tarih, surum: card.surum },
    );
    this.submission.gonder(
      this.form,
      (key) =>
        id === null
          ? this.api.post<ComplaintCard>(COMPLAINTS, body, { islemAnahtari: key })
          : this.api.put<ComplaintCard>(recordPath(COMPLAINTS, id), body, { islemAnahtari: key }),
      {
        esleme: {
          rentalId: 'kira',
          teslimAlanPersonelId: 'teslimAlan',
          teslimEdenPersonelId: 'teslimEden',
          cariId: 'cari',
        },
        basarili: () => {
          this.toast.basari(this.t(id === null ? 'crm.sikayet.eklendi' : 'crm.sikayet.kaydedildi'));
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
