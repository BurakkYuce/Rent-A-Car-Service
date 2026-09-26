import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { ApiHatasi } from '@core/api/api-hatasi';
import type { Sayfa } from '@core/api/sayfa';
import { provideTranslation } from '@core/i18n/ceviri';
import type { StoreState } from '@core/veri/temel-store';
import { provideTurkishLocale } from '@core/yerel/tr-yerel';

import { Table } from './table';
import { InMemoryTableLayoutStore, TableLayoutStore } from './table-layout-store';
import { TableCell } from './table-cell';
import type { TabloSutunu } from './tablo-modeli';

interface Arac {
  id: string;
  plaka: string;
  marka: string;
  tutar: number;
  km: number;
}

const COLUMNS: readonly TabloSutunu<Arac>[] = [
  { kod: 'plaka', baslik: 'Plaka', deger: (a) => a.plaka, sabit: true, sirala: true },
  { kod: 'marka', baslik: 'Marka', deger: (a) => a.marka, sirala: true },
  { kod: 'tutar', baslik: 'Tutar', deger: (a) => a.tutar, tur: 'para', sirala: 'toplamTutar' },
  { kod: 'km', baslik: 'Km', deger: (a) => a.km, tur: 'sayi' },
];

const VEHICLES: readonly Arac[] = [
  { id: 'a1', plaka: '34 ABC 001', marka: 'Renault', tutar: 1234.5, km: 12000 },
  { id: 'a2', plaka: '06 DEF 002', marka: 'Fiat', tutar: -93040, km: 150 },
  { id: 'a3', plaka: '35 GHI 003', marka: 'Škoda', tutar: 0.005, km: 7 },
];

const sayfa = (records: readonly Arac[], total = records.length): Sayfa<Arac> => ({
  kayitlar: records,
  toplam: total,
  sayfaNo: 1,
  boyut: 50,
});

@Component({
  selector: 'rc-tablo-deneme',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Table, TableCell],
  template: `
    <rc-tablo
      etiket="Araçlar"
      [sutunlar]="columns"
      [kaynak]="kaynak()"
      [satirKimligi]="identity"
      [tabloKodu]="tableCode()"
      [sirala]="sort()"
      varsayilanSirala="plaka"
      [secilebilir]="true"
      [(secim)]="secim"
      [satirSinifi]="rowClass()"
      (siralaDegisti)="sorts.push($event)"
      (satirAc)="opened.push($event.id)"
      (yenidenDene)="retry = retry + 1"
    >
      <ng-template rcTabloHucre="marka" [rcTabloHucreSutunlar]="columns" let-arac>
        <b class="marka">{{ arac.marka }}</b>
        <a class="marka-bag" href="/listeler/export/araclar">PDF</a>
      </ng-template>
    </rc-tablo>
  `,
})
class TestHost {
  readonly columns = COLUMNS;
  readonly kaynak = signal<StoreState<Sayfa<Arac>>>({ tur: 'hazir', veri: sayfa(VEHICLES, 120) });
  readonly tableCode = signal<string | null>(null);
  readonly sort = signal<string | null>('plaka');
  readonly secim = signal<readonly string[]>([]);
  readonly identity = (a: Arac) => a.id;
  readonly rowClass = signal<((a: Arac) => string | null) | null>(null);
  readonly sorts: (string | null)[] = [];
  readonly opened: string[] = [];
  retry = 0;
}

describe('Tablo motoru', () => {
  let store: InMemoryTableLayoutStore;

  beforeEach(async () => {
    store = new InMemoryTableLayoutStore();
    TestBed.configureTestingModule({
      providers: [
        provideTurkishLocale(),
        ...provideTranslation(),
        { provide: TableLayoutStore, useValue: store },
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  async function exchangeRate(setting?: (d: TestHost) => void) {
    const fixture = TestBed.createComponent(TestHost);
    setting?.(fixture.componentInstance);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    return {
      kok: root,
      d: fixture.componentInstance,
      yenile: () => fixture.whenStable(),
      basliklar: () => [...root.querySelectorAll('th')].map((th) => th.textContent?.trim() ?? ''),
      satirlar: () => [
        ...root.querySelectorAll<HTMLTableRowElement>('tbody tr.satir:not([aria-hidden])'),
      ],
      hucre: (row: number, column: number) => {
        const h = root.querySelector<HTMLElement>(`[data-hucre="${row}:${column}"]`);
        if (h === null) throw new Error(`hücre yok ${row}:${column}`);
        return h;
      },
    };
  }

  function tus(target: HTMLElement, key: string, extra: KeyboardEventInit = {}): void {
    target.dispatchEvent(
      new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...extra }),
    );
  }

  it('satırlar, erişilebilir ızgara, para sağa yaslı ve tr biçimli, özel hücre şablonu', async () => {
    const { kok, basliklar, satirlar, hucre } = await exchangeRate();
    const table = kok.querySelector('table');
    expect(table?.getAttribute('role')).toBe('grid');
    expect(table?.getAttribute('aria-label')).toBe('Araçlar');
    expect(table?.getAttribute('aria-rowcount')).toBe('121'); // 120 kayıt + başlık
    expect(basliklar()).toEqual(['', 'Plaka', 'Marka', 'Tutar', 'Km']);
    expect(satirlar()).toHaveLength(3);
    expect(satirlar()[0].getAttribute('aria-rowindex')).toBe('2');

    const amount = hucre(1, 3);
    expect(amount.textContent?.trim()).toBe('1.234,50 ₺');
    expect(amount.classList).toContain('hucre--son');
    expect(hucre(2, 3).textContent?.trim()).toBe('-93.040,00 ₺');
    expect(hucre(3, 3).textContent?.trim()).toBe('0,01 ₺'); // yarım kuruş sıfırdan uzağa
    expect(hucre(1, 4).textContent?.trim()).toBe('12.000');
    expect(hucre(1, 2).querySelector('b.marka')?.textContent).toBe('Renault');

    // Sabit sütunlar: seçim + plaka, sol konumları birikimli.
    expect(hucre(1, 0).classList).toContain('hucre--sabit');
    expect(hucre(1, 1).classList).toContain('hucre--son-sabit');
    expect(hucre(1, 1).style.left).toBe('36px');
  });

  it('satirSinifi: satıra ek sınıf verir (bugün vurgusu), motor sınıfları ve seçim korunur', async () => {
    const { satirlar, d, yenile } = await exchangeRate((d) =>
      d.rowClass.set((a) => (a.id === 'a2' ? 'rc-satir-bugun' : null)),
    );
    expect(satirlar()[1].classList).toContain('rc-satir-bugun');
    expect(satirlar()[1].classList).toContain('satir');
    expect(satirlar()[0].classList).not.toContain('rc-satir-bugun');

    d.secim.set(['a2']);
    await yenile();
    expect(satirlar()[1].classList).toContain('satir--secili');
    expect(satirlar()[1].classList).toContain('rc-satir-bugun');
  });

  it('hata ≠ boş: hata bandı + yeniden dene; "Kayıt bulunamadı" görünmez, satır yok', async () => {
    const {
      kok,
      d,
      satirlar: rows,
      yenile,
    } = await exchangeRate((x) =>
      x.kaynak.set({
        tur: 'hata',
        hata: new ApiHatasi({ status: 503, kod: 'sunucu', detay: 'Sunucu yanıt vermedi.' }),
      }),
    );
    const banner = kok.querySelector('[role="alert"]');
    expect(banner?.textContent).toContain('Liste yüklenemedi');
    expect(banner?.textContent).toContain('Sunucu yanıt vermedi.');
    expect(kok.querySelector('rc-bos-durum')).toBeNull();
    expect(rows()).toHaveLength(0);

    banner?.querySelector('button')?.click();
    await yenile();
    expect(d.retry).toBe(1);
  });

  it('başarılı ve sıfır kayıt: yalnız o zaman "Kayıt bulunamadı"; yükleniyor: iskelet + aria-busy', async () => {
    const { kok, d, yenile } = await exchangeRate((x) =>
      x.kaynak.set({ tur: 'hazir', veri: sayfa([]) }),
    );
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
    const { kok, d, yenile } = await exchangeRate();
    const button = (name: string) =>
      [...kok.querySelectorAll<HTMLButtonElement>('th button')].find((b) =>
        b.textContent?.includes(name),
      );
    expect(kok.querySelector('th[data-kod="plaka"]')?.getAttribute('aria-sort')).toBe('ascending');

    button('Tutar')?.click();
    expect(d.sorts).toEqual(['toplamTutar']);
    d.sort.set('toplamTutar');
    await yenile();
    expect(kok.querySelector('th[data-kod="tutar"]')?.getAttribute('aria-sort')).toBe('ascending');

    button('Tutar')?.click();
    d.sort.set('-toplamTutar');
    await yenile();
    button('Tutar')?.click();
    expect(d.sorts).toEqual(['toplamTutar', '-toplamTutar', null]);
    expect(kok.querySelector('th[data-kod="tutar"]')?.getAttribute('aria-sort')).toBe('descending');
  });

  it('klavye: oklar hücreler arası gezer (roving tabindex), Enter açar, Boşluk seçer', async () => {
    const { d, hucre, yenile } = await exchangeRate();
    const first = hucre(1, 2);
    first.focus();
    await yenile();
    expect(first.tabIndex).toBe(0);

    tus(first, 'ArrowRight');
    await yenile();
    expect(document.activeElement).toBe(hucre(1, 3));
    expect(hucre(1, 3).tabIndex).toBe(0);
    expect(hucre(1, 2).tabIndex).toBe(-1);

    tus(hucre(1, 3), 'ArrowDown');
    await yenile();
    expect(document.activeElement).toBe(hucre(2, 3));

    tus(hucre(2, 3), 'Enter');
    expect(d.opened).toEqual(['a2']);

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

  it('hücredeki bağlantıda Enter ve çift tıklama satırı AÇMAZ (bağlantının kendi davranışı)', async () => {
    const { d, hucre: cell } = await exchangeRate();
    const bag = cell(1, 2).querySelector<HTMLAnchorElement>('a.marka-bag');
    if (bag === null) throw new Error('bağlantı yok');
    bag.focus();
    const evt = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });
    bag.dispatchEvent(evt);
    expect(evt.defaultPrevented).toBe(false);
    bag.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true }));
    expect(d.opened).toEqual([]);

    // Hücrenin kendisine çift tıklama açar.
    cell(1, 2).dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true }));
    expect(d.opened).toEqual(['a1']);
  });

  it('sayfa seçimi: başlık kutusu sayfadaki tüm satırları seçer, kısmi seçimde belirsiz', async () => {
    const { kok, d, yenile } = await exchangeRate((x) => x.secim.set(['a1']));
    const all = kok.querySelector<HTMLInputElement>('th input[type="checkbox"]');
    expect(all?.indeterminate).toBe(true);
    all?.click();
    await yenile();
    expect([...d.secim()].sort()).toEqual(['a1', 'a2', 'a3']);
    expect(kok.querySelectorAll('tr[aria-selected="true"]')).toHaveLength(3);
  });

  it('kayıtlı düzen uygulanır; sütun gizleme kullanıcı düzenine yazılır', async () => {
    store.records.set('araclar.liste', {
      sutunlar: [
        { kod: 'km', gorunur: true, genislik: 90 },
        { kod: 'marka', gorunur: false, genislik: null },
        { kod: 'tutar', gorunur: true, genislik: null },
      ],
      siralama: [],
    });
    const {
      kok,
      basliklar: headers,
      yenile: refresh,
    } = await exchangeRate((x) => x.tableCode.set('araclar.liste'));
    await refresh();
    expect(headers()).toEqual(['', 'Plaka', 'Km', 'Tutar']);

    // Sütun seçicide Km'yi gizle → gecikmeli kayıt.
    kok.querySelector<HTMLButtonElement>('rc-tablo-sutun-secici > button')?.click();
    await refresh();
    const km = [...kok.querySelectorAll<HTMLLabelElement>('rc-tablo-sutun-secici label')].find(
      (l) => l.textContent?.includes('Km'),
    );
    km?.querySelector('input')?.click();
    await refresh();
    expect(headers()).toEqual(['', 'Plaka', 'Tutar']);

    await new Promise((r) => setTimeout(r, 700));
    expect(store.records.get('araclar.liste')).toEqual({
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
    store.records.set('araclar.liste', {
      sutunlar: [],
      siralama: [{ kod: 'tutar', azalan: true }],
    });
    const byDefault = await exchangeRate((x) => x.tableCode.set('araclar.liste'));
    expect(byDefault.d.sorts).toEqual(['-toplamTutar']);

    const openSort = await exchangeRate((x) => {
      x.tableCode.set('araclar.liste');
      x.sort.set('marka');
    });
    expect(openSort.d.sorts).toEqual([]);
  });
});
