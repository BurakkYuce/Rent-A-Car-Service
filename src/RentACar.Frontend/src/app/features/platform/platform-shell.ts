import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { Icon } from '@shared/ikon/icon';

import { PlatformSessionService } from './platform-session';

/**
 * Platform operator layout (Blazor `PlatformLayout` parity): brand, console navigation, operator name,
 * sign-out. NO tenant sidebar, menu, tabs or command palette — the two authority domains stay apart.
 * Pages render their own `rc-sayfa-bandi` (the single `<h1>`) + `.rc-sayfa` body, so the outlet has no padding.
 */
@Component({
  selector: 'rc-platform-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslocoPipe, Icon],
  template: `
    <header class="top">
      <a class="brand" routerLink="/platform">
        {{ 'uygulama.ad' | transloco }} · <b>{{ 'platform.kabuk.platform' | transloco }}</b>
      </a>
      <nav class="nav" [attr.aria-label]="'platform.kabuk.gezinme' | transloco">
        <a
          routerLink="/platform"
          routerLinkActive="active"
          [routerLinkActiveOptions]="{ exact: true }"
          ariaCurrentWhenActive="page"
          >{{ 'platform.kabuk.ozet' | transloco }}</a
        >
        <a
          routerLink="/platform/kiracilar"
          routerLinkActive="active"
          ariaCurrentWhenActive="page"
          >{{ 'platform.kabuk.firmalar' | transloco }}</a
        >
        <a routerLink="/platform/belgeler" routerLinkActive="active" ariaCurrentWhenActive="page">{{
          'platform.kabuk.belgeler' | transloco
        }}</a>
      </nav>
      <span class="spacer"></span>
      <span class="user" data-testid="platform-kullanici">{{ session.session()?.kullanici }}</span>
      <button type="button" class="rc-dugme rc-dugme--hayalet rc-dugme--kucuk" (click)="signOut()">
        <rc-ikon ad="logout" [boyut]="14" />
        {{ 'platform.kabuk.cikis' | transloco }}
      </button>
    </header>
    <main class="body">
      <router-outlet />
    </main>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      min-height: 100vh;
      background-color: var(--rc-zemin);
    }
    .top {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--rc-bosluk-2) var(--rc-bosluk-4);
      padding: var(--rc-bosluk-2) var(--rc-bosluk-4);
      border-bottom: 1px solid var(--rc-kenar);
      background-color: var(--rc-yuzey);
    }
    .brand {
      color: var(--rc-metin);
      text-decoration: none;
      white-space: nowrap;
    }
    .nav {
      display: flex;
      flex-wrap: wrap;
      gap: var(--rc-bosluk-1) var(--rc-bosluk-3);
    }
    .nav a {
      color: var(--rc-metin-ikincil);
      text-decoration: none;
      padding: var(--rc-bosluk-1) 0;
      border-bottom: 2px solid transparent;
    }
    .nav a.active {
      color: var(--rc-metin);
      border-bottom-color: var(--rc-vurgu);
    }
    .spacer {
      flex: 1;
    }
    .user {
      color: var(--rc-metin-ikincil);
      overflow-wrap: anywhere;
    }
    .body {
      flex: 1;
      width: 100%;
      max-width: 90rem;
      margin: 0 auto;
      min-width: 0;
    }
  `,
})
export class PlatformShell {
  protected readonly session = inject(PlatformSessionService);

  protected signOut(): void {
    void this.session.signOut();
  }
}
