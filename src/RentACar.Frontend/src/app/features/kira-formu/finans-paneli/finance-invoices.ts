import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { toNumber } from '../kira-formu-modeli';
import { KF_SHARED } from '../sekmeler/ortak';
import { displayMoney } from './finans-modeli';
import { RentalFinanceState } from './rental-finance-state';

/**
 * Kiranın faturaları (kira / fark / iade) + kiradan fatura kes (`POST finans/fatura`, yapısal: kira başına
 * fatura + fark sırası; ikinci çağrı sunucudan 400 → form üstü hata). Vergi alanları bilgi amaçlı.
 */
@Component({
  selector: 'rc-kf-finans-faturalar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...KF_SHARED, RouterLink],
  template: `
    @let s = f.faturalar;
    <div
      class="rc-tablo-kap"
      role="region"
      tabindex="0"
      [attr.aria-label]="'kiraFinans.fatura.liste' | transloco"
      [attr.aria-busy]="s.isLoading()"
    >
      <table class="rc-duz-tablo" [attr.aria-label]="'kiraFinans.fatura.liste' | transloco">
        <thead>
          <tr>
            <th scope="col">{{ 'kiraFinans.fatura.no' | transloco }}</th>
            <th scope="col">{{ 'kiraFinans.alan.tarih' | transloco }}</th>
            <th scope="col" class="rc-num">{{ 'kiraFinans.fatura.genelToplam' | transloco }}</th>
            <th scope="col">{{ 'kiraFinans.alan.tur' | transloco }}</th>
            <th scope="col">{{ 'kiraFinans.alan.durum' | transloco }}</th>
          </tr>
        </thead>
        <tbody>
          @if (s.tur() === 'hata') {
            <tr>
              <td colspan="5" class="rc-bos">{{ s.hata()?.detay }}</td>
            </tr>
          }
          @for (x of s.veri() ?? []; track x.id) {
            <tr>
              <td>
                <a routerLink="/faturalar">{{ x.no }}</a>
              </td>
              <td>{{ x.tarih | tarih }}</td>
              <td class="rc-num">{{ money(x.genelToplam, x.currency) }}</td>
              <td>{{ x.tur }}</td>
              <td>{{ x.durum }}</td>
            </tr>
          } @empty {
            @if (s.tur() === 'hazir') {
              <tr>
                <td colspan="5" class="rc-bos">{{ 'kiraFinans.fatura.yok' | transloco }}</td>
              </tr>
            }
          }
        </tbody>
      </table>
    </div>

    @if (canIssue()) {
      @if (!f.finans()) {
        <p class="kf-not">{{ 'kiraFinans.yetkiYok' | transloco }}</p>
      } @else {
        <section
          class="kf-finans__islem"
          aria-labelledby="kf-finans-fatura"
          [formGroup]="f.invoiceForm"
        >
          <h3 class="kf-finans__baslik" id="kf-finans-fatura">
            {{ 'kiraFinans.fatura.kes' | transloco }}
          </h3>
          <details class="kf-acilir" [open]="isTaxOpen()" (toggle)="opened($event)">
            <summary>{{ 'kiraFinans.fatura.vergiAlanlari' | transloco }}</summary>
            <div class="rc-form-izgara">
              <rc-alan [etiket]="'kiraFinans.fatura.otv' | transloco">
                <rc-para-girdisi formControlName="otv" />
              </rc-alan>
              <rc-alan [etiket]="'kiraFinans.fatura.tevkifatOran' | transloco">
                <rc-sayi-girdisi formControlName="tevkifatOran" [kesir]="2" />
              </rc-alan>
              <rc-alan [etiket]="'kiraFinans.fatura.tevkifatTutar' | transloco">
                <rc-para-girdisi formControlName="tevkifatTutar" />
              </rc-alan>
              <rc-alan
                [etiket]="'kiraFinans.fatura.damga' | transloco"
                [ipucu]="'kiraFinans.fatura.damgaIpucu' | transloco"
              >
                <rc-para-girdisi formControlName="damgaVergisi" />
              </rc-alan>
              <rc-alan [etiket]="'kiraFinans.fatura.iade' | transloco" etiketGizli>
                <rc-onay-kutusu formControlName="iadeMi">{{
                  'kiraFinans.fatura.iade' | transloco
                }}</rc-onay-kutusu>
              </rc-alan>
              <rc-alan [etiket]="'kiraFinans.fatura.manuel' | transloco" etiketGizli>
                <rc-onay-kutusu formControlName="manuelMi">{{
                  'kiraFinans.fatura.manuel' | transloco
                }}</rc-onay-kutusu>
              </rc-alan>
            </div>
          </details>
          <rc-form-hatalari [hatalar]="f.invoiceSubmission.genelHatalar()" />
          <div class="kf-eylemler">
            <button
              type="button"
              class="rc-dugme rc-dugme--birincil"
              data-testid="fatura-kes"
              [disabled]="f.invoiceSubmission.gonderiliyor()"
              (click)="issue()"
            >
              {{
                (f.invoiceSubmission.gonderiliyor()
                  ? 'form.gonderiliyor'
                  : 'kiraFinans.fatura.kesDugme'
                ) | transloco
              }}
            </button>
          </div>
          <p class="kf-not">{{ 'kiraFinans.fatura.not' | transloco }}</p>
        </section>
      }
    }
  `,
})
export class FinanceInvoices {
  protected readonly f = inject(RentalFinanceState);
  protected readonly money = displayMoney;
  private readonly root = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  /** Vergi alanları kutusu açık mı (geçersiz alan varsa gönderimde açılır — adversarial L4). */
  protected readonly isTaxOpen = signal(false);

  protected issue(): void {
    this.f.issueInvoice(() => {
      this.isTaxOpen.set(true);
      afterNextRender(
        () => this.root.nativeElement.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus(),
        { injector: this.injector },
      );
    });
  }

  protected opened(evt: Event): void {
    const d = evt.target;
    if (d instanceof HTMLDetailsElement) this.isTaxOpen.set(d.open);
  }

  /** Blazor koşulu: genel toplam > 0 ve iptal değil. */
  protected readonly canIssue = computed(() => {
    const k = this.f.kira();
    return k !== null && (toNumber(k.genelToplam) ?? 0) > 0 && !this.f.iptal();
  });
}
