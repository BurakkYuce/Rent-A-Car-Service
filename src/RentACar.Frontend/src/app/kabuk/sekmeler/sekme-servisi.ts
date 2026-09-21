import { DOCUMENT } from '@angular/common';
import { inject, Injectable, InjectionToken, signal } from '@angular/core';
import { NavigationEnd, Router, type Routes } from '@angular/router';

import { kirliBilesenMi, ONAY_ISTEMI } from '@core/form/kaydedilmemis-degisiklik';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import {
  anahtarUret,
  KABUK_ISARETI,
  rotaDeseni,
  sekmeAnahtari,
  SEKME_VERISI,
  sonYaprak,
} from '@core/sekme/sekme-anahtari';
import { SekmeDurumu } from '@core/sekme/sekme-durumu';
import { SekmeRotaStratejisi } from '@core/sekme/sekme-stratejisi';

import { SAYFALAR } from '../../sayfalar';

/** Açık tutulabilecek en fazla sekme (bellekte yaşayan sayfa bileşeni sayısını da sınırlar). */
export const EN_FAZLA_SEKME = 10;

/** Tarayıcı deposu anahtarı (`rc.` öneki: çıkışta `OturumServisi` de siler). */
export const SEKME_DEPOSU = 'rc.sekmeler';

/** Kabuk sayfaları (sekme başlığı ve geri yüklemede desen doğrulaması); testlerde değiştirilir. */
export const KABUK_SAYFALARI = new InjectionToken<Routes>('KABUK_SAYFALARI', {
  providedIn: 'root',
  factory: () => SAYFALAR,
});

export interface Sekme {
  readonly anahtar: string;
  /** Rota deseni (`/kiralar/:id`). */
  readonly desen: string;
  /** `:id` parametresi; yoksa `null`. */
  readonly id: string | null;
  /** Sekmedeki son adres (sorgu + fragment dahil) — YALNIZ BELLEKTE. */
  readonly url: string;
  /** Rota başlığı (kişisel veri değil); sayfanın verdiği özel etiket `SekmeDurumu`'nda. */
  readonly baslik: string;
  /** Depoya yazılabilir mi (yol parametresi yalnız `:id`). */
  readonly kalici: boolean;
}

/** Depodaki kayıt: YALNIZ rota deseni + id (KVKK — ad, sorgu, filtre, form değeri yazılmaz). */
export interface SekmeDepoKaydi {
  readonly rota: string;
  readonly id: string | null;
}

const BASLIK_SONEKI = /\s+—\s+RentACar$/;
const DESEN = /^\/(?:(?:[a-z0-9-]+|:id)(?:\/(?:[a-z0-9-]+|:id))*)?$/;
const KIMLIK = /^[A-Za-z0-9_-]{1,64}$/;

/**
 * Sekmeli çalışma alanı (Revlo `TabsService` fikri; kullanıcı/otel/dil/sunucuya-kayıt/sayfa-kataloğu
 * bağları kesilerek yeniden yazıldı). Her kabuk sayfası (desen + yol parametresi) bir sekmedir;
 * sekmeler arasında geçişte sayfa bileşeni `SekmeRotaStratejisi`'nde yaşar (form durumu korunur).
 *
 * - En fazla {@link EN_FAZLA_SEKME}. Doluyken yeni sayfa açılırsa en uzun süredir kullanılmayan,
 *   kaydedilmemiş değişikliği OLMAYAN sekme kapatılır (bilgi toast'u); hepsi kirliyse gezinme durur
 *   (uyarı toast'u).
 * - Tarayıcıda (`rc.sekmeler`) yalnız `{ rota, id }` saklanır; yenilemede sekmeler geri gelir, içerikleri
 *   ilk tıklamada yüklenir. Çıkışta `OturumServisi.temizlikKaydet` ile tümü silinir.
 */
@Injectable({ providedIn: 'root' })
export class SekmeServisi {
  private readonly router = inject(Router);
  private readonly strateji = inject(SekmeRotaStratejisi);
  private readonly durum = inject(SekmeDurumu);
  private readonly toast = inject(ToastServisi);
  private readonly onayIstemi = inject(ONAY_ISTEMI);
  private readonly pencere = inject(DOCUMENT).defaultView;
  private readonly t = ceviriFonksiyonu();
  private readonly basliklar = baslikHaritasi(inject(KABUK_SAYFALARI));

  private readonly _sekmeler = signal<readonly Sekme[]>([]);
  readonly sekmeler = this._sekmeler.asReadonly();
  readonly etkinAnahtar = this.durum.etkinAnahtar.asReadonly();

  /** Görünür sayfa bileşeni (kabuğun outlet'i bildirir) — kirli mi sorusu için. */
  private etkinBilesen: unknown = null;
  private readonly kullanim = new Map<string, number>();
  private kullanimSayaci = 0;

  constructor() {
    this._sekmeler.set(this.depodanOku());
    this.esitle(false);
    // Kök servis: uygulama ömrünce yaşar, abonelik bilinçli olarak kapatılmaz.
    this.router.events.subscribe((olay) => {
      if (olay instanceof NavigationEnd) this.gezinmeBitti();
    });
    inject(OturumServisi).temizlikKaydet(() => this.temizle());
  }

  /** Kabuk outlet'i: etkin bileşen değişti (`activate`/`attach` → bileşen, `deactivate`/`detach` → null). */
  etkinBileseniAyarla(bilesen: unknown): void {
    this.etkinBilesen = bilesen;
  }

  /** Sekmenin sayfası kaydedilmemiş değişiklik taşıyor mu (görünür ya da arka planda). */
  kirliMi(anahtar: string): boolean {
    const bilesen =
      anahtar === this.durum.etkinAnahtar()
        ? this.etkinBilesen
        : this.strateji.ayrikBilesen(anahtar);
    return kirliBilesenMi(bilesen);
  }

  /** Herhangi bir sekmede (ya da sekme dışı görünür sayfada) kaydedilmemiş değişiklik var mı. */
  herhangiKirli(): boolean {
    return (
      kirliBilesenMi(this.etkinBilesen) || this._sekmeler().some((s) => this.kirliMi(s.anahtar))
    );
  }

  /**
   * Uygulamadan ayrılmadan (Blazor ekranı, çıkış) önce TEK soru: kirli sekme varsa onay istenir.
   * `true` → devam edilebilir.
   */
  async ayrilmaOnayi(): Promise<boolean> {
    if (!this.herhangiKirli()) return true;
    return this.onayIstemi(this.t('form.kaydedilmemis.onay'));
  }

  /**
   * Yeni sekme için yer (kabuğun `canActivateChild`'ı çağırır). Sekme zaten açıksa ya da yer varsa
   * `true`; doluysa en eski temiz sekmeyi kapatır; hepsi kirliyse `false` (gezinme durur).
   */
  yerAc(anahtar: string): boolean {
    const liste = this._sekmeler();
    if (liste.some((s) => s.anahtar === anahtar) || liste.length < EN_FAZLA_SEKME) return true;
    const etkin = this.durum.etkinAnahtar();
    const aday = liste
      .filter((s) => s.anahtar !== etkin && !this.kirliMi(s.anahtar))
      .sort((a, b) => (this.kullanim.get(a.anahtar) ?? 0) - (this.kullanim.get(b.anahtar) ?? 0))[0];
    if (!aday) {
      this.toast.uyari(this.t('kabuk.sekme.sinir', { sayi: EN_FAZLA_SEKME }));
      return false;
    }
    const etiket = this.etiket(aday);
    this.cikar(aday.anahtar);
    this.toast.bilgi(this.t('kabuk.sekme.eskiKapatildi', { sayi: EN_FAZLA_SEKME, etiket }));
    return true;
  }

  /** Sekmenin görünen adı: sayfanın verdiği etiket, yoksa rota başlığı (+ kısa id). */
  etiket(sekme: Sekme): string {
    const ozel = this.durum.etiketler().get(sekme.anahtar);
    if (ozel) return ozel;
    return sekme.id ? `${sekme.baslik} · ${sekme.id.slice(0, 8)}` : sekme.baslik;
  }

  /** Sekmeye geç (son adresine). */
  gec(anahtar: string): Promise<boolean> {
    const sekme = this._sekmeler().find((s) => s.anahtar === anahtar);
    return sekme ? this.router.navigateByUrl(sekme.url) : Promise.resolve(false);
  }

  /**
   * Sekmeyi kapat. Arka plandaki kirli sekme için onay sorulur. Görünür sekme kapanınca komşu sekmeye
   * (yoksa ana sayfaya) gidilir; sayfanın kendi `canDeactivate` sorusu reddedilirse sekme yerinde kalır.
   */
  async kapat(anahtar: string): Promise<void> {
    const liste = this._sekmeler();
    const sira = liste.findIndex((s) => s.anahtar === anahtar);
    const sekme = liste[sira];
    if (!sekme) return;
    if (anahtar !== this.durum.etkinAnahtar()) {
      if (this.kirliMi(anahtar) && !(await this.onayIstemi(this.t('form.kaydedilmemis.onay')))) {
        return;
      }
      this.cikar(anahtar);
      return;
    }
    const komsu = liste[sira + 1] ?? liste[sira - 1];
    // Önce listeden çıkar: sayfa artık saklanmaz, kendi canDeactivate'i kirli formu sorar.
    this._sekmeler.set(liste.filter((s) => s !== sekme));
    this.esitle();
    const gidildi = await this.router.navigateByUrl(komsu?.url ?? '/');
    if (!gidildi && this.durum.etkinAnahtar() === anahtar) {
      // Vazgeçildi: sekme yerine döner (bileşen hâlâ görünür, hiçbir şey kaybolmadı).
      const simdiki = this._sekmeler().filter((s) => s.anahtar !== anahtar);
      this._sekmeler.set([...simdiki.slice(0, sira), sekme, ...simdiki.slice(sira)]);
      this.esitle();
      return;
    }
    this.kullanim.delete(anahtar);
    this.durum.etiketAyarla(anahtar, null);
  }

  /** Çıkış (tam temizlik): sekmeler, arka plandaki bileşenler, etiketler, depo. */
  temizle(): void {
    this._sekmeler.set([]);
    this.kullanim.clear();
    this.etkinBilesen = null;
    this.strateji.temizle();
    this.durum.temizle();
    try {
      this.pencere?.localStorage.removeItem(SEKME_DEPOSU);
    } catch {
      // Engelli depolama.
    }
  }

  private gezinmeBitti(): void {
    const yaprak = sonYaprak(this.router.routerState.snapshot.root);
    const kabukta = yaprak.pathFromRoot.some((r) => r.routeConfig?.data?.[KABUK_ISARETI] === true);
    if (!kabukta) {
      this.durum.etkinlestir(null);
      return;
    }
    const anahtar = sekmeAnahtari(yaprak);
    if (anahtar === null) {
      this.durum.etkinlestir('');
      return;
    }
    const url = this.router.url;
    this.kullanim.set(anahtar, ++this.kullanimSayaci);
    const liste = this._sekmeler();
    const mevcut = liste.find((s) => s.anahtar === anahtar);
    if (mevcut) {
      if (mevcut.url !== url) {
        this._sekmeler.set(liste.map((s) => (s === mevcut ? { ...s, url } : s)));
      }
    } else {
      const desen = rotaDeseni(yaprak);
      const id = yaprak.paramMap.get('id');
      this._sekmeler.set([
        ...liste,
        {
          anahtar,
          desen,
          id,
          url,
          baslik: basligiSadelestir(yaprak.title) ?? this.basliklar.get(desen) ?? desen,
          kalici:
            Object.keys(yaprak.params).every((p) => p === 'id') &&
            DESEN.test(desen) &&
            (id === null || KIMLIK.test(id)),
        },
      ]);
    }
    this.durum.etkinlestir(anahtar);
    this.esitle();
  }

  private cikar(anahtar: string): void {
    this._sekmeler.update((liste) => liste.filter((s) => s.anahtar !== anahtar));
    this.kullanim.delete(anahtar);
    this.durum.etiketAyarla(anahtar, null);
    this.esitle();
  }

  /** Strateji (hangi sayfalar saklanır) + depo senkronu. */
  private esitle(yaz = true): void {
    this.strateji.acikAnahtarlariAyarla(this._sekmeler().map((s) => s.anahtar));
    if (!yaz) return;
    const kayitlar: SekmeDepoKaydi[] = this._sekmeler()
      .filter((s) => s.kalici)
      .map((s) => ({ rota: s.desen, id: s.id }));
    try {
      this.pencere?.localStorage.setItem(SEKME_DEPOSU, JSON.stringify(kayitlar));
    } catch {
      // Engelli depolama: sekmeler yalnız bu sayfa ömrünce yaşar.
    }
  }

  /** Depodaki `{ rota, id }` listesi → sekmeler. Bilinmeyen desen, bozuk id, fazlası atılır. */
  private depodanOku(): Sekme[] {
    let ham: unknown;
    try {
      ham = JSON.parse(this.pencere?.localStorage.getItem(SEKME_DEPOSU) ?? '[]');
    } catch {
      return [];
    }
    if (!Array.isArray(ham)) return [];
    const sekmeler: Sekme[] = [];
    for (const kayit of ham as unknown[]) {
      const sekme = this.kayittanSekme(kayit);
      if (sekme && !sekmeler.some((s) => s.anahtar === sekme.anahtar)) sekmeler.push(sekme);
      if (sekmeler.length === EN_FAZLA_SEKME) break;
    }
    return sekmeler;
  }

  private kayittanSekme(kayit: unknown): Sekme | null {
    if (typeof kayit !== 'object' || kayit === null) return null;
    const { rota, id } = kayit as Partial<Record<keyof SekmeDepoKaydi, unknown>>;
    if (typeof rota !== 'string' || !DESEN.test(rota)) return null;
    const baslik = this.basliklar.get(rota);
    if (baslik === undefined) return null;
    let kimlik: string | null = null;
    if (rota.includes(':id')) {
      if (typeof id !== 'string' || !KIMLIK.test(id)) return null;
      kimlik = id;
    } else if (id !== null) {
      return null;
    }
    return {
      anahtar: anahtarUret(rota, kimlik ? { id: kimlik } : {}),
      desen: rota,
      id: kimlik,
      url: kimlik ? rota.replace(':id', kimlik) : rota,
      baslik,
      kalici: true,
    };
  }
}

function basligiSadelestir(baslik: string | undefined): string | null {
  const sade = baslik?.replace(BASLIK_SONEKI, '').trim();
  return sade ? sade : null;
}

/** Sayfa rotalarından desen → başlık (sekme dışı ve yönlendirme rotaları hariç). */
function baslikHaritasi(sayfalar: Routes, ust = ''): Map<string, string> {
  const harita = new Map<string, string>();
  for (const rota of sayfalar) {
    if (rota.path === undefined || rota.path === '**' || rota.redirectTo !== undefined) continue;
    const desen = [ust, rota.path].filter(Boolean).join('/');
    if (rota.children) {
      for (const [d, b] of baslikHaritasi(rota.children, desen)) harita.set(d, b);
      continue;
    }
    if (rota.data?.[SEKME_VERISI] === false) continue;
    const baslik = typeof rota.title === 'string' ? basligiSadelestir(rota.title) : null;
    harita.set(`/${desen}`, baslik ?? `/${desen}`);
  }
  return harita;
}
