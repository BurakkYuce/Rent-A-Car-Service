import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { ApiHatasi } from '@core/api/api-hatasi';
import { provideTranslation } from '@core/i18n/ceviri';
import { DefinitionCrud } from './definition-crud';
import type {
  TanimAlani,
  DefinitionValue,
  DefinitionSource,
  DefinitionRow,
} from './definition-source';

/**
 * F11.2a çekirdek eki: satır `surum`'u PUT'a gider; sürümsüz satır düzenlemede tekil okunur; 409 `cakisma`
 * formu silmez — dokunulmayan alan sunucu değerine çekilir, iki tarafça değişen alan işaretlenir, sonraki
 * kayıt yeni sürümle gider (otomatik yeniden gönderme yok). Beklenen değerler elle kurulmuş senaryodan.
 */
let server: Record<string, DefinitionRow>;
let listRows: DefinitionRow[];
let puts: { id: string; value: DefinitionValue; version: string | null | undefined }[];
let reads: string[];
let conflictOnce: boolean;
let suggestQueries: string[];

const source: DefinitionSource = {
  listele: (): Observable<readonly DefinitionRow[]> => of(listRows),
  read: (id) => {
    reads.push(id);
    const row = server[id];
    return row
      ? of(row)
      : throwError(() => new ApiHatasi({ status: 404, kod: 'dogrulama', detay: 'Yok.' }));
  },
  olustur: () => of(null),
  guncelle: (id, value, _key, version) => {
    puts.push({ id, value, version });
    if (conflictOnce) {
      conflictOnce = false;
      return throwError(
        () => new ApiHatasi({ status: 409, kod: 'cakisma', detay: 'Kayıt değişti.' }),
      );
    }
    return of(null);
  },
  sil: () => of(null),
};

@Component({
  selector: 'rc-version-host',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DefinitionCrud],
  template: `<rc-tanim-crud baslik="Markalar" [alanlar]="fields" [kaynak]="source" />`,
})
class VersionHost {
  readonly source = source;
  readonly fields: TanimAlani[] = [
    { ad: 'kod', etiket: 'Kod', tur: 'metin', zorunlu: true },
    { ad: 'ad', etiket: 'Ad', tur: 'metin' },
    {
      ad: 'grup',
      etiket: 'Grup',
      tur: 'datalist',
      suggestions: (q) => {
        suggestQueries.push(q);
        return of(['EKO', 'LUKS'].filter((s) => s.includes(q)));
      },
    },
  ];
}

async function setup(rows: DefinitionRow[]) {
  listRows = rows;
  server = Object.fromEntries(rows.map((r) => [r.id, r]));
  puts = [];
  reads = [];
  conflictOnce = false;
  suggestQueries = [];
  TestBed.configureTestingModule({ providers: [...provideTranslation()] });
  const fixture = TestBed.createComponent(VersionHost);
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  const button = (text: string) =>
    [...root.querySelectorAll<HTMLButtonElement>('button')].find((b) =>
      b.textContent?.includes(text),
    );
  const inputs = () => root.querySelectorAll<HTMLInputElement>('.duzenleme input');
  const type = async (i: number, text: string) => {
    const el = inputs()[i];
    if (!el) throw new Error('girdi');
    el.value = text;
    el.dispatchEvent(new Event('input'));
    await fixture.whenStable();
  };
  return { root, button, inputs, type, stable: () => fixture.whenStable() };
}

describe('rc-tanim-crud sürüm + 409', () => {
  it("satırın surum'u PUT'a gider", async () => {
    const { button, type, stable } = await setup([
      { id: 'm1', kod: 'FIAT', ad: 'Fiat', grup: null, surum: 'v1' },
    ]);
    button('Düzenle')?.click();
    await stable();
    await type(1, 'Fiat Otomobil');
    button('Kaydet')?.click();
    await stable();
    expect(reads).toEqual([]);
    expect(puts).toEqual([
      { id: 'm1', value: { kod: 'FIAT', ad: 'Fiat Otomobil', grup: null }, version: 'v1' },
    ]);
  });

  it('sürümsüz satır düzenlemede tekil okunur; form güncel değerle, PUT okunan sürümle', async () => {
    const { button, inputs, stable } = await setup([
      { id: 'm1', kod: 'FIAT', ad: 'Eski', grup: null },
    ]);
    server['m1'] = { id: 'm1', kod: 'FIAT', ad: 'Fiat', grup: null, surum: 'v7' };
    button('Düzenle')?.click();
    await stable();
    expect(reads).toEqual(['m1']);
    expect(inputs()[1]?.value).toBe('Fiat');
    button('Kaydet')?.click();
    await stable();
    expect(puts[0]?.version).toBe('v7');
  });

  it('409 cakisma: form silinmez, dokunulmayan alan güncellenir, iki taraflı değişiklik işaretlenir, yeniden gönderme yok', async () => {
    const { root, button, inputs, type, stable } = await setup([
      { id: 'm1', kod: 'FIAT', ad: 'Fiat', grup: 'EKO', surum: 'v1' },
    ]);
    button('Düzenle')?.click();
    await stable();
    await type(1, 'Fiat Benim');
    // Arada başka oturum: ad VE grup değişti, sürüm v2.
    server['m1'] = { id: 'm1', kod: 'FIAT', ad: 'Fiat Onların', grup: 'LUKS', surum: 'v2' };
    conflictOnce = true;
    button('Kaydet')?.click();
    await stable();
    expect(puts).toHaveLength(1);
    expect(reads).toEqual(['m1']);
    expect(root.querySelector('.duzenleme')).not.toBeNull();
    expect(inputs()[1]?.value).toBe('Fiat Benim');
    expect(inputs()[2]?.value).toBe('LUKS');
    expect(root.querySelector('.duzenleme')?.textContent).toContain('siz düzenlerken değişti');
    // Kullanıcı gözden geçirip yeniden kaydeder → yeni sürüm.
    button('Kaydet')?.click();
    await stable();
    expect(puts).toHaveLength(2);
    expect(puts[1]).toEqual({
      id: 'm1',
      value: { kod: 'FIAT', ad: 'Fiat Benim', grup: 'LUKS' },
      version: 'v2',
    });
    expect(root.querySelector('.duzenleme')).toBeNull();
  });

  it('datalist: yazdıkça öneri sorulur ve listeye düşer', async () => {
    const { root, button, type, stable } = await setup([]);
    button('Yeni kayıt')?.click();
    await stable();
    await type(2, 'LU');
    await new Promise((r) => setTimeout(r, 300));
    await stable();
    expect(suggestQueries).toContain('LU');
    const options = [...root.querySelectorAll('datalist option')].map((o) =>
      o.getAttribute('value'),
    );
    expect(options).toEqual(['LUKS']);
    const input = root.querySelectorAll<HTMLInputElement>('.duzenleme input')[2];
    expect(input?.getAttribute('list')).toBe(root.querySelector('datalist')?.id);
  });
});
