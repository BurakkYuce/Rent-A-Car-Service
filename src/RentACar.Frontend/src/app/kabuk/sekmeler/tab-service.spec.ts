import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component, inject, type OnDestroy } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import {
  ActivatedRoute,
  provideRouter,
  RouteReuseStrategy,
  Router,
  RouterOutlet,
  type Routes,
} from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { unsavedChangesGuard, CONFIRM_PROMPT } from '@core/form/kaydedilmemis-degisiklik';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { provideTranslation } from '@core/i18n/ceviri';
import { SessionService } from '@core/oturum/session-service';
import { TabRouteStrategy } from '@core/sekme/sekme-stratejisi';

import { tabLimitGuard } from './sekme-siniri';
import { MAX_TABS, SHELL_PAGES, TAB_STORE, TabService } from './tab-service';

/** Kirli sayılan sayfa kimlikleri (`form` ya da kayıt id'si). */
let dirtyItems: Set<string>;
let destroyed: string[];

@Component({
  selector: 'rc-deneme-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: 'form',
})
class FormPage implements OnDestroy {
  deger = '';
  hasUnsavedChanges(): boolean {
    return dirtyItems.has('form');
  }
  ngOnDestroy(): void {
    destroyed.push('form');
  }
}

@Component({
  selector: 'rc-deneme-kayit',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: 'kayıt',
})
class RecordPage implements OnDestroy {
  readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';
  hasUnsavedChanges(): boolean {
    return dirtyItems.has(this.id);
  }
  ngOnDestroy(): void {
    destroyed.push(this.id);
  }
}

@Component({
  selector: 'rc-deneme-bos',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '',
})
class EmptyPage {}

@Component({
  selector: 'rc-deneme-kabuk',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet],
  template: `<router-outlet
    (activate)="active($event)"
    (attach)="active($event)"
    (deactivate)="active(null)"
    (detach)="active(null)"
  />`,
})
class ShellTestHost {
  private readonly tabs = inject(TabService);
  active(component: unknown): void {
    this.tabs.setActiveComponent(component);
  }
}

const PAGES: Routes = [
  { path: '', pathMatch: 'full', component: EmptyPage, title: 'Ana — RentACar' },
  {
    path: 'form',
    component: FormPage,
    title: 'Form — RentACar',
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'kayit/:id',
    component: RecordPage,
    title: 'Kayıt — RentACar',
    canDeactivate: [unsavedChangesGuard],
  },
];

describe('SekmeServisi (sekmeli çalışma alanı)', () => {
  let confirmResponse: boolean;
  let questions: string[];

  function exchangeRate(): void {
    TestBed.configureTestingModule({
      providers: [
        ...provideTranslation(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          {
            path: '',
            component: ShellTestHost,
            data: { kabuk: true },
            canActivateChild: [tabLimitGuard],
            children: PAGES,
          },
        ]),
        { provide: RouteReuseStrategy, useExisting: TabRouteStrategy },
        { provide: SHELL_PAGES, useValue: PAGES },
        { provide: CONFIRM_PROMPT, useValue: (m: string) => (questions.push(m), confirmResponse) },
      ],
    });
  }

  beforeEach(async () => {
    localStorage.clear();
    dirtyItems = new Set();
    destroyed = [];
    confirmResponse = false;
    questions = [];
    exchangeRate();
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  async function harness(): Promise<RouterTestingHarness> {
    const h = await RouterTestingHarness.create();
    TestBed.inject(TabService);
    return h;
  }

  const service = () => TestBed.inject(TabService);
  const store = (): unknown => JSON.parse(localStorage.getItem(TAB_STORE) ?? 'null');

  it('sekme değiştirince sayfa bileşeni YAŞAR (form durumu korunur); kayıtlar ayrı sekme', async () => {
    const h = await harness();
    await h.navigateByUrl('/form');
    const form = h.fixture.debugElement.query(By.directive(FormPage)).componentInstance as FormPage;
    form.deger = 'yazılan değer';
    dirtyItems.add('form');

    await h.navigateByUrl('/kayit/5');
    await h.navigateByUrl('/kayit/6');
    // Başka sekmeye geçiş kirli formu SORMAZ (kaybolmuyor) ve yok etmez.
    expect(questions).toEqual([]);
    expect(destroyed).toEqual([]);

    await h.navigateByUrl('/form');
    const back = h.fixture.debugElement.query(By.directive(FormPage)).componentInstance as FormPage;
    expect(back).toBe(form);
    expect(back.deger).toBe('yazılan değer');
    expect(
      service()
        .tabs()
        .map((s) => service().etiket(s)),
    ).toEqual(['Form', 'Kayıt · 5', 'Kayıt · 6']);
    expect(service().activeKey()).toBe('/form');
  });

  it('depoya YALNIZ rota deseni + id yazılır: sorgu, etiket, ad yok (KVKK)', async () => {
    const h = await harness();
    await h.navigateByUrl('/form?musteri=Ahmet%20Y%C4%B1lmaz');
    await h.navigateByUrl('/kayit/5?ara=Ay%C5%9Fe#sekme=odeme');
    TestBed.inject(ToastService).clear();

    expect(store()).toEqual([
      { rota: '/form', id: null },
      { rota: '/kayit/:id', id: '5' },
    ]);
    const raw = localStorage.getItem(TAB_STORE) ?? '';
    expect(raw).not.toMatch(/Ahmet|Ayşe|musteri|ara=|sekme=|Kayıt|Form/);
    for (const record of store() as object[])
      expect(Object.keys(record).sort()).toEqual(['id', 'rota']);
    // Bellekte sekme son adresini (sorgu + fragment) tutar.
    expect(service().tabs()[1]?.url).toBe('/kayit/5?ara=Ay%C5%9Fe#sekme=odeme');
  });

  it('yenilemede depodan geri yüklenir; bilinmeyen desen, bozuk id ve fazlalık atılır', () => {
    localStorage.setItem(
      TAB_STORE,
      JSON.stringify([
        { rota: '/kayit/:id', id: '7' },
        { rota: '/bilinmeyen', id: null },
        { rota: '/form', id: 'fazla' },
        { rota: '/kayit/:id', id: '../../cikis' },
        { rota: '/kayit/:id', id: '7' },
        'bozuk',
        { rota: '/form', id: null },
      ]),
    );
    expect(
      service()
        .tabs()
        .map((s) => [s.url, s.anahtar]),
    ).toEqual([
      ['/kayit/7', '/kayit/:id?id=7'],
      ['/form', '/form'],
    ]);
  });

  it('bozuk depo içeriği sekmesiz açılır (hata fırlamaz)', () => {
    localStorage.setItem(TAB_STORE, '{bozuk');
    expect(service().tabs()).toEqual([]);
  });

  it('çıkış (OturumServisi.temizle): sekmeler, arka plandaki bileşenler ve depo silinir', async () => {
    const h = await harness();
    await h.navigateByUrl('/form');
    await h.navigateByUrl('/kayit/5');
    expect(store()).toHaveLength(2);

    TestBed.inject(SessionService).clear();
    expect(service().tabs()).toEqual([]);
    expect(localStorage.getItem(TAB_STORE)).toBeNull();
    expect(destroyed).toEqual(['form']); // arka plandaki (ayrık) form yok edildi
  });

  it('arka plandaki kirli sekmeyi kapatmak onay ister; vazgeçince kalır, onayda yok edilir', async () => {
    const h = await harness();
    await h.navigateByUrl('/form');
    dirtyItems.add('form');
    await h.navigateByUrl('/kayit/5');

    await service().kapat('/form');
    expect(questions).toHaveLength(1);
    expect(service().tabs()).toHaveLength(2);

    confirmResponse = true;
    await service().kapat('/form');
    expect(
      service()
        .tabs()
        .map((s) => s.anahtar),
    ).toEqual(['/kayit/:id?id=5']);
    expect(destroyed).toEqual(['form']);
  });

  it('görünür kirli sekmeyi kapatmak sayfanın guard’ını sorar; vazgeçilirse sekme ve adres yerinde', async () => {
    const h = await harness();
    await h.navigateByUrl('/kayit/5');
    await h.navigateByUrl('/form');
    dirtyItems.add('form');

    await service().kapat('/form');
    expect(questions).toHaveLength(1);
    expect(TestBed.inject(Router).url).toBe('/form');
    expect(
      service()
        .tabs()
        .map((s) => s.anahtar),
    ).toEqual(['/kayit/:id?id=5', '/form']);

    confirmResponse = true;
    await service().kapat('/form');
    expect(TestBed.inject(Router).url).toBe('/kayit/5'); // komşu sekmeye
    expect(
      service()
        .tabs()
        .map((s) => s.anahtar),
    ).toEqual(['/kayit/:id?id=5']);
    expect(destroyed).toEqual(['form']);
  });

  it(`sınır ${MAX_TABS}: en eski TEMİZ sekme kapanır (bilgi); hepsi kirliyse gezinme durur (uyarı)`, async () => {
    const h = await harness();
    const toast = TestBed.inject(ToastService);
    const info = vi.spyOn(toast, 'bilgi');
    const warning = vi.spyOn(toast, 'uyari');
    for (let i = 1; i <= MAX_TABS; i++) await h.navigateByUrl(`/kayit/${i}`);
    dirtyItems.add('1');

    await h.navigateByUrl('/form'); // 1 kirli → 2 kapanır
    expect(service().tabs()).toHaveLength(MAX_TABS);
    expect(
      service()
        .tabs()
        .some((s) => s.id === '2'),
    ).toBe(false);
    expect(
      service()
        .tabs()
        .some((s) => s.id === '1'),
    ).toBe(true);
    expect(info).toHaveBeenCalledWith(expect.stringContaining('“Kayıt · 2” sekmesi kapatıldı'));

    for (const s of service().tabs()) dirtyItems.add(s.id ?? 'form');
    const router = TestBed.inject(Router);
    expect(await router.navigateByUrl('/kayit/99')).toBe(false);
    expect(router.url).toBe('/form');
    expect(warning).toHaveBeenCalledWith(expect.stringContaining('En fazla 10 sekme'));
    expect(service().tabs()).toHaveLength(MAX_TABS);
    toast.clear();
  });
});
