import { ChangeDetectionStrategy, Component, booleanAttribute, inject, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { BICIM_PIPELARI } from '@shared/bicim/bicim-pipe';
import { PlateChipComponent } from '@shared/plaka/plaka';
import { KiraFormuDurumu } from '../kira-formu-durumu';
import { sayiya } from '../kira-formu-modeli';

/**
 * Seçili aracın bilgi kartı (gri, salt okunur). Müsait liste / kayıtlı sözleşmeden gelen tam kart;
 * F1.6 araç aramasından seçildiyse yalnız plaka + grup + durum (kart alanları "—").
 */
@Component({
  selector: 'rc-kf-arac-karti',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, ...BICIM_PIPELARI, PlateChipComponent],
  template: `
    @let a = d.secilenArac();
    @if (a?.plaka; as plaka) {
      <rc-plaka [plaka]="plaka" />
    }
    <dl class="kf-bilgiler" [attr.aria-label]="'kiraFormu.bolum.aracBilgisi' | transloco">
      <div>
        <dt>{{ 'kiraFormu.arac.marka' | transloco }}</dt>
        <dd>{{ a?.marka || '—' }}</dd>
      </div>
      <div>
        <dt>{{ 'kiraFormu.arac.tip' | transloco }}</dt>
        <dd>{{ a?.tip || '—' }}</dd>
      </div>
      @if (!kisa()) {
        <div>
          <dt>{{ 'kiraFormu.arac.yil' | transloco }}</dt>
          <dd>{{ a?.modelYili ?? '—' }}</dd>
        </div>
      }
      <div>
        <dt>{{ 'kiraFormu.arac.vites' | transloco }}</dt>
        <dd>{{ a?.vites || '—' }}</dd>
      </div>
      <div>
        <dt>{{ 'kiraFormu.arac.yakit' | transloco }}</dt>
        <dd>{{ a?.yakit || '—' }}</dd>
      </div>
      <div>
        <dt>{{ 'kiraFormu.arac.grup' | transloco }}</dt>
        <dd>{{ a?.grup || '—' }}</dd>
      </div>
      @if (!kisa()) {
        <div>
          <dt>{{ 'kiraFormu.arac.segment' | transloco }}</dt>
          <dd>{{ a?.segment || '—' }}</dd>
        </div>
      }
      <div>
        <dt>{{ 'kiraFormu.arac.km' | transloco }}</dt>
        <dd>{{ (km(a?.km) | sayi) || '—' }}</dd>
      </div>
      @if (!kisa()) {
        <div>
          <dt>{{ 'kiraFormu.arac.sube' | transloco }}</dt>
          <dd>{{ a?.sube || '—' }}</dd>
        </div>
        <div>
          <dt>{{ 'kiraFormu.arac.konum' | transloco }}</dt>
          <dd>{{ a?.konum || '—' }}</dd>
        </div>
        @if (d.finans() && !d.yeni) {
          <div>
            <dt>{{ 'kiraFormu.arac.doluluk' | transloco }}</dt>
            <dd>
              @let dol = d.karne.veri()?.dolulukYuzde;
              {{ dol === null || dol === undefined ? '—' : '%' + (km(dol) | sayi) }}
            </dd>
          </div>
        }
      }
    </dl>
  `,
})
export class AracKarti {
  protected readonly d = inject(KiraFormuDurumu);
  readonly kisa = input(false, { transform: booleanAttribute });
  protected readonly km = sayiya;
}
