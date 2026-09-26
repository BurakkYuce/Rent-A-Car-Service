import {
  REFRESH_DURATION_MS,
  type TazelemeDurumu,
  pickupActiveTab,
  returnActiveTab,
  bucketRows,
  count,
  resolveTab,
  collectionBody,
  isRefreshDue,
  isWritableField,
  percent,
} from './panel-modeli';

const bucket = (overdue: number, defaultTab: string) => ({
  gecikmis: Array.from({ length: overdue }, (_, i) => i),
  varsayilanSekme: defaultTab,
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
  ])('sekmeCoz(%j) → %j', (raw, expected) => expect(resolveTab(raw)).toBe(expected));

  it('dönüşler: seçim yokken gecikmiş varsa GECİKMİŞ açılır (sunucunun varsayılanı)', () => {
    expect(returnActiveTab(null, bucket(2, 'gec'))).toBe('gec');
  });

  it('dönüşler: seçim yok, gecikmiş yok → Bugün', () => {
    expect(returnActiveTab(null, bucket(0, 'bugun'))).toBe('bugun');
  });

  it('dönüşler: sunucu tanınmayan varsayılan gönderirse kural istemcide işler', () => {
    expect(returnActiveTab(null, bucket(1, '???'))).toBe('gec');
    expect(returnActiveTab(null, bucket(0, ''))).toBe('bugun');
  });

  it('dönüşler: açık seçim varsayılanı ezer (gecikmiş olsa da)', () => {
    expect(returnActiveTab('yarin', bucket(5, 'gec'))).toBe('yarin');
    expect(returnActiveTab('bugun', bucket(5, 'gec'))).toBe('bugun');
  });

  it('çıkışlar: seçim yokken DAİMA Bugün (gecikmiş çıkış bayat no-show)', () => {
    expect(pickupActiveTab(null)).toBe('bugun');
    expect(pickupActiveTab('gec')).toBe('gec');
  });

  it('kovaSatirlari sekmeye göre kovayı seçer', () => {
    const k = { gecikmis: ['a'], bugun: ['b', 'c'], yarin: [] as string[] };
    expect(bucketRows(k, 'gec')).toEqual(['a']);
    expect(bucketRows(k, 'bugun')).toEqual(['b', 'c']);
    expect(bucketRows(k, 'yarin')).toEqual([]);
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
  ])('sayi(%j) → %j', (input, expected) => expect(count(input)).toBe(expected));

  it('yüzde: 3/12 → 25, toplam 0 → 0, yuvarlama', () => {
    expect(percent(3, 12)).toBe(25);
    expect(percent(5, 0)).toBe(0);
    expect(percent(1, 3)).toBe(33);
    expect(percent('2', '3')).toBe(67);
  });
});

describe('otomatik tazeleme kararı', () => {
  const eligible: TazelemeDurumu = {
    gecen: REFRESH_DURATION_MS,
    belgeGorunur: true,
    sekmeAktif: true,
    yaziyor: false,
    mesgul: false,
  };

  it('120 sn dolunca ve her şey uygunsa tazeler', () => {
    expect(isRefreshDue(eligible)).toBe(true);
  });

  it.each<[string, Partial<TazelemeDurumu>]>([
    ['süre dolmadı', { gecen: REFRESH_DURATION_MS - 1 }],
    ['kullanıcı yazıyor / tahsilat formu açık', { yaziyor: true }],
    ['tarayıcı sekmesi gizli', { belgeGorunur: false }],
    ['uygulama sekmesi arkada', { sekmeAktif: false }],
    ['yükleme sürüyor', { mesgul: true }],
  ])('%s → ertelenir', (_, difference) => {
    expect(isRefreshDue({ ...eligible, ...difference })).toBe(false);
  });

  it('yazılabilir alan: metin/sayı girdisi, metin alanı, seçim; düğme/onay kutusu değil', () => {
    const el = (label: string, type?: string) => {
      const e = document.createElement(label);
      if (type) e.setAttribute('type', type);
      return e;
    };
    expect(isWritableField(el('input'))).toBe(true);
    expect(isWritableField(el('input', 'number'))).toBe(true);
    expect(isWritableField(el('textarea'))).toBe(true);
    expect(isWritableField(el('select'))).toBe(true);
    expect(isWritableField(el('input', 'checkbox'))).toBe(false);
    expect(isWritableField(el('button'))).toBe(false);
    expect(isWritableField(el('div'))).toBe(false);
    expect(isWritableField(null)).toBe(false);
  });
});

describe('tahsilat gövdesi (para kuralı)', () => {
  const info = {
    anahtar: '11111111-2222-4333-8444-555555555555',
    cariId: 'cari-1',
    rentalId: 'kira-1',
    doviz: 'EUR',
    varsayilanTutar: 1250.5,
  };

  it('sunucunun anahtarını AYNEN, cari/kira/dövizi yanıttan taşır; kur göndermez', () => {
    const body = collectionBody(
      info,
      { tutar: '1000.00', hesap: 'Banka', hesapId: null },
      'Hızlı tahsilat (pano) — 34 ABC 123',
    );
    expect(body).toEqual({
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
    expect('kur' in body).toBe(false);
  });
});
