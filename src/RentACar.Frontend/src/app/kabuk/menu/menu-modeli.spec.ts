import { activeEntry, searchMenu, menuTarget, buildMenuModel } from './menu-modeli';
import { SAMPLE_MENU } from './menu-ornegi';

describe('menu modeli', () => {
  const model = buildMenuModel(SAMPLE_MENU);

  it('sunucu sırasıyla: hızlı bağlantılar ayrı, grup ilk öğesinin yerinde, grupsuz öğe tek başına', () => {
    expect(model.hizli.map((k) => k.etiket)).toEqual(['Yeni Rezervasyon']);
    expect(model.bloklar.map((b) => (b.tur === 'grup' ? `[${b.ad}]` : b.kayit.etiket))).toEqual([
      '[Araçlar]',
      'Panel',
      '[Vitrin]',
      '[Kira]',
      '[Servis & Sigorta]',
      '[Tanımlar]',
      'Bildirimler',
    ]);
    expect(model.rozetler.get('okunmamis-bildirim')).toBe(3);
  });

  it('istemci SÜZMEZ: sunucunun göndermediği öğe (Finans) yok, gönderdiği her öğe var', () => {
    expect(model.tumu).toHaveLength(SAMPLE_MENU.ogeler.length);
    expect(model.tumu.some((k) => k.grup === 'Finans')).toBe(false);
  });

  it('hedef: spa → router yolu (/app öneki atılır), diğer sahip → Blazor tam sayfa; güvensiz rota atılır', () => {
    expect(menuTarget({ rota: '/app/vitrin/tablo', sahip: 'spa' })).toEqual({
      tur: 'spa',
      yol: '/vitrin/tablo',
    });
    expect(menuTarget({ rota: '/app', sahip: 'spa' })).toEqual({ tur: 'spa', yol: '/' });
    expect(menuTarget({ rota: '/kasa', sahip: 'blazor' })).toEqual({
      tur: 'blazor',
      adres: '/kasa',
    });
    expect(menuTarget({ rota: '/kasa', sahip: 'bilinmeyen' })?.tur).toBe('blazor');
    expect(menuTarget({ rota: '//kotu.example', sahip: 'blazor' })).toBeNull();
    expect(menuTarget({ rota: 'javascript:alert(1)', sahip: 'blazor' })).toBeNull();
    expect(menuTarget({ rota: '/\\kotu', sahip: 'blazor' })).toBeNull();
  });

  it('etkin sayfa: tam ya da /-sınırlı önek eşleşmesi, en uzun kazanır; Blazor öğesi SPA yolunda etkin olmaz', () => {
    expect(activeEntry(model, '/vitrin/form')?.etiket).toBe('Form vitrini');
    expect(activeEntry(model, '/vitrin/tablo/detay')?.etiket).toBe('Tablo vitrini');
    expect(activeEntry(model, '/vitrin/formlar')).toBeNull();
    expect(activeEntry(model, '/kiralar')).toBeNull();
    expect(activeEntry(model, '/')).toBeNull();
  });

  it('palet araması Türkçe-gevşek: "İş" ve "is" aynı öğeyi bulur; "ISIK" → "Işık Raporu"', () => {
    const labels = (query: string) => searchMenu(model.tumu, query).map((k) => k.etiket);
    expect(labels('İş')).toContain('İş Emirleri');
    expect(labels('is')).toContain('İş Emirleri');
    expect(labels('iş emir')).toEqual(['İş Emirleri']);
    expect(labels('ISIK')).toEqual(['Işık Raporu']);
    // Grup adında da aranır; etiket eşleşmesi önce gelir.
    expect(labels('sigorta')).toEqual(['İş Emirleri', 'Işık Raporu']);
    expect(labels('bulunmayan')).toEqual([]);
  });

  it('aynı ekran iki grupta: palette tek sonuç; boş sorgu tüm menüyü sırayla verir', () => {
    expect(searchMenu(model.tumu, 'arac tip').map((k) => k.grup)).toEqual(['Araçlar']);
    const all = searchMenu(model.tumu, '');
    expect(all[0]?.etiket).toBe('Yeni Rezervasyon');
    expect(all).toHaveLength(SAMPLE_MENU.ogeler.length - 1);
  });
});
