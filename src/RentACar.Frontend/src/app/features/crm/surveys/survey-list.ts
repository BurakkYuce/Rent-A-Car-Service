import { ChangeDetectionStrategy, Component, effect, inject, untracked } from '@angular/core';
import { FormArray, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
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
import { SayiPipe } from '@shared/bicim/bicim-pipe';
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

import { surveyColumns } from '../crm-columns';
import { type AnswerRow, answerRows, emptySurvey, surveyRequest, surveyToForm } from '../crm-forms';
import {
  SURVEYS,
  SURVEY_LIST,
  SURVEY_STATUSES,
  SURVEY_TYPES,
  recordPath,
  type Survey,
  type SurveyCard,
} from '../crm-model';
import { SurveyStore, officeSuggestionFetch, rentalPickSource } from '../crm.store';
import { CustomerFilterLabel } from '../customer-filter-label';
import { RecordEditor } from '../record-editor';

type AnswerGroup = FormGroup<{
  soru: FormControl<string | null>;
  cevap: FormControl<string | null>;
  aciklama: FormControl<string | null>;
}>;

const answerGroup = (r: AnswerRow): AnswerGroup =>
  new FormGroup({
    soru: new FormControl<string | null>(r.soru, Validators.maxLength(512)),
    cevap: new FormControl<string | null>(r.cevap, Validators.maxLength(1024)),
    aciklama: new FormControl<string | null>(r.aciklama, Validators.maxLength(1024)),
  });

/**
 * Müşteri anketleri (`/app/anketler`) — Blazor `AnketList` paritesi: süzgeçler (müşteri, tür, durum, tarih aralığı,
 * çıkış ofisi), özet (N anket · yapıldı · yapılmadı — sunucu sayar), liste, satırda Düzenle / Sil (onaylı), altta
 * Yeni/Düzenle formu + soru tablosu (soru METNİ cevapla saklanır — snapshot; sorusu boş satır kaydedilmez).
 * Kira seçimi şube kapsamına süzülü. PUT `surum`; 409 `cakisma` formu silmez. OperationsWrite.
 */
@Component({
  selector: 'rc-survey-list',
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
    SayiPipe,
    Secim,
    Tablo,
    TabloHucre,
    TarihSecici,
  ],
  providers: [FetchPolicy, SurveyStore, CustomerFilterLabel],
  templateUrl: './survey-list.html',
  styleUrl: '../crm.scss',
})
export class SurveyList implements KaydedilmemisDegisiklikSahibi {
  protected readonly store = inject(SurveyStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly labels = inject(CustomerFilterLabel);
  private readonly t = ceviriFonksiyonu();

  protected readonly query = listeSorgusuUrlSenkronu(SURVEY_LIST);
  protected readonly columns = surveyColumns(this.t);
  protected readonly rowId = (r: Survey) => r.id;
  protected readonly customers = sunucuSecimKaynagi('musteri');
  protected readonly rentals = rentalPickSource();

  protected readonly typeOptions: readonly SecenekOgesi<string>[] = SURVEY_TYPES.map((x) => ({
    deger: x,
    etiket: this.t(`crm.anket.turler.${x}`),
  }));
  protected readonly statusOptions: readonly SecenekOgesi<string>[] = SURVEY_STATUSES.map((x) => ({
    deger: x,
    etiket: this.t(`crm.anket.durumlar.${x}`),
  }));

  protected readonly filterForm = new FormGroup({
    cari: new FormControl<SecimSecenegi | null>(null),
    anketTuru: new FormControl<string | null>(null),
    durum: new FormControl<string | null>(null),
    tarihBas: new FormControl<GunMetni | null>(null),
    tarihBit: new FormControl<GunMetni | null>(null),
    cikisOfisi: new FormControl<string | null>(null),
  });

  protected readonly form = new FormGroup({
    cari: new FormControl<SecimSecenegi | null>(null),
    kira: new FormControl<SecimSecenegi | null>(null),
    anketTuru: new FormControl<string | null>(null),
    durum: new FormControl<string | null>('Yapildi', Validators.required),
    cikisOfisi: new FormControl<string | null>(null, Validators.maxLength(128)),
    tarih: new FormControl<GunMetni | null>(null),
    puan: new FormControl<number | null>(0, [
      Validators.required,
      Validators.min(0),
      Validators.max(10),
    ]),
    kaynak: new FormControl<string | null>(null, Validators.maxLength(64)),
    yorum: new FormControl<string | null>(null, Validators.maxLength(1024)),
  });
  protected readonly answers = new FormArray<AnswerGroup>([]);
  protected readonly submission = formGonderimi();
  protected readonly offices = suggestionList(
    this.form.controls.cikisOfisi,
    officeSuggestionFetch(this.api),
  );
  protected readonly filterOffices = suggestionList(
    this.filterForm.controls.cikisOfisi,
    officeSuggestionFetch(this.api),
  );
  protected readonly editor = new RecordEditor<Survey, SurveyCard>({
    path: SURVEYS,
    form: this.form,
    anchor: 'rc-anket-formu',
    lock: this.submission.kilit,
    empty: emptySurvey,
    toForm: surveyToForm,
    rowOf: (c) => c.anket,
    reload: () => this.reload(),
    cardArrived: (card, fresh) => {
      if (fresh || this.answers.pristine) this.fillAnswers(card.cevaplar);
    },
    reset: () => this.fillAnswers([]),
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
    this.store.defaults.yukle();
    effect(() => {
      const f = this.query.sorgu().filtreler;
      const label = this.labels.label(f.cariId);
      untracked(() =>
        this.filterForm.reset({
          cari: label,
          anketTuru: f.anketTuru ?? null,
          durum: f.durum ?? null,
          tarihBas: (f.tarihBas as GunMetni | undefined) ?? null,
          tarihBit: (f.tarihBit as GunMetni | undefined) ?? null,
          cikisOfisi: f.cikisOfisi ?? null,
        }),
      );
    });
    // Varsayılan sorular gelince boş (yeni) form satırları kurulur.
    effect(() => {
      const d = this.store.defaults.veri();
      if (d !== undefined)
        untracked(() => {
          if (this.editor.editing().kind === 'new' && this.answers.pristine) this.fillAnswers([]);
        });
    });
    sayfaTerkKorumasi(() => this.form.dirty || this.answers.dirty);
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.form.dirty || this.answers.dirty;
  }

  protected statusLabel(s: string): string {
    return (SURVEY_STATUSES as readonly string[]).includes(s)
      ? this.t(`crm.anket.durumlar.${s as (typeof SURVEY_STATUSES)[number]}`)
      : s;
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    this.labels.remember(v.cari);
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        cariId: v.cari?.id ?? undefined,
        anketTuru: (v.anketTuru as (typeof SURVEY_TYPES)[number] | null) ?? undefined,
        durum: (v.durum as (typeof SURVEY_STATUSES)[number] | null) ?? undefined,
        tarihBas: v.tarihBas ?? undefined,
        tarihBit: v.tarihBit ?? undefined,
        cikisOfisi: v.cikisOfisi?.trim() || undefined,
      },
    });
  }

  protected clear(): void {
    void this.query.sifirla();
  }

  protected edit(row: Survey): void {
    void this.editor.edit(row, row.id);
  }

  protected remove(row: Survey): void {
    void this.editor.remove(
      row.id,
      this.t('crm.anket.silBaslik'),
      this.t('crm.anket.silMesaj'),
      this.t('crm.anket.silindi'),
    );
  }

  protected save(): void {
    const id = this.editor.recordId;
    if (this.editor.waiting) return;
    const card = this.editor.base();
    const body = surveyRequest(
      this.form.getRawValue(),
      this.answers.getRawValue(),
      id === null || card === null ? null : { tarih: card.anket.tarih, surum: card.surum },
    );
    this.submission.gonder(
      this.form,
      (key) =>
        id === null
          ? this.api.post<SurveyCard>(SURVEYS, body, { islemAnahtari: key })
          : this.api.put<SurveyCard>(recordPath(SURVEYS, id), body, { islemAnahtari: key }),
      {
        esleme: { rentalId: 'kira', cariId: 'cari' },
        basarili: () => {
          this.toast.basari(this.t(id === null ? 'crm.anket.eklendi' : 'crm.anket.kaydedildi'));
          this.editor.saved();
        },
        hata: (h) => this.editor.failed(h.kod),
      },
    );
  }

  private fillAnswers(answers: SurveyCard['cevaplar']): void {
    const rows = answerRows(this.store.defaults.veri() ?? [], answers);
    this.answers.clear({ emitEvent: false });
    for (const r of rows) this.answers.push(answerGroup(r), { emitEvent: false });
    this.answers.markAsPristine();
  }

  private reload(): void {
    this.store.list.yenile();
    this.store.counts.yenile();
  }
}
