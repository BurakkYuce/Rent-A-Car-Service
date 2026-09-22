import { DOCUMENT } from '@angular/common';
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
import { paraBicimle } from '@core/bicim/bicim';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { KiraFormuDurumu } from '../kira-formu-durumu';
import {
  ALT_SEKMELER,
  type AltSekmeKimligi,
  altSekmeAdresi,
  altSekmeMi,
  hashParcala,
  isoParaBirimi,
  sayiya,
} from '../kira-formu-modeli';
import type { SunucuSayisi } from '../kira-tipleri';
import { KF_ORTAK } from './ortak';

/**
 * AYRINTILAR — 8 alt sekme (Blazor ile aynı kimlikler; `#sekme=ayrintilar&alt=aksesuar`). Alt paneller
 * gizlenir ama DOM'da kalır (değerler kaybolmaz). Gönderimde hatalı alan gizli alt paneldeyse
 * `ilkGecersizeGit()` o alt sekmeyi açar (ana sekmeyi `rc-sekmeli-form` açar).
 */
@Component({
  selector: 'rc-kf-ayrintilar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...KF_ORTAK],
  host: { '(window:hashchange)': 'adrestenOku()' },
  template: `
    <div
      class="kf-alt-sekmeler"
      role="tablist"
      [attr.aria-label]="'kiraFormu.alt.bolumler' | transloco"
    >
      @for (s of sekmeler(); track s.kimlik) {
        <button
          type="button"
          role="tab"
          class="kf-alt-sekme"
          [id]="'kf-alt-' + s.kimlik"
          [attr.aria-selected]="aktif() === s.kimlik"
          [attr.aria-controls]="'kf-alt-panel-' + s.kimlik"
          [tabindex]="aktif() === s.kimlik ? 0 : -1"
          (click)="sec(s.kimlik)"
          (keydown)="tus($event)"
        >
          {{ s.etiket }}
        </button>
      }
    </div>

    <div [formGroup]="d.form">
      <div
        role="tabpanel"
        id="kf-alt-panel-aciklama"
        aria-labelledby="kf-alt-aciklama"
        data-kf-alt="aciklama"
        [hidden]="aktif() !== 'aciklama'"
      >
        <div class="rc-form-izgara">
          <rc-alan [etiket]="'kiraFormu.alan.aciklama' | transloco" class="rc-form-izgara__genis">
            <rc-metin-alani formControlName="aciklama" [azamiUzunluk]="1024" />
          </rc-alan>
          <rc-alan
            [etiket]="'kiraFormu.alan.uyariAciklama' | transloco"
            class="rc-form-izgara__genis"
          >
            <rc-metin-alani formControlName="uyariAciklama" [azamiUzunluk]="512" [satir]="2" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.ozelFatura' | transloco" class="rc-form-izgara__genis">
            <rc-metin-alani formControlName="ozelFaturaAciklama" [azamiUzunluk]="512" [satir]="2" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.faturaGizle' | transloco" etiketGizli>
            <rc-onay-kutusu formControlName="faturaListesindeGizle">{{
              'kiraFormu.alan.faturaGizle' | transloco
            }}</rc-onay-kutusu>
          </rc-alan>
        </div>
      </div>

      <div
        role="tabpanel"
        id="kf-alt-panel-finans"
        aria-labelledby="kf-alt-finans"
        data-kf-alt="finans"
        [hidden]="aktif() !== 'finans'"
      >
        <section class="kf-kart">
          <h4 class="kf-kart__baslik">{{ 'kiraFormu.bolum.provizyon' | transloco }}</h4>
          <p class="kf-not">{{ 'kiraFormu.not.pci' | transloco }}</p>
          @if (d.kira(); as k) {
            <p>
              {{ 'kiraFormu.alan.provizyonDurum' | transloco }}:
              <strong data-testid="provizyon-durum">{{
                'kiraFormu.provizyon.' + k.provizyonDurum | transloco
              }}</strong>
              @if (k.provizyonKapamaTarih) {
                — {{ k.provizyonKapamaTarih | tarihSaat }},
                {{ para(k.provizyonKapamaTutar, k.doviz) }}
              }
            </p>
            <div class="kf-eylemler">
              <button
                type="button"
                class="rc-dugme"
                [disabled]="
                  !d.operasyon() || k.provizyonDurum !== 'Yok' || d.provizyonAlKilidi.gonderiliyor()
                "
                (click)="d.provizyonAl()"
              >
                {{ 'kiraFormu.eylem.provizyonAl' | transloco }}
              </button>
            </div>
            @if (k.provizyonDurum === 'Alindi') {
              <div class="rc-form-izgara" [formGroup]="d.provizyonKapatFormu">
                <rc-alan
                  [etiket]="'kiraFormu.alan.kapamaTutar' | transloco"
                  [ipucu]="'kiraFormu.ipucu.kapamaTutar' | transloco"
                >
                  <rc-para-girdisi formControlName="kapamaTutar" [paraBirimi]="d.kiraDovizi()" />
                </rc-alan>
                <rc-alan [etiket]="'kiraFormu.alan.provizyonIade' | transloco" etiketGizli>
                  <rc-onay-kutusu formControlName="iade">{{
                    'kiraFormu.alan.provizyonIade' | transloco
                  }}</rc-onay-kutusu>
                </rc-alan>
                <div class="kf-eylemler">
                  <button
                    type="button"
                    class="rc-dugme"
                    [disabled]="!d.operasyon() || d.provizyonKapatGonderimi.gonderiliyor()"
                    (click)="d.provizyonKapat()"
                  >
                    {{ 'kiraFormu.eylem.provizyonKapat' | transloco }}
                  </button>
                </div>
              </div>
              <rc-form-hatalari [hatalar]="d.provizyonKapatGonderimi.genelHatalar()" />
            }
          } @else {
            <p class="kf-not">{{ 'kiraFormu.not.onceKaydet' | transloco }}</p>
          }
        </section>
        <section class="kf-kart">
          <h4 class="kf-kart__baslik">{{ 'kiraFormu.bolum.referanslar' | transloco }}</h4>
          <div class="rc-form-izgara">
            <rc-alan [etiket]="'kiraFormu.alan.provizyonNo' | transloco">
              <rc-metin-girdisi formControlName="provizyonNo" [azamiUzunluk]="64" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.provizyonTarih' | transloco">
              <rc-tarih-secici formControlName="provizyonTarih" [hazirlar]="false" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.ucusNo' | transloco">
              <rc-metin-girdisi formControlName="ucusNo" [azamiUzunluk]="32" yerTutucu="TK1923" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.onayKodu' | transloco">
              <rc-metin-girdisi formControlName="onayKodu" [azamiUzunluk]="64" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.firmaKodu' | transloco">
              <rc-metin-girdisi formControlName="firmaKodu" [azamiUzunluk]="64" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.projeAdi' | transloco">
              <rc-metin-girdisi formControlName="projeAdi" [azamiUzunluk]="128" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.ozelKod' | transloco">
              <rc-metin-girdisi
                formControlName="ozelKod"
                liste="kf-dl-ozelkod"
                [azamiUzunluk]="64"
              />
            </rc-alan>
          </div>
        </section>
      </div>

      <div
        role="tabpanel"
        id="kf-alt-panel-suruculer"
        aria-labelledby="kf-alt-suruculer"
        data-kf-alt="suruculer"
        [hidden]="aktif() !== 'suruculer'"
      >
        <p class="kf-not">{{ 'kiraFormu.not.birinciSurucu' | transloco }}</p>
        <div class="rc-form-izgara">
          <div [formGroup]="d.form.controls.ayna" class="kf-hucre">
            <rc-alan [etiket]="'kiraFormu.alan.ikinciSurucu' | transloco">
              <rc-arama-secim formControlName="ikinciSurucu" [kaynak]="d.musteriKaynagi" />
            </rc-alan>
          </div>
          <rc-alan [etiket]="'kiraFormu.alan.ozelSofor' | transloco" class="rc-form-izgara__genis">
            <rc-metin-girdisi formControlName="ozelSoforBilgisi" [azamiUzunluk]="512" />
          </rc-alan>
        </div>
      </div>

      <div
        role="tabpanel"
        id="kf-alt-panel-diger"
        aria-labelledby="kf-alt-diger"
        data-kf-alt="diger"
        [hidden]="aktif() !== 'diger'"
      >
        <div class="rc-form-izgara">
          <rc-alan [etiket]="'kiraFormu.alan.assistFirma' | transloco">
            <rc-metin-girdisi formControlName="assistFirma" [azamiUzunluk]="128" />
          </rc-alan>
          <div class="kf-icerik" [formGroup]="d.form.controls.ayna">
            <rc-alan [etiket]="'kiraFormu.alan.talepTuru' | transloco">
              <rc-metin-girdisi formControlName="talepTuru" [azamiUzunluk]="64" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.geldigiBirim' | transloco">
              <rc-metin-girdisi formControlName="geldigiBirim" [azamiUzunluk]="64" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.findeks' | transloco">
              <rc-sayi-girdisi formControlName="manuelFindexPuan" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.kefil' | transloco">
              <rc-metin-girdisi formControlName="kefilBilgisi" [azamiUzunluk]="512" />
            </rc-alan>
          </div>
        </div>
      </div>

      <div
        role="tabpanel"
        id="kf-alt-panel-hizmetalimi"
        aria-labelledby="kf-alt-hizmetalimi"
        data-kf-alt="hizmetalimi"
        [hidden]="aktif() !== 'hizmetalimi'"
      >
        <p class="kf-not">{{ 'kiraFormu.not.hizmetAlimi' | transloco }}</p>
      </div>

      <div
        role="tabpanel"
        id="kf-alt-panel-webapi"
        aria-labelledby="kf-alt-webapi"
        data-kf-alt="webapi"
        [hidden]="aktif() !== 'webapi'"
      >
        @let rez = d.kaynakRezervasyon.veri()?.rezervasyon ?? null;
        @if (rez) {
          <p class="kf-not">
            {{ 'kiraFormu.not.kaynakRez' | transloco: { no: rez.reservationNo } }}
          </p>
        }
        <dl class="kf-bilgiler">
          @for (o of ota(); track o.anahtar) {
            <div>
              <dt>{{ o.anahtar | transloco }}</dt>
              <dd>{{ para(o.deger, d.kira()?.doviz) }}</dd>
            </div>
          }
        </dl>
        <p class="kf-not">{{ 'kiraFormu.not.webApi' | transloco }}</p>
      </div>

      <div
        role="tabpanel"
        id="kf-alt-panel-aksesuar"
        aria-labelledby="kf-alt-aksesuar"
        data-kf-alt="aksesuar"
        [hidden]="aktif() !== 'aksesuar'"
      >
        <div
          class="kf-tablo-kutusu"
          role="region"
          tabindex="0"
          [attr.aria-label]="'kiraFormu.alt.aksesuar' | transloco"
        >
          <table class="kf-tablo" [attr.aria-label]="'kiraFormu.alt.aksesuar' | transloco">
            <thead>
              <tr>
                <th scope="col">{{ 'kiraFormu.aksesuar.ad' | transloco }}</th>
                <th scope="col">{{ 'kiraFormu.aksesuar.cikis' | transloco }}</th>
                <th scope="col">{{ 'kiraFormu.aksesuar.donus' | transloco }}</th>
              </tr>
            </thead>
            <tbody>
              @for (a of aksesuarlar; track a.cikis) {
                <tr>
                  <td>{{ a.etiket | transloco }}</td>
                  <td>
                    <rc-onay-kutusu
                      [formControlName]="a.cikis"
                      [ariaEtiketi]="
                        (a.etiket | transloco) + ' — ' + ('kiraFormu.aksesuar.cikis' | transloco)
                      "
                    />
                  </td>
                  <td>
                    <rc-onay-kutusu
                      [formControlName]="a.donus"
                      [ariaEtiketi]="
                        (a.etiket | transloco) + ' — ' + ('kiraFormu.aksesuar.donus' | transloco)
                      "
                    />
                  </td>
                </tr>
              }
              <tr>
                <td>{{ 'kiraFormu.aksesuar.lastik' | transloco }}</td>
                <td>
                  <rc-metin-girdisi
                    formControlName="aksLastikCikis"
                    [azamiUzunluk]="64"
                    [ariaEtiketi]="
                      ('kiraFormu.aksesuar.lastik' | transloco) +
                      ' — ' +
                      ('kiraFormu.aksesuar.cikis' | transloco)
                    "
                  />
                </td>
                <td>
                  <rc-metin-girdisi
                    formControlName="aksLastikDonus"
                    [azamiUzunluk]="64"
                    [ariaEtiketi]="
                      ('kiraFormu.aksesuar.lastik' | transloco) +
                      ' — ' +
                      ('kiraFormu.aksesuar.donus' | transloco)
                    "
                  />
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <p class="kf-not">{{ 'kiraFormu.not.aksesuar' | transloco }}</p>
      </div>

      <div
        role="tabpanel"
        id="kf-alt-panel-ekkosullar"
        aria-labelledby="kf-alt-ekkosullar"
        data-kf-alt="ekkosullar"
        [hidden]="aktif() !== 'ekkosullar'"
      >
        <div class="rc-form-izgara">
          <rc-alan
            [etiket]="'kiraFormu.alan.belgeSablonu' | transloco"
            class="rc-form-izgara__genis"
          >
            <rc-secim
              formControlName="belgeSablonId"
              [secenekler]="d.belgeSablonuSecenekleri()"
              [bosEtiket]="'kiraFormu.alan.varsayilanSablon' | transloco"
            />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.ekKosullar' | transloco" class="rc-form-izgara__genis">
            <rc-metin-alani formControlName="ekKosullar" [azamiUzunluk]="2048" [satir]="6" />
          </rc-alan>
        </div>
      </div>
    </div>
  `,
})
export class Ayrintilar {
  protected readonly d = inject(KiraFormuDurumu);
  private readonly t = ceviriFonksiyonu();
  private readonly belge = inject(DOCUMENT);
  private readonly eleman = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected readonly aktif = signal<AltSekmeKimligi>(this.adrestekiAlt() ?? 'aciklama');
  protected readonly sekmeler = computed(() =>
    ALT_SEKMELER.map((k) => ({
      kimlik: k,
      etiket: this.t(`kiraFormu.alt.${k}` as CeviriAnahtari),
    })),
  );

  protected readonly aksesuarlar = [
    {
      etiket: 'kiraFormu.aksesuar.yedekAnahtar',
      cikis: 'aksYedekAnahtarCikis',
      donus: 'aksYedekAnahtarDonus',
    },
    { etiket: 'kiraFormu.aksesuar.stepne', cikis: 'aksStepneCikis', donus: 'aksStepneDonus' },
    { etiket: 'kiraFormu.aksesuar.zincir', cikis: 'aksZincirCikis', donus: 'aksZincirDonus' },
    {
      etiket: 'kiraFormu.aksesuar.ilkYardim',
      cikis: 'aksIlkYardimCikis',
      donus: 'aksIlkYardimDonus',
    },
  ] as const;

  /** OTA bileşen fiyatları (kaynak rezervasyon; manuel kirada "—"). */
  protected readonly ota = computed(() => {
    const r = this.d.kaynakRezervasyon.veri()?.rezervasyon ?? null;
    return [
      { anahtar: 'kiraFormu.ota.kira', deger: r?.otaKiraBedeli },
      { anahtar: 'kiraFormu.ota.drop', deger: r?.otaDropBedeli },
      { anahtar: 'kiraFormu.ota.bebek', deger: r?.otaBebekKoltugu },
      { anahtar: 'kiraFormu.ota.navigasyon', deger: r?.otaNavigasyon },
      { anahtar: 'kiraFormu.ota.lcf', deger: r?.otaLcf },
      { anahtar: 'kiraFormu.ota.cdw', deger: r?.otaCdw },
      { anahtar: 'kiraFormu.ota.scdw', deger: r?.otaScdw },
      { anahtar: 'kiraFormu.ota.ekSurucu', deger: r?.otaEkSurucu },
    ] as const;
  });

  sec(kimlik: AltSekmeKimligi, { adreseYaz = true }: { adreseYaz?: boolean } = {}): void {
    this.aktif.set(kimlik);
    if (!adreseYaz) return;
    const konum = this.belge.location;
    const pencere = this.belge.defaultView;
    if (!konum || !pencere) return;
    pencere.history.replaceState(
      pencere.history.state,
      '',
      altSekmeAdresi(konum.pathname, konum.search, kimlik),
    );
  }

  /** Gönderimde gizli alt paneldeki ilk hatalı alanın alt sekmesini açar. */
  ilkGecersizeGit(): void {
    afterNextRender(
      () => {
        const ilk = this.eleman.nativeElement.querySelector<HTMLElement>('[aria-invalid="true"]');
        const alt = ilk?.closest<HTMLElement>('[data-kf-alt]')?.dataset['kfAlt'];
        if (altSekmeMi(alt)) this.aktif.set(alt);
      },
      { injector: this.injector },
    );
  }

  protected adrestenOku(): void {
    const alt = this.adrestekiAlt();
    if (alt) this.aktif.set(alt);
  }

  protected tus(olay: KeyboardEvent): void {
    const sira = ALT_SEKMELER.indexOf(this.aktif());
    const n = ALT_SEKMELER.length;
    const hedef =
      olay.key === 'ArrowRight'
        ? (sira + 1) % n
        : olay.key === 'ArrowLeft'
          ? (sira - 1 + n) % n
          : olay.key === 'Home'
            ? 0
            : olay.key === 'End'
              ? n - 1
              : null;
    const kimlik = hedef === null ? undefined : ALT_SEKMELER[hedef];
    if (kimlik === undefined) return;
    olay.preventDefault();
    this.sec(kimlik);
    this.belge.getElementById(`kf-alt-${kimlik}`)?.focus();
  }

  protected para(v: SunucuSayisi, doviz: string | null | undefined): string {
    return paraBicimle(sayiya(v), isoParaBirimi(doviz)) || '—';
  }

  private adrestekiAlt(): AltSekmeKimligi | null {
    const alt = hashParcala(this.belge.location?.hash ?? '')['alt'];
    return altSekmeMi(alt) ? alt : null;
  }
}
