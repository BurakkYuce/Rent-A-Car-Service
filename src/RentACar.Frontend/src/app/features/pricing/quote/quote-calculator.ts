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
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { SecimOgesi, Sema } from '@core/api/ui-tipleri';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { metinDegeri } from '@features/planlama-ortak/form-yardimcilari';
import { ParaPipe, SayiPipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { SayiGirdisi } from '@shared/form/kontroller/sayi-girdisi';
import { TarihSaatSecici } from '@shared/form/tarih/tarih-saat-secici';

type PriceQuote = Sema<'PriceQuoteDto'>;
type PriceQuoteRequest = Sema<'PriceQuoteRequest'>;

const toNum = (v: number | string | null | undefined): number | null => {
  if (v === null || v === undefined || v === '') return null;
  const n = typeof v === 'number' ? v : Number(v);
  return Number.isFinite(n) ? n : null;
};

/** "SCDW, IMM" → ["SCDW", "IMM"] (boşlar ve tekrarlar düşer; büyük harf ve doğrulama SUNUCUDA). */
export function parseCodes(text: string | null): string[] {
  if (!text) return [];
  return [
    ...new Set(
      text
        .split(',')
        .map((c) => c.trim())
        .filter((c) => c !== ''),
    ),
  ];
}

/**
 * Fiyat motoru — kira teklifi hesapla (`/app/fiyat-hesapla`, Blazor `QuoteCalculator.razor`): tarife matrisi + araç
 * grubu kuralları + kiralama kuralı + sigorta/ek hizmet kataloğundan kalemli teklif. HESAP SUNUCUDA
 * (`POST /fiyat-hesapla` → `RentalQuoteEngine`); UI formül taşımaz, yalnız gösterir. Deftere yazmaz. İzin
 * OperationsWrite ∨ ViewReports; seç-veya-yaz önerileri seçim uçlarından (OperationsWrite) — izin yoksa alan serbest.
 */
@Component({
  selector: 'rc-quote-calculator',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    FormHatalari,
    MetinGirdisi,
    ParaPipe,
    SayiGirdisi,
    SayiPipe,
    TarihSaatSecici,
  ],
  templateUrl: './quote-calculator.html',
  styleUrl: '../pricing.scss',
})
export class QuoteCalculator {
  private readonly api = inject(ApiIstemcisi);
  private readonly session = inject(OturumServisi);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly num = toNum;
  protected readonly result = signal<PriceQuote | null>(null);
  protected readonly groups = signal<readonly SecimOgesi[]>([]);
  protected readonly branches = signal<readonly SecimOgesi[]>([]);
  protected readonly channels = signal<readonly SecimOgesi[]>([]);
  protected readonly products = signal<readonly SecimOgesi[]>([]);
  protected readonly productCodes = computed(() =>
    this.products()
      .map((p) => p.kod)
      .filter((k): k is string => typeof k === 'string' && k !== '')
      .join(', '),
  );

  protected readonly form = new FormGroup({
    aracGrupKod: new FormControl<string | null>(null, [
      Validators.required,
      Validators.maxLength(32),
    ]),
    kanal: new FormControl<string | null>(null, Validators.maxLength(64)),
    sube: new FormControl<string | null>(null, Validators.maxLength(64)),
    basTar: new FormControl<string | null>(null, Validators.required),
    bitTar: new FormControl<string | null>(null, Validators.required),
    surucuYas: new FormControl<number | null>(null),
    tahminiKm: new FormControl<number | null>(null),
    sigortaUrunKodlari: new FormControl<string | null>(null),
    musteriSegment: new FormControl<string | null>(null, Validators.maxLength(64)),
    kampanyaKodu: new FormControl<string | null>(null, Validators.maxLength(64)),
  });
  protected readonly submission = formGonderimi();

  constructor() {
    if (this.session.izinVar('OperationsWrite')) {
      this.load('arac-grubu', this.groups);
      this.load('sube', this.branches);
      this.load('rezervasyon-kaynagi', this.channels);
      this.load('sigorta-urunu', this.products);
    }
  }

  protected calculate(): void {
    const v = this.form.getRawValue();
    const body: PriceQuoteRequest = {
      aracGrupKod: metinDegeri(v.aracGrupKod),
      kanal: metinDegeri(v.kanal),
      sube: metinDegeri(v.sube),
      basTar: v.basTar,
      bitTar: v.bitTar,
      surucuYas: v.surucuYas,
      tahminiKm: v.tahminiKm,
      sigortaUrunKodlari: parseCodes(v.sigortaUrunKodlari),
      musteriSegment: metinDegeri(v.musteriSegment),
      kampanyaKodu: metinDegeri(v.kampanyaKodu),
    };
    this.submission.gonder(
      this.form,
      (key) => this.api.post<PriceQuote>('/api/ui/v1/fiyat-hesapla', body, { islemAnahtari: key }),
      { basarili: (q) => this.result.set(q) },
    );
  }

  private load(endpoint: string, target: { set(v: readonly SecimOgesi[]): void }): void {
    this.api
      .get<readonly SecimOgesi[]>(`/api/ui/v1/secim/${endpoint}`, {
        parametreler: { limit: 20 },
        context: istekBaglami({ sessiz: true }),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (items) => target.set(items), error: () => undefined });
  }
}
