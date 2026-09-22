import {
  GMAIL_TASLAK_KOKU,
  WHATSAPP_KOKU,
  epostaGecerliMi,
  gmailTaslakBaglantisi,
  gsmNormalize,
  whatsappBaglantisi,
} from './dis-baglantilar';

/** Blazor `rc-kira-tabs.js` `bindPaylas` davranışı — beklenen adresler elle yazılmıştır. */
describe('dış paylaşım bağlantıları', () => {
  it('kökler yalnız iki izinli adres', () => {
    expect(WHATSAPP_KOKU).toBe('https://wa.me/');
    expect(GMAIL_TASLAK_KOKU).toBe('https://mail.google.com/mail/?view=cm&fs=1');
  });

  it.each([
    ['0532 111 22 33', '905321112233'],
    ['5321112233', '905321112233'],
    ['+90 (532) 111-22-33', '905321112233'],
    ['0049 170 1234567', '491701234567'],
    ['12345', null],
    ['', null],
    [null, null],
  ])('GSM %s → %s', (girdi, beklenen) => {
    expect(gsmNormalize(girdi)).toBe(beklenen);
  });

  it('WhatsApp: numara + kodlanmış metin; geçersiz numarada null', () => {
    // Lint istisnası yalnız iki kökün TAM metnine izin verir → beklenen adres kök + kuyruk olarak yazılır.
    expect(whatsappBaglantisi('05321112233', 'Sayın Ayşe, 2026 & 50%')).toBe(
      'https://wa.me/' + '905321112233?text=Say%C4%B1n%20Ay%C5%9Fe%2C%202026%20%26%2050%25',
    );
    expect(whatsappBaglantisi('123', 'x')).toBeNull();
  });

  it('Gmail taslağı: alıcı + konu + gövde kodlanır; geçersiz adreste null', () => {
    expect(gmailTaslakBaglantisi(' ayse@ornek.test ', 'Kira Sözleşmesi 1', 'a&b')).toBe(
      'https://mail.google.com/mail/?view=cm&fs=1' +
        '&to=ayse%40ornek.test&su=Kira%20S%C3%B6zle%C5%9Fmesi%201&body=a%26b',
    );
    expect(gmailTaslakBaglantisi('@ornek.test', 'k', 'g')).toBeNull();
    expect(gmailTaslakBaglantisi('', 'k', 'g')).toBeNull();
    expect(epostaGecerliMi('a@b')).toBe(true);
  });
});
