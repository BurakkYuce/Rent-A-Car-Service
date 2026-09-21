import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { ApiHatasi } from '@core/api/api-hatasi';
import type { Sayfa } from '@core/api/sayfa';
import { provideCeviri } from '@core/i18n/ceviri';
import type { StoreDurumu } from '@core/veri/temel-store';
import { provideTurkceYerel } from '@core/yerel/tr-yerel';

import { Tablo } from './tablo';
import { BellekTabloDuzeniDeposu, TabloDuzeniDeposu } from './tablo-duzeni-deposu';
import { TabloHucre } from './tablo-hucre';
import type { TabloSutunu } from './tablo-modeli';

interface Arac {
  id: string;
  plaka: string;
  marka: string;
  tutar: number;
  km: number;
}

const SUTUNLAR: readonly TabloSutunu<Arac>[] = [
  { kod: 'plaka', baslik: 'Plaka', deger: (a) => a.plaka, sabit: true, sirala: true },
  { kod: 'marka', baslik: 'Marka', deger: (a) => a.marka, sirala: true },
  { kod: 'tutar', baslik: 'Tutar', deger: (a) => a.tutar, tur: 'para', sirala: 'toplamTutar' },
  { kod: 'km', baslik: 'Km', deger: (a) => a.km, tur: 'sayi' },
];

const ARACLAR: readonly Arac[] = [
  { id: 'a1', plaka: '34 ABC 001', marka: 'Renault', tutar: 1234.5, km: 12000 },
  { id: 'a2', plaka: '06 DEF 002', marka: 'Fiat', tutar: -93040, km: 150 },
  { id: 'a3', plaka: '35 GHI 003', marka: 'Škoda', tutar: 0.005, km: 7 },
];

const sayfa = (kayitlar: readonly Arac[], toplam = kayitlar.length): Sayfa<Arac> => ({
  kayitlar,
  toplam,
  sayfaNo: 1,
  boyut: 50,
});

@Component({
  selector: 'rc-tablo-deneme',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Tablo, TabloHucre],
  template: `
    <rc-tablo
      etiket="Araçlar"
      [sutunlar]="sutunlar"
      [kaynak]="kaynak()"
      [satirKimligi]="kimlik"
      [tabloKodu]="tabloKodu()"
      [sirala]="sirala()"
      varsayilanSirala="plaka"
      [secilebilir]="true"
      [(secim)]="secim"
      (siralaDegisti)="siralamalar.push($event)"
      (satirAc)="acilanlar.push($event.id)"
      (yenidenDene)="yenidenDeneme = yenidenDeneme + 1"
    >
      <ng-template rcTabloHucre="marka" [rcTabloHucreSutunlar]="sutunlar" let-arac>
        <b class="marka">{{ arac.marka }}</b>
      </ng-template>
    </rc-tablo>
  `,
})
class Deneme {
  readonly sutunlar = SUTUNLAR;
  readonly kaynak = signal<StoreDurumu<Sayfa<Arac>>>({ tur: 'hazir', veri: sayfa(ARACLAR, 120) });
  readonly tabloKodu = signal<string | null>(null);
  readonly sirala = signal<string | null>('plaka');
  readonly secim = signal<readonly string[]>([]);
  readonly kimlik = (a: Arac) => a.id;
  readonly siralamalar: (string | null)[] = [];
  readonly acilanlar: string[] = [];
  yenidenDeneme = 0;
}

describe('Tablo motoru', () => {
  let depo: BellekTabloDuzeniDeposu;

  beforeEach(async () => {
    depo = new BellekTabloDuzeniDeposu();
    TestBed.configureTestingModule({
      providers: [
        provideTurkceYerel(),
        ...provideCeviri(),
        { provide: TabloDuzeniDeposu, useValue: depo },
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  async function kur(ayar?: (d: Deneme) => void) {
    const fixture = TestBed.createComponent(Deneme);
    ayar?.(fixture.componentInstance);
    await fixture.whenStable();
    const kok = fixture.nativeElement as HTMLElement;
    return {
      kok,
      d: fixture.componentInstance,
      yenile: () => fixture.whenStable(),
      basliklar: () => [...kok.querySelectorAll('th')].map((th) => th.textContent?.trim() ?? ''),
      satirlar: () => [
        ...kok.querySelectorAll<HTMLTableRowElement>('tbody tr.satir:not([aria-hidden])'),
      ],
      hucre: (satir: number, sutun: number) => {
        const h = kok.querySelector<HTMLElement>(`[data-hucre="${satir}:${sutun}"]`);
        if (h === null) throw new Error(`hücre yok ${satir}:${sutun}`);
        return h;
      },
    };
  }

  function tus(hedef: HTMLElement, key: string, ek: KeyboardEventInit = {}): void {
    hedef.dispatchEvent(
      new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...ek }),
    );
  }

  it('satırlar, erişilebilir ızgara, para sağa yaslı ve tr biçimli, özel hücre şablonu', async () => {
    const { kok, basliklar, satirlar, hucre } = await kur();
    const tablo = kok.querySelector('table');
    expect(tablo?.getAttribute('role')).toBe('grid');
    expect(tablo?.getAttribute('aria-label')).toBe('Araçlar');
    expect(tablo?.getAttribute('aria-rowcount')).toBe('121'); // 120 kayıt + başlık
    expect(basliklar()).toEqual(['', 'Plaka', 'Marka', 'Tutar', 'Km']);
    expect(satirlar()).toHaveLength(3);
    expect(satirlar()[0].getAttribute('aria-rowindex')).toBe('2');

    const tutar = hucre(1, 3);
    expect(tutar.textContent?.trim()).toBe('1.234,50 ₺');
    expect(tutar.classList).toContain('hucre--son');
    expect(hucre(2, 3).textContent?.trim()).toBe('-93.040,00 ₺');
    expect(hucre(3, 3).textContent?.trim()).toBe('0,01 ₺'); // yarım kuruş sıfırdan uzağa
    expect(hucre(1, 4).textContent?.trim()).toBe('12.000');
    expect(hucre(1, 2).querySelector('b.marka')?.textContent).toBe('Renault');

    // Sabit sütunlar: seçim + plaka, sol konumları birikimli.
    expect(hucre(1, 0).classList).toContain('hucre--sabit');
    expect(hucre(1, 1).classList).toContain('hucre--son-sabit');
    expect(hucre(1, 1).style.left).toBe('36px');
  });

  it('hata ≠ boş: hata bandı + yeniden dene; "Kayıt bulunamadı" görünmez, satır yok', async () => {
    const { kok, d, satirlar, yenile } = await kur((x) =>
      x.kaynak.set({
        tur: 'hata',
        hata: new ApiHatasi({ status: 503, kod: 'sunucu', detay: 'Sunucu yanıt vermedi.' }),
      }),
    );
    const bant = kok.querySelector('[role="alert"]');
    expect(bant?.textContent).toContain('Liste yüklenemedi');
    expect(bant?.textContent).toContain('Sunucu yanıt vermedi.');
    expect(kok.querySelector('rc-bos-durum')).toBeNull();
    expect(satirlar()).toHaveLength(0);

    bant?.querySelector('button')?.click();
    await yenile();
    expect(d.yenidenDeneme).toBe(1);
  });

  it('başarılı ve sıfır kayıt: yalnız o zaman "Kayıt bulunamadı"; yükleniyor: iskelet + aria-busy', async () => {
    const { kok, d, yenile } = await kur((x) => x.kaynak.set({ tur: 'hazir', veri: sayfa([]) }));
    expect(kok.querySelector('rc-bos-durum')?.textContent).toContain('Kayıt bulunamadı');
    expect(kok.querySelector('[role="alert"]')).toBeNull();

    d.kaynak.set({ tur: 'yukleniyor', onceki: undefined });
    await yenile();
    expect(kok.querySelector('table')?.getAttribute('aria-busy')).toBe('true');
    expect(kok.querySelectorAll('.rc-iskelet').length).toBeGreaterThan(0);
    expect(kok.querySelector('rc-bos-durum')).toBeNull();

    d.kaynak.set({ tur: 'bos' });
    await yenile();
    expect(kok.querySelector('rc-bos-durum')).toBeNull();
    expect(kok.querySelector('[role="alert"]')).toBeNull();
  });

  it('başlık tıklaması sunucu sıralama metnini yayar (alan adıyla), aria-sort işaretler', async () => {
    const { kok, d, yenile } = await kur();
    const dugme = (ad: string) =>
      [...kok.querySelectorAll<HTMLButtonElement>('th button')].find((b) =>
        b.textContent?.includes(ad),
      );
    expect(kok.querySelector('th[data-kod="plaka"]')?.getAttribute('aria-sort')).toBe('ascending');

    dugme('Tutar')?.click();
    expect(d.siralamalar).toEqual(['toplamTutar']);
    d.sirala.set('toplamTutar');
    await yenile();
    expect(kok.querySelector('th[data-kod="tutar"]')?.getAttribute('aria-sort')).toBe('ascending');

    dugme('Tutar')?.click();
    d.sirala.set('-toplamTutar');
    await yenile();
    dugme('Tutar')?.click();
    expect(d.siralamalar).toEqual(['toplamTutar', '-toplamTutar', null]);
    expect(kok.querySelector('th[data-kod="tutar"]')?.getAttribute('aria-sort')).toBe('descending');
  });

  it('klavye: oklar hücreler arası gezer (roving tabindex), Enter açar, Boşluk seçer', async () => {
    const { d, hucre, yenile } = await kur();
    const ilk = hucre(1, 2);
    ilk.focus();
    await yenile();
    expect(ilk.tabIndex).toBe(0);

    tus(ilk, 'ArrowRight');
    await yenile();
    expect(document.activeElement).toBe(hucre(1, 3));
    expect(hucre(1, 3).tabIndex).toBe(0);
    expect(hucre(1, 2).tabIndex).toBe(-1);

    tus(hucre(1, 3), 'ArrowDown');
    await yenile();
    expect(document.activeElement).toBe(hucre(2, 3));

    tus(hucre(2, 3), 'Enter');
    expect(d.acilanlar).toEqual(['a2']);

    tus(hucre(2, 3), ' ');
    await yenile();
    expect(d.secim()).toEqual(['a2']);

    // Yukarı: başlık satırındaki sıralama düğmesi odak alır; Home satır başına (seçim kutusu).
    tus(hucre(2, 3), 'ArrowUp');
    tus(hucre(1, 3), 'ArrowUp');
    await yenile();
    expect(document.activeElement).toBe(hucre(0, 3).querySelector('button'));
    tus(hucre(0, 3), 'Home');
    await yenile();
    expect(document.activeElement).toBe(hucre(0, 0).querySelector('input'));
  });

  it('sayfa seçimi: başlık kutusu sayfadaki tüm satırları seçer, kısmi seçimde belirsiz', async () => {
    const { kok, d, yenile } = await kur((x) => x.secim.set(['a1']));
    const tumu = kok.querySelector<HTMLInputElement>('th input[type="checkbox"]');
    expect(tumu?.indeterminate).toBe(true);
    tumu?.click();
    await yenile();
    expect([...d.secim()].sort()).toEqual(['a1', 'a2', 'a3']);
    expect(kok.querySelectorAll('tr[aria-selected="true"]')).toHaveLength(3);
  });

  it('kayıtlı düzen uygulanır; sütun gizleme kullanıcı düzenine yazılır', async () => {
    depo.kayitlar.set('araclar.liste', {
      sutunlar: [
        { kod: 'km', gorunur: true, genislik: 90 },
        { kod: 'marka', gorunur: false, genislik: null },
        { kod: 'tutar', gorunur: true, genislik: null },
      ],
      siralama: [],
    });
    const { kok, basliklar, yenile } = await kur((x) => x.tabloKodu.set('araclar.liste'));
    await yenile();
    expect(basliklar()).toEqual(['', 'Plaka', 'Km', 'Tutar']);

    // Sütun seçicide Km'yi gizle → gecikmeli kayıt.
    kok.querySelector<HTMLButtonElement>('rc-tablo-sutun-secici > button')?.click();
    await yenile();
    const km = [...kok.querySelectorAll<HTMLLabelElement>('rc-tablo-sutun-secici label')].find(
      (l) => l.textContent?.includes('Km'),
    );
    km?.querySelector('input')?.click();
    await yenile();
    expect(basliklar()).toEqual(['', 'Plaka', 'Tutar']);

    await new Promise((r) => setTimeout(r, 700));
    expect(depo.kayitlar.get('araclar.liste')).toEqual({
      sutunlar: [
        { kod: 'plaka', gorunur: true, genislik: null },
        { kod: 'km', gorunur: false, genislik: 90 },
        { kod: 'marka', gorunur: false, genislik: null },
        { kod: 'tutar', gorunur: true, genislik: null },
      ],
      siralama: [],
    });
  });

  it('kayıtlı sıralama tercihi yalnız sayfa varsayılandayken uygulanır', async () => {
    depo.kayitlar.set('araclar.liste', {
      sutunlar: [],
      siralama: [{ kod: 'tutar', azalan: true }],
    });
    const varsayilanda = await kur((x) => x.tabloKodu.set('araclar.liste'));
    expect(varsayilanda.d.siralamalar).toEqual(['-toplamTutar']);

    const acikSiralama = await kur((x) => {
      x.tabloKodu.set('araclar.liste');
      x.sirala.set('marka');
    });
    expect(acikSiralama.d.siralamalar).toEqual([]);
  });
});
