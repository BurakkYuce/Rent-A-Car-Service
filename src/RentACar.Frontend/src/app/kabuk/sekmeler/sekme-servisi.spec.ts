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

import { kaydedilmemisDegisiklikGuard, ONAY_ISTEMI } from '@core/form/kaydedilmemis-degisiklik';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { provideCeviri } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { SekmeRotaStratejisi } from '@core/sekme/sekme-stratejisi';

import { sekmeSiniriGuard } from './sekme-siniri';
import { EN_FAZLA_SEKME, KABUK_SAYFALARI, SEKME_DEPOSU, SekmeServisi } from './sekme-servisi';

/** Kirli sayılan sayfa kimlikleri (`form` ya da kayıt id'si). */
let kirliler: Set<string>;
let yokEdilenler: string[];

@Component({
  selector: 'rc-deneme-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: 'form',
})
class FormSayfasi implements OnDestroy {
  deger = '';
  kaydedilmemisDegisiklikVar(): boolean {
    return kirliler.has('form');
  }
  ngOnDestroy(): void {
    yokEdilenler.push('form');
  }
}

@Component({
  selector: 'rc-deneme-kayit',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: 'kayıt',
})
class KayitSayfasi implements OnDestroy {
  readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';
  kaydedilmemisDegisiklikVar(): boolean {
    return kirliler.has(this.id);
  }
  ngOnDestroy(): void {
    yokEdilenler.push(this.id);
  }
}

@Component({
  selector: 'rc-deneme-bos',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '',
})
class BosSayfa {}

@Component({
  selector: 'rc-deneme-kabuk',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet],
  template: `<router-outlet
    (activate)="etkin($event)"
    (attach)="etkin($event)"
    (deactivate)="etkin(null)"
    (detach)="etkin(null)"
  />`,
})
class DenemeKabuk {
  private readonly sekmeler = inject(SekmeServisi);
  etkin(bilesen: unknown): void {
    this.sekmeler.etkinBileseniAyarla(bilesen);
  }
}

const SAYFALAR: Routes = [
  { path: '', pathMatch: 'full', component: BosSayfa, title: 'Ana — RentACar' },
  {
    path: 'form',
    component: FormSayfasi,
    title: 'Form — RentACar',
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'kayit/:id',
    component: KayitSayfasi,
    title: 'Kayıt — RentACar',
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
];

describe('SekmeServisi (sekmeli çalışma alanı)', () => {
  let onayYaniti: boolean;
  let sorular: string[];

  function kur(): void {
    TestBed.configureTestingModule({
      providers: [
        ...provideCeviri(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          {
            path: '',
            component: DenemeKabuk,
            data: { kabuk: true },
            canActivateChild: [sekmeSiniriGuard],
            children: SAYFALAR,
          },
        ]),
        { provide: RouteReuseStrategy, useExisting: SekmeRotaStratejisi },
        { provide: KABUK_SAYFALARI, useValue: SAYFALAR },
        { provide: ONAY_ISTEMI, useValue: (m: string) => (sorular.push(m), onayYaniti) },
      ],
    });
  }

  beforeEach(async () => {
    localStorage.clear();
    kirliler = new Set();
    yokEdilenler = [];
    onayYaniti = false;
    sorular = [];
    kur();
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  async function harness(): Promise<RouterTestingHarness> {
    const h = await RouterTestingHarness.create();
    TestBed.inject(SekmeServisi);
    return h;
  }

  const servis = () => TestBed.inject(SekmeServisi);
  const depo = (): unknown => JSON.parse(localStorage.getItem(SEKME_DEPOSU) ?? 'null');

  it('sekme değiştirince sayfa bileşeni YAŞAR (form durumu korunur); kayıtlar ayrı sekme', async () => {
    const h = await harness();
    await h.navigateByUrl('/form');
    const form = h.fixture.debugElement.query(By.directive(FormSayfasi))
      .componentInstance as FormSayfasi;
    form.deger = 'yazılan değer';
    kirliler.add('form');

    await h.navigateByUrl('/kayit/5');
    await h.navigateByUrl('/kayit/6');
    // Başka sekmeye geçiş kirli formu SORMAZ (kaybolmuyor) ve yok etmez.
    expect(sorular).toEqual([]);
    expect(yokEdilenler).toEqual([]);

    await h.navigateByUrl('/form');
    const geri = h.fixture.debugElement.query(By.directive(FormSayfasi))
      .componentInstance as FormSayfasi;
    expect(geri).toBe(form);
    expect(geri.deger).toBe('yazılan değer');
    expect(
      servis()
        .sekmeler()
        .map((s) => servis().etiket(s)),
    ).toEqual(['Form', 'Kayıt · 5', 'Kayıt · 6']);
    expect(servis().etkinAnahtar()).toBe('/form');
  });

  it('depoya YALNIZ rota deseni + id yazılır: sorgu, etiket, ad yok (KVKK)', async () => {
    const h = await harness();
    await h.navigateByUrl('/form?musteri=Ahmet%20Y%C4%B1lmaz');
    await h.navigateByUrl('/kayit/5?ara=Ay%C5%9Fe#sekme=odeme');
    TestBed.inject(ToastServisi).temizle();

    expect(depo()).toEqual([
      { rota: '/form', id: null },
      { rota: '/kayit/:id', id: '5' },
    ]);
    const ham = localStorage.getItem(SEKME_DEPOSU) ?? '';
    expect(ham).not.toMatch(/Ahmet|Ayşe|musteri|ara=|sekme=|Kayıt|Form/);
    for (const kayit of depo() as object[])
      expect(Object.keys(kayit).sort()).toEqual(['id', 'rota']);
    // Bellekte sekme son adresini (sorgu + fragment) tutar.
    expect(servis().sekmeler()[1]?.url).toBe('/kayit/5?ara=Ay%C5%9Fe#sekme=odeme');
  });

  it('yenilemede depodan geri yüklenir; bilinmeyen desen, bozuk id ve fazlalık atılır', () => {
    localStorage.setItem(
      SEKME_DEPOSU,
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
      servis()
        .sekmeler()
        .map((s) => [s.url, s.anahtar]),
    ).toEqual([
      ['/kayit/7', '/kayit/:id?id=7'],
      ['/form', '/form'],
    ]);
  });

  it('bozuk depo içeriği sekmesiz açılır (hata fırlamaz)', () => {
    localStorage.setItem(SEKME_DEPOSU, '{bozuk');
    expect(servis().sekmeler()).toEqual([]);
  });

  it('çıkış (OturumServisi.temizle): sekmeler, arka plandaki bileşenler ve depo silinir', async () => {
    const h = await harness();
    await h.navigateByUrl('/form');
    await h.navigateByUrl('/kayit/5');
    expect(depo()).toHaveLength(2);

    TestBed.inject(OturumServisi).temizle();
    expect(servis().sekmeler()).toEqual([]);
    expect(localStorage.getItem(SEKME_DEPOSU)).toBeNull();
    expect(yokEdilenler).toEqual(['form']); // arka plandaki (ayrık) form yok edildi
  });

  it('arka plandaki kirli sekmeyi kapatmak onay ister; vazgeçince kalır, onayda yok edilir', async () => {
    const h = await harness();
    await h.navigateByUrl('/form');
    kirliler.add('form');
    await h.navigateByUrl('/kayit/5');

    await servis().kapat('/form');
    expect(sorular).toHaveLength(1);
    expect(servis().sekmeler()).toHaveLength(2);

    onayYaniti = true;
    await servis().kapat('/form');
    expect(
      servis()
        .sekmeler()
        .map((s) => s.anahtar),
    ).toEqual(['/kayit/:id?id=5']);
    expect(yokEdilenler).toEqual(['form']);
  });

  it('görünür kirli sekmeyi kapatmak sayfanın guard’ını sorar; vazgeçilirse sekme ve adres yerinde', async () => {
    const h = await harness();
    await h.navigateByUrl('/kayit/5');
    await h.navigateByUrl('/form');
    kirliler.add('form');

    await servis().kapat('/form');
    expect(sorular).toHaveLength(1);
    expect(TestBed.inject(Router).url).toBe('/form');
    expect(
      servis()
        .sekmeler()
        .map((s) => s.anahtar),
    ).toEqual(['/kayit/:id?id=5', '/form']);

    onayYaniti = true;
    await servis().kapat('/form');
    expect(TestBed.inject(Router).url).toBe('/kayit/5'); // komşu sekmeye
    expect(
      servis()
        .sekmeler()
        .map((s) => s.anahtar),
    ).toEqual(['/kayit/:id?id=5']);
    expect(yokEdilenler).toEqual(['form']);
  });

  it(`sınır ${EN_FAZLA_SEKME}: en eski TEMİZ sekme kapanır (bilgi); hepsi kirliyse gezinme durur (uyarı)`, async () => {
    const h = await harness();
    const toast = TestBed.inject(ToastServisi);
    const bilgi = vi.spyOn(toast, 'bilgi');
    const uyari = vi.spyOn(toast, 'uyari');
    for (let i = 1; i <= EN_FAZLA_SEKME; i++) await h.navigateByUrl(`/kayit/${i}`);
    kirliler.add('1');

    await h.navigateByUrl('/form'); // 1 kirli → 2 kapanır
    expect(servis().sekmeler()).toHaveLength(EN_FAZLA_SEKME);
    expect(
      servis()
        .sekmeler()
        .some((s) => s.id === '2'),
    ).toBe(false);
    expect(
      servis()
        .sekmeler()
        .some((s) => s.id === '1'),
    ).toBe(true);
    expect(bilgi).toHaveBeenCalledWith(expect.stringContaining('“Kayıt · 2” sekmesi kapatıldı'));

    for (const s of servis().sekmeler()) kirliler.add(s.id ?? 'form');
    const router = TestBed.inject(Router);
    expect(await router.navigateByUrl('/kayit/99')).toBe(false);
    expect(router.url).toBe('/form');
    expect(uyari).toHaveBeenCalledWith(expect.stringContaining('En fazla 10 sekme'));
    expect(servis().sekmeler()).toHaveLength(EN_FAZLA_SEKME);
    toast.temizle();
  });
});
