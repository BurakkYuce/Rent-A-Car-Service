import {
  TAZELEME_SURESI_MS,
  type TazelemeDurumu,
  cikisEtkinSekme,
  donusEtkinSekme,
  kovaSatirlari,
  sayi,
  sekmeCoz,
  tahsilatGovdesi,
  tazelemeZamaniMi,
  yazilabilirAlanMi,
  yuzde,
} from './panel-modeli';

const kova = (gecikmis: number, varsayilanSekme: string) => ({
  gecikmis: Array.from({ length: gecikmis }, (_, i) => i),
  varsayilanSekme,
});

describe('panel sekme kuralı (Blazor PanelSekme)', () => {
  it.each([
    ['gec', 'gec'],
    ['BUGUN', 'bugun'],
    [' Yarin ', 'yarin'],
    ['', null],
    ['dun', null],
    [null, null],
    [3, null],
  ])('sekmeCoz(%j) → %j', (ham, beklenen) => expect(sekmeCoz(ham)).toBe(beklenen));

  it('dönüşler: seçim yokken gecikmiş varsa GECİKMİŞ açılır (sunucunun varsayılanı)', () => {
    expect(donusEtkinSekme(null, kova(2, 'gec'))).toBe('gec');
  });

  it('dönüşler: seçim yok, gecikmiş yok → Bugün', () => {
    expect(donusEtkinSekme(null, kova(0, 'bugun'))).toBe('bugun');
  });

  it('dönüşler: sunucu tanınmayan varsayılan gönderirse kural istemcide işler', () => {
    expect(donusEtkinSekme(null, kova(1, '???'))).toBe('gec');
    expect(donusEtkinSekme(null, kova(0, ''))).toBe('bugun');
  });

  it('dönüşler: açık seçim varsayılanı ezer (gecikmiş olsa da)', () => {
    expect(donusEtkinSekme('yarin', kova(5, 'gec'))).toBe('yarin');
    expect(donusEtkinSekme('bugun', kova(5, 'gec'))).toBe('bugun');
  });

  it('çıkışlar: seçim yokken DAİMA Bugün (gecikmiş çıkış bayat no-show)', () => {
    expect(cikisEtkinSekme(null)).toBe('bugun');
    expect(cikisEtkinSekme('gec')).toBe('gec');
  });

  it('kovaSatirlari sekmeye göre kovayı seçer', () => {
    const k = { gecikmis: ['a'], bugun: ['b', 'c'], yarin: [] as string[] };
    expect(kovaSatirlari(k, 'gec')).toEqual(['a']);
    expect(kovaSatirlari(k, 'bugun')).toEqual(['b', 'c']);
    expect(kovaSatirlari(k, 'yarin')).toEqual([]);
  });
});

describe('sayı yardımcıları', () => {
  it.each([
    [12, 12],
    ['12.5', 12.5],
    ['', null],
    [null, null],
    [undefined, null],
    ['abc', null],
  ])('sayi(%j) → %j', (girdi, beklenen) => expect(sayi(girdi)).toBe(beklenen));

  it('yüzde: 3/12 → 25, toplam 0 → 0, yuvarlama', () => {
    expect(yuzde(3, 12)).toBe(25);
    expect(yuzde(5, 0)).toBe(0);
    expect(yuzde(1, 3)).toBe(33);
    expect(yuzde('2', '3')).toBe(67);
  });
});

describe('otomatik tazeleme kararı', () => {
  const uygun: TazelemeDurumu = {
    gecen: TAZELEME_SURESI_MS,
    belgeGorunur: true,
    sekmeAktif: true,
    yaziyor: false,
    mesgul: false,
  };

  it('120 sn dolunca ve her şey uygunsa tazeler', () => {
    expect(tazelemeZamaniMi(uygun)).toBe(true);
  });

  it.each<[string, Partial<TazelemeDurumu>]>([
    ['süre dolmadı', { gecen: TAZELEME_SURESI_MS - 1 }],
    ['kullanıcı yazıyor / tahsilat formu açık', { yaziyor: true }],
    ['tarayıcı sekmesi gizli', { belgeGorunur: false }],
    ['uygulama sekmesi arkada', { sekmeAktif: false }],
    ['yükleme sürüyor', { mesgul: true }],
  ])('%s → ertelenir', (_, fark) => {
    expect(tazelemeZamaniMi({ ...uygun, ...fark })).toBe(false);
  });

  it('yazılabilir alan: metin/sayı girdisi, metin alanı, seçim; düğme/onay kutusu değil', () => {
    const el = (etiket: string, tur?: string) => {
      const e = document.createElement(etiket);
      if (tur) e.setAttribute('type', tur);
      return e;
    };
    expect(yazilabilirAlanMi(el('input'))).toBe(true);
    expect(yazilabilirAlanMi(el('input', 'number'))).toBe(true);
    expect(yazilabilirAlanMi(el('textarea'))).toBe(true);
    expect(yazilabilirAlanMi(el('select'))).toBe(true);
    expect(yazilabilirAlanMi(el('input', 'checkbox'))).toBe(false);
    expect(yazilabilirAlanMi(el('button'))).toBe(false);
    expect(yazilabilirAlanMi(el('div'))).toBe(false);
    expect(yazilabilirAlanMi(null)).toBe(false);
  });
});

describe('tahsilat gövdesi (para kuralı)', () => {
  const bilgi = {
    anahtar: '11111111-2222-4333-8444-555555555555',
    cariId: 'cari-1',
    rentalId: 'kira-1',
    doviz: 'EUR',
    varsayilanTutar: 1250.5,
  };

  it('sunucunun anahtarını AYNEN, cari/kira/dövizi yanıttan taşır; kur göndermez', () => {
    const govde = tahsilatGovdesi(
      bilgi,
      { tutar: '1000.00', hesap: 'Banka', hesapId: null },
      'Hızlı tahsilat (pano) — 34 ABC 123',
    );
    expect(govde).toEqual({
      cariId: 'cari-1',
      kiraId: 'kira-1',
      tutar: '1000.00',
      hesap: 'Banka',
      hesapId: null,
      doviz: 'EUR',
      kanal: 'Masaüstü',
      aciklama: 'Hızlı tahsilat (pano) — 34 ABC 123',
      tahsilatAnahtar: '11111111-2222-4333-8444-555555555555',
    });
    expect('kur' in govde).toBe(false);
  });
});
