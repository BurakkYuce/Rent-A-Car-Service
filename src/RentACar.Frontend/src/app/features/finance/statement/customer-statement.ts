import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChildren,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { finalize } from 'rxjs';

import { ConfirmGate } from '@core/form/money-submission';
import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi, type SorguParametreleri } from '@core/api/api-istemcisi';
import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { sekmeBaglami } from '@core/sekme/sekme-durumu';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { TemelStore } from '@core/veri/temel-store';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';

import { CashOperationForm } from '../cash/cash-operation-form';
import {
  type CustomerStatement as Statement,
  type CustomerStatementLine,
  RENTAL_STATUSES,
  customerPath,
  financePath,
  queryParams,
} from '../finance-model';
import { AccountList, FIN_COMMON, balanceSide, toAmount } from '../finance-shared';

interface StatementQuery {
  readonly id: string;
  readonly p: SorguParametreleri;
}

/**
 * Cari ekstre (`/app/cariler/:id/ekstre`, Blazor `CustomerStatement.razor`): bakiye (daima FİLTRESİZ, sunucu), devir
 * satırı + yürüyen bakiye (sunucu; döviz/kaynak/kira durumu süzgecinde "Birikim"), özet görünüm (ay × kaynak), tahsilat
 * / ödeme formları (Nakit İşlem ile AYNI bileşen; FinanceWrite) ve Tahsilat/Ödeme satırında ters kayıt (FinanceReverse,
 * onaylı; E08 yapısal — ikinci ters 409). Okuma üç izinden biri. Dışa aktarma sunucu ucuyla, süzgeç aynen taşınır.
 */
@Component({
  selector: 'rc-customer-statement',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...FIN_COMMON, CashOperationForm],
  providers: [FetchPolicy, AccountList],
  templateUrl: './customer-statement.html',
  styleUrl: '../finance.scss',
})
export class CustomerStatementPage implements KaydedilmemisDegisiklikSahibi {
  private readonly api = inject(ApiIstemcisi);
  private readonly session = inject(OturumServisi);
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly tab = sekmeBaglami();
  private readonly t = ceviriFonksiyonu();

  protected readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';
  protected readonly canWrite = computed(() => this.session.izinVar('FinanceWrite'));
  protected readonly canReverse = computed(() => this.session.izinVar('FinanceReverse'));
  protected readonly reversing = signal<string | null>(null);
  private readonly gate = new ConfirmGate();
  private readonly forms = viewChildren(CashOperationForm);

  protected readonly statement = new TemelStore(
    (q: StatementQuery) =>
      this.api.get<Statement>(customerPath(q.id, '/ekstre'), { parametreler: q.p }),
    { oncekiVeriyiKoru: true },
  );
  protected readonly filter = new FormGroup({
    bas: new FormControl<string | null>(null),
    bit: new FormControl<string | null>(null),
    doviz: new FormControl<string | null>(null),
    kaynak: new FormControl<string | null>(null),
    kiraDurum: new FormControl<string | null>(null),
    mod: new FormControl<string | null>(null),
  });
  private readonly params = signal<SorguParametreleri>({});
  protected readonly filtered = computed(() => Object.keys(this.params()).length > 0);
  protected readonly summaryMode = computed(() => this.params()['mod'] === 'ozet');
  protected readonly rentalFilter = computed(() => this.params()['kiraDurum'] !== undefined);
  /** Gösterim yardımcıları (sunucu sayısı; hesap yok). */
  protected readonly isZero = (v: number | string) => (toAmount(v) ?? 0) === 0;
  protected readonly abs = (v: number | string) => Math.abs(toAmount(v) ?? 0);
  protected readonly side = computed(() => balanceSide(this.statement.veri()?.bakiye));
  protected readonly currencyOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    (this.statement.veri()?.dovizler ?? []).map((d) => ({ deger: d, etiket: d })),
  );
  protected readonly sourceOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    (this.statement.veri()?.kaynaklar ?? []).map((k) => ({ deger: k, etiket: k })),
  );
  protected readonly rentalStatusOptions: readonly SecenekOgesi<string>[] = RENTAL_STATUSES.map(
    (s) => ({
      deger: s,
      etiket: this.t(`finans.ekstre.kiraDurumu.${s}`),
    }),
  );
  protected readonly modeOptions: readonly SecenekOgesi<string>[] = [
    { deger: 'ozet', etiket: this.t('finans.ekstre.ozetGorunum') },
  ];

  constructor() {
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
    inject(FetchPolicy).baglan({
      parametre: computed(() => ({ id: this.id, p: this.params() })),
      yukle: (q) => this.statement.yukle(q),
    });
    effect(() => {
      const s = this.statement.veri();
      if (s)
        untracked(() => this.tab.etiketAyarla(this.t('finans.ekstre.sekme', { ad: s.cariAd })));
    });
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.forms().some((f) => f.pending() || f.dirty());
  }

  kaydedilmemisDegisiklikMesaji(): string | null {
    return this.forms().some((f) => f.pending()) ? this.t('finans.islem.terkMesaji') : null;
  }

  protected applyFilter(): void {
    this.params.set(queryParams(this.filter.getRawValue()));
  }

  protected clearFilter(): void {
    this.filter.reset();
    this.params.set({});
  }

  protected reload(): void {
    this.statement.yenile();
  }

  /** Dışa aktarma adresi: ekrandaki süzgeç aynen taşınır (görünen = indirilen; `mod` taşınmaz). */
  protected exportUrl(format: 'excel' | 'csv' | 'pdf'): string {
    const q = new URLSearchParams({ cariId: this.id, format });
    for (const [k, v] of Object.entries(this.params()))
      if (k !== 'mod' && v !== null && v !== undefined) q.set(k, String(v));
    return `/listeler/export/cari-ekstre?${q.toString()}`;
  }

  protected async reverse(line: CustomerStatementLine): Promise<void> {
    const txId = line.kasaIslemId;
    if (!txId || this.reversing()) return;
    const yes = await this.gate.ask(() =>
      this.confirm.sor({
        baslik: this.t('finans.ekstre.tersBaslik'),
        mesaj: this.t('finans.ekstre.tersMesaj'),
        onayEtiketi: this.t('finans.ekstre.ters'),
        tehlikeli: true,
      }),
    );
    if (!yes || this.reversing()) return;
    this.reversing.set(txId);
    this.api
      .post<{ id: string }>(financePath(`/kasa/islemler/${encodeURIComponent(txId)}/ters`), null)
      .pipe(
        finalize(() => this.reversing.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.toast.basari(this.t('finans.ekstre.tersAlindi'));
          this.reload();
        },
        error: (raw: unknown) => {
          const e = apiHatasinaCevir(raw);
          if (!genelGosterilir(e)) this.toast.hata(e.detay);
          this.reload();
        },
      });
  }
}
