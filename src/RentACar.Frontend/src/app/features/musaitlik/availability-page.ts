import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import {
  type AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  type ValidationErrors,
  Validators,
} from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { formatDateTime } from '@core/bicim/bicim';
import { parseHour } from '@core/form/tarih-girdisi';
import { translationFunction } from '@core/i18n/ceviri';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listQueryUrlSync } from '@core/veri/liste-sorgusu-url';
import type { StoreState } from '@core/veri/temel-store';
import { EmptyState } from '@shared/bos-durum/empty-state';
import { Alan } from '@shared/form/alan/alan';
import { TextInput } from '@shared/form/kontroller/text-input';
import { NumberInput } from '@shared/form/kontroller/number-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { DatePicker } from '@shared/form/tarih/date-picker';
import { Icon } from '@shared/ikon/icon';
import { PlateChipComponent } from '@shared/plaka/plaka';
import { Table } from '@shared/tablo/table';
import { TableCell } from '@shared/tablo/table-cell';

import { PageBand } from '../../kabuk/sayfa-bandi/page-band';
import { MUSAITLIK, type AvailabilityRow, searchParams, rentParameters } from './musaitlik-modeli';
import { availabilityColumns } from './availability-columns';
import { AvailabilityStore } from './availability.store';

/** Saat kutusu: boş serbest; doluysa `saatCoz` kabul etmeli (`25:00` sessizce kırpılmaz). */
function hourValidator(k: AbstractControl<string | null>): ValidationErrors | null {
  const v = k.value?.trim() ?? '';
  return v === '' || parseHour(v) !== 'gecersiz' ? null : { saatGecersiz: true };
}

const jsonEqual = (a: unknown, b: unknown) => JSON.stringify(a) === JSON.stringify(b);

/**
 * Müsait araç arama (`/app/musaitlik`) — Blazor `MusaitlikArama.razor` paritesi (FAZ-48/73): pencere
 * (gün sayısı bitişin yerine geçer, alış/dönüş saati İSTANBUL saatidir), grup/şube/kaynak/döviz/plaka
 * süzgeçleri, 25 sütun, broker çiti notu ve "Kirala" → kira formu (`?varac&vfrom&vto&vgrup`). Pencere
 * kuralı, fiyat, broker ve döviz süzgeci SUNUCUDA; ekran yalnız gösterir. Yanıttaki `pencereBas/Bit`
 * gerçek UTC anıdır ve İstanbul saatiyle yazılır.
 */
@Component({
  selector: 'rc-musaitlik-sayfasi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    EmptyState,
    PlateChipComponent,
    PageBand,
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    Icon,
    TextInput,
    NumberInput,
    Selection,
    Table,
    TableCell,
    DatePicker,
  ],
  providers: [FetchPolicy, AvailabilityStore],
  templateUrl: './availability-page.html',
  styleUrl: './availability-page.scss',
})
export class AvailabilityPage {
  protected readonly store = inject(AvailabilityStore);
  private readonly t = translationFunction();
  protected readonly liste = listQueryUrlSync(MUSAITLIK);
  protected readonly columns = availabilityColumns(this.t);
  protected readonly identity = (r: AvailabilityRow) => r.id;

  protected readonly form = new FormGroup({
    basGun: new FormControl<string | null>(null, Validators.required),
    bitGun: new FormControl<string | null>(null),
    gun: new FormControl<number | null>(null, [Validators.min(1), Validators.max(365)]),
    basSaat: new FormControl<string | null>(null, hourValidator),
    bitSaat: new FormControl<string | null>(null, hourValidator),
    grup: new FormControl<string | null>(null),
    sube: new FormControl<string | null>(null),
    rezKaynak: new FormControl<string | null>(null),
    doviz: new FormControl<string | null>(null),
    plaka: new FormControl<string | null>(null),
  });

  private readonly arama = computed(() => searchParams(this.liste.apiParametreleri()), {
    equal: jsonEqual,
  });
  protected readonly hasSearch = computed(() => this.arama() !== null);

  /** Tablo kaynağı: yanıtın araç listesi (dört durum korunur; hata ASLA boş liste değildir). */
  protected readonly tableSource = computed<StoreState<readonly AvailabilityRow[]>>(() => {
    const d = this.store.sonuc.durum();
    switch (d.tur) {
      case 'hazir':
        return { tur: 'hazir', veri: d.veri.araclar };
      case 'yukleniyor':
        return { tur: 'yukleniyor', onceki: d.onceki?.araclar };
      default:
        return d;
    }
  });

  protected readonly ozet = computed(() => {
    const v = this.store.sonuc.veri();
    if (!v) return null;
    return this.t('musaitlikSayfasi.ozet', {
      bas: formatDateTime(v.pencereBas),
      bit: formatDateTime(v.pencereBit),
      sayi: v.araclar.length,
    });
  });
  protected readonly brokerNote = computed(() => {
    const v = this.store.sonuc.veri();
    const excluded = Number(v?.brokerElenen ?? 0);
    if (!v || excluded <= 0) return null;
    return this.t('musaitlikSayfasi.brokerNotu', {
      sayi: excluded,
      kaynak: this.liste.sorgu().filtreler.rezKaynak ?? '',
      gerekce: v.brokerGerekce.join(', '),
    });
  });

  private readonly options = computed(() => this.store.options.veri());
  protected readonly groupSuggestions = computed(() => this.options()?.gruplar ?? []);
  protected readonly branchSuggestions = computed(() => this.options()?.subeler ?? []);
  protected readonly sourceOptions = computed(() =>
    this.textOptions(this.options()?.kaynaklar, this.liste.sorgu().filtreler.rezKaynak),
  );
  protected readonly currencyOptions = computed(() =>
    this.textOptions(this.options()?.dovizler, this.liste.sorgu().filtreler.doviz),
  );

  constructor() {
    const policy = inject(FetchPolicy);
    policy.connect({
      parametre: this.arama,
      yukle: (p) => (p === null ? this.store.sonuc.reset() : this.store.sonuc.yukle(p)),
      sifirla: () => this.store.sonuc.reset(),
      esit: jsonEqual,
    });
    policy.connect({
      parametre: signal(0).asReadonly(),
      yukle: () => this.store.options.yukle(),
      sifirla: () => this.store.options.reset(),
    });
    // URL → form (geri/ileri, paylaşılan bağlantı).
    effect(() => {
      const f = this.liste.sorgu().filtreler;
      untracked(() =>
        this.form.reset({
          basGun: f.basGun ?? null,
          bitGun: f.bitGun ?? null,
          gun: f.gun ?? null,
          basSaat: f.basSaat ?? null,
          bitSaat: f.bitSaat ?? null,
          grup: f.grup ?? null,
          sube: f.sube ?? null,
          rezKaynak: f.rezKaynak ?? null,
          doviz: f.doviz ?? null,
          plaka: f.plaka ?? null,
        }),
      );
    });
  }

  protected searchAction(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const v = this.form.getRawValue();
    const hour = (s: string | null) => {
      const c = parseHour(s ?? '');
      return c === null || c === 'gecersiz' ? undefined : c;
    };
    const text = (s: string | null) => (s === null || s.trim() === '' ? undefined : s.trim());
    void this.liste.degistir({
      filtreler: {
        basGun: v.basGun ?? undefined,
        bitGun: v.bitGun ?? undefined,
        gun: v.gun ?? undefined,
        basSaat: hour(v.basSaat),
        bitSaat: hour(v.bitSaat),
        grup: text(v.grup),
        sube: text(v.sube),
        rezKaynak: v.rezKaynak ?? undefined,
        doviz: v.doviz ?? undefined,
        plaka: text(v.plaka),
      },
    });
  }

  protected rentQuery(row: AvailabilityRow): Record<string, string> | null {
    const v = this.store.sonuc.veri();
    return v ? rentParameters(row.id, v.kiralaSorgusu) : null;
  }

  private textOptions(
    list: readonly string[] | undefined,
    selected: string | undefined,
  ): readonly SecenekOgesi<string>[] {
    const values = [...(list ?? [])];
    if (selected !== undefined && !values.includes(selected)) values.unshift(selected);
    return values.map((d) => ({ deger: d, etiket: d }));
  }
}
