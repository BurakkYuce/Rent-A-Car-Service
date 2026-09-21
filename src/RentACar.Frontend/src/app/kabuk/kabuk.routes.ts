import type { Routes } from '@angular/router';

import { KABUK_ISARETI } from '@core/sekme/sekme-anahtari';

import { SAYFALAR } from '../sayfalar';
import { Kabuk } from './kabuk';
import { sekmeSiniriGuard } from './sekmeler/sekme-siniri';

/** Kabuk (tembel): menü + üst çubuk + sekmeler; sayfalar çocuk rota. Bilinmeyen adres ana sayfaya. */
export const KABUK_ROTALARI: Routes = [
  {
    path: '',
    component: Kabuk,
    data: { [KABUK_ISARETI]: true },
    canActivateChild: [sekmeSiniriGuard],
    children: [...SAYFALAR, { path: '**', redirectTo: '' }],
  },
];
