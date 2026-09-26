import { computed, inject, Injectable, signal } from '@angular/core';
import { CanMatchFn, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { requestContext } from '@core/oturum/request-context';
import { SessionService } from '@core/oturum/session-service';

import { PLATFORM_API, type PlatformSession } from './platform-model';

/** Session calls report their own errors to the caller: no band, no re-login dialog. */
function sessionRequest() {
  return { context: requestContext({ sessiz: true, yenidenGirisYok: true }) };
}

/** SPA route of the platform login (router path, without `/app`). */
export const PLATFORM_LOGIN_ROUTE = '/platform/giris';
/** Landing page after login. */
export const PLATFORM_HOME_ROUTE = '/platform';

/**
 * Platform operator session — a SEPARATE authority domain from the tenant session (`OturumServisi`).
 * The server decides: `GET platform/oturum/ben` answers only for a cookie carrying `platform_admin=true`;
 * a tenant user (Admin included) gets 403 `yetki_yok`, anonymous 401. Both mean "no platform session"
 * here — this class never looks at tenant claims, and the tenant shell/menu never loads on these routes.
 *
 * Login replaces the one `racar.session` cookie, so any tenant state still in memory is wiped
 * (`OturumServisi.temizle`) — the platform screens must never show a previous tenant user's data.
 */
@Injectable({ providedIn: 'root' })
export class PlatformSessionService {
  private readonly api = inject(ApiIstemcisi);
  private readonly router = inject(Router);
  private readonly tenantSession = inject(SessionService);

  private readonly value = signal<PlatformSession | null>(null);
  private firstLoad: Promise<PlatformSession | null> | null = null;

  readonly session = this.value.asReadonly();
  readonly signedIn = computed(() => this.value() !== null);

  /** Loaded once per app start (guards await it). Any error = no platform session. */
  load(): Promise<PlatformSession | null> {
    this.firstLoad ??= this.fetch();
    return this.firstLoad;
  }

  private async fetch(): Promise<PlatformSession | null> {
    try {
      const me = await firstValueFrom(
        this.api.get<PlatformSession>(`${PLATFORM_API}/oturum/ben`, sessionRequest()),
      );
      this.set(me);
      return me;
    } catch {
      this.set(null);
      return null;
    }
  }

  /**
   * Anonymous XSRF token first (`GET oturum/xsrf`, open to every session kind), then
   * `POST platform/oturum/giris`; the server issues a new token bound to the new principal.
   * Errors are thrown as `ApiHatasi` — the page picks the message (`loginErrorMessage`).
   */
  async signIn(user: string, password: string): Promise<PlatformSession> {
    await firstValueFrom(this.api.get<unknown>('/api/ui/v1/oturum/xsrf', sessionRequest()));
    const me = await firstValueFrom(
      this.api.post<PlatformSession>(
        `${PLATFORM_API}/oturum/giris`,
        { kullanici: user, sifre: password },
        sessionRequest(),
      ),
    );
    this.tenantSession.clear();
    this.set(me);
    return me;
  }

  /** Server sign-out (idempotent; errors swallowed — local cleanup always happens), then login page. */
  async signOut(): Promise<void> {
    try {
      await firstValueFrom(
        this.api.post<unknown>(`${PLATFORM_API}/oturum/cikis`, {}, sessionRequest()),
      );
    } catch {
      // Session already gone or network down: local cleanup still runs.
    }
    this.set(null);
    await this.router.navigate([PLATFORM_LOGIN_ROUTE], {
      queryParams: { neden: 'cikis' },
      replaceUrl: true,
    });
  }

  /**
   * A platform API call answered 401 `oturum_yok` (cookie expired / signed out in another tab):
   * drop the session and go to the login page. Returns whether it handled the error.
   */
  handleSessionLoss(error: unknown): boolean {
    if (toApiError(error).kod !== 'oturum_yok') return false;
    this.set(null);
    void this.router.navigate([PLATFORM_LOGIN_ROUTE], {
      queryParams: { neden: 'oturum' },
      replaceUrl: true,
    });
    return true;
  }

  private set(me: PlatformSession | null): void {
    this.value.set(me);
    this.firstLoad = Promise.resolve(me);
  }
}

/** Login error → message. `dogrulama` is ALWAYS generic (which field was wrong is not disclosed). */
export function loginErrorMessage(error: unknown): CeviriAnahtari {
  switch (toApiError(error).kod) {
    case 'dogrulama':
      return 'platform.giris.hata.hatali';
    case 'cok_istek':
      return 'platform.giris.hata.cokIstek';
    case 'ag':
      return 'platform.giris.hata.ag';
    default:
      return 'platform.giris.hata.genel';
  }
}

/** Platform screens (canMatch): no platform session → platform login. UX only; the API is the boundary. */
export const platformSessionGuard: CanMatchFn = async () => {
  const session = inject(PlatformSessionService);
  const router = inject(Router);
  await session.load();
  return session.signedIn() || router.parseUrl(PLATFORM_LOGIN_ROUTE);
};

/** Platform login (canMatch): an operator already signed in goes to the console. */
export const platformGuestGuard: CanMatchFn = async () => {
  const session = inject(PlatformSessionService);
  const router = inject(Router);
  await session.load();
  return !session.signedIn() || router.parseUrl(PLATFORM_HOME_ROUTE);
};
