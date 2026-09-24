import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { FormArray, ReactiveFormsModule, type FormControl } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { UyariBandiServisi } from '@core/geri-bildirim/uyari-bandi-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { sekmeBaglami } from '@core/sekme/sekme-durumu';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { sunucuDegerleriniBirlestir } from '@features/planlama-ortak/form-yardimcilari';
import { suggestionList } from '@features/vehicles/suggestions';
import { TarihSaatPipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { OnayKutusu } from '@shared/form/kontroller/onay-kutusu';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import { SayiGirdisi } from '@shared/form/kontroller/sayi-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { SekmeliForm, SekmePaneli, type SekmeTanimi } from '@shared/form/sekmeli-form/sekmeli-form';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';
import { Ikon } from '@shared/ikon/ikon';

import { CUSTOMERS, CUSTOMER_TYPES, customerPath, type CustomerCard } from '../customer-model';
import {
  CustomerCardStore,
  addressSuggestionFetch,
  sourceSuggestionFetch,
  type SuggestionFetch,
} from '../customer.store';
import {
  CLASS_SUGGESTIONS,
  MAX_CONTACTS,
  PRIVACY_FLAGS,
  SECTIONS,
  type FieldSpec,
  type SuggestionKind,
} from './customer-form-fields';
import {
  CONTACTS_FIELD,
  type ContactGroup,
  buildForm,
  cardToForm,
  clearFlag,
  contactGroup,
  contactsOf,
  formToRequest,
  liftedPrivacyFlags,
  newCustomerValue,
  taxNumberIsSecret,
} from './customer-form-model';

/**
 * Cari kartı (`/app/cariler/yeni`, `/app/cariler/:id`) — Blazor `CustomerEdit` + liste içi "Yeni Cari" paritesi, TÜM
 * alanlar yedi sekmede. KVKK: TC/ehliyet/pasaport (ve bireysel caride vergi no) YALNIZ YAZILIR — kart onları
 * taşımaz, form boş açılır; boş bırakılan kayıtlı değeri korur (`null`), "Temizle" `""` gönderir. Anonimleştirilmiş
 * grubun alanları kilitli (`null` → sunucu korur). Anonimleştirmeyi kaldırmak yalnız ManageUsers (sunucu 403 —
 * mesaj KVKK sekmesinde de gösterilir). PUT tam değiştirme (`surum`); 409 `cakisma` → güncel kart KİRLİ forma
 * birleşir (form silinmez). Hiçbir değer tarayıcı deposuna yazılmaz. Yazma OperationsWrite; diğerleri salt okur.
 */
@Component({
  selector: 'rc-customer-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    FormHatalari,
    Ikon,
    MetinGirdisi,
    OnayKutusu,
    ParaGirdisi,
    SayiGirdisi,
    Secim,
    SekmeliForm,
    SekmePaneli,
    TarihSaatPipe,
    TarihSecici,
  ],
  providers: [FetchPolicy, CustomerCardStore],
  templateUrl: './customer-form.html',
  styleUrls: ['../customers.scss', './customer-form.scss'],
})
export class CustomerForm implements KaydedilmemisDegisiklikSahibi {
  protected readonly store = inject(CustomerCardStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly session = inject(OturumServisi);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastServisi);
  private readonly banner = inject(UyariBandiServisi);
  private readonly tab = sekmeBaglami();
  private readonly t = ceviriFonksiyonu();
  private readonly tabs = viewChild(SekmeliForm);

  /** Düzenlenen carinin kimliği; `null` = yeni cari. */
  protected readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id');
  protected readonly isNew = this.id === null;

  protected readonly form = buildForm();
  protected readonly contacts = this.form.controls[CONTACTS_FIELD] as FormArray<ContactGroup>;
  protected readonly submission = formGonderimi();
  /** Son okunan sunucu hâli: `surum` PUT'a gider, birleştirmenin tabanıdır. */
  protected readonly base = signal<CustomerCard | null>(null);
  /** Anonimleştirme kaldırma reddi (403) — KVKK sekmesinde açıkça gösterilir. */
  protected readonly privacyDenied = signal<string | null>(null);

  protected readonly sections = SECTIONS;
  protected readonly privacyFlags = PRIVACY_FLAGS;
  protected readonly maxContacts = MAX_CONTACTS;
  protected readonly clearFlag = clearFlag;
  protected readonly documentSecrets = ['ehliyetNo', 'pasaportNo'] as const;
  protected readonly canWrite = computed(() => this.session.izinVar('OperationsWrite'));
  protected readonly canLiftPrivacy = computed(() => this.session.izinVar('ManageUsers'));
  protected readonly taxSecret = computed(() => taxNumberIsSecret(this.base()));
  protected readonly typeOptions: readonly SecenekOgesi<string>[] = CUSTOMER_TYPES.map((x) => ({
    deger: x,
    etiket: this.t(`cari.tipler.${x}`),
  }));
  protected readonly triOptions: readonly SecenekOgesi<string>[] = [
    { deger: 'true', etiket: this.t('cari.evet') },
    { deger: 'false', etiket: this.t('cari.hayir') },
  ];
  protected readonly classSuggestions = CLASS_SUGGESTIONS;

  protected readonly tabDefs: readonly SekmeTanimi[] = [
    ...SECTIONS.map((s) => ({ kimlik: s.id, etiket: this.t(`cari.bolum.${s.id}`) })),
    { kimlik: 'kisiler', etiket: this.t('cari.bolum.kisiler') },
    { kimlik: 'kvkk', etiket: this.t('cari.bolum.kvkk') },
  ];

  private readonly suggestionSignals: Record<
    Exclude<SuggestionKind, 'sinif'>,
    () => readonly string[]
  >;

  constructor() {
    const control = (name: string) => this.form.controls[name] as FormControl<unknown>;
    const fetchers: Record<Exclude<SuggestionKind, 'sinif'>, SuggestionFetch> = {
      il: addressSuggestionFetch(this.api, 'il'),
      ilce: addressSuggestionFetch(this.api, 'ilce'),
      kaynak: sourceSuggestionFetch(this.api),
    };
    const enabled = () => this.canWrite();
    this.suggestionSignals = {
      il: suggestionList(control('il'), fetchers.il, enabled),
      ilce: suggestionList(control('ilce'), fetchers.ilce, enabled),
      kaynak: suggestionList(control('kaynak'), fetchers.kaynak, enabled),
    };

    const policy = inject(FetchPolicy);
    if (this.id !== null) {
      policy.baglan({
        parametre: signal(this.id).asReadonly(),
        yukle: (x) => this.store.card.yukle(x),
        sifirla: () => this.store.card.sifirla(),
      });
      effect(() => {
        const d = this.store.card.durum();
        if (d.tur === 'hazir') untracked(() => this.cardArrived(d.veri));
      });
    } else {
      this.form.reset({ ...newCustomerValue() });
    }
    effect(() => {
      if (!this.canWrite()) untracked(() => this.form.disable({ emitEvent: false }));
    });
    sayfaTerkKorumasi(() => this.form.dirty);
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.form.dirty;
  }

  protected label(name: string): string {
    return this.t(`cari.alan.${name}` as CeviriAnahtari);
  }

  protected suggestions(kind: SuggestionKind | undefined): readonly string[] {
    if (!kind) return [];
    return kind === 'sinif' ? this.classSuggestions : this.suggestionSignals[kind]();
  }

  protected listId(spec: FieldSpec): string | undefined {
    return spec.suggest ? `dl-cari-${spec.name}` : undefined;
  }

  /** Sabit seçenekler + kayıttaki listede olmayan (eski) değer — seçim sessizce boşalmasın. */
  protected options(spec: FieldSpec): readonly SecenekOgesi<string>[] {
    const list = [...(spec.options ?? [])];
    const current = this.base()?.[spec.name as keyof CustomerCard];
    if (typeof current === 'string' && current !== '' && !list.includes(current))
      list.push(current);
    return list.map((o) => ({ deger: o, etiket: o }));
  }

  /** Kayıtlı ehliyet/pasaport numarasının SUNUCU maskesi (istemci maske üretmez). */
  protected maskOf(name: string): string | null {
    const b = this.base();
    if (!b) return null;
    return (name === 'ehliyetNo' ? b.ehliyetNoMaske : b.pasaportNoMaske) ?? null;
  }

  /** KVKK ile anonimleştirilmiş grubun alanı (kart değeri göstermedi; kontrol kilitli, `null` = koru). */
  protected hidden(spec: FieldSpec): boolean {
    const b = this.base();
    return spec.privacy !== undefined && b !== null && b[spec.privacy] === true;
  }

  protected addContact(): void {
    if (this.contacts.length >= MAX_CONTACTS) return;
    this.contacts.push(contactGroup());
    this.contacts.markAsDirty();
  }

  protected removeContact(index: number): void {
    this.contacts.removeAt(index);
    this.contacts.markAsDirty();
  }

  protected save(): void {
    if (!this.canWrite()) return;
    const base = this.base();
    if (!this.isNew && (base === null || this.store.card.yukleniyor())) return;
    this.privacyDenied.set(null);
    const value = this.form.getRawValue();
    const body = formToRequest(value, this.contacts.getRawValue(), base);
    const lifted = liftedPrivacyFlags(value, base);
    const id = this.id;
    this.submission.gonder(
      this.form,
      (key) =>
        id === null
          ? this.api.post<CustomerCard>(CUSTOMERS, body, { islemAnahtari: key })
          : this.api.put<CustomerCard>(customerPath(id), body, { islemAnahtari: key }),
      {
        gecersiz: () => this.tabs()?.ilkGecersizeGit(),
        basarili: (card) => {
          if (id === null) {
            this.toast.basari(this.t('cari.kart.olusturuldu', { ad: this.displayName(card) }));
            void this.router.navigate(['/cariler', card.id], { replaceUrl: true });
            return;
          }
          this.toast.basari(this.t('cari.kart.kaydedildi', { ad: this.displayName(card) }));
          this.fill(card);
        },
        hata: (h) => {
          if (h.kod === 'cakisma') this.store.card.yenile();
          else if (h.kod === 'yetki_yok' && lifted.length > 0) {
            this.privacyDenied.set(h.detay || this.t('cari.kvkk.kaldirmaYetkisiz'));
            this.tabs()?.sec('kvkk');
          }
        },
      },
    );
  }

  protected displayName(card: CustomerCard | null): string {
    if (!card) return '';
    if (card.anonimAd) return this.t('cari.anonim');
    const person = [card.ad, card.soyad].filter((x) => x && x.trim() !== '').join(' ');
    return (card.tip === 'Bireysel' ? person : card.unvan || person) || '';
  }

  private fill(card: CustomerCard): void {
    this.base.set(card);
    this.form.reset({ ...cardToForm(card) });
    this.resetContacts(card);
    this.applyLocks(card);
  }

  private resetContacts(card: CustomerCard | null): void {
    this.contacts.clear({ emitEvent: false });
    for (const c of contactsOf(card)) this.contacts.push(contactGroup(c), { emitEvent: false });
    this.contacts.markAsPristine();
  }

  /** Anonimleştirilmiş grup alanları ve gizli numara kilitleri (kart değeri yok → yazılamaz, korunur). */
  private applyLocks(card: CustomerCard): void {
    if (!this.canWrite()) {
      this.form.disable({ emitEvent: false });
      return;
    }
    for (const section of SECTIONS) {
      for (const spec of section.fields) {
        const c = this.form.controls[spec.name];
        if (!c) continue;
        if (spec.privacy && card[spec.privacy] === true) c.disable({ emitEvent: false });
        else c.enable({ emitEvent: false });
      }
    }
    const lock = (name: string, locked: boolean) => {
      const c = this.form.controls[name];
      if (locked) c?.disable({ emitEvent: false });
      else c?.enable({ emitEvent: false });
    };
    lock('tcKimlik', card.anonimTc === true);
    lock(clearFlag('tcKimlik'), card.anonimTc === true);
    for (const name of ['ehliyetNo', 'pasaportNo'] as const) {
      lock(name, card.anonimBelge === true);
      lock(clearFlag(name), card.anonimBelge === true);
    }
  }

  /** Temiz form sunucu hâline sıfırlanır; kirli formda dokunulan alanlar korunur (çakışan işaretlenir). */
  private cardArrived(card: CustomerCard): void {
    this.tab.etiketAyarla(this.displayName(card) || this.t('cari.kart.baslikBos'));
    const previous = this.base();
    if (!this.form.dirty || previous === null) {
      this.fill(card);
      return;
    }
    const fresh: Record<string, unknown> = { ...cardToForm(card) };
    const conflicts = sunucuDegerleriniBirlestir(
      this.form,
      fresh,
      cardToForm(previous),
      this.t('cari.kart.cakismaAlan'),
    );
    if (this.contacts.pristine) this.resetContacts(card);
    if (conflicts.length > 0) {
      this.banner.goster({
        tur: 'uyari',
        mesaj: this.t('cari.kart.cakismaBant', { sayi: conflicts.length }),
        kod: 'cakisma',
      });
    }
    this.base.set(card);
    this.applyLocks(card);
  }
}
