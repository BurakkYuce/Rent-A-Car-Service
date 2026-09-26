import { navigateCell, clampPosition, type IzgaraBoyutu } from './tablo-klavye';

/** 1 başlık + 20 veri satırı, 5 sütun, sayfa adımı 8. */
const SIZE: IzgaraBoyutu = { satirSayisi: 21, sutunSayisi: 5, sayfaAdimi: 8 };

describe('Izgara klavye gezinmesi (APG Data Grid)', () => {
  it('oklar bir hücre taşır, kenarda durur (sarmaz)', () => {
    expect(navigateCell({ satir: 3, sutun: 2 }, { key: 'ArrowRight' }, SIZE)).toEqual({
      satir: 3,
      sutun: 3,
    });
    expect(navigateCell({ satir: 3, sutun: 2 }, { key: 'ArrowLeft' }, SIZE)).toEqual({
      satir: 3,
      sutun: 1,
    });
    expect(navigateCell({ satir: 3, sutun: 2 }, { key: 'ArrowDown' }, SIZE)).toEqual({
      satir: 4,
      sutun: 2,
    });
    expect(navigateCell({ satir: 3, sutun: 2 }, { key: 'ArrowUp' }, SIZE)).toEqual({
      satir: 2,
      sutun: 2,
    });
    expect(navigateCell({ satir: 0, sutun: 0 }, { key: 'ArrowUp' }, SIZE)).toEqual({
      satir: 0,
      sutun: 0,
    });
    expect(navigateCell({ satir: 20, sutun: 4 }, { key: 'ArrowDown' }, SIZE)).toEqual({
      satir: 20,
      sutun: 4,
    });
    expect(navigateCell({ satir: 20, sutun: 4 }, { key: 'ArrowRight' }, SIZE)).toEqual({
      satir: 20,
      sutun: 4,
    });
  });

  it('Home/End satır içinde, Ctrl/⌘ ile ızgaranın köşeleri', () => {
    expect(navigateCell({ satir: 5, sutun: 3 }, { key: 'Home' }, SIZE)).toEqual({
      satir: 5,
      sutun: 0,
    });
    expect(navigateCell({ satir: 5, sutun: 1 }, { key: 'End' }, SIZE)).toEqual({
      satir: 5,
      sutun: 4,
    });
    expect(navigateCell({ satir: 5, sutun: 3 }, { key: 'Home', ctrlKey: true }, SIZE)).toEqual({
      satir: 0,
      sutun: 0,
    });
    expect(navigateCell({ satir: 5, sutun: 1 }, { key: 'End', metaKey: true }, SIZE)).toEqual({
      satir: 20,
      sutun: 4,
    });
  });

  it('PageDown/PageUp sayfa adımı kadar; PageUp başlığa çıkmaz', () => {
    expect(navigateCell({ satir: 1, sutun: 0 }, { key: 'PageDown' }, SIZE)).toEqual({
      satir: 9,
      sutun: 0,
    });
    expect(navigateCell({ satir: 18, sutun: 0 }, { key: 'PageDown' }, SIZE)).toEqual({
      satir: 20,
      sutun: 0,
    });
    expect(navigateCell({ satir: 5, sutun: 0 }, { key: 'PageUp' }, SIZE)).toEqual({
      satir: 1,
      sutun: 0,
    });
    expect(navigateCell({ satir: 0, sutun: 2 }, { key: 'PageUp' }, SIZE)).toEqual({
      satir: 0,
      sutun: 2,
    });
  });

  it('gezinme dışı tuşlar ve Alt/Shift kısayolları null (çağıran başka iş yapar)', () => {
    expect(navigateCell({ satir: 1, sutun: 1 }, { key: 'Enter' }, SIZE)).toBeNull();
    expect(navigateCell({ satir: 1, sutun: 1 }, { key: ' ' }, SIZE)).toBeNull();
    expect(
      navigateCell({ satir: 0, sutun: 1 }, { key: 'ArrowLeft', altKey: true }, SIZE),
    ).toBeNull();
    expect(
      navigateCell({ satir: 0, sutun: 1 }, { key: 'ArrowRight', shiftKey: true }, SIZE),
    ).toBeNull();
  });

  it('veri yokken yalnız başlık satırı; ızgara küçülünce konum içeri kırpılır', () => {
    const empty: IzgaraBoyutu = { satirSayisi: 1, sutunSayisi: 3, sayfaAdimi: 8 };
    expect(navigateCell({ satir: 0, sutun: 1 }, { key: 'ArrowDown' }, empty)).toEqual({
      satir: 0,
      sutun: 1,
    });
    expect(clampPosition({ satir: 50, sutun: 9 }, SIZE)).toEqual({ satir: 20, sutun: 4 });
    expect(navigateCell({ satir: 50, sutun: 9 }, { key: 'ArrowUp' }, SIZE)).toEqual({
      satir: 19,
      sutun: 4,
    });
  });
});
