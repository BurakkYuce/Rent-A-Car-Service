import type { Route, Routes } from '@angular/router';

import { SHELL_MARKER } from '@core/sekme/tab-key';

import { PAGES } from '../sayfalar';
import { Kabuk } from './kabuk';
import { NotFoundPage } from './not-found/not-found-page';
import { tabLimitGuard } from './sekmeler/sekme-siniri';

/**
 * Bilinmeyen adres: gerçek 404 sayfası (adres korunur, Panel'e dönüş bağlantısı). Sekme açmaz. Bileşen küçük ve
 * kabuk parçasında (ayrı tembel parça gereksiz). Kök `''` ise `sayfalar.ts`'te Panel'e yönlenir.
 */
export const NOT_FOUND_ROUTE: Route = {
  path: '**',
  title: 'Sayfa bulunamadı — RentACar',
  data: { sekme: false },
  component: NotFoundPage,
};

/** Kabuk (tembel): menü + üst çubuk + sekmeler; sayfalar çocuk rota. Bilinmeyen adres 404 sayfası. */
export const SHELL_ROUTES: Routes = [
  {
    path: '',
    component: Kabuk,
    data: { [SHELL_MARKER]: true },
    canActivateChild: [tabLimitGuard],
    children: [...PAGES, NOT_FOUND_ROUTE],
  },
];
