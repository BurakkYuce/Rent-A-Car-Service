import {
  SECRET_FIELDS,
  SETTINGS_FIELDS,
  type SettingsDto,
  needsTxtRecord,
  settingsBody,
  settingsFormValue,
  settingsServerValues,
} from './settings-model';

/** Elle kurulmuş GET yanıtı: sırlar yalnız `*Tanimli` bayrağıyla gelir. */
const DTO = {
  firmaUnvan: 'Örnek Otomotiv A.Ş.',
  smtpHost: 'mail.ornek.test',
  smtpPort: 587,
  smtpSifreTanimli: true,
  eFaturaSifreTanimli: false,
  smsApiKeyTanimli: true,
  posApiKeyTanimli: false,
  kurElleGirisKilitli: false,
  donemselFaturalamaJob: true,
  donemselOtomatikTahsilat: false,
  domainler: [],
  whatsAppGonderimleri: [],
  surum: 'v7',
} as unknown as SettingsDto;

describe('ayarlar modeli', () => {
  it('form değeri sır alanlarını DAİMA boş kurar (yanıtta sır yok)', () => {
    const v = settingsFormValue(DTO);
    for (const s of SECRET_FIELDS) expect(v[s]).toBeNull();
    expect(v.firmaUnvan).toBe('Örnek Otomotiv A.Ş.');
    expect(v.smtpPort).toBe(587);
  });

  it('boş sır null gider (sunucu korur); yazılan sır aynen gider; surum eklenir', () => {
    const value = { ...settingsFormValue(DTO), smtpSifre: '   ', posApiKey: 'yeni-anahtar' };
    const body = settingsBody(value, 'v7');
    expect(body['smtpSifre']).toBeNull();
    expect(body['eFaturaSifre']).toBeNull();
    expect(body['posApiKey']).toBe('yeni-anahtar');
    expect(body['surum']).toBe('v7');
    expect(Object.keys(body).length).toBe(SETTINGS_FIELDS.length + 1);
  });

  it('zorunlu bool alanlar null gönderilmez', () => {
    const body = settingsBody({}, null);
    expect(body['kurElleGirisKilitli']).toBe(false);
    expect(body['donemselFaturalamaJob']).toBe(false);
    expect(body['donemselOtomatikTahsilat']).toBe(false);
    expect(body['surum']).toBeNull();
  });

  it('birleştirme değerlerinde sır anahtarı hiç yok (yazılan sır korunur)', () => {
    const v = settingsServerValues(DTO);
    for (const s of SECRET_FIELDS) expect(s in v).toBe(false);
    expect(v['smtpHost']).toBe('mail.ornek.test');
  });

  it('TXT talimatı yalnız kayıt adı ve değeri doluysa', () => {
    const base = { host: 'kirala.ornek.test', tur: 'Custom', durum: 'PendingVerification' };
    expect(
      needsTxtRecord({
        ...base,
        dogrulamaKaydi: '_racar-verify.kirala.ornek.test',
        dogrulamaDegeri: 'racar-abc',
      }),
    ).toBe(true);
    expect(needsTxtRecord({ ...base, dogrulamaKaydi: null, dogrulamaDegeri: null })).toBe(false);
  });
});
