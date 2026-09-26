import type { Routes } from '@angular/router';

import { SHELL_MARKER } from '@core/sekme/tab-key';

import { PAGES } from '../sayfalar';
import { Kabuk } from './kabuk';
import { tabLimitGuard } from './sekmeler/sekme-siniri';

/** Kabuk (tembel): menü + üst çubuk + sekmeler; sayfalar çocuk rota. Bilinmeyen adres ana sayfaya. */
export const SHELL_ROUTES: Routes = [
  {
    path: '',
    component: Kabuk,
    data: { [SHELL_MARKER]: true },
    canActivateChild: [tabLimitGuard],
    children: [...PAGES, { path: '**', redirectTo: '' }],
  },
];
