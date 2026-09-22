import { DOCUMENT } from '@angular/common';
import { computed, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { ApiHatasi, apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { UyariBandiServisi } from '@core/geri-bildirim/uyari-bandi-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { TEMA_ANAHTARI, TemaServisi } from '@core/tema/tema-servisi';

import { istekBaglami } from './istek-baglami';
import type { OturumBaglami } from './oturum-baglami';
import type { Ben, GirisBilgileri, Izin } from './oturum-tipleri';

/** Oturum uçları kendi hatalarını çağırana verir: diyalog ya da genel toast/bant yok. Her istekte taze bağlam. */
function oturumIstegi() {
  return { context: istekBaglami({ sessiz: true, yenidenGirisYok: true }) };
}

/**
 * Yeni arayüzün tarayıcıda sakladığı anahtarların öneki. Çıkışta `rc.` ile başlayan her anahtar silinir
 * (tema tercihi hariç — kişisel veri değil). Aynı origin'deki Blazor anahtarlarına dokunulmaz.
 */
export const DEPO_ONEKI = 'rc.';

/**
 * Oturum durumu (signal) ve giriş/çıkış. Revlo `AuthService` YENİDEN YAZILDI: token yok, oturum
 * `racar.session` çerezinde (`withCredentials`), CSRF Angular'ın XSRF çerez/başlık desenine bağlı.
 *
 * - `ben`: `GET oturum/ben` — kullanıcı, firma, rol, etkin izinler, şube kapsamı, modüller, renkler, pilot.
 * - `baglam`: F3.4 `OTURUM_BAGLAMI`'nın değeri (kiracı|kullanıcı|şube). Değişince sayfalar yeniden
 *   yüklenir, `null` olunca store'lar sıfırlanır.
 * - Çıkışta tam temizlik: kayıtlı temizleyiciler (sekmeler, sorgu önbellekleri, açık diyaloglar),
 *   `rc.*` depo anahtarları (tema hariç), toast/bant, firma renkleri.
 */
@Injectable({ providedIn: 'root' })
export class OturumServisi {
  private readonly api = inject(ApiIstemcisi);
  private readonly router = inject(Router);
  private readonly tema = inject(TemaServisi);
  private readonly toast = inject(ToastServisi);
  private readonly bant = inject(UyariBandiServisi);
  private readonly pencere = inject(DOCUMENT).defaultView;
  private readonly t = ceviriFonksiyonu();

  private readonly deger = signal<Ben | null>(null);
  private readonly temizleyiciler = new Set<() => void>();
  private ilkYuklemeSozu: Promise<Ben | null> | null = null;

  /** Oturumdaki kullanıcı; oturum yoksa `null`. */
  readonly ben = this.deger.asReadonly();
  readonly girisYapildi = computed(() => this.deger() !== null);
  private readonly izinKumesi = computed(() => new Set(this.deger()?.izinler ?? []));

  /** F3.4 `OTURUM_BAGLAMI` değeri: oturum/kiracı/kullanıcı/şube kapsamı değişince değişir. */
  readonly baglam = computed<OturumBaglami | null>(
    () => {
      const ben = this.deger();
      if (!ben) return null;
      const sube = ben.subeKapsami.tumSubeler ? '*' : (ben.subeKapsami.subeId ?? '-');
      return { anahtar: `${ben.kiraci.id}|${ben.kullanici.id}|${sube}` };
    },
    { equal: (a, b) => a?.anahtar === b?.anahtar },
  );

  izinVar(izin: Izin): boolean {
    return this.izinKumesi().has(izin);
  }

  /** İzinlerin HEPSİ var mı (düğme kapıları: `DUGME_IZINLERI[...].izinler`). Boş liste = oturum yeterli. */
  izinlerVar(izinler: readonly Izin[]): boolean {
    const kume = this.izinKumesi();
    return izinler.every((izin) => kume.has(izin));
  }

  /**
   * Uygulama açılışında bir kez `ben` okunur (guard'lar bekler). Oturum yoksa `null`; ağ/sunucu
   * hatası da `null` (giriş sayfası açılır, sunucu hatası toast'la söylenir).
   */
  ilkYukleme(): Promise<Ben | null> {
    this.ilkYuklemeSozu ??= this.yukle();
    return this.ilkYuklemeSozu;
  }

  /** `GET oturum/ben` → `ben` günceller. */
  async yukle(): Promise<Ben | null> {
    try {
      const ben = await firstValueFrom(this.api.get<Ben>('/api/ui/v1/oturum/ben', oturumIstegi()));
      this.benAyarla(ben);
      return ben;
    } catch (hata: unknown) {
      const apiHatasi = apiHatasinaCevir(hata);
      if (apiHatasi.kod === 'sunucu' || apiHatasi.kod === 'ag') {
        this.toast.hata(apiHatasi.detay);
      }
      this.deger.set(null);
      return null;
    }
  }

  /**
   * Giriş: önce anonim XSRF belirteci (`GET oturum/xsrf`), sonra `POST oturum/giris`. Sunucu girişten
   * sonra YENİ kimliğe bağlı belirteç verir; Angular'ın çerez okuyucusu onu sonraki isteklere koyar.
   * Hata `ApiHatasi` olarak fırlar (`dogrulama` / `cok_istek` / `kiraci_kapali` …) — mesajı çağıran seçer.
   */
  async girisYap(bilgi: GirisBilgileri): Promise<Ben> {
    await firstValueFrom(this.api.get<unknown>('/api/ui/v1/oturum/xsrf', oturumIstegi()));
    const ben = await firstValueFrom(
      this.api.post<Ben>('/api/ui/v1/oturum/giris', bilgi, oturumIstegi()),
    );
    this.benAyarla(ben);
    return ben;
  }

  /** Çıkış: sunucu oturumu kapatılır (hata yutulur — yerel temizlik her durumda), giriş sayfasına. */
  async cikisYap(): Promise<void> {
    try {
      await firstValueFrom(this.api.post<unknown>('/api/ui/v1/oturum/cikis', {}, oturumIstegi()));
    } catch {
      // Oturum zaten düşmüş ya da ağ yok: yerel temizlik yine yapılır.
    }
    this.temizle();
    await this.router.navigate(['/giris'], {
      queryParams: { neden: 'cikis' },
      replaceUrl: true,
    });
  }

  /** Firma kapatıldı (`kiraci_kapali`): tam temizlik + mesajlı giriş sayfası. */
  async kiraciKapandi(): Promise<void> {
    this.temizle();
    await this.router.navigate(['/giris'], {
      queryParams: { neden: 'kiraci_kapali' },
      replaceUrl: true,
    });
  }

  /**
   * Çıkışta çalışacak temizleyici kaydı (sekmeler F3.2, sorgu önbellekleri F3.4, açık diyaloglar).
   * Dönen fonksiyon kaydı siler.
   */
  temizlikKaydet(temizleyici: () => void): () => void {
    this.temizleyiciler.add(temizleyici);
    return () => this.temizleyiciler.delete(temizleyici);
  }

  /** Oturuma bağlı her şeyi siler; sunucuya gitmez. */
  temizle(): void {
    for (const temizleyici of [...this.temizleyiciler]) {
      try {
        temizleyici();
      } catch (hata: unknown) {
        console.error(hata);
      }
    }
    this.deger.set(null);
    this.ilkYuklemeSozu = Promise.resolve(null);
    this.tema.kiraciRenkleriUygula(null);
    this.toast.temizle();
    this.bant.kapat();
    this.depoTemizle(() => this.pencere?.localStorage, TEMA_ANAHTARI);
    this.depoTemizle(() => this.pencere?.sessionStorage, null);
  }

  private benAyarla(ben: Ben): void {
    this.deger.set(ben);
    this.ilkYuklemeSozu = Promise.resolve(ben);
    this.tema.kiraciRenkleriUygula(ben.renkler);
    if (!ben.pilot) {
      this.bant.goster({
        tur: 'uyari',
        mesaj: this.t('oturum.pilotDegil'),
        kod: 'pilot_degil',
        kalici: true,
      });
    } else if (this.bant.bant()?.kod === 'pilot_degil') {
      this.bant.kapat();
    }
  }

  private depoTemizle(depoAl: () => Storage | undefined, korunan: string | null): void {
    try {
      const depo = depoAl(); // erişimin kendisi de fırlatabilir (engelli depolama)
      if (!depo) return;
      const silinecek: string[] = [];
      for (let i = 0; i < depo.length; i++) {
        const anahtar = depo.key(i);
        if (anahtar?.startsWith(DEPO_ONEKI) && anahtar !== korunan) silinecek.push(anahtar);
      }
      for (const anahtar of silinecek) depo.removeItem(anahtar);
    } catch {
      // Engelli depolama: silinecek bir şey de yok.
    }
  }
}

/** `ApiHatasi` mi ve kodu bu mu (tip daraltmalı yardımcı). */
export function hataKoduMu(hata: unknown, kod: ApiHatasi['kod']): hata is ApiHatasi {
  return hata instanceof ApiHatasi && hata.kod === kod;
}
