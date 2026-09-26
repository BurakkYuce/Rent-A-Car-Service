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
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { monthStart, monthTitle, bugun } from '@core/form/tarih-girdisi';
import { translationFunction } from '@core/i18n/ceviri';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listQueryUrlSync } from '@core/veri/liste-sorgusu-url';
import { Alan } from '@shared/form/alan/alan';
import { TextInput } from '@shared/form/kontroller/text-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { Icon } from '@shared/ikon/icon';
import { PlateChipComponent } from '@shared/plaka/plaka';

import { PageBand } from '../../kabuk/sayfa-bandi/page-band';
import {
  CALENDAR,
  type CalendarVehicle,
  isMonthValid,
  daysOfMonth,
  doluluk,
  rentQuery,
  calendarParameters,
} from './takvim-modeli';
import { CalendarStore } from './calendar.store';

/** Sunucunun satır tavanı (`PlanlamaApi.TakvimAracSiniri`); aşılırsa kullanıcı süzgece yönlendirilir. */
const VEHICLE_LIMIT = 200;

/**
 * Rezervasyon takvimi (`/app/takvim`) — Blazor `ReservationCalendar.razor` paritesi: ay ızgarası (araç ×
 * gün; sarı = rezervasyon Rezerv/Onaylı, mavi = aktif kira, kira öncelikli), ay gezinmesi süzgeçleri
 * KORUR, süzgeçler plaka/marka araması + grup (öneri listesi) + şube. Doluluk SUNUCUDA hesaplanır; gün
 * sınırı İstanbul günüdür. Plaka, aracı ön seçili yeni kira formuna bağlanır (`?varac=`).
 */
@Component({
  selector: 'rc-takvim-sayfasi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PlateChipComponent,
    PageBand,
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    Icon,
    TextInput,
    Selection,
  ],
  providers: [FetchPolicy, CalendarStore],
  templateUrl: './calendar-page.html',
  styleUrl: './calendar-page.scss',
})
export class CalendarPage {
  protected readonly store = inject(CalendarStore);
  private readonly t = translationFunction();
  protected readonly liste = listQueryUrlSync(CALENDAR);

  protected readonly filterForm = new FormGroup({
    plaka: new FormControl<string | null>(null),
    grup: new FormControl<string | null>(null),
    sube: new FormControl<string | null>(null),
  });

  /** Görüntülenen ay: sunucu yanıtı (kanonik) → URL → İstanbul'da bu ay. */
  protected readonly month = computed(() => {
    const data = this.store.grid.veri();
    if (data) return data.ay;
    const url = this.liste.sorgu().filtreler.ay;
    return isMonthValid(url) ? url : bugun().slice(0, 7);
  });
  protected readonly monthLabel = computed(() => monthTitle(`${this.month()}-01`));
  protected readonly previousMonth = computed(
    () => this.store.grid.veri()?.oncekiAy ?? monthStart(`${this.month()}-01`, -1).slice(0, 7),
  );
  protected readonly nextMonth = computed(
    () => this.store.grid.veri()?.sonrakiAy ?? monthStart(`${this.month()}-01`, 1).slice(0, 7),
  );
  protected readonly previousLabel = computed(() => monthTitle(`${this.previousMonth()}-01`));
  protected readonly nextLabel = computed(() => monthTitle(`${this.nextMonth()}-01`));

  protected readonly gunler = computed(() => {
    const data = this.store.grid.veri();
    return data ? daysOfMonth(data.ay, Number(data.gunSayisi)) : [];
  });
  protected readonly araclar = computed(() => this.store.grid.veri()?.araclar ?? []);
  protected readonly issued = computed(() => {
    const data = this.store.grid.veri();
    return data !== undefined && Number(data.aracToplam) > data.araclar.length;
  });
  protected readonly vehicleTotal = computed(() => Number(this.store.grid.veri()?.aracToplam ?? 0));
  protected readonly limit = VEHICLE_LIMIT;

  protected readonly branchOptions = computed<readonly SecenekOgesi<string>[]>(() => {
    const list = [...(this.store.options.veri()?.subeler ?? [])];
    const selected = this.liste.sorgu().filtreler.sube;
    if (selected !== undefined && !list.includes(selected)) list.unshift(selected);
    return list.map((s) => ({ deger: s, etiket: s }));
  });
  protected readonly groupSuggestions = computed(() => this.store.options.veri()?.gruplar ?? []);
  protected readonly hasFilter = computed(() => {
    const f = this.liste.sorgu().filtreler;
    return f.plaka !== undefined || f.grup !== undefined || f.sube !== undefined;
  });

  constructor() {
    const policy = inject(FetchPolicy);
    policy.connect({
      parametre: computed(() => calendarParameters(this.liste.apiParametreleri()), {
        equal: (a, b) => JSON.stringify(a) === JSON.stringify(b),
      }),
      yukle: (p) => this.store.grid.yukle(p),
      sifirla: () => this.store.grid.reset(),
      // Başka sekmede kira/rezervasyon açılmış olabilir: dönüşte taze doluluk.
      sekmeyeDonunce: 'yenile',
    });
    policy.connect({
      parametre: signal(0).asReadonly(),
      yukle: () => this.store.options.yukle(),
      sifirla: () => this.store.options.reset(),
    });
    effect(() => {
      const f = this.liste.sorgu().filtreler;
      untracked(() =>
        this.filterForm.reset({
          plaka: f.plaka ?? null,
          grup: f.grup ?? null,
          sube: f.sube ?? null,
        }),
      );
    });
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    void this.liste.degistir({
      filtreler: {
        ay: this.month(),
        plaka: v.plaka ?? undefined,
        grup: v.grup ?? undefined,
        sube: v.sube ?? undefined,
      },
    });
  }

  /** Blazor "Temizle": süzgeçler kalkar, AY korunur. */
  protected clear(): void {
    void this.liste.degistir({
      filtreler: { ay: this.month(), plaka: undefined, grup: undefined, sube: undefined },
    });
  }

  /** Ay gezinmesi süzgeçleri KORUR (Blazor `AyUrl`). */
  protected goToMonth(month: string): void {
    void this.liste.degistir({ filtreler: { ...this.liste.sorgu().filtreler, ay: month } });
  }

  protected cell(vehicle: CalendarVehicle, index: number) {
    return doluluk(vehicle.gunler[index]);
  }

  protected cellHeader(vehicle: CalendarVehicle, index: number): string | null {
    const d = this.cell(vehicle, index);
    return d === null
      ? null
      : this.t('takvimSayfasi.hucreBasligi', {
          plaka: vehicle.plaka,
          tur: this.t(`takvimSayfasi.doluluk.${d}`),
        });
  }

  protected rentQuery(vehicle: CalendarVehicle): Record<string, string> {
    return rentQuery(vehicle.id);
  }
}
