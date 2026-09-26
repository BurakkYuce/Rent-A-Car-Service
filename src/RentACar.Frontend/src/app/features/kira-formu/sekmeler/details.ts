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
import { formatMoney } from '@core/bicim/bicim';
import { translationFunction } from '@core/i18n/ceviri';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { RentalFormState } from '../rental-form-state';
import {
  SUB_TABS,
  type SubTabId,
  subTabUrl,
  isSubTab,
  parseHash,
  isoCurrency,
  toNumber,
} from '../kira-formu-modeli';
import type { ServerNumber } from '../kira-tipleri';
import { KF_SHARED } from './ortak';

/**
 * AYRINTILAR — 8 alt sekme (Blazor ile aynı kimlikler; `#sekme=ayrintilar&alt=aksesuar`). Alt paneller
 * gizlenir ama DOM'da kalır (değerler kaybolmaz). Gönderimde hatalı alan gizli alt paneldeyse
 * `ilkGecersizeGit()` o alt sekmeyi açar (ana sekmeyi `rc-sekmeli-form` açar).
 */
@Component({
  selector: 'rc-kf-ayrintilar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...KF_SHARED],
  host: { '(window:hashchange)': 'readFromUrl()' },
  template: `
    <div
      class="kf-alt-sekmeler"
      role="tablist"
      [attr.aria-label]="'kiraFormu.alt.bolumler' | transloco"
    >
      @for (s of tabs(); track s.kimlik) {
        <button
          type="button"
          role="tab"
          class="kf-alt-sekme"
          [id]="'kf-alt-' + s.kimlik"
          [attr.aria-selected]="aktif() === s.kimlik"
          [attr.aria-controls]="'kf-alt-panel-' + s.kimlik"
          [tabindex]="aktif() === s.kimlik ? 0 : -1"
          (click)="select(s.kimlik)"
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
        <section class="rc-bolum kf-kart">
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
                {{ money(k.provizyonKapamaTutar, k.doviz) }}
              }
            </p>
            <div class="kf-eylemler">
              <button
                type="button"
                class="rc-dugme"
                [disabled]="
                  !d.operasyon() || k.provizyonDurum !== 'Yok' || d.takePreAuthLock.gonderiliyor()
                "
                (click)="d.takePreAuth()"
              >
                {{ 'kiraFormu.eylem.provizyonAl' | transloco }}
              </button>
            </div>
            @if (k.provizyonDurum === 'Alindi') {
              <div class="rc-form-izgara" [formGroup]="d.closePreAuthForm">
                <rc-alan
                  [etiket]="'kiraFormu.alan.kapamaTutar' | transloco"
                  [ipucu]="'kiraFormu.ipucu.kapamaTutar' | transloco"
                >
                  <rc-para-girdisi
                    formControlName="kapamaTutar"
                    [paraBirimi]="d.rentalCurrency()"
                  />
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
                    [disabled]="!d.operasyon() || d.closePreAuthSubmission.gonderiliyor()"
                    (click)="d.closePreAuth()"
                  >
                    {{ 'kiraFormu.eylem.provizyonKapat' | transloco }}
                  </button>
                </div>
              </div>
              <rc-form-hatalari [hatalar]="d.closePreAuthSubmission.genelHatalar()" />
            }
          } @else {
            <p class="kf-not">{{ 'kiraFormu.not.onceKaydet' | transloco }}</p>
          }
        </section>
        <section class="rc-bolum kf-kart">
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
              <rc-arama-secim formControlName="ikinciSurucu" [kaynak]="d.customerDataSource" />
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
        @let rez = d.sourceReservation.veri()?.rezervasyon ?? null;
        @if (rez) {
          <p class="kf-not">
            {{ 'kiraFormu.not.kaynakRez' | transloco: { no: rez.reservationNo } }}
          </p>
        }
        <dl class="kf-bilgiler">
          @for (o of ota(); track o.anahtar) {
            <div>
              <dt>{{ o.anahtar | transloco }}</dt>
              <dd>{{ money(o.deger, d.kira()?.doviz) }}</dd>
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
          class="rc-tablo-kap"
          role="region"
          tabindex="0"
          [attr.aria-label]="'kiraFormu.alt.aksesuar' | transloco"
        >
          <table class="rc-duz-tablo" [attr.aria-label]="'kiraFormu.alt.aksesuar' | transloco">
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
              [secenekler]="d.documentTemplateOptions()"
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
export class Details {
  protected readonly d = inject(RentalFormState);
  private readonly t = translationFunction();
  private readonly belge = inject(DOCUMENT);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected readonly aktif = signal<SubTabId>(this.subTabInUrl() ?? 'aciklama');
  protected readonly tabs = computed(() =>
    SUB_TABS.map((k) => ({
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
    const r = this.d.sourceReservation.veri()?.rezervasyon ?? null;
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

  select(identity: SubTabId, { adreseYaz: writeToUrl = true }: { adreseYaz?: boolean } = {}): void {
    this.aktif.set(identity);
    if (!writeToUrl) return;
    const location = this.belge.location;
    const window = this.belge.defaultView;
    if (!location || !window) return;
    window.history.replaceState(
      window.history.state,
      '',
      subTabUrl(location.pathname, location.search, identity),
    );
  }

  /** Gönderimde gizli alt paneldeki ilk hatalı alanın alt sekmesini açar. */
  goToFirstInvalid(): void {
    afterNextRender(
      () => {
        const first =
          this.element.nativeElement.querySelector<HTMLElement>('[aria-invalid="true"]');
        const sub = first?.closest<HTMLElement>('[data-kf-alt]')?.dataset['kfAlt'];
        if (isSubTab(sub)) this.aktif.set(sub);
      },
      { injector: this.injector },
    );
  }

  protected readFromUrl(): void {
    const sub = this.subTabInUrl();
    if (sub) this.aktif.set(sub);
  }

  protected tus(evt: KeyboardEvent): void {
    const order = SUB_TABS.indexOf(this.aktif());
    const n = SUB_TABS.length;
    const target =
      evt.key === 'ArrowRight'
        ? (order + 1) % n
        : evt.key === 'ArrowLeft'
          ? (order - 1 + n) % n
          : evt.key === 'Home'
            ? 0
            : evt.key === 'End'
              ? n - 1
              : null;
    const identity = target === null ? undefined : SUB_TABS[target];
    if (identity === undefined) return;
    evt.preventDefault();
    this.select(identity);
    this.belge.getElementById(`kf-alt-${identity}`)?.focus();
  }

  protected money(v: ServerNumber, currency: string | null | undefined): string {
    return formatMoney(toNumber(v), isoCurrency(currency)) || '—';
  }

  private subTabInUrl(): SubTabId | null {
    const sub = parseHash(this.belge.location?.hash ?? '')['alt'];
    return isSubTab(sub) ? sub : null;
  }
}
