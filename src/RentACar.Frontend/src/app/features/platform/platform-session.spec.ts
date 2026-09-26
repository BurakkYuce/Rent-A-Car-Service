import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, UrlTree } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { ApiHatasi } from '@core/api/api-hatasi';
import { provideApiClient } from '@core/api/api-istemcisi';
import { WarningBannerService } from '@core/geri-bildirim/warning-banner-service';
import { provideTranslation } from '@core/i18n/ceviri';
import { sessionInterceptor } from '@core/oturum/session-interceptor';
import { SessionService } from '@core/oturum/session-service';

import {
  loginErrorMessage,
  platformGuestGuard,
  PlatformSessionService,
  platformSessionGuard,
} from './platform-session';

const BEN = '/api/ui/v1/platform/oturum/ben';

function problem(status: number, code: string) {
  return {
    body: { status, kod: code, detail: 'x' },
    opts: { status, statusText: 'x', headers: { 'content-type': 'application/problem+json' } },
  };
}

describe('PlatformSessionService', () => {
  let http: HttpTestingController;
  let session: PlatformSessionService;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        ...provideTranslation(),
        provideRouter([]),
        provideApiClient(sessionInterceptor),
        provideHttpClientTesting(),
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
    http = TestBed.inject(HttpTestingController);
    session = TestBed.inject(PlatformSessionService);
  });

  afterEach(() => http.verify());

  const runGuard = (guard: typeof platformSessionGuard) =>
    TestBed.runInInjectionContext(() => guard({}, [])) as Promise<boolean | UrlTree>;

  it('tenant session (403 yetki_yok) = no platform session: guard sends to platform login, no band', async () => {
    const result = runGuard(platformSessionGuard);
    const p = problem(403, 'yetki_yok');
    http.expectOne(BEN).flush(p.body, p.opts);
    const tree = await result;
    expect(tree instanceof UrlTree && TestBed.inject(Router).serializeUrl(tree)).toBe(
      '/platform/giris',
    );
    expect(session.signedIn()).toBe(false);
    // Session probe is silent: a tenant user must not see a "no permission" band just for opening the URL.
    expect(TestBed.inject(WarningBannerService).bant()).toBeNull();
  });

  it('anonymous (401) → platform login; signed-in operator → guard passes, login page redirects home', async () => {
    const first = runGuard(platformSessionGuard);
    const p = problem(401, 'oturum_yok');
    http.expectOne(BEN).flush(p.body, p.opts);
    expect(await first).toBeInstanceOf(UrlTree);

    const login = session.signIn('admin', 'test-parolasi');
    http.expectOne('/api/ui/v1/oturum/xsrf').flush(null, { status: 204, statusText: 'No Content' });
    const post = await vi.waitFor(() => http.expectOne('/api/ui/v1/platform/oturum/giris'));
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ kullanici: 'admin', sifre: 'test-parolasi' });
    post.flush({ kullanici: 'admin' });
    await login;

    expect(await runGuard(platformSessionGuard)).toBe(true);
    const guest = await runGuard(platformGuestGuard);
    expect(guest instanceof UrlTree && TestBed.inject(Router).serializeUrl(guest)).toBe(
      '/platform',
    );
  });

  it('login wipes tenant state held in memory (the cookie was replaced)', async () => {
    const wipe = vi.spyOn(TestBed.inject(SessionService), 'clear');
    const login = session.signIn('admin', 'p');
    http.expectOne('/api/ui/v1/oturum/xsrf').flush(null, { status: 204, statusText: 'No Content' });
    (await vi.waitFor(() => http.expectOne('/api/ui/v1/platform/oturum/giris'))).flush({
      kullanici: 'admin',
    });
    await login;
    expect(wipe).toHaveBeenCalledTimes(1);
  });

  it('sign-out: server call, session cleared, login page with reason; server error still signs out locally', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const out = session.signOut();
    const p = problem(500, 'sunucu');
    http.expectOne('/api/ui/v1/platform/oturum/cikis').flush(p.body, p.opts);
    await out;
    expect(session.signedIn()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/platform/giris'], {
      queryParams: { neden: 'cikis' },
      replaceUrl: true,
    });
  });

  it('login error message is generic for validation (which field was wrong is not disclosed)', () => {
    const e = (code: 'dogrulama' | 'cok_istek') =>
      new ApiHatasi({ kod: code, status: 400, detay: 'Kullanıcı adı yanlış' });
    expect(loginErrorMessage(e('dogrulama'))).toBe('platform.giris.hata.hatali');
    expect(loginErrorMessage(e('cok_istek'))).toBe('platform.giris.hata.cokIstek');
    expect(loginErrorMessage(new Error('x'))).toBe('platform.giris.hata.genel');
  });
});
