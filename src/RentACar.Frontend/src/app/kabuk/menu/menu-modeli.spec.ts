import { etkinKayit, menuAra, menuHedefi, menuModeliKur } from './menu-modeli';
import { ORNEK_MENU } from './menu-ornegi';

describe('menu modeli', () => {
  const model = menuModeliKur(ORNEK_MENU);

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
    expect(model.tumu).toHaveLength(ORNEK_MENU.ogeler.length);
    expect(model.tumu.some((k) => k.grup === 'Finans')).toBe(false);
  });

  it('hedef: spa → router yolu (/app öneki atılır), diğer sahip → Blazor tam sayfa; güvensiz rota atılır', () => {
    expect(menuHedefi({ rota: '/app/vitrin/tablo', sahip: 'spa' })).toEqual({
      tur: 'spa',
      yol: '/vitrin/tablo',
    });
    expect(menuHedefi({ rota: '/app', sahip: 'spa' })).toEqual({ tur: 'spa', yol: '/' });
    expect(menuHedefi({ rota: '/kasa', sahip: 'blazor' })).toEqual({
      tur: 'blazor',
      adres: '/kasa',
    });
    expect(menuHedefi({ rota: '/kasa', sahip: 'bilinmeyen' })?.tur).toBe('blazor');
    expect(menuHedefi({ rota: '//kotu.example', sahip: 'blazor' })).toBeNull();
    expect(menuHedefi({ rota: 'javascript:alert(1)', sahip: 'blazor' })).toBeNull();
    expect(menuHedefi({ rota: '/\\kotu', sahip: 'blazor' })).toBeNull();
  });

  it('etkin sayfa: tam ya da /-sınırlı önek eşleşmesi, en uzun kazanır; Blazor öğesi SPA yolunda etkin olmaz', () => {
    expect(etkinKayit(model, '/vitrin/form')?.etiket).toBe('Form vitrini');
    expect(etkinKayit(model, '/vitrin/tablo/detay')?.etiket).toBe('Tablo vitrini');
    expect(etkinKayit(model, '/vitrin/formlar')).toBeNull();
    expect(etkinKayit(model, '/kiralar')).toBeNull();
    expect(etkinKayit(model, '/')).toBeNull();
  });

  it('palet araması Türkçe-gevşek: "İş" ve "is" aynı öğeyi bulur; "ISIK" → "Işık Raporu"', () => {
    const etiketler = (sorgu: string) => menuAra(model.tumu, sorgu).map((k) => k.etiket);
    expect(etiketler('İş')).toContain('İş Emirleri');
    expect(etiketler('is')).toContain('İş Emirleri');
    expect(etiketler('iş emir')).toEqual(['İş Emirleri']);
    expect(etiketler('ISIK')).toEqual(['Işık Raporu']);
    // Grup adında da aranır; etiket eşleşmesi önce gelir.
    expect(etiketler('sigorta')).toEqual(['İş Emirleri', 'Işık Raporu']);
    expect(etiketler('bulunmayan')).toEqual([]);
  });

  it('aynı ekran iki grupta: palette tek sonuç; boş sorgu tüm menüyü sırayla verir', () => {
    expect(menuAra(model.tumu, 'arac tip').map((k) => k.grup)).toEqual(['Araçlar']);
    const tumu = menuAra(model.tumu, '');
    expect(tumu[0]?.etiket).toBe('Yeni Rezervasyon');
    expect(tumu).toHaveLength(ORNEK_MENU.ogeler.length - 1);
  });
});
