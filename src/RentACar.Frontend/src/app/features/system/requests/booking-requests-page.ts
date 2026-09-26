import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import type { Observable } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { Sema } from '@core/api/ui-tipleri';
import { sayfaTerkKorumasi } from '@core/form/kaydedilmemis-degisiklik';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { TemelStore } from '@core/veri/temel-store';
import { ParaPipe, TarihPipe, TarihSaatPipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinAlani } from '@shared/form/kontroller/metin-alani';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { Secim } from '@shared/form/kontroller/secim';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';

import { SayfaBandi } from '../../../kabuk/sayfa-bandi/sayfa-bandi';
import { pageCount } from '../audit/audit-page';

type RequestPage = Sema<'BookingRequestPageDto'>;
type RequestRow = Sema<'BookingRequestRowDto'>;
type Note = Sema<'BookingRequestNoteDto'>;
type Candidate = Sema<'CandidateVehicleDto'>;
type Converted = Sema<'BookingRequestConvertedDto'>;

const ROOT = '/api/ui/v1/gelen-talepler' as const;
export const REQUEST_PAGE_SIZE = 25;
export const REQUEST_STATUSES = [
  'Yeni',
  'Iletisimde',
  'TeklifVerildi',
  'Donustu',
  'Reddedildi',
  'Kayip',
] as const;

/** Blazor `?durum=<sayı>` kodları (`PublicBookingRequestDurum` enum değerleri; eski yer imleri ve panel bağlantısı). */
const LEGACY_STATUS_CODES: Readonly<Record<string, (typeof REQUEST_STATUSES)[number]>> = {
  '0': 'Yeni',
  '1': 'Donustu',
  '2': 'Reddedildi',
  '3': 'Iletisimde',
  '4': 'TeklifVerildi',
  '5': 'Kayip',
};

/**
 * F11.3: sorgudaki `durum` süzgeci — durum adı (`Yeni`) ya da Blazor sayı kodu (`0`). Bilinmeyen değer `null`
 * (varsayılan liste). Yalnız durum okunur; kişisel veri taşıyabilen `ara` URL'den alınmaz.
 */
export function statusFromQuery(value: string | null): string | null {
  if (value === null) return null;
  if ((REQUEST_STATUSES as readonly string[]).includes(value)) return value;
  return Object.hasOwn(LEGACY_STATUS_CODES, value) ? (LEGACY_STATUS_CODES[value] ?? null) : null;
}

interface RequestQuery {
  readonly durum: string | null;
  readonly ara: string | null;
  readonly sayfa: number;
}

/**
 * F11.2b gelen site talepleri (Blazor `GelenTalepler`; OperationsWrite). Talep bir LEAD'dir: ziyaretçinin girdiği
 * ad/telefon/e-posta gösterilir (TC yok), yalnız bellekte tutulur. Durum ilerletme, üstlen/bırak, notlar, onaylı ret ve
 * müsait araçla rezervasyona dönüştürme (aday araçlar sunucuda şube kapsamına süzülür; kapsam dışı araç 403).
 */
@Component({
  selector: 'rc-booking-requests-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    SayfaBandi,
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    ParaPipe,
    TarihPipe,
    TarihSaatPipe,
    Alan,
    FormHatalari,
    MetinAlani,
    MetinGirdisi,
    Secim,
  ],
  styleUrl: '../system.scss',
  templateUrl: './booking-requests-page.html',
})
export class BookingRequestsPage {
  private readonly api = inject(ApiIstemcisi);
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();

  protected readonly filters = new FormGroup({
    durum: new FormControl<string | null>(null),
    ara: new FormControl<string | null>(null, Validators.maxLength(100)),
  });
  protected readonly statusOptions: readonly SecenekOgesi<string>[] = REQUEST_STATUSES.map((d) => ({
    deger: d,
    etiket: this.statusLabel(d),
  }));
  private readonly query = signal<RequestQuery>({ durum: null, ara: null, sayfa: 1 });
  protected readonly list = new TemelStore<RequestPage, RequestQuery>(
    (q) =>
      this.api.get<RequestPage>(ROOT, {
        parametreler: { durum: q.durum, ara: q.ara, sayfa: q.sayfa, boyut: REQUEST_PAGE_SIZE },
      }),
    { oncekiVeriyiKoru: true },
  );
  protected readonly page = computed(() => this.query().sayfa);
  protected readonly pages = computed(() => {
    const d = this.list.veri();
    return d ? pageCount(d.toplam, d.boyut) : 1;
  });

  /** Açık talep (notlar + dönüştürme paneli). */
  protected readonly open = signal<RequestRow | null>(null);
  protected readonly notes = signal<readonly Note[] | null>(null);
  protected readonly candidates = signal<readonly Candidate[] | null>(null);
  protected readonly noteForm = new FormGroup({
    metin: new FormControl<string | null>(null, [Validators.required, Validators.maxLength(2000)]),
  });
  protected readonly noteSubmit = formGonderimi();
  protected readonly convertForm = new FormGroup({
    aracId: new FormControl<string | null>(null, Validators.required),
  });
  protected readonly convertSubmit = formGonderimi();
  protected readonly candidateOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    (this.candidates() ?? []).map((c) => ({
      deger: c.id,
      etiket:
        [c.plaka, c.marka, c.tip, c.grup, c.sube].filter(Boolean).join(' · ') +
        (c.ilanAraci ? ` (${this.t('sistem.talep.ilanAraci')})` : ''),
    })),
  );
  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);

  constructor() {
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
    const initialStatus = statusFromQuery(
      inject(ActivatedRoute).snapshot.queryParamMap.get('durum'),
    );
    if (initialStatus) {
      this.filters.controls.durum.setValue(initialStatus);
      this.query.set({ durum: initialStatus, ara: null, sayfa: 1 });
    }
    this.list.yukle(this.query());
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.noteForm.dirty;
  }

  protected statusLabel(d: string): string {
    return (REQUEST_STATUSES as readonly string[]).includes(d)
      ? this.t(`sistem.talep.durum.${d}` as CeviriAnahtari)
      : d;
  }

  protected search(): void {
    if (this.filters.invalid) return;
    const v = this.filters.getRawValue();
    this.query.set({ durum: v.durum, ara: v.ara?.trim() || null, sayfa: 1 });
    this.list.yukle(this.query());
  }

  protected go(delta: number): void {
    const next = Math.min(this.pages(), Math.max(1, this.page() + delta));
    if (next === this.page()) return;
    this.query.update((q) => ({ ...q, sayfa: next }));
    this.list.yukle(this.query());
  }

  protected toggle(r: RequestRow): void {
    if (this.open()?.id === r.id) {
      this.open.set(null);
      return;
    }
    this.open.set(r);
    this.notes.set(null);
    this.candidates.set(null);
    this.noteForm.reset();
    this.convertForm.reset();
    this.noteSubmit.kilit.yenile();
    this.convertSubmit.kilit.yenile();
    const id = encodeURIComponent(r.id);
    this.api
      .get<readonly Note[]>(`${ROOT}/${id}/notlar`)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (n) => this.notes.set(n), error: () => this.notes.set([]) });
    if (r.aktif) {
      this.api
        .get<readonly Candidate[]>(`${ROOT}/${id}/aday-araclar`)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({ next: (c) => this.candidates.set(c), error: () => this.candidates.set([]) });
    }
  }

  protected setStatus(r: RequestRow, durum: string): void {
    this.act(this.api.post(`${ROOT}/${encodeURIComponent(r.id)}/durum`, { durum }));
  }

  protected claim(r: RequestRow, ustlen: boolean): void {
    this.act(this.api.post(`${ROOT}/${encodeURIComponent(r.id)}/ustlen`, { ustlen }));
  }

  protected async reject(r: RequestRow): Promise<void> {
    const yes = await this.confirm.sor({
      baslik: this.t('sistem.talep.reddetBaslik', { ad: r.adSoyad }),
      mesaj: this.t('sistem.talep.reddetMesaj'),
      onayEtiketi: this.t('sistem.talep.reddet'),
      tehlikeli: true,
    });
    if (yes) this.act(this.api.post(`${ROOT}/${encodeURIComponent(r.id)}/reddet`, {}));
  }

  protected addNote(r: RequestRow): void {
    const v = this.noteForm.getRawValue();
    this.noteSubmit.gonder(
      this.noteForm,
      (key) =>
        this.api.post<readonly Note[]>(`${ROOT}/${encodeURIComponent(r.id)}/notlar`, v, {
          islemAnahtari: key,
        }),
      {
        basarili: (n) => {
          this.noteForm.reset();
          this.notes.set(n);
          this.list.yenile();
        },
      },
    );
  }

  protected convert(r: RequestRow): void {
    const v = this.convertForm.getRawValue();
    this.convertSubmit.gonder(
      this.convertForm,
      (key) =>
        this.api.post<Converted>(`${ROOT}/${encodeURIComponent(r.id)}/donustur`, v, {
          islemAnahtari: key,
        }),
      {
        basarili: () => {
          this.open.set(null);
          this.toast.basari(this.t('sistem.talep.donusturuldu'));
          this.list.yenile();
        },
      },
    );
  }

  private act(request: Observable<unknown>): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.actionError.set(null);
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.busy.set(false);
        this.open.set(null);
        this.toast.basari(this.t('sistem.ortak.kaydedildi'));
        this.list.yenile();
      },
      error: (e: unknown) => {
        this.busy.set(false);
        const h = apiHatasinaCevir(e);
        this.actionError.set(h.alanlar?.['durum']?.[0] ?? h.detay);
      },
    });
  }
}
