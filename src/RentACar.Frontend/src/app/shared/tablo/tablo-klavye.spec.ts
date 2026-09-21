import { hucreGezin, konumKirp, type IzgaraBoyutu } from './tablo-klavye';

/** 1 başlık + 20 veri satırı, 5 sütun, sayfa adımı 8. */
const BOYUT: IzgaraBoyutu = { satirSayisi: 21, sutunSayisi: 5, sayfaAdimi: 8 };

describe('Izgara klavye gezinmesi (APG Data Grid)', () => {
  it('oklar bir hücre taşır, kenarda durur (sarmaz)', () => {
    expect(hucreGezin({ satir: 3, sutun: 2 }, { key: 'ArrowRight' }, BOYUT)).toEqual({
      satir: 3,
      sutun: 3,
    });
    expect(hucreGezin({ satir: 3, sutun: 2 }, { key: 'ArrowLeft' }, BOYUT)).toEqual({
      satir: 3,
      sutun: 1,
    });
    expect(hucreGezin({ satir: 3, sutun: 2 }, { key: 'ArrowDown' }, BOYUT)).toEqual({
      satir: 4,
      sutun: 2,
    });
    expect(hucreGezin({ satir: 3, sutun: 2 }, { key: 'ArrowUp' }, BOYUT)).toEqual({
      satir: 2,
      sutun: 2,
    });
    expect(hucreGezin({ satir: 0, sutun: 0 }, { key: 'ArrowUp' }, BOYUT)).toEqual({
      satir: 0,
      sutun: 0,
    });
    expect(hucreGezin({ satir: 20, sutun: 4 }, { key: 'ArrowDown' }, BOYUT)).toEqual({
      satir: 20,
      sutun: 4,
    });
    expect(hucreGezin({ satir: 20, sutun: 4 }, { key: 'ArrowRight' }, BOYUT)).toEqual({
      satir: 20,
      sutun: 4,
    });
  });

  it('Home/End satır içinde, Ctrl/⌘ ile ızgaranın köşeleri', () => {
    expect(hucreGezin({ satir: 5, sutun: 3 }, { key: 'Home' }, BOYUT)).toEqual({
      satir: 5,
      sutun: 0,
    });
    expect(hucreGezin({ satir: 5, sutun: 1 }, { key: 'End' }, BOYUT)).toEqual({
      satir: 5,
      sutun: 4,
    });
    expect(hucreGezin({ satir: 5, sutun: 3 }, { key: 'Home', ctrlKey: true }, BOYUT)).toEqual({
      satir: 0,
      sutun: 0,
    });
    expect(hucreGezin({ satir: 5, sutun: 1 }, { key: 'End', metaKey: true }, BOYUT)).toEqual({
      satir: 20,
      sutun: 4,
    });
  });

  it('PageDown/PageUp sayfa adımı kadar; PageUp başlığa çıkmaz', () => {
    expect(hucreGezin({ satir: 1, sutun: 0 }, { key: 'PageDown' }, BOYUT)).toEqual({
      satir: 9,
      sutun: 0,
    });
    expect(hucreGezin({ satir: 18, sutun: 0 }, { key: 'PageDown' }, BOYUT)).toEqual({
      satir: 20,
      sutun: 0,
    });
    expect(hucreGezin({ satir: 5, sutun: 0 }, { key: 'PageUp' }, BOYUT)).toEqual({
      satir: 1,
      sutun: 0,
    });
    expect(hucreGezin({ satir: 0, sutun: 2 }, { key: 'PageUp' }, BOYUT)).toEqual({
      satir: 0,
      sutun: 2,
    });
  });

  it('gezinme dışı tuşlar ve Alt/Shift kısayolları null (çağıran başka iş yapar)', () => {
    expect(hucreGezin({ satir: 1, sutun: 1 }, { key: 'Enter' }, BOYUT)).toBeNull();
    expect(hucreGezin({ satir: 1, sutun: 1 }, { key: ' ' }, BOYUT)).toBeNull();
    expect(
      hucreGezin({ satir: 0, sutun: 1 }, { key: 'ArrowLeft', altKey: true }, BOYUT),
    ).toBeNull();
    expect(
      hucreGezin({ satir: 0, sutun: 1 }, { key: 'ArrowRight', shiftKey: true }, BOYUT),
    ).toBeNull();
  });

  it('veri yokken yalnız başlık satırı; ızgara küçülünce konum içeri kırpılır', () => {
    const bos: IzgaraBoyutu = { satirSayisi: 1, sutunSayisi: 3, sayfaAdimi: 8 };
    expect(hucreGezin({ satir: 0, sutun: 1 }, { key: 'ArrowDown' }, bos)).toEqual({
      satir: 0,
      sutun: 1,
    });
    expect(konumKirp({ satir: 50, sutun: 9 }, BOYUT)).toEqual({ satir: 20, sutun: 4 });
    expect(hucreGezin({ satir: 50, sutun: 9 }, { key: 'ArrowUp' }, BOYUT)).toEqual({
      satir: 19,
      sutun: 4,
    });
  });
});
