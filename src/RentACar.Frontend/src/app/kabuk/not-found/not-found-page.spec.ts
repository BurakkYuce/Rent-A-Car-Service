import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, type Routes } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideTranslation } from '@core/i18n/ceviri';

import { HOME_REDIRECT } from '../../sayfalar';
import { NOT_FOUND_ROUTE } from '../kabuk.routes';
import { NotFoundPage } from './not-found-page';

@Component({
  selector: 'rc-sahte-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<h1>Panel</h1>',
})
class FakePanel {}

/** Kabuğun kök ve "bilinmeyen adres" kuralı — gerçek rota nesneleriyle, sahte Panel bileşeniyle. */
const ROUTES: Routes = [HOME_REDIRECT, { path: 'panel', component: FakePanel }, NOT_FOUND_ROUTE];

describe('404 sayfası ve kabuk kökü', () => {
  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [...provideTranslation(), provideRouter(ROUTES)],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  it('başlık "Sayfa bulunamadı", tek h1 ve Panel’e dönüş bağlantısı; yer tutucu/vitrin bağlantısı yok', async () => {
    const fixture = TestBed.createComponent(NotFoundPage);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;

    const headings = root.querySelectorAll('h1');
    expect(headings.length).toBe(1);
    expect(headings[0]?.textContent?.trim()).toBe('Sayfa bulunamadı');
    const links = [...root.querySelectorAll('a')];
    expect(links.map((a) => [a.getAttribute('href'), a.textContent?.trim()])).toEqual([
      ['/panel', "Panel'e dön"],
    ]);
    expect(root.textContent).not.toContain('yapım aşamasında');
    expect(root.textContent).not.toContain('vitrin');
  });

  it('kök Panel’e gider; bilinmeyen adres adresini koruyarak 404 sayfasını gösterir', async () => {
    const harness = await RouterTestingHarness.create();
    const router = TestBed.inject(Router);

    await harness.navigateByUrl('/');
    expect(router.url).toBe('/panel');

    const page = await harness.navigateByUrl('/olmayan-sayfa', NotFoundPage);
    expect(page).toBeInstanceOf(NotFoundPage);
    expect(router.url).toBe('/olmayan-sayfa');

    await harness.navigateByUrl('/kiralar/olmayan/alt?x=1');
    expect(router.url).toBe('/kiralar/olmayan/alt?x=1');
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent?.trim()).toBe(
      'Sayfa bulunamadı',
    );
  });

  it('404 sekme açmaz (tek seferlik sayfa) ve başlığı Türkçe', () => {
    expect(NOT_FOUND_ROUTE.path).toBe('**');
    expect(NOT_FOUND_ROUTE.data).toEqual({ sekme: false });
    expect(NOT_FOUND_ROUTE.title).toBe('Sayfa bulunamadı — RentACar');
  });
});
