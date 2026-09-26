import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { translationFunction } from '@core/i18n/ceviri';
import { DUGME_IZINLERI } from '@core/oturum/dugme-izinleri';
import { SessionService } from '@core/oturum/session-service';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listQueryUrlSync } from '@core/veri/liste-sorgusu-url';
import { Alan } from '@shared/form/alan/alan';
import { SearchSelection } from '@shared/form/arama-secim/search-selection';
import {
  type SecimSecenegi,
  serverSelectionSource,
} from '@shared/form/arama-secim/selection-source';
import { TextInput } from '@shared/form/kontroller/text-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { DatePicker } from '@shared/form/tarih/date-picker';
import { Icon } from '@shared/ikon/icon';
import { CollapsibleFilter } from '@shared/katlanir-filtre/collapsible-filter';
import { PlateChipComponent } from '@shared/plaka/plaka';
import type { DisaAktarma } from '@shared/tablo/disa-aktarma';
import { Table } from '@shared/tablo/table';
import { TableCell } from '@shared/tablo/table-cell';

import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { ReservationActions } from '../reservation-actions';
import {
  RESERVATION_STATUSES,
  statusBadge,
  isReservationStatus,
  type ReservationStatus,
  type ReservationListRow,
} from '../rezervasyon-modeli';
import { RESERVATION_LIST, ReservationListStore, exportParams } from './reservation-list.store';
import { reservationColumns } from './reservation-columns';

/**
 * Rezervasyon listesi (`/app/rezervasyonlar`) — Blazor `ReservationList.razor` paritesi (F5.2a): FAZ-48 sütunları
 * ve süzgeçleri, sunucu sayfalama/sıralama (URL tek doğruluk kaynağı), süzgeci taşıyan dışa aktarma, satır
 * eylemleri Onayla / Kiraya çevir, "Düzenle" = rezervasyon formu (`/rezervasyonlar/:id`; iptal orada — kapısı
 * sunucunun `yetkiler.iptal`'i). Sayfa OperationsWrite ister (rota kapısı + sunucu); dışa aktarma ViewReports.
 */
@Component({
  selector: 'rc-rezervasyon-listesi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PlateChipComponent,
    PageBand,
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    SearchSelection,
    Icon,
    CollapsibleFilter,
    TextInput,
    Selection,
    Table,
    TableCell,
    DatePicker,
  ],
  providers: [FetchPolicy, ReservationListStore, ReservationActions],
  templateUrl: './reservation-list.html',
  styleUrl: './reservation-list.scss',
})
export class ReservationList {
  protected readonly store = inject(ReservationListStore);
  protected readonly islemler = inject(ReservationActions);
  private readonly oturum = inject(SessionService);
  private readonly router = inject(Router);
  private readonly t = translationFunction();

  protected readonly liste = listQueryUrlSync(RESERVATION_LIST);
  protected readonly columns = reservationColumns(this.t);
  protected readonly defaultSort = RESERVATION_LIST.varsayilanSirala;
  protected readonly identity = (r: ReservationListRow) => r.id;
  protected readonly filterOpen = signal(false);
  protected readonly sources = serverSelectionSource('rezervasyon-kaynagi');

  // Dışa aktarma Blazor liste ucu (ViewReports) — izin haritası tek yerde (`DUGME_IZINLERI`, UiDugmeIzinTests).
  private readonly rapor = computed(() =>
    this.oturum.hasPermissions(DUGME_IZINLERI.kiraDisaAktar.izinler),
  );
  protected readonly exportItem = computed<DisaAktarma | null>(() =>
    this.rapor()
      ? {
          yol: '/listeler/export/rezervasyonlar',
          parametreler: exportParams(this.liste.apiParametreleri()),
          bicimler: ['excel', 'csv', 'pdf'],
        }
      : null,
  );

  protected readonly filterForm = new FormGroup({
    q: new FormControl<string | null>(null),
    durum: new FormControl<ReservationStatus | null>(null),
    basMin: new FormControl<string | null>(null),
    basMax: new FormControl<string | null>(null),
    kaynak: new FormControl<SecimSecenegi | null>(null),
  });

  protected readonly statusOptions: readonly SecenekOgesi<ReservationStatus>[] =
    RESERVATION_STATUSES.map((d) => ({ deger: d, etiket: this.t(`rezervasyon.durumlar.${d}`) }));

  constructor() {
    inject(FetchPolicy).connect({
      parametre: this.liste.apiParametreleri,
      yukle: (p) => this.store.liste.yukle(p),
      sifirla: () => this.store.liste.reset(),
      // Durum başka sekmede (form, takvim, teklif kabulü) değişebilir: dönüşte taze liste.
      sekmeyeDonunce: 'yenile',
    });

    // URL → form (geri/ileri, paylaşılan bağlantı, "Temizle").
    effect(() => {
      const f = this.liste.sorgu().filtreler;
      untracked(() =>
        this.filterForm.reset({
          q: f.q ?? null,
          durum: f.durum ?? null,
          basMin: f.basMin ?? null,
          basMax: f.basMax ?? null,
          kaynak: f.kaynak === undefined ? null : { id: f.kaynak, etiket: f.kaynak },
        }),
      );
    });
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    void this.liste.degistir({
      filtreler: {
        q: v.q ?? undefined,
        durum: v.durum ?? undefined,
        basMin: v.basMin ?? undefined,
        basMax: v.basMax ?? undefined,
        kaynak: v.kaynak?.etiket ?? undefined,
      },
    });
  }

  protected clear(): void {
    void this.liste.sifirla();
  }

  protected openRow(row: ReservationListRow): void {
    void this.router.navigate(['/rezervasyonlar', row.id]);
  }

  protected rozet(status: string): string {
    return statusBadge(status);
  }

  protected statusLabel(status: string): string {
    return isReservationStatus(status) ? this.t(`rezervasyon.durumlar.${status}`) : status;
  }

  protected acik(row: ReservationListRow): boolean {
    return row.durum === 'Rezerv' || row.durum === 'Onayli';
  }

  protected onayla(row: ReservationListRow): void {
    this.islemler.onayla({ id: row.id, no: row.no }, () => this.store.liste.yenile());
  }

  protected convertToRental(row: ReservationListRow): void {
    void this.islemler.kirayaCevir({ id: row.id, no: row.no }, () => this.store.liste.yenile());
  }
}
