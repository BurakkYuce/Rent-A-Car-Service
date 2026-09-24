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
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { sekmeBaglami } from '@core/sekme/sekme-durumu';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { toNumber } from '@features/vehicles/vehicle-model';
import { ParaPipe, TarihPipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { SekmeliForm, SekmePaneli, type SekmeTanimi } from '@shared/form/sekmeli-form/sekmeli-form';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';
import { Ikon } from '@shared/ikon/ikon';

import { balanceSide, type CustomerProfile } from '../customer-model';
import { CustomerDetailStore } from '../customer.store';

/** Kayıttaki eski döviz adları (`TL`, `EURO`) → ISO kodu; tanınmayan TRY gösterilir. */
export function currencyCode(value: string | null | undefined): string {
  const v = (value ?? '').trim();
  if (v === '' || v === 'TL') return 'TRY';
  if (v === 'EURO') return 'EUR';
  return /^[A-Z]{3}$/.test(v) ? v : 'TRY';
}

/**
 * Cari 360° (`/app/cariler/:id/detay`) — Blazor `CustomerDetail` paritesi + F8.1a cari ekstre sekmesi. Müşteri özeti
 * SUNUCUNUN KVKK görünümüyle (TC yok; ehliyet/pasaport maskeli; anonim ad → etiket). Kiralar şube kapsamına
 * süzülmüş gelir. Bakiye ve son hareketler yalnız FinanceWrite ∨ ViewReports ile dolu (aksi `null` → not gösterilir).
 * Ekstre (devir, yürüyen bakiye) sunucuda hesaplanır; SPA toplamaz.
 */
@Component({
  selector: 'rc-customer-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    Ikon,
    ParaPipe,
    SekmeliForm,
    SekmePaneli,
    TarihPipe,
    TarihSecici,
  ],
  providers: [FetchPolicy, CustomerDetailStore],
  templateUrl: './customer-detail.html',
  styleUrl: '../customers.scss',
})
export class CustomerDetail {
  protected readonly store = inject(CustomerDetailStore);
  private readonly session = inject(OturumServisi);
  private readonly tab = sekmeBaglami();
  private readonly t = ceviriFonksiyonu();

  protected readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';
  protected readonly num = toNumber;
  protected readonly currency = currencyCode;
  protected readonly canCreateRental = computed(() => this.session.izinVar('OperationsWrite'));

  protected readonly tabs: readonly SekmeTanimi[] = [
    { kimlik: 'ozet', etiket: this.t('cari.detayBolum.ozet') },
    { kimlik: 'kiralar', etiket: this.t('cari.detayBolum.kiralar') },
    { kimlik: 'ekstre', etiket: this.t('cari.detayBolum.ekstre') },
  ];

  protected readonly name = computed(() => {
    const d = this.store.detail.veri();
    return d ? (d.musteri.ad ?? this.t('cari.anonim')) : '';
  });
  protected readonly anonymous = computed(() => {
    const d = this.store.detail.veri();
    return d !== undefined && d.musteri.ad === null;
  });
  protected readonly balance = computed(() => toNumber(this.store.detail.veri()?.bakiye ?? null));
  protected readonly side = computed(() => balanceSide(this.balance()));

  protected readonly statementForm = new FormGroup({
    bas: new FormControl<string | null>(null),
    bit: new FormControl<string | null>(null),
  });
  private readonly statementRange = signal<{ bas: string | null; bit: string | null }>({
    bas: null,
    bit: null,
  });

  constructor() {
    const policy = inject(FetchPolicy);
    policy.baglan({
      parametre: signal(this.id).asReadonly(),
      yukle: (x) => {
        this.store.detail.yukle(x);
        this.store.statement.yukle({ id: x, ...this.statementRange() });
      },
      sifirla: () => {
        this.store.detail.sifirla();
        this.store.statement.sifirla();
      },
      sekmeyeDonunce: 'yenile',
    });
    effect(() => {
      const n = this.name();
      if (n !== '') untracked(() => this.tab.etiketAyarla(n));
    });
  }

  protected address(m: CustomerProfile): string {
    const parts = [m.adres, m.ilce, m.il].filter((x): x is string => !!x && x.trim() !== '');
    return parts.length === 0 ? '—' : parts.join(', ');
  }

  protected showStatement(): void {
    const v = this.statementForm.getRawValue();
    this.statementRange.set({ bas: v.bas, bit: v.bit });
    this.store.statement.yukle({ id: this.id, bas: v.bas, bit: v.bit });
  }
}
