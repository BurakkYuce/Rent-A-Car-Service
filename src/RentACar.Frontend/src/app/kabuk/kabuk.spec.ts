import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, RouteReuseStrategy, Router, type Routes } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom, of } from 'rxjs';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import {
  unsavedChangesGuard,
  CONFIRM_PROMPT,
  FULL_PAGE_NAVIGATION,
} from '@core/form/kaydedilmemis-degisiklik';
import { provideTranslation } from '@core/i18n/ceviri';
import { SESSION_CONTEXT } from '@core/oturum/oturum-baglami';
import { SessionService } from '@core/oturum/session-service';
import type { Ben } from '@core/oturum/oturum-tipleri';
import { ShellCounters } from '@core/sayac/shell-counters';
import { TabRouteStrategy } from '@core/sekme/sekme-stratejisi';

import { Kabuk } from './kabuk';
import { SAMPLE_MENU } from './menu/menu-ornegi';
import { tabLimitGuard } from './sekmeler/sekme-siniri';
import { SHELL_PAGES, TabService } from './sekmeler/tab-service';

let formDirty = false;

@Component({
  selector: 'rc-deneme-bos',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<h1>Ana</h1>',
})
class EmptyPage {}

@Component({
  selector: 'rc-deneme-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<h1>Form</h1>',
})
class FormPage {
  hasUnsavedChanges(): boolean {
    return formDirty;
  }
}

const PAGES: Routes = [
  { path: '', pathMatch: 'full', component: EmptyPage, title: 'Ana — RentACar' },
  {
    path: 'vitrin/form',
    component: FormPage,
    title: 'Form vitrini — RentACar',
    canDeactivate: [unsavedChangesGuard],
  },
  { path: 'vitrin/tablo', component: EmptyPage, title: 'Tablo vitrini — RentACar' },
  { path: 'araclar', component: EmptyPage, title: 'Araçlar — RentACar' },
  { path: 'kiralar', component: EmptyPage, title: 'Kiralar — RentACar' },
];

const BEN: Ben = {
  kullanici: { id: 'u-1', kullaniciAdi: 'ayse', adSoyad: 'Ayşe Yılmaz' },
  kiraci: { id: 't-1', kod: 'pilot', ad: 'Pilot Firma' },
  rol: 'Admin',
  izinler: [],
  subeKapsami: { tumSubeler: true, subeId: null, subeAd: null },
  moduller: { webSitesi: false },
  renkler: {},
  pilot: true,
};

describe('Kabuk', () => {
  const getMenu = vi.fn(() => of(SAMPLE_MENU));
  const logout = vi.fn(() => Promise.resolve());
  const navigate = vi.fn<(address: string) => void>();
  let confirmResponse: boolean;
  let questions: string[];

  beforeEach(async () => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-theme');
    formDirty = false;
    confirmResponse = false;
    questions = [];
    getMenu.mockClear();
    logout.mockClear();
    navigate.mockClear();
    TestBed.configureTestingModule({
      providers: [
        ...provideTranslation(),
        provideRouter([
          {
            path: '',
            component: Kabuk,
            data: { kabuk: true },
            canActivateChild: [tabLimitGuard],
            children: PAGES,
          },
        ]),
        { provide: RouteReuseStrategy, useExisting: TabRouteStrategy },
        { provide: SHELL_PAGES, useValue: PAGES },
        { provide: ApiIstemcisi, useValue: { get: getMenu } },
        { provide: SESSION_CONTEXT, useValue: signal({ anahtar: 't-1|u-1|*' }).asReadonly() },
        {
          provide: SessionService,
          useValue: {
            ben: signal(BEN).asReadonly(),
            logout: logout,
            registerCleanup: () => () => undefined,
          },
        },
        { provide: CONFIRM_PROMPT, useValue: (m: string) => (questions.push(m), confirmResponse) },
        { provide: FULL_PAGE_NAVIGATION, useValue: navigate },
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  afterEach(() => TestBed.inject(TabService).clear());

  async function open(url = '/'): Promise<{ h: RouterTestingHarness; kok: HTMLElement }> {
    const h = await RouterTestingHarness.create();
    await h.navigateByUrl(url);
    await h.fixture.whenStable();
    return { h, kok: h.fixture.nativeElement as HTMLElement };
  }

  const link = (root: HTMLElement, name: string) =>
    [...root.querySelectorAll<HTMLAnchorElement>('rc-yan-menu a')].find(
      (a) => a.querySelector('.oge__etiket')?.textContent?.trim() === name,
    );
  const groupButton = (root: HTMLElement, name: string) =>
    [...root.querySelectorAll<HTMLButtonElement>('rc-yan-menu .grup__baslik')].find(
      (b) => b.textContent?.trim() === name,
    );

  it('menüyü sunucu yanıtından çizer: hızlı bağlantı, gruplar, rozet; sunucunun göndermediği öğe YOK', async () => {
    const { kok } = await open();
    expect(getMenu).toHaveBeenCalledWith('/api/ui/v1/menu', expect.anything());
    const links = kok.querySelectorAll('rc-yan-menu a');
    expect(links).toHaveLength(SAMPLE_MENU.ogeler.length);
    expect(
      [...kok.querySelectorAll('rc-yan-menu .grup__baslik')].map((b) => b.textContent?.trim()),
    ).toEqual(['Araçlar', 'Vitrin', 'Kira', 'Servis & Sigorta', 'Tanımlar']);
    // Kısayol çifti sunucunun hızlı bağlantısından: görünen "Rezervasyon", erişilebilir ad sunucu etiketi.
    const res = kok.querySelector<HTMLAnchorElement>('rc-yan-menu .kisayol a');
    expect(res?.getAttribute('aria-label')).toBe('Yeni Rezervasyon');
    expect(res?.textContent?.trim()).toBe('Rezervasyon');
    expect(res?.getAttribute('href')).toBe('/rezervasyonlar');
    expect(link(kok, 'Bildirimler')?.textContent).toMatch(/3\s*yeni/);
    // İzin süzmesi sunucuda: Finans (FinanceWrite) yanıtta yok → menüde de yok, istemci eklemez.
    expect(kok.textContent).not.toContain('Faturalar');
    // Blazor öğesi sunucu adresine, SPA öğesi /app altındaki adrese bağlanır.
    expect(link(kok, 'Kiralar')?.getAttribute('href')).toBe('/kiralar');
    expect(link(kok, 'Kiralar')?.getAttribute('aria-describedby')).toBe('rc-menu-eski-ekran');
    expect(link(kok, 'Form vitrini')?.getAttribute('href')).toBe('/vitrin/form');
  });

  it('etkin sayfa aria-current="page" ile işaretlenir, grubu kendiliğinden açılır', async () => {
    const { h, kok } = await open('/vitrin/form');
    expect(link(kok, 'Form vitrini')?.getAttribute('aria-current')).toBe('page');
    expect(link(kok, 'Form vitrini')?.classList).toContain('oge--etkin');
    expect(groupButton(kok, 'Vitrin')?.getAttribute('aria-expanded')).toBe('true');
    expect(groupButton(kok, 'Kira')?.getAttribute('aria-expanded')).toBe('false');
    expect(kok.querySelectorAll('rc-yan-menu [aria-current="page"]')).toHaveLength(1);

    link(kok, 'Tablo vitrini')?.click();
    await h.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/vitrin/tablo');
    expect(link(kok, 'Tablo vitrini')?.getAttribute('aria-current')).toBe('page');
    expect(link(kok, 'Form vitrini')?.hasAttribute('aria-current')).toBe(false);
  });

  it('Blazor öğesi tam sayfa açılır; kirli form varsa önce sorulur, vazgeçilirse gidilmez', async () => {
    const { h, kok } = await open('/vitrin/form');
    formDirty = true;
    link(kok, 'Kiralar')?.click();
    await h.fixture.whenStable();
    expect(questions).toHaveLength(1);
    expect(navigate).not.toHaveBeenCalled();

    confirmResponse = true;
    link(kok, 'Kiralar')?.click();
    await h.fixture.whenStable();
    expect(navigate).toHaveBeenCalledWith('/kiralar');

    formDirty = false;
    questions = [];
    link(kok, 'Panel')?.click();
    await h.fixture.whenStable();
    expect(questions).toEqual([]);
    expect(navigate).toHaveBeenLastCalledWith('/');
  });

  it('Ctrl+K paleti açar; "is" yazınca İş Emirleri bulunur, Enter açar (Blazor → tam sayfa)', async () => {
    const { h } = await open();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true }));
    await vi.waitFor(() => expect(document.querySelector('rc-komut-paleti input')).not.toBeNull());
    const input = document.querySelector<HTMLInputElement>('rc-komut-paleti input');
    if (!input) throw new Error('palet girdisi yok');
    await vi.waitFor(() => expect(document.activeElement).toBe(input));

    input.value = 'is';
    input.dispatchEvent(new Event('input'));
    await h.fixture.whenStable();
    const options = [...document.querySelectorAll('rc-komut-paleti [role="option"]')];
    // Etiket eşleşmesi önce; "Kısa Yollar" grubundaki öğe yalnız grup adıyla eşleştiği için sonda.
    expect(options.map((s) => s.querySelector('.palet__etiket')?.textContent?.trim())).toEqual([
      'İş Emirleri',
      'Işık Raporu',
      'Yeni Rezervasyon',
    ]);
    expect(options[0]?.getAttribute('aria-selected')).toBe('true');
    expect(input.getAttribute('aria-activedescendant')).toBe(options[0]?.id);

    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown' }));
    await h.fixture.whenStable();
    expect(input.getAttribute('aria-activedescendant')).toBe('rc-palet-1');
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp' }));
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith('/is-emirleri'));
    await vi.waitFor(() => expect(document.querySelector('rc-komut-paleti')).toBeNull());
  });

  it('palette SPA öğesi router ile açılır ve sekme olur', async () => {
    const { h } = await open();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'K', metaKey: true }));
    await vi.waitFor(() => expect(document.querySelector('rc-komut-paleti input')).not.toBeNull());
    const input = document.querySelector<HTMLInputElement>('rc-komut-paleti input');
    if (!input) throw new Error('palet girdisi yok');
    input.value = 'tablo';
    input.dispatchEvent(new Event('input'));
    await h.fixture.whenStable();
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/vitrin/tablo'));
    expect(navigate).not.toHaveBeenCalled();
    expect(
      TestBed.inject(TabService)
        .tabs()
        .map((s) => s.desen),
    ).toEqual(['/', '/vitrin/tablo']);
  });

  it('kenar çubuğu altı: firma, kullanıcı kartı (baş harf, rol · şube), şube; çıkış kirli formda önce sorar', async () => {
    const { h, kok } = await open('/vitrin/form');
    expect(kok.querySelector('.yan__firma')?.textContent?.trim()).toBe('Pilot Firma');
    expect(kok.querySelector('.avatar')?.textContent?.trim()).toBe('AY');
    expect(kok.querySelector('.kullanici__ad')?.textContent?.trim()).toBe('Ayşe Yılmaz');
    expect(kok.querySelector('.kullanici__rol')?.textContent?.trim()).toBe('Admin · Tüm şubeler');
    expect(kok.querySelector('.sube')?.textContent).toContain('Tüm şubeler');
    // Kullanıcı bloğu üst çubukta tekrar edilmez.
    expect(kok.querySelector('rc-ust-cubuk')?.textContent).not.toContain('Ayşe Yılmaz');

    const pickup = kok.querySelector<HTMLButtonElement>(
      '#rc-yan-menu button[aria-label="Çıkış yap"]',
    );
    if (!pickup) throw new Error('çıkış düğmesi yok');
    formDirty = true;
    pickup.click();
    await h.fixture.whenStable();
    expect(questions).toHaveLength(1);
    expect(logout).not.toHaveBeenCalled();
    confirmResponse = true;
    pickup.click();
    await vi.waitFor(() => expect(logout).toHaveBeenCalledTimes(1));
  });

  it('üst çubuk: tema üçlüsü; zil okunmamış sayıyla bildirim öğesini açar', async () => {
    const { h, kok } = await open();
    const theme = (name: string) =>
      kok.querySelector<HTMLButtonElement>(`rc-ust-cubuk button[aria-label="${name}"]`);
    theme('Koyu tema')?.click();
    await h.fixture.whenStable();
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(theme('Koyu tema')?.getAttribute('aria-pressed')).toBe('true');
    theme('Sistem teması')?.click();
    await h.fixture.whenStable();
    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);

    const zil = theme('Bildirimler (3 okunmamış)');
    expect(zil?.querySelector('.zil__rozet')?.textContent?.trim()).toBe('3');
    zil?.click();
    await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith('/bildirimler'));
  });

  it('akordeon: tek grup açık, açık grup kalıcı (rc.menu.acik)', async () => {
    const { h, kok } = await open();
    groupButton(kok, 'Kira')?.click();
    await h.fixture.whenStable();
    expect(groupButton(kok, 'Kira')?.getAttribute('aria-expanded')).toBe('true');
    groupButton(kok, 'Araçlar')?.click();
    await h.fixture.whenStable();
    expect(groupButton(kok, 'Araçlar')?.getAttribute('aria-expanded')).toBe('true');
    expect(groupButton(kok, 'Kira')?.getAttribute('aria-expanded')).toBe('false');
    expect(localStorage.getItem('rc.menu.acik')).toBe('Araçlar');
    groupButton(kok, 'Araçlar')?.click();
    await h.fixture.whenStable();
    expect(kok.querySelectorAll('rc-yan-menu .grup__baslik[aria-expanded="true"]')).toHaveLength(0);
    expect(localStorage.getItem('rc.menu.acik')).toBeNull();
  });

  it('daralt: 56 px şerit kalıcı (rc.kabuk.dar); şeritte gruba tıklamak menüyü genişletip grubu açar', async () => {
    const { h, kok } = await open();
    const collapse = kok.querySelector<HTMLButtonElement>('.daralt-dugmesi');
    expect(collapse?.getAttribute('aria-label')).toBe('Menüyü daralt');
    collapse?.click();
    await h.fixture.whenStable();
    expect(kok.querySelector('.kabuk')?.classList).toContain('kabuk--dar');
    expect(localStorage.getItem('rc.kabuk.dar')).toBe('1');
    expect(collapse?.getAttribute('aria-label')).toBe('Menüyü genişlet');
    // Etiket görsel olarak gizli, erişilebilir ad duruyor.
    expect(groupButton(kok, 'Kira')?.getAttribute('title')).toBe('Kira');

    groupButton(kok, 'Kira')?.click();
    await h.fixture.whenStable();
    expect(kok.querySelector('.kabuk')?.classList).not.toContain('kabuk--dar');
    expect(groupButton(kok, 'Kira')?.getAttribute('aria-expanded')).toBe('true');
    expect(localStorage.getItem('rc.kabuk.dar')).toBeNull();
  });

  it('Kira grubu: SPA kira listesi kayıtlı görünümlerle; sayaçlar Panel kaynağından, geciken kırmızı', async () => {
    getMenu.mockReturnValueOnce(
      of({
        ...SAMPLE_MENU,
        ogeler: SAMPLE_MENU.ogeler.map((o) =>
          o.rota === '/kiralar' ? { ...o, rota: '/app/kiralar', sahip: 'spa' } : o,
        ),
      }),
    );
    TestBed.inject(ShellCounters).publish({
      kirada: 23,
      geciken: 3,
      bugunCikan: 1,
      bugunDonecek: 0,
    });
    const { h, kok } = await open('/kiralar?gorunum=geciken');
    expect(groupButton(kok, 'Kira')?.getAttribute('aria-expanded')).toBe('true');
    const view = (name: string) =>
      [...kok.querySelectorAll<HTMLAnchorElement>('rc-yan-menu .oge--alt')].find(
        (a) => a.querySelector('.oge__etiket')?.textContent?.trim() === name,
      );
    expect(
      [...kok.querySelectorAll('rc-yan-menu .oge--alt .oge__etiket')].map((e) =>
        e.textContent?.trim(),
      ),
    ).toEqual([
      'Tüm sözleşmeler',
      'Kiradaki araçlar',
      'Dönüşü gecikenler',
      'Bugün çıkanlar',
      'Bugün dönecekler',
      'Faturası kesilmeyenler',
      'Kapalı sözleşmeler',
    ]);
    expect(view('Tüm sözleşmeler')?.getAttribute('href')).toBe('/kiralar');
    expect(view('Kiradaki araçlar')?.getAttribute('href')).toBe('/kiralar?gorunum=kirada');
    expect(view('Kiradaki araçlar')?.textContent).toMatch(/23\s*kayıt/);
    expect(view('Dönüşü gecikenler')?.querySelector('.oge__rozet--hata')?.textContent).toMatch(
      /^3/,
    );
    expect(view('Bugün dönecekler')?.querySelector('.oge__rozet')?.textContent).toMatch(/^0/);
    expect(view('Faturası kesilmeyenler')?.querySelector('.oge__rozet')).toBeNull();
    // Etkin: sorgudaki görünüm (tek aria-current).
    expect(view('Dönüşü gecikenler')?.getAttribute('aria-current')).toBe('page');
    expect(kok.querySelectorAll('rc-yan-menu [aria-current="page"]')).toHaveLength(1);

    view('Kiradaki araçlar')?.click();
    await h.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/kiralar?gorunum=kirada');
    expect(view('Kiradaki araçlar')?.getAttribute('aria-current')).toBe('page');
  });

  it('plaka arama: Enter araç listesine plaka süzgeciyle gider; boş aramada gezinme yok', async () => {
    const { h, kok } = await open();
    const input = kok.querySelector<HTMLInputElement>('rc-ust-cubuk rc-plaka-arama input');
    if (!input) throw new Error('plaka arama yok');
    // Erişilebilir ad "Plaka" içermez (e2e getByLabel('Plaka') form alanlarını hedefler).
    expect(input.labels?.[0]?.textContent?.trim()).toBe('Hızlı araç arama');
    const enter = () =>
      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', cancelable: true }));
    input.value = '   ';
    enter();
    await h.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/');

    input.value = ' 07  bkl 496 ';
    enter();
    await h.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/araclar?q=07%20bkl%20496');
    expect(input.value).toBe('');
  });

  it('mobil çekmece: menü düğmesi açar (odak içeride, arka plan inert), Esc kapatır ve odağı geri verir', async () => {
    const old = window.matchMedia;
    window.matchMedia = ((query: string) => ({
      matches: query.includes('max-width: 900px'),
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    })) as unknown as typeof window.matchMedia;
    try {
      const { h, kok: root } = await open();
      const yan = root.querySelector('#rc-yan-menu') as HTMLElement;
      const main = root.querySelector('.ana') as HTMLElement;
      const button = root.querySelector('.menu-dugmesi') as HTMLButtonElement;
      expect(yan.hasAttribute('inert')).toBe(true);
      expect(button.getAttribute('aria-expanded')).toBe('false');

      button.click();
      await h.fixture.whenStable();
      expect(yan.hasAttribute('inert')).toBe(false);
      expect(main.hasAttribute('inert')).toBe(true);
      expect(button.getAttribute('aria-expanded')).toBe('true');
      await vi.waitFor(() => expect(yan.contains(document.activeElement)).toBe(true));

      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
      await h.fixture.whenStable();
      expect(yan.hasAttribute('inert')).toBe(true);
      expect(main.hasAttribute('inert')).toBe(false);
      expect(document.activeElement).toBe(button);
    } finally {
      window.matchMedia = old;
    }
  });
});
