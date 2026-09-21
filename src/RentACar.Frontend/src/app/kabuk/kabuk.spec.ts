import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, RouteReuseStrategy, Router, type Routes } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom, of } from 'rxjs';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import {
  kaydedilmemisDegisiklikGuard,
  ONAY_ISTEMI,
  TAM_SAYFA_GEZINMESI,
} from '@core/form/kaydedilmemis-degisiklik';
import { provideCeviri } from '@core/i18n/ceviri';
import { OTURUM_BAGLAMI } from '@core/oturum/oturum-baglami';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import type { Ben } from '@core/oturum/oturum-tipleri';
import { SekmeRotaStratejisi } from '@core/sekme/sekme-stratejisi';

import { Kabuk } from './kabuk';
import { ORNEK_MENU } from './menu/menu-ornegi';
import { sekmeSiniriGuard } from './sekmeler/sekme-siniri';
import { KABUK_SAYFALARI, SekmeServisi } from './sekmeler/sekme-servisi';

let formKirli = false;

@Component({
  selector: 'rc-deneme-bos',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<h1>Ana</h1>',
})
class BosSayfa {}

@Component({
  selector: 'rc-deneme-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<h1>Form</h1>',
})
class FormSayfasi {
  kaydedilmemisDegisiklikVar(): boolean {
    return formKirli;
  }
}

const SAYFALAR: Routes = [
  { path: '', pathMatch: 'full', component: BosSayfa, title: 'Ana — RentACar' },
  {
    path: 'vitrin/form',
    component: FormSayfasi,
    title: 'Form vitrini — RentACar',
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  { path: 'vitrin/tablo', component: BosSayfa, title: 'Tablo vitrini — RentACar' },
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
  const menuGetir = vi.fn(() => of(ORNEK_MENU));
  const cikisYap = vi.fn(() => Promise.resolve());
  const gezin = vi.fn<(adres: string) => void>();
  let onayYaniti: boolean;
  let sorular: string[];

  beforeEach(async () => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-theme');
    formKirli = false;
    onayYaniti = false;
    sorular = [];
    menuGetir.mockClear();
    cikisYap.mockClear();
    gezin.mockClear();
    TestBed.configureTestingModule({
      providers: [
        ...provideCeviri(),
        provideRouter([
          {
            path: '',
            component: Kabuk,
            data: { kabuk: true },
            canActivateChild: [sekmeSiniriGuard],
            children: SAYFALAR,
          },
        ]),
        { provide: RouteReuseStrategy, useExisting: SekmeRotaStratejisi },
        { provide: KABUK_SAYFALARI, useValue: SAYFALAR },
        { provide: ApiIstemcisi, useValue: { get: menuGetir } },
        { provide: OTURUM_BAGLAMI, useValue: signal({ anahtar: 't-1|u-1|*' }).asReadonly() },
        {
          provide: OturumServisi,
          useValue: {
            ben: signal(BEN).asReadonly(),
            cikisYap,
            temizlikKaydet: () => () => undefined,
          },
        },
        { provide: ONAY_ISTEMI, useValue: (m: string) => (sorular.push(m), onayYaniti) },
        { provide: TAM_SAYFA_GEZINMESI, useValue: gezin },
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  afterEach(() => TestBed.inject(SekmeServisi).temizle());

  async function ac(url = '/'): Promise<{ h: RouterTestingHarness; kok: HTMLElement }> {
    const h = await RouterTestingHarness.create();
    await h.navigateByUrl(url);
    await h.fixture.whenStable();
    return { h, kok: h.fixture.nativeElement as HTMLElement };
  }

  const baglanti = (kok: HTMLElement, ad: string) =>
    [...kok.querySelectorAll<HTMLAnchorElement>('rc-yan-menu a')].find(
      (a) => a.querySelector('.oge__etiket')?.textContent?.trim() === ad,
    );
  const grupDugmesi = (kok: HTMLElement, ad: string) =>
    [...kok.querySelectorAll<HTMLButtonElement>('rc-yan-menu .grup__baslik')].find(
      (b) => b.textContent?.trim() === ad,
    );

  it('menüyü sunucu yanıtından çizer: hızlı bağlantı, gruplar, rozet; sunucunun göndermediği öğe YOK', async () => {
    const { kok } = await ac();
    expect(menuGetir).toHaveBeenCalledWith('/api/ui/v1/menu', expect.anything());
    const baglantilar = kok.querySelectorAll('rc-yan-menu a');
    expect(baglantilar).toHaveLength(ORNEK_MENU.ogeler.length);
    expect(
      [...kok.querySelectorAll('rc-yan-menu .grup__baslik')].map((b) => b.textContent?.trim()),
    ).toEqual(['Araçlar', 'Vitrin', 'Kira', 'Servis & Sigorta', 'Tanımlar']);
    expect(kok.querySelector('.liste--hizli')?.textContent).toContain('Yeni Rezervasyon');
    expect(baglanti(kok, 'Bildirimler')?.textContent).toMatch(/3\s*yeni/);
    // İzin süzmesi sunucuda: Finans (FinanceWrite) yanıtta yok → menüde de yok, istemci eklemez.
    expect(kok.textContent).not.toContain('Faturalar');
    // Blazor öğesi sunucu adresine, SPA öğesi /app altındaki adrese bağlanır.
    expect(baglanti(kok, 'Kiralar')?.getAttribute('href')).toBe('/kiralar');
    expect(baglanti(kok, 'Kiralar')?.getAttribute('aria-describedby')).toBe('rc-menu-eski-ekran');
    expect(baglanti(kok, 'Form vitrini')?.getAttribute('href')).toBe('/vitrin/form');
  });

  it('etkin sayfa aria-current="page" ile işaretlenir, grubu kendiliğinden açılır', async () => {
    const { h, kok } = await ac('/vitrin/form');
    expect(baglanti(kok, 'Form vitrini')?.getAttribute('aria-current')).toBe('page');
    expect(baglanti(kok, 'Form vitrini')?.classList).toContain('oge--etkin');
    expect(grupDugmesi(kok, 'Vitrin')?.getAttribute('aria-expanded')).toBe('true');
    expect(grupDugmesi(kok, 'Kira')?.getAttribute('aria-expanded')).toBe('false');
    expect(kok.querySelectorAll('rc-yan-menu [aria-current="page"]')).toHaveLength(1);

    baglanti(kok, 'Tablo vitrini')?.click();
    await h.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/vitrin/tablo');
    expect(baglanti(kok, 'Tablo vitrini')?.getAttribute('aria-current')).toBe('page');
    expect(baglanti(kok, 'Form vitrini')?.hasAttribute('aria-current')).toBe(false);
  });

  it('Blazor öğesi tam sayfa açılır; kirli form varsa önce sorulur, vazgeçilirse gidilmez', async () => {
    const { h, kok } = await ac('/vitrin/form');
    formKirli = true;
    baglanti(kok, 'Kiralar')?.click();
    await h.fixture.whenStable();
    expect(sorular).toHaveLength(1);
    expect(gezin).not.toHaveBeenCalled();

    onayYaniti = true;
    baglanti(kok, 'Kiralar')?.click();
    await h.fixture.whenStable();
    expect(gezin).toHaveBeenCalledWith('/kiralar');

    formKirli = false;
    sorular = [];
    baglanti(kok, 'Panel')?.click();
    await h.fixture.whenStable();
    expect(sorular).toEqual([]);
    expect(gezin).toHaveBeenLastCalledWith('/');
  });

  it('Ctrl+K paleti açar; "is" yazınca İş Emirleri bulunur, Enter açar (Blazor → tam sayfa)', async () => {
    const { h } = await ac();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true }));
    await vi.waitFor(() => expect(document.querySelector('rc-komut-paleti input')).not.toBeNull());
    const girdi = document.querySelector<HTMLInputElement>('rc-komut-paleti input');
    if (!girdi) throw new Error('palet girdisi yok');
    await vi.waitFor(() => expect(document.activeElement).toBe(girdi));

    girdi.value = 'is';
    girdi.dispatchEvent(new Event('input'));
    await h.fixture.whenStable();
    const secenekler = [...document.querySelectorAll('rc-komut-paleti [role="option"]')];
    // Etiket eşleşmesi önce; "Kısa Yollar" grubundaki öğe yalnız grup adıyla eşleştiği için sonda.
    expect(secenekler.map((s) => s.querySelector('.palet__etiket')?.textContent?.trim())).toEqual([
      'İş Emirleri',
      'Işık Raporu',
      'Yeni Rezervasyon',
    ]);
    expect(secenekler[0]?.getAttribute('aria-selected')).toBe('true');
    expect(girdi.getAttribute('aria-activedescendant')).toBe(secenekler[0]?.id);

    girdi.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown' }));
    await h.fixture.whenStable();
    expect(girdi.getAttribute('aria-activedescendant')).toBe('rc-palet-1');
    girdi.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp' }));
    girdi.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await vi.waitFor(() => expect(gezin).toHaveBeenCalledWith('/is-emirleri'));
    await vi.waitFor(() => expect(document.querySelector('rc-komut-paleti')).toBeNull());
  });

  it('palette SPA öğesi router ile açılır ve sekme olur', async () => {
    const { h } = await ac();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'K', metaKey: true }));
    await vi.waitFor(() => expect(document.querySelector('rc-komut-paleti input')).not.toBeNull());
    const girdi = document.querySelector<HTMLInputElement>('rc-komut-paleti input');
    if (!girdi) throw new Error('palet girdisi yok');
    girdi.value = 'tablo';
    girdi.dispatchEvent(new Event('input'));
    await h.fixture.whenStable();
    girdi.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/vitrin/tablo'));
    expect(gezin).not.toHaveBeenCalled();
    expect(
      TestBed.inject(SekmeServisi)
        .sekmeler()
        .map((s) => s.desen),
    ).toEqual(['/', '/vitrin/tablo']);
  });

  it('üst çubuk: kullanıcı/firma/şube, tema düğmeleri, çıkış (kirli formda önce sorar)', async () => {
    const { h, kok } = await ac('/vitrin/form');
    expect(kok.querySelector('.kimlik')?.textContent).toContain('Ayşe Yılmaz');
    expect(kok.querySelector('.kimlik')?.textContent).toContain('Pilot Firma · Tüm şubeler');

    const tema = (ad: string) =>
      kok.querySelector<HTMLButtonElement>(`rc-ust-cubuk button[aria-label="${ad}"]`);
    tema('Koyu tema')?.click();
    await h.fixture.whenStable();
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(tema('Koyu tema')?.getAttribute('aria-pressed')).toBe('true');
    tema('Sistem teması')?.click();
    await h.fixture.whenStable();
    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);

    const cikis = [...kok.querySelectorAll('rc-ust-cubuk button')].find((b) =>
      b.textContent?.includes('Çıkış yap'),
    ) as HTMLButtonElement;
    formKirli = true;
    cikis.click();
    await h.fixture.whenStable();
    expect(sorular).toHaveLength(1);
    expect(cikisYap).not.toHaveBeenCalled();
    onayYaniti = true;
    cikis.click();
    await vi.waitFor(() => expect(cikisYap).toHaveBeenCalledTimes(1));
  });

  it('mobil çekmece: menü düğmesi açar (odak içeride, arka plan inert), Esc kapatır ve odağı geri verir', async () => {
    const eski = window.matchMedia;
    window.matchMedia = ((sorgu: string) => ({
      matches: sorgu.includes('max-width: 900px'),
      media: sorgu,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    })) as unknown as typeof window.matchMedia;
    try {
      const { h, kok } = await ac();
      const yan = kok.querySelector('#rc-yan-menu') as HTMLElement;
      const ana = kok.querySelector('.ana') as HTMLElement;
      const dugme = kok.querySelector('.menu-dugmesi') as HTMLButtonElement;
      expect(yan.hasAttribute('inert')).toBe(true);
      expect(dugme.getAttribute('aria-expanded')).toBe('false');

      dugme.click();
      await h.fixture.whenStable();
      expect(yan.hasAttribute('inert')).toBe(false);
      expect(ana.hasAttribute('inert')).toBe(true);
      expect(dugme.getAttribute('aria-expanded')).toBe('true');
      await vi.waitFor(() => expect(yan.contains(document.activeElement)).toBe(true));

      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
      await h.fixture.whenStable();
      expect(yan.hasAttribute('inert')).toBe(true);
      expect(ana.hasAttribute('inert')).toBe(false);
      expect(document.activeElement).toBe(dugme);
    } finally {
      window.matchMedia = eski;
    }
  });
});
