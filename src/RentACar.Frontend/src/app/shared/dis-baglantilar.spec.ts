import {
  GMAIL_DRAFT_ROOT,
  WHATSAPP_ROOT,
  isEmailValid,
  gmailDraftLink,
  gsmNormalize,
  whatsappLink,
} from './dis-baglantilar';

/** Blazor `rc-kira-tabs.js` `bindPaylas` davranışı — beklenen adresler elle yazılmıştır. */
describe('dış paylaşım bağlantıları', () => {
  it('kökler yalnız iki izinli adres', () => {
    expect(WHATSAPP_ROOT).toBe('https://wa.me/');
    expect(GMAIL_DRAFT_ROOT).toBe('https://mail.google.com/mail/?view=cm&fs=1');
  });

  it.each([
    ['0532 111 22 33', '905321112233'],
    ['5321112233', '905321112233'],
    ['+90 (532) 111-22-33', '905321112233'],
    ['0049 170 1234567', '491701234567'],
    ['12345', null],
    ['', null],
    [null, null],
  ])('GSM %s → %s', (input, expected) => {
    expect(gsmNormalize(input)).toBe(expected);
  });

  it('WhatsApp: numara + kodlanmış metin; geçersiz numarada null', () => {
    // Lint istisnası yalnız iki kökün TAM metnine izin verir → beklenen adres kök + kuyruk olarak yazılır.
    expect(whatsappLink('05321112233', 'Sayın Ayşe, 2026 & 50%')).toBe(
      'https://wa.me/' + '905321112233?text=Say%C4%B1n%20Ay%C5%9Fe%2C%202026%20%26%2050%25',
    );
    expect(whatsappLink('123', 'x')).toBeNull();
  });

  it('Gmail taslağı: alıcı + konu + gövde kodlanır; geçersiz adreste null', () => {
    expect(gmailDraftLink(' ayse@ornek.test ', 'Kira Sözleşmesi 1', 'a&b')).toBe(
      'https://mail.google.com/mail/?view=cm&fs=1' +
        '&to=ayse%40ornek.test&su=Kira%20S%C3%B6zle%C5%9Fmesi%201&body=a%26b',
    );
    expect(gmailDraftLink('@ornek.test', 'k', 'g')).toBeNull();
    expect(gmailDraftLink('', 'k', 'g')).toBeNull();
    expect(isEmailValid('a@b')).toBe(true);
  });
});
