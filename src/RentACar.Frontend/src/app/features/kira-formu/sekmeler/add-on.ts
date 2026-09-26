import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl } from '@angular/forms';
import { formatMoney } from '@core/bicim/bicim';
import { SERVER_ERROR } from '@core/form/sunucu-hatalari';
import { RentalFormState, catalogTruncated } from '../rental-form-state';
import { toNumber } from '../kira-formu-modeli';
import type { AddOnCatalogItem, ServerNumber } from '../kira-tipleri';
import type { SecimSecenegi } from '@shared/form/arama-secim/selection-source';
import { KF_SHARED } from './ortak';

/**
 * EK HİZMETLER. Yeni kira: Blazor matrisi (F4.3b) — tüm aktif tanımlar (katalog ucu; SYS-* hariç) onay kutusu +
 * miktar + birim net + KDV; işaretlenenler kira KAYDIYLA eklenir (tanım fiyat anlık görüntüsü — serbest fiyat
 * yok). Satır toplamı canlı hesaptan (`hesapla`), UI formül taşımaz. Kayıtlı kira: kalemler + ekle/sil (F4.1).
 * Ekleme ANAHTARSIZ: çift gönderim iki kalem yazar → düğme istek boyunca kilitli.
 */
@Component({
  selector: 'rc-kf-ek-hizmet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...KF_SHARED],
  template: `
    @if (d.yeni) {
      <section class="rc-bolum kf-kart" [formGroup]="d.form">
        <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.ekHizmetSecimi' | transloco }}</h3>
        @if (serverErrors().length > 0) {
          <div class="rc-form-hatalari" role="alert" aria-invalid="true" tabindex="-1">
            @for (m of serverErrors(); track $index) {
              <p>{{ m }}</p>
            }
          </div>
        }
        @if (d.addOnCatalog.veri(); as kat) {
          @if (kat.ogeler.length === 0) {
            <p class="kf-not">{{ 'kiraFormuParite.ekHizmet.tanimYok' | transloco }}</p>
          } @else {
            <div
              class="rc-tablo-kap"
              formArrayName="ekHizmetler"
              role="region"
              tabindex="0"
              [attr.aria-label]="'kiraFormu.bolum.ekHizmetSecimi' | transloco"
            >
              <table
                class="rc-duz-tablo"
                data-testid="ek-hizmet-matrisi"
                [attr.aria-label]="'kiraFormu.bolum.ekHizmetSecimi' | transloco"
              >
                <thead>
                  <tr>
                    <th scope="col">
                      <span class="rc-gorunmez">{{
                        'kiraFormuParite.ekHizmet.sec' | transloco
                      }}</span>
                    </th>
                    <th scope="col">{{ 'kiraFormu.ekHizmet.hizmet' | transloco }}</th>
                    <th scope="col">{{ 'kiraFormu.ekHizmet.miktar' | transloco }}</th>
                    <th scope="col" class="rc-num">
                      {{ 'kiraFormuParite.ekHizmet.birimNet' | transloco }}
                    </th>
                    <th scope="col" class="rc-num">
                      {{ 'kiraFormuParite.ekHizmet.kdv' | transloco }}
                    </th>
                    <th scope="col" class="rc-num">
                      {{ 'kiraFormuParite.ekHizmet.satirToplami' | transloco }}
                    </th>
                  </tr>
                </thead>
                <tbody>
                  @for (oge of kat.ogeler; track oge.id) {
                    @let sira = selectedItems().get(oge.id) ?? -1;
                    <tr>
                      <td>
                        <input
                          type="checkbox"
                          class="kf-matris-secim"
                          [checked]="sira >= 0"
                          [disabled]="!d.operasyon()"
                          [attr.aria-label]="
                            ('kiraFormuParite.ekHizmet.sec' | transloco) + ' ' + oge.ad
                          "
                          (change)="selectionChanged(oge, $event)"
                        />
                      </td>
                      <td [attr.title]="oge.aciklama">
                        {{ oge.ad }} <span class="kf-not">({{ oge.kod }})</span>
                      </td>
                      <td class="kf-miktar">
                        @if (sira >= 0) {
                          <ng-container [formGroupName]="sira">
                            <rc-sayi-girdisi
                              formControlName="miktar"
                              [kesir]="2"
                              [ariaEtiketi]="
                                ('kiraFormu.ekHizmet.miktar' | transloco) + ' ' + oge.ad
                              "
                            />
                          </ng-container>
                        } @else {
                          —
                        }
                        @if (oge.maxGun) {
                          <span class="kf-not">{{
                            'kiraFormuParite.ekHizmet.maksGun' | transloco: { gun: oge.maxGun }
                          }}</span>
                        }
                      </td>
                      <td class="rc-num">{{ money(oge.birimUcret) }}</td>
                      <td class="rc-num">{{ count(oge.kdvOrani) | percent: '1.0-2' }}</td>
                      <td class="rc-num">
                        {{ sira >= 0 ? money(calculationItem(oge.id)?.toplam) : '—' }}
                      </td>
                    </tr>
                  }
                  <!-- Katalog dışı seçimler (katalog kesildiğinde sunucu aramasıyla eklenenler — #262 L2). -->
                  @for (r of outsideCatalog(); track r.id) {
                    <tr>
                      <td>
                        <input
                          type="checkbox"
                          class="kf-matris-secim"
                          checked
                          [disabled]="!d.operasyon()"
                          [attr.aria-label]="
                            ('kiraFormuParite.ekHizmet.sec' | transloco) + ' ' + r.etiket
                          "
                          (change)="d.deleteAddOnRow(r.sira)"
                        />
                      </td>
                      <td>{{ r.etiket }}</td>
                      <td class="kf-miktar">
                        <ng-container [formGroupName]="r.sira">
                          <rc-sayi-girdisi
                            formControlName="miktar"
                            [kesir]="2"
                            [ariaEtiketi]="
                              ('kiraFormu.ekHizmet.miktar' | transloco) + ' ' + r.etiket
                            "
                          />
                        </ng-container>
                      </td>
                      <td class="rc-num">—</td>
                      <td class="rc-num">—</td>
                      <td class="rc-num">{{ money(calculationItem(r.id)?.toplam) }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            @if (issued()) {
              <p class="kf-not">
                {{
                  'kiraFormuParite.ekHizmet.kesildi'
                    | transloco: { sayi: kat.ogeler.length, toplam: kat.toplam }
                }}
              </p>
              <div class="rc-form-izgara">
                <rc-alan
                  [etiket]="'kiraFormuParite.ekHizmet.diger' | transloco"
                  class="rc-form-izgara__genis"
                >
                  <rc-arama-secim [formControl]="otherPicker" [kaynak]="d.addOnSource" />
                </rc-alan>
              </div>
            }
          }
        } @else if (d.addOnCatalog.tur() === 'hata') {
          <p class="kf-not kf-not--uyari" role="status">
            {{ 'kiraFormuParite.ekHizmet.katalogAlinamadi' | transloco }}
          </p>
          <button type="button" class="rc-dugme rc-dugme--kucuk" (click)="d.addOnCatalog.yenile()">
            {{ 'kiraFormuParite.ekHizmet.yenidenDene' | transloco }}
          </button>
        }
        <p class="kf-not">
          {{ 'kiraFormu.ekHizmet.toplamEtiket' | transloco }}:
          <strong>{{ money(d.hesap.veri()?.ekHizmetToplam) }}</strong> —
          {{ 'kiraFormu.not.ekHizmetYeni' | transloco }}
        </p>
      </section>
    } @else {
      <section class="rc-bolum kf-kart">
        <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.ekHizmetler' | transloco }}</h3>
        <div
          class="rc-tablo-kap"
          role="region"
          tabindex="0"
          [attr.aria-label]="'kiraFormu.bolum.ekHizmetler' | transloco"
        >
          <table class="rc-duz-tablo" [attr.aria-label]="'kiraFormu.bolum.ekHizmetler' | transloco">
            <thead>
              <tr>
                <th scope="col">{{ 'kiraFormu.ekHizmet.hizmet' | transloco }}</th>
                <th scope="col" class="rc-num">{{ 'kiraFormu.ekHizmet.miktar' | transloco }}</th>
                <th scope="col" class="rc-num">{{ 'kiraFormu.ekHizmet.birimNet' | transloco }}</th>
                <th scope="col" class="rc-num">{{ 'kiraFormu.ekHizmet.kdvOrani' | transloco }}</th>
                <th scope="col" class="rc-num">{{ 'kiraFormu.ekHizmet.net' | transloco }}</th>
                <th scope="col" class="rc-num">{{ 'kiraFormu.ekHizmet.kdv' | transloco }}</th>
                <th scope="col" class="rc-num">{{ 'kiraFormu.ekHizmet.toplam' | transloco }}</th>
                <th scope="col">
                  <span class="rc-gorunmez">{{ 'kiraFormu.eylem.sil' | transloco }}</span>
                </th>
              </tr>
            </thead>
            <tbody>
              @for (k of d.visibleDetail()?.ekHizmetler ?? []; track k.id) {
                <tr>
                  <td>{{ k.ad }}</td>
                  <td class="rc-num">{{ count(k.miktar) | sayi }}</td>
                  <td class="rc-num">{{ money(k.birimNetFiyat) }}</td>
                  <td class="rc-num">{{ count(k.kdvOrani) | percent: '1.0-2' }}</td>
                  <td class="rc-num">{{ money(k.netTutar) }}</td>
                  <td class="rc-num">{{ money(k.kdvTutar) }}</td>
                  <td class="rc-num">{{ money(k.toplam) }}</td>
                  <td>
                    @if (!d.iptal()) {
                      <button
                        type="button"
                        class="rc-dugme rc-dugme--kucuk rc-dugme--hayalet"
                        [disabled]="!d.operasyon() || d.deleteAddOnLock.gonderiliyor()"
                        [attr.aria-label]="('kiraFormu.eylem.sil' | transloco) + ' ' + k.ad"
                        (click)="d.deleteAddOn(k)"
                      >
                        {{ 'kiraFormu.eylem.sil' | transloco }}
                      </button>
                    }
                  </td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="8" class="rc-bos">{{ 'kiraFormu.ekHizmet.yok' | transloco }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        @if (!d.iptal()) {
          <div class="rc-form-izgara" [formGroup]="d.addAddOnForm" data-testid="ek-hizmet-ekle">
            <rc-alan [etiket]="'kiraFormu.ekHizmet.hizmet' | transloco">
              <rc-arama-secim formControlName="tanim" [kaynak]="d.addOnSource" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.ekHizmet.miktar' | transloco">
              <rc-sayi-girdisi formControlName="miktar" [kesir]="2" />
            </rc-alan>
            <div class="kf-eylemler">
              <button
                type="button"
                class="rc-dugme rc-dugme--birincil"
                [disabled]="!d.operasyon() || d.addAddOnSubmission.gonderiliyor()"
                (click)="d.addAddOn()"
              >
                {{
                  (d.addAddOnSubmission.gonderiliyor()
                    ? 'form.gonderiliyor'
                    : 'kiraFormu.ekHizmet.ekleDugme'
                  ) | transloco
                }}
              </button>
            </div>
          </div>
          <rc-form-hatalari [hatalar]="d.addAddOnSubmission.genelHatalar()" />
          <p class="kf-not">{{ 'kiraFormu.not.ekHizmetKayitli' | transloco }}</p>
        }
      </section>
    }
  `,
})
export class AddOn {
  protected readonly d = inject(RentalFormState);
  protected readonly count = toNumber;

  /** Katalog kesikken "listede olmayan" tanım araması (sunucu `q`); seçilen satır olur, kutu boşalır. */
  protected readonly otherPicker = new FormControl<SecimSecenegi | null>(null);

  protected readonly issued = computed(() => {
    const k = this.d.addOnCatalog.veri();
    return k !== undefined && catalogTruncated(k);
  });

  /** Seçili ama katalog satırlarında OLMAYAN tanımlar (form sırasıyla) — matriste ayrıca çizilir. */
  protected readonly outsideCatalog = computed(() => {
    this.d.extraRowVersion();
    const inCatalog = new Set((this.d.addOnCatalog.veri()?.ogeler ?? []).map((x) => x.id));
    return this.d.form.controls.ekHizmetler.controls
      .map((s, order) => ({ tanim: s.controls.tanim.value, sira: order }))
      .filter((x): x is { tanim: SecimSecenegi; sira: number } => x.tanim !== null)
      .filter((x) => !inCatalog.has(x.tanim.id))
      .map((x) => ({ id: x.tanim.id, etiket: x.tanim.etiket, sira: x.sira }));
  });

  constructor() {
    this.otherPicker.valueChanges.pipe(takeUntilDestroyed()).subscribe((definition) => {
      if (definition) {
        queueMicrotask(() => {
          this.d.addAddOnRow(definition);
          this.otherPicker.setValue(null);
        });
      }
    });
  }

  /** Seçili tanım → FormArray sırası (matris satırı ile form satırını eşler; satır ekle/çıkar'da yenilenir). */
  protected readonly selectedItems = computed(() => {
    this.d.extraRowVersion();
    return new Map(
      this.d.form.controls.ekHizmetler.controls.map((s, i) => [s.controls.tanim.value?.id, i]),
    );
  });

  protected selectionChanged(oge: AddOnCatalogItem, evt: Event): void {
    const box = evt.target;
    if (box instanceof HTMLInputElement) this.d.addOnSelection(oge, box.checked);
  }

  /** Dizi düzeyindeki sunucu hatası (`ekHizmetler` alanı) — tabloya bağlı görünür. */
  protected readonly serverErrors = computed((): readonly string[] => {
    this.d.extraRowVersion();
    this.d.kayit.gonderiliyor();
    const error: unknown = this.d.form.controls.ekHizmetler.errors?.[SERVER_ERROR];
    return Array.isArray(error) ? (error as string[]) : [];
  });

  protected calculationItem(definitionId: string | undefined) {
    return this.d.hesap.veri()?.ekKalemler.find((k) => k.tanimId === definitionId);
  }

  protected money(v: ServerNumber): string {
    return formatMoney(toNumber(v), this.d.rentalCurrency()) || '—';
  }
}
