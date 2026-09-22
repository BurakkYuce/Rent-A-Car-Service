import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl } from '@angular/forms';
import { paraBicimle } from '@core/bicim/bicim';
import { SUNUCU_HATASI } from '@core/form/sunucu-hatalari';
import { KiraFormuDurumu, katalogKesildi } from '../kira-formu-durumu';
import { sayiya } from '../kira-formu-modeli';
import type { EkHizmetKatalogOgesi, SunucuSayisi } from '../kira-tipleri';
import type { SecimSecenegi } from '@shared/form/arama-secim/secim-kaynagi';
import { KF_ORTAK } from './ortak';

/**
 * EK HİZMETLER. Yeni kira: Blazor matrisi (F4.3b) — tüm aktif tanımlar (katalog ucu; SYS-* hariç) onay kutusu +
 * miktar + birim net + KDV; işaretlenenler kira KAYDIYLA eklenir (tanım fiyat anlık görüntüsü — serbest fiyat
 * yok). Satır toplamı canlı hesaptan (`hesapla`), UI formül taşımaz. Kayıtlı kira: kalemler + ekle/sil (F4.1).
 * Ekleme ANAHTARSIZ: çift gönderim iki kalem yazar → düğme istek boyunca kilitli.
 */
@Component({
  selector: 'rc-kf-ek-hizmet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...KF_ORTAK],
  template: `
    @if (d.yeni) {
      <section class="kf-kart" [formGroup]="d.form">
        <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.ekHizmetSecimi' | transloco }}</h3>
        @if (sunucuHatalari().length > 0) {
          <div class="rc-form-hatalari" role="alert" aria-invalid="true" tabindex="-1">
            @for (m of sunucuHatalari(); track $index) {
              <p>{{ m }}</p>
            }
          </div>
        }
        @if (d.ekHizmetKatalogu.veri(); as kat) {
          @if (kat.ogeler.length === 0) {
            <p class="kf-not">{{ 'kiraFormuParite.ekHizmet.tanimYok' | transloco }}</p>
          } @else {
            <div
              class="kf-tablo-kutusu"
              formArrayName="ekHizmetler"
              role="region"
              tabindex="0"
              [attr.aria-label]="'kiraFormu.bolum.ekHizmetSecimi' | transloco"
            >
              <table
                class="kf-tablo"
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
                    <th scope="col" class="num">
                      {{ 'kiraFormuParite.ekHizmet.birimNet' | transloco }}
                    </th>
                    <th scope="col" class="num">
                      {{ 'kiraFormuParite.ekHizmet.kdv' | transloco }}
                    </th>
                    <th scope="col" class="num">
                      {{ 'kiraFormuParite.ekHizmet.satirToplami' | transloco }}
                    </th>
                  </tr>
                </thead>
                <tbody>
                  @for (oge of kat.ogeler; track oge.id) {
                    @let sira = secililer().get(oge.id) ?? -1;
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
                          (change)="secimDegisti(oge, $event)"
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
                      <td class="num">{{ para(oge.birimUcret) }}</td>
                      <td class="num">{{ sayi(oge.kdvOrani) | percent: '1.0-2' }}</td>
                      <td class="num">
                        {{ sira >= 0 ? para(hesapKalemi(oge.id)?.toplam) : '—' }}
                      </td>
                    </tr>
                  }
                  <!-- Katalog dışı seçimler (katalog kesildiğinde sunucu aramasıyla eklenenler — #262 L2). -->
                  @for (r of katalogDisi(); track r.id) {
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
                          (change)="d.ekHizmetSatiriSil(r.sira)"
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
                      <td class="num">—</td>
                      <td class="num">—</td>
                      <td class="num">{{ para(hesapKalemi(r.id)?.toplam) }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            @if (kesildi()) {
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
                  <rc-arama-secim [formControl]="digerSecici" [kaynak]="d.ekHizmetKaynagi" />
                </rc-alan>
              </div>
            }
          }
        } @else if (d.ekHizmetKatalogu.tur() === 'hata') {
          <p class="kf-not kf-not--uyari" role="status">
            {{ 'kiraFormuParite.ekHizmet.katalogAlinamadi' | transloco }}
          </p>
          <button
            type="button"
            class="rc-dugme rc-dugme--kucuk"
            (click)="d.ekHizmetKatalogu.yenile()"
          >
            {{ 'kiraFormuParite.ekHizmet.yenidenDene' | transloco }}
          </button>
        }
        <p class="kf-not">
          {{ 'kiraFormu.ekHizmet.toplamEtiket' | transloco }}:
          <strong>{{ para(d.hesap.veri()?.ekHizmetToplam) }}</strong> —
          {{ 'kiraFormu.not.ekHizmetYeni' | transloco }}
        </p>
      </section>
    } @else {
      <section class="kf-kart">
        <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.ekHizmetler' | transloco }}</h3>
        <div
          class="kf-tablo-kutusu"
          role="region"
          tabindex="0"
          [attr.aria-label]="'kiraFormu.bolum.ekHizmetler' | transloco"
        >
          <table class="kf-tablo" [attr.aria-label]="'kiraFormu.bolum.ekHizmetler' | transloco">
            <thead>
              <tr>
                <th scope="col">{{ 'kiraFormu.ekHizmet.hizmet' | transloco }}</th>
                <th scope="col" class="num">{{ 'kiraFormu.ekHizmet.miktar' | transloco }}</th>
                <th scope="col" class="num">{{ 'kiraFormu.ekHizmet.birimNet' | transloco }}</th>
                <th scope="col" class="num">{{ 'kiraFormu.ekHizmet.kdvOrani' | transloco }}</th>
                <th scope="col" class="num">{{ 'kiraFormu.ekHizmet.net' | transloco }}</th>
                <th scope="col" class="num">{{ 'kiraFormu.ekHizmet.kdv' | transloco }}</th>
                <th scope="col" class="num">{{ 'kiraFormu.ekHizmet.toplam' | transloco }}</th>
                <th scope="col">
                  <span class="rc-gorunmez">{{ 'kiraFormu.eylem.sil' | transloco }}</span>
                </th>
              </tr>
            </thead>
            <tbody>
              @for (k of d.detay.veri()?.ekHizmetler ?? []; track k.id) {
                <tr>
                  <td>{{ k.ad }}</td>
                  <td class="num">{{ sayi(k.miktar) | sayi }}</td>
                  <td class="num">{{ para(k.birimNetFiyat) }}</td>
                  <td class="num">{{ sayi(k.kdvOrani) | percent: '1.0-2' }}</td>
                  <td class="num">{{ para(k.netTutar) }}</td>
                  <td class="num">{{ para(k.kdvTutar) }}</td>
                  <td class="num">{{ para(k.toplam) }}</td>
                  <td>
                    @if (!d.iptal()) {
                      <button
                        type="button"
                        class="rc-dugme rc-dugme--kucuk rc-dugme--hayalet"
                        [disabled]="!d.operasyon() || d.ekHizmetSilKilidi.gonderiliyor()"
                        [attr.aria-label]="('kiraFormu.eylem.sil' | transloco) + ' ' + k.ad"
                        (click)="d.ekHizmetSil(k)"
                      >
                        {{ 'kiraFormu.eylem.sil' | transloco }}
                      </button>
                    }
                  </td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="8" class="kf-bos">{{ 'kiraFormu.ekHizmet.yok' | transloco }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        @if (!d.iptal()) {
          <div
            class="rc-form-izgara"
            [formGroup]="d.ekHizmetEkleFormu"
            data-testid="ek-hizmet-ekle"
          >
            <rc-alan [etiket]="'kiraFormu.ekHizmet.hizmet' | transloco">
              <rc-arama-secim formControlName="tanim" [kaynak]="d.ekHizmetKaynagi" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.ekHizmet.miktar' | transloco">
              <rc-sayi-girdisi formControlName="miktar" [kesir]="2" />
            </rc-alan>
            <div class="kf-eylemler">
              <button
                type="button"
                class="rc-dugme rc-dugme--birincil"
                [disabled]="!d.operasyon() || d.ekHizmetEkleGonderimi.gonderiliyor()"
                (click)="d.ekHizmetEkle()"
              >
                {{
                  (d.ekHizmetEkleGonderimi.gonderiliyor()
                    ? 'form.gonderiliyor'
                    : 'kiraFormu.ekHizmet.ekleDugme'
                  ) | transloco
                }}
              </button>
            </div>
          </div>
          <rc-form-hatalari [hatalar]="d.ekHizmetEkleGonderimi.genelHatalar()" />
          <p class="kf-not">{{ 'kiraFormu.not.ekHizmetKayitli' | transloco }}</p>
        }
      </section>
    }
  `,
})
export class EkHizmet {
  protected readonly d = inject(KiraFormuDurumu);
  protected readonly sayi = sayiya;

  /** Katalog kesikken "listede olmayan" tanım araması (sunucu `q`); seçilen satır olur, kutu boşalır. */
  protected readonly digerSecici = new FormControl<SecimSecenegi | null>(null);

  protected readonly kesildi = computed(() => {
    const k = this.d.ekHizmetKatalogu.veri();
    return k !== undefined && katalogKesildi(k);
  });

  /** Seçili ama katalog satırlarında OLMAYAN tanımlar (form sırasıyla) — matriste ayrıca çizilir. */
  protected readonly katalogDisi = computed(() => {
    this.d.ekSatirSurumu();
    const katalogda = new Set((this.d.ekHizmetKatalogu.veri()?.ogeler ?? []).map((x) => x.id));
    return this.d.form.controls.ekHizmetler.controls
      .map((s, sira) => ({ tanim: s.controls.tanim.value, sira }))
      .filter((x): x is { tanim: SecimSecenegi; sira: number } => x.tanim !== null)
      .filter((x) => !katalogda.has(x.tanim.id))
      .map((x) => ({ id: x.tanim.id, etiket: x.tanim.etiket, sira: x.sira }));
  });

  constructor() {
    this.digerSecici.valueChanges.pipe(takeUntilDestroyed()).subscribe((tanim) => {
      if (tanim) {
        queueMicrotask(() => {
          this.d.ekHizmetSatiriEkle(tanim);
          this.digerSecici.setValue(null);
        });
      }
    });
  }

  /** Seçili tanım → FormArray sırası (matris satırı ile form satırını eşler; satır ekle/çıkar'da yenilenir). */
  protected readonly secililer = computed(() => {
    this.d.ekSatirSurumu();
    return new Map(
      this.d.form.controls.ekHizmetler.controls.map((s, i) => [s.controls.tanim.value?.id, i]),
    );
  });

  protected secimDegisti(oge: EkHizmetKatalogOgesi, olay: Event): void {
    const kutu = olay.target;
    if (kutu instanceof HTMLInputElement) this.d.ekHizmetSecimi(oge, kutu.checked);
  }

  /** Dizi düzeyindeki sunucu hatası (`ekHizmetler` alanı) — tabloya bağlı görünür. */
  protected readonly sunucuHatalari = computed((): readonly string[] => {
    this.d.ekSatirSurumu();
    this.d.kayit.gonderiliyor();
    const hata: unknown = this.d.form.controls.ekHizmetler.errors?.[SUNUCU_HATASI];
    return Array.isArray(hata) ? (hata as string[]) : [];
  });

  protected hesapKalemi(tanimId: string | undefined) {
    return this.d.hesap.veri()?.ekKalemler.find((k) => k.tanimId === tanimId);
  }

  protected para(v: SunucuSayisi): string {
    return paraBicimle(sayiya(v), this.d.kiraDovizi()) || '—';
  }
}
