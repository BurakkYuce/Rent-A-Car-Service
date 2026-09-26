import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { KF_SHARED } from '../sekmeler/ortak';
import { displayMoney } from './finans-modeli';
import type { AccountType, RentalPeriod } from './finans-tipleri';
import { RentalFinanceState } from './rental-finance-state';

/**
 * Dönem faturaları (uzun/aylık kira planı) + satır başına kes (+ isteğe bağlı tahsilat) —
 * `POST finans/donem-fatura`. Tekrar SESSİZ 200 (aynı fatura); `tahsilatYazildi=false` iken sunucunun
 * `bilgi`'si panelde ve bildirimde gösterilir (gizlenmez).
 */
@Component({
  selector: 'rc-kf-finans-donem',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...KF_SHARED],
  template: `
    @let s = f.periods;
    @let k = f.kira();
    @if (s.tur() === 'hata') {
      <p class="kf-not">{{ 'kiraFinans.donem.okunamadi' | transloco }} {{ s.hata()?.detay }}</p>
    } @else if (s.tur() === 'hazir' && (s.veri() ?? []).length === 0) {
      <p class="kf-not">{{ 'kiraFinans.donem.yok' | transloco }}</p>
    } @else {
      <div
        class="rc-tablo-kap"
        role="region"
        tabindex="0"
        [attr.aria-label]="'kiraFinans.donem.liste' | transloco"
        [attr.aria-busy]="s.isLoading()"
      >
        <table class="rc-duz-tablo" [attr.aria-label]="'kiraFinans.donem.liste' | transloco">
          <thead>
            <tr>
              <th scope="col">#</th>
              <th scope="col">{{ 'kiraFinans.donem.aralik' | transloco }}</th>
              <th scope="col" class="rc-num">{{ 'kiraFinans.donem.tahakkuk' | transloco }}</th>
              <th scope="col">{{ 'kiraFinans.alan.durum' | transloco }}</th>
              <th scope="col" class="rc-num">{{ 'kiraFinans.donem.kesilen' | transloco }}</th>
              <th scope="col">{{ 'kiraFinans.donem.islem' | transloco }}</th>
            </tr>
          </thead>
          <tbody>
            @for (d of s.veri() ?? []; track d.donemSira) {
              <tr>
                <td>{{ d.donemSira }}</td>
                <td>{{ d.donemBas | tarih }} – {{ d.donemBit | tarih }}</td>
                <td class="rc-num">{{ money(d.tahakkuk, k?.doviz) }}</td>
                <td>{{ d.durum }}</td>
                <td class="rc-num">
                  {{ d.kesilenTutar === null ? '—' : money(d.kesilenTutar, k?.doviz) }}
                </td>
                <td>
                  @if (d.durum === 'Planlandi' && f.finans() && !f.iptal()) {
                    @let df = f.periodForm(order(d));
                    <div class="kf-finans__satir" [formGroup]="df">
                      <rc-onay-kutusu
                        formControlName="tahsilat"
                        [ariaEtiketi]="
                          ('kiraFinans.donem.tahsilat' | transloco) + ' ' + d.donemSira
                        "
                        >{{ 'kiraFinans.donem.tahsilat' | transloco }}</rc-onay-kutusu
                      >
                      <rc-secim
                        formControlName="hesap"
                        [secenekler]="accountTypes"
                        [ariaEtiketi]="
                          ('kiraFinans.alan.hesapTuru' | transloco) + ' ' + d.donemSira
                        "
                      />
                      <button
                        type="button"
                        class="rc-dugme rc-dugme--kucuk"
                        [attr.data-testid]="'donem-kes-' + d.donemSira"
                        [disabled]="f.periodLock.gonderiliyor()"
                        (click)="f.issuePeriod(d)"
                      >
                        {{
                          (f.submittedPeriod() === order(d)
                            ? 'form.gonderiliyor'
                            : 'kiraFinans.donem.kes'
                          ) | transloco
                        }}
                      </button>
                    </div>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      @if (f.periodInfo(); as bilgi) {
        <p class="rc-form-mesaji rc-form-mesaji--uyari" role="status" data-testid="donem-bilgi">
          {{ bilgi }}
        </p>
      }
      <p class="kf-not">{{ 'kiraFinans.donem.not' | transloco }}</p>
    }
  `,
})
export class FinancePeriod {
  protected readonly f = inject(RentalFinanceState);
  protected readonly money = displayMoney;
  protected readonly order = (d: RentalPeriod): number => Number(d.donemSira);
  protected readonly accountTypes: readonly SecenekOgesi<AccountType>[] = [
    { deger: 'Kasa', etiket: 'Kasa' },
    { deger: 'Banka', etiket: 'Banka' },
  ];
}
