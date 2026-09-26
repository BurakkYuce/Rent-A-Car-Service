import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { provideTranslation } from '@core/i18n/ceviri';
import { DefinitionCrud } from './definition-crud';
import type {
  TanimAlani,
  DefinitionValue,
  DefinitionSource,
  DefinitionRow,
} from './definition-source';

/** F11.2b çekirdek eki: `textarea` alan tipi (çok satırlı düz metin; listede 80 karakterde kısaltılır). */
const LONG = 'Birinci paragraf. '.repeat(10).trim();
let rows: DefinitionRow[];
let sent: DefinitionValue[];

const source: DefinitionSource = {
  listele: () => of([...rows]),
  olustur: (d) => {
    sent.push(d);
    return of(null);
  },
  guncelle: (_id, d) => {
    sent.push(d);
    return of(null);
  },
  sil: () => of(null),
};

@Component({
  selector: 'rc-textarea-host',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DefinitionCrud],
  template: `<rc-tanim-crud baslik="SSS" [alanlar]="fields" [kaynak]="source" layout="panel" />`,
})
class TextareaHost {
  readonly source = source;
  readonly fields: TanimAlani[] = [
    { ad: 'soru', etiket: 'Soru', tur: 'metin', zorunlu: true },
    { ad: 'cevap', etiket: 'Cevap', tur: 'textarea', zorunlu: true, azamiUzunluk: 4000 },
  ];
}

async function setup() {
  rows = [{ id: '1', soru: 'Depozito?', cevap: LONG, surum: 's1' }];
  sent = [];
  TestBed.configureTestingModule({ providers: [...provideTranslation()] });
  const fixture = TestBed.createComponent(TextareaHost);
  await fixture.whenStable();
  return { fixture, root: fixture.nativeElement as HTMLElement };
}

describe('TanimCrud textarea alanı', () => {
  it('listede metni 80 karakterde kısaltır', async () => {
    const { root } = await setup();
    const cell = [...root.querySelectorAll('tbody td')][1]?.textContent?.trim() ?? '';
    expect(cell).toBe(`${LONG.slice(0, 80)}…`);
  });

  it('düzenlemede çok satırlı kutu açar, tam değeri gösterir ve aynen gönderir', async () => {
    const { fixture, root } = await setup();
    const edit = [...root.querySelectorAll<HTMLButtonElement>('button')].find((b) =>
      b.textContent?.includes('Düzenle'),
    );
    edit?.click();
    await fixture.whenStable();
    const area = root.querySelector<HTMLTextAreaElement>('.duzenleme textarea');
    expect(area).not.toBeNull();
    expect(area?.value).toBe(LONG);
    expect(area?.getAttribute('maxlength')).toBe('4000');
    if (!area) return;
    area.value = 'Satır 1\n\nSatır 2';
    area.dispatchEvent(new Event('input'));
    const save = [...root.querySelectorAll<HTMLButtonElement>('button')].find((b) =>
      b.textContent?.includes('Kaydet'),
    );
    save?.click();
    await fixture.whenStable();
    expect(sent).toEqual([{ soru: 'Depozito?', cevap: 'Satır 1\n\nSatır 2' }]);
  });
});
