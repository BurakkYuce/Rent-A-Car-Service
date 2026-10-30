import { DOCUMENT } from '@angular/common';
import { computed, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { ApiHatasi, toApiError } from '@core/api/api-error';
import { ApiIstemcisi } from '@core/api/api-client';
import { ToastService } from '@core/feedback/toast-service';
import { WarningBannerService } from '@core/feedback/warning-banner-service';
import { translationFunction } from '@core/i18n/translation';
import { THEME_KEY, TemaServisi } from '@core/theme/theme-service';

import { requestContext } from './request-context';
import { contextOfSession, identityOfContext, type OturumBaglami } from './session-context';
import type { Ben, LoginCredentials, Permission } from './session-types';

/** Oturum uçları kendi hatalarını çağırana verir: diyalog ya da genel toast/bant yok. Her istekte taze bağlam. */
function sessionRequest() {
  return { context: requestContext({ sessiz: true, yenidenGirisYok: true }) };
}

/**
 * Yeni arayüzün tarayıcıda sakladığı anahtarların öneki. Çıkışta `rc.` ile başlayan her anahtar silinir
 * (tema tercihi hariç — kişisel veri değil). Aynı origin'deki Blazor anahtarlarına dokunulmaz.
 */
export const STORE_PREFIX = 'rc.';

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
export class SessionService {
  private readonly api = inject(ApiIstemcisi);
  private readonly router = inject(Router);
  private readonly tema = inject(TemaServisi);
  private readonly toast = inject(ToastService);
  private readonly bant = inject(WarningBannerService);
  private readonly window = inject(DOCUMENT).defaultView;
  private readonly t = translationFunction();

  private readonly deger = signal<Ben | null>(null);
  private readonly cleaners = new Set<() => void>();
  private initialLoadPromise: Promise<Ben | null> | null = null;
  /**
   * `Idempotency-Key` → anahtarın İLK gönderildiği andaki kimlik + kesin reddi görülmemiş deneme sayısı
   * ({@link expectedIdentityFor}, {@link keyAttemptRejected}).
   */
  private readonly keyIdentities = new Map<string, { identity: string; open: number }>();

  /** Oturumdaki kullanıcı; oturum yoksa `null`. */
  readonly ben = this.deger.asReadonly();
  readonly loggedIn = computed(() => this.deger() !== null);
  private readonly permissionSet = computed(() => new Set(this.deger()?.izinler ?? []));

  /** F3.4 `OTURUM_BAGLAMI` değeri: oturum/kiracı/kullanıcı/şube kapsamı değişince değişir. */
  readonly context = computed<OturumBaglami | null>(
    () => {
      const ben = this.deger();
      // Anahtar biçiminin TEK kaynağı: para denemeleri de aynı fonksiyonla yazar (`MoneyAttempt.context`).
      return ben ? { anahtar: contextOfSession(ben) } : null;
    },
    { equal: (a, b) => a?.anahtar === b?.anahtar },
  );

  izinVar(permission: Permission): boolean {
    return this.permissionSet().has(permission);
  }

  /** İzinlerin HEPSİ var mı (düğme kapıları: `DUGME_IZINLERI[...].izinler`). Boş liste = oturum yeterli. */
  hasPermissions(permissions: readonly Permission[]): boolean {
    const set = this.permissionSet();
    return permissions.every((permission) => set.has(permission));
  }

  /**
   * #417 L1/L2: bir işlem anahtarının (`Idempotency-Key`) bağlı olduğu kimlik (`kiraciId|kullaniciId`, biçim tek
   * kaynaktan: `identityOfContext`). Anahtar ilk kez sorulduğunda o anki kimliğe BAĞLANIR; aynı anahtarın sonraki her
   * gönderimi (tekrar, yeniden girişten sonraki tekrar) bu kimliği taşır. Sunucu anahtarı kiracı|kullanıcı|anahtar'dan
   * türettiği için, başka sekmede başka kullanıcıyla girildikten sonra başlıksız tekrar YENİ işlem sayılır ve ikinci kez
   * yazılırdı; başlıkla 409 `oturum_degisti` döner. Bağ kimlik değişince SİLİNMEZ (silinseydi tekrar yeni kimliğe
   * bağlanıp yazılırdı); çıkışta (`clear`) ya da anahtarın HER denemesi kesin reddedilince ({@link keyAttemptRejected})
   * düşer. Oturum yoksa `null` (başlık gönderilmez, bağlanmaz). Her çağrı bir deneme sayılır (açık deneme +1).
   */
  expectedIdentityFor(key: string): string | null {
    let bound = this.keyIdentities.get(key);
    if (bound === undefined) {
      const current = this.context()?.anahtar;
      if (current === undefined) return null;
      bound = { identity: identityOfContext(current), open: 0 };
      this.keyIdentities.set(key, bound);
    }
    bound.open++;
    return bound.identity;
  }

  /**
   * adv-419 L1: anahtarın bir denemesi KESİN reddedildi (4xx; `mukerrer`/`oturum_degisti` hariç — sunucu işledi, yazmadı).
   * Anahtarın açık denemesi kalmadıysa bağ düşer: o anahtarla A adına hiçbir şey yazılmadığı KESİN, bu yüzden aynı formu
   * sonra B'nin göndermesi ilk yazımdır (A'nın anahtarı B'yi sayfa yenilenene kadar kilitlemez). Başarılı, belirsiz
   * (5xx/ağ) ya da yanıtı hiç görülmeyen (iptal) deneme açık kalır → bağ ömür boyu kalır; o anahtarın başka kullanıcı
   * adına yeniden yazılması (anahtar kullanıcı başına türetilir) yine 409'a düşer.
   */
  keyAttemptRejected(key: string): void {
    const bound = this.keyIdentities.get(key);
    if (bound !== undefined && --bound.open <= 0) this.keyIdentities.delete(key);
  }

  /**
   * Uygulama açılışında bir kez `ben` okunur (guard'lar bekler). Oturum yoksa `null`; açılıştaki ağ/sunucu
   * hatası da `null` (değer henüz boş — giriş sayfası açılır, sunucu hatası toast'la söylenir).
   */
  initialLoad(): Promise<Ben | null> {
    this.initialLoadPromise ??= this.yukle();
    return this.initialLoadPromise;
  }

  /**
   * `GET oturum/ben` → `ben` günceller; dönen değer YALNIZ sunucudan taze okunmuş `ben`'dir (hata → `null`).
   * Ağ/sunucu hatası oturumun bittiğini GÖSTERMEZ: mevcut `ben` (sinyal) korunur (#330 L4 — `null`a çekmek kimliği değiştirir, para denemelerinin kimlik efekti sekmedeki donmuş denemeleri
   * düşürür; kullanıcı tutarı yeni anahtarla yeniden girip çift kayıt yazabilirdi). Açılışta değer zaten `null`.
   * Diğer hatalar (`oturum_yok`, `kiraci_kapali` …) oturumu kapatır.
   */
  async yukle(): Promise<Ben | null> {
    try {
      const ben = await firstValueFrom(
        this.api.get<Ben>('/api/ui/v1/oturum/ben', sessionRequest()),
      );
      this.setMe(ben);
      return ben;
    } catch (error: unknown) {
      const apiError = toApiError(error);
      if (apiError.kod === 'sunucu' || apiError.kod === 'ag') {
        this.toast.hata(apiError.detay);
        return null;
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
  async login(info: LoginCredentials): Promise<Ben> {
    await firstValueFrom(this.api.get<unknown>('/api/ui/v1/oturum/xsrf', sessionRequest()));
    const ben = await firstValueFrom(
      this.api.post<Ben>('/api/ui/v1/oturum/giris', info, sessionRequest()),
    );
    this.setMe(ben);
    return ben;
  }

  /** Çıkış: sunucu oturumu kapatılır (hata yutulur — yerel temizlik her durumda), giriş sayfasına. */
  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.api.post<unknown>('/api/ui/v1/oturum/cikis', {}, sessionRequest()));
    } catch {
      // Oturum zaten düşmüş ya da ağ yok: yerel temizlik yine yapılır.
    }
    this.clear();
    await this.router.navigate(['/giris'], {
      queryParams: { neden: 'cikis' },
      replaceUrl: true,
    });
  }

  /** Firma kapatıldı (`kiraci_kapali`): tam temizlik + mesajlı giriş sayfası. */
  async tenantClosed(): Promise<void> {
    this.clear();
    await this.router.navigate(['/giris'], {
      queryParams: { neden: 'kiraci_kapali' },
      replaceUrl: true,
    });
  }

  /**
   * Çıkışta çalışacak temizleyici kaydı (sekmeler F3.2, sorgu önbellekleri F3.4, açık diyaloglar).
   * Dönen fonksiyon kaydı siler.
   */
  registerCleanup(cleaner: () => void): () => void {
    this.cleaners.add(cleaner);
    return () => this.cleaners.delete(cleaner);
  }

  /** Oturuma bağlı her şeyi siler; sunucuya gitmez. */
  clear(): void {
    for (const cleaner of [...this.cleaners]) {
      try {
        cleaner();
      } catch (error: unknown) {
        console.error(error);
      }
    }
    this.deger.set(null);
    this.keyIdentities.clear();
    this.initialLoadPromise = Promise.resolve(null);
    this.tema.applyTenantColors(null);
    this.toast.clear();
    this.bant.kapat();
    this.clearStore(() => this.window?.localStorage, THEME_KEY);
    this.clearStore(() => this.window?.sessionStorage, null);
  }

  private setMe(ben: Ben): void {
    this.deger.set(ben);
    this.initialLoadPromise = Promise.resolve(ben);
    this.tema.applyTenantColors(ben.renkler);
    // F13 sonrası: "pilot değil" bandı kalktı — sunucu pilot kapısını kaldırdı (`ben.pilot` hep true).
  }

  private clearStore(getStore: () => Storage | undefined, preserved: string | null): void {
    try {
      const store = getStore(); // erişimin kendisi de fırlatabilir (engelli depolama)
      if (!store) return;
      const toDelete: string[] = [];
      for (let i = 0; i < store.length; i++) {
        const key = store.key(i);
        if (key?.startsWith(STORE_PREFIX) && key !== preserved) toDelete.push(key);
      }
      for (const key of toDelete) store.removeItem(key);
    } catch {
      // Engelli depolama: silinecek bir şey de yok.
    }
  }
}

/** `ApiHatasi` mi ve kodu bu mu (tip daraltmalı yardımcı). */
export function isErrorCode(error: unknown, code: ApiHatasi['kod']): error is ApiHatasi {
  return error instanceof ApiHatasi && error.kod === code;
}
