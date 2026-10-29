import {
  EINVOICE_SCENARIOS,
  SECRET_FIELDS,
  SETTINGS_FIELDS,
  SITE_THEMES,
  type SettingsDto,
  integratorOptions,
  isHexColor,
  needsTxtRecord,
  paletteAfterThemeChange,
  palettesFor,
  secretsToClear,
  settingsBody,
  settingsFormValue,
  settingsServerValues,
  siteThemeFormValue,
  themePreviewUrl,
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
  it('site teması: kayıtsızsa anahtar seçili; kayıtlıysa aynen; önizleme adresi ?tema=', () => {
    expect(SITE_THEMES).toEqual(['anahtar', 'kiyas', 'pazar', 'vitrin', 'rota']);
    expect(settingsFormValue(DTO).siteTemasi).toBe('anahtar');
    expect(settingsFormValue({ ...DTO, siteTemasi: 'pazar' } as SettingsDto).siteTemasi).toBe(
      'pazar',
    );
    expect(settingsFormValue({ ...DTO, siteTemasi: 'rota' } as SettingsDto).siteTemasi).toBe(
      'rota',
    );
    expect(themePreviewUrl('kiyi.site.test', 'vitrin')).toBe('//kiyi.site.test/?tema=vitrin');
    expect(themePreviewUrl('kiyi.site.test', 'pazar')).toBe('//kiyi.site.test/?tema=pazar');
  });

  it('site teması: eski anahtar yeni karşılığıyla seçili görünür (tarife → pazar, kontuar → rota)', () => {
    expect(settingsFormValue({ ...DTO, siteTemasi: 'tarife' } as SettingsDto).siteTemasi).toBe(
      'pazar',
    );
    expect(settingsFormValue({ ...DTO, siteTemasi: ' Kontuar ' } as SettingsDto).siteTemasi).toBe(
      'rota',
    );
    expect(siteThemeFormValue('')).toBe('anahtar');
    expect(siteThemeFormValue('VITRIN')).toBe('vitrin');
    expect(siteThemeFormValue('TARIFE')).toBe('pazar'); // Türkçe küçültme ı üretir; anahtar yine bulunur
    // Tanınmayan değer uydurulmaz: aynen kalır (radyo seçimsiz; sunucu kaydı reddeder).
    expect(siteThemeFormValue('mavi')).toBe('mavi');
    // Gövdeye yeni anahtar gider.
    const body = settingsBody(
      settingsFormValue({ ...DTO, siteTemasi: 'kontuar' } as SettingsDto),
      'v7',
    );
    expect(body['siteTemasi']).toBe('rota');
  });

  it('palet setleri temaya göre; boş tema varsayılan tema (anahtar); katalog yoksa boş', () => {
    const swatch = {
      zemin: '#ffffff',
      murekkep: '#000000',
      ana: '#0b5d6b',
      vurgu: null,
      geceZemin: '#111111',
    };
    const dto = {
      ...DTO,
      sitePaletSecenekleri: [
        { tema: 'anahtar', paletler: [{ anahtar: 'zumrut', ad: 'Zümrüt', ...swatch }] },
        { tema: 'pazar', paletler: [{ anahtar: 'petrol', ad: 'Petrol', ...swatch }] },
        {
          tema: 'vitrin',
          paletler: [
            { anahtar: 'grafit', ad: 'Grafit', ...swatch },
            { anahtar: 'bordo', ad: 'Bordo', ...swatch },
          ],
        },
      ],
    } as unknown as SettingsDto;
    expect(palettesFor(dto, 'vitrin').map((p) => p.anahtar)).toEqual(['grafit', 'bordo']);
    expect(palettesFor(dto, 'pazar').map((p) => p.anahtar)).toEqual(['petrol']);
    expect(palettesFor(dto, null).map((p) => p.anahtar)).toEqual(['zumrut']);
    expect(palettesFor(dto, '').map((p) => p.anahtar)).toEqual(['zumrut']);
    expect(palettesFor(null, 'vitrin')).toEqual([]);
    expect(settingsFormValue(dto).sitePaleti).toBeNull(); // kayıtsız = temanın varsayılanı (null gider)

    // Tema değişince: yeni temaya aitse korunur, değilse temizlenir; katalog yoksa dokunulmaz.
    const vitrin = palettesFor(dto, 'vitrin');
    expect(paletteAfterThemeChange('bordo', vitrin)).toBe('bordo');
    expect(paletteAfterThemeChange('petrol', vitrin)).toBeNull();
    expect(paletteAfterThemeChange(null, vitrin)).toBeNull();
    expect(paletteAfterThemeChange('', vitrin)).toBeNull();
    expect(paletteAfterThemeChange('petrol', [])).toBe('petrol');
  });

  it('önizleme adresi tema + palet + yalnız geçerli renk (%23 ile) taşır', () => {
    expect(themePreviewUrl('kiyi.site.test', 'vitrin', 'gece-mavisi', '#C0392B')).toBe(
      '//kiyi.site.test/?tema=vitrin&palet=gece-mavisi&renk=%23C0392B',
    );
    expect(themePreviewUrl('kiyi.site.test', 'vitrin', null, 'kirmizi')).toBe(
      '//kiyi.site.test/?tema=vitrin',
    );
    expect(themePreviewUrl('kiyi.site.test', 'pazar', 'lacivert', null)).toBe(
      '//kiyi.site.test/?tema=pazar&palet=lacivert',
    );
    expect(isHexColor('#0b5d6b')).toBe(true);
    for (const bad of ['0b5d6b', '#0b5d6', '#0b5d6bb', '#0b5d6g', null, 7]) {
      expect(isHexColor(bad)).toBe(false);
    }
  });

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
    expect(Object.keys(body).length).toBe(SETTINGS_FIELDS.length + 4 + 1); // + 4 silme bayrağı
  });

  it('silme bayrakları: form DAİMA kapalı kurar, yalnız işaretlenen true gider, birleştirmede yok', () => {
    const v = settingsFormValue(DTO);
    expect(v.smtpSifreTemizle).toBe(false);
    expect(secretsToClear(v)).toEqual([]);

    const value = { ...v, smtpSifreTemizle: true, posApiKeyTemizle: true };
    expect(secretsToClear(value)).toEqual(['posApiKey', 'smtpSifre']);
    const body = settingsBody(value, 'v7');
    expect(body['smtpSifreTemizle']).toBe(true);
    expect(body['posApiKeyTemizle']).toBe(true);
    expect(body['eFaturaSifreTemizle']).toBe(false);
    expect(body['smsApiKeyTemizle']).toBe(false);
    expect(body['smtpSifre']).toBeNull(); // boş sır + bayrak → sunucu siler

    expect('smtpSifreTemizle' in settingsServerValues(DTO)).toBe(false);
  });

  it('zorunlu bool alanlar null gönderilmez', () => {
    const body = settingsBody({}, null);
    expect(body['kurElleGirisKilitli']).toBe(false);
    expect(body['donemselFaturalamaJob']).toBe(false);
    expect(body['donemselOtomatikTahsilat']).toBe(false);
    expect(body['eFaturaCanliOrtam']).toBe(false);
    expect(body['siteKmSinirsiz']).toBe(false);
    expect(body['surum']).toBeNull();
  });

  it('site bilgileri tam PUT gövdesinde aynen gider; puan tarihi (salt okunur) gitmez', () => {
    const value = {
      ...settingsFormValue({
        ...DTO,
        siteGooglePuani: 4.8,
        siteGooglePuanTarihi: '2026-01-02T03:04:05Z',
      } as SettingsDto),
      siteDepozitoTutari: 5000,
      siteKmSinirsiz: true,
      siteGoogleProfilUrl: 'g.page/ornek', // biçim sunucuda; istemci aynen taşır
    };
    const body = settingsBody(value, 'v7');
    expect(body['siteGooglePuani']).toBe(4.8);
    expect(body['siteDepozitoTutari']).toBe(5000);
    expect(body['siteKmSinirsiz']).toBe(true);
    expect(body['siteGoogleProfilUrl']).toBe('g.page/ornek');
    expect(body['siteMinYas']).toBeNull();
    expect('siteGooglePuanTarihi' in body).toBe(false);
  });

  it('e-belge ayarları tam PUT gövdesinde; şifre yalnız yazılınca, ortam bayrağı bool', () => {
    const value = {
      ...settingsFormValue(DTO),
      eFaturaEntegrator: 'test-entegrator',
      eFaturaGondericiEtiket: 'urn:mail:defaultgb@ornek.test',
      eFaturaSenaryo: 'TICARIFATURA',
      eFaturaCanliOrtam: true,
      firmaIl: 'İstanbul',
      firmaIlce: 'Şişli',
      eFaturaSifre: 'entegrator-anahtari',
    };
    const body = settingsBody(value, 'v7');
    expect(body['eFaturaEntegrator']).toBe('test-entegrator');
    expect(body['eFaturaGondericiEtiket']).toBe('urn:mail:defaultgb@ornek.test');
    expect(body['eFaturaSenaryo']).toBe('TICARIFATURA');
    expect(body['eFaturaCanliOrtam']).toBe(true);
    expect(body['firmaIl']).toBe('İstanbul');
    expect(body['eFaturaSifre']).toBe('entegrator-anahtari');
    // Yanıt tarafı salt-okunur alanlar gövdeye girmez.
    for (const k of ['eFaturaBagli', 'eFaturaDurumMesaji', 'eFaturaEntegratorSecenekleri']) {
      expect(k in body).toBe(false);
    }
  });

  it('entegratör seçenekleri: kayıtlı adaptörler; listede olmayan kayıtlı değer düşmez', () => {
    expect(integratorOptions(null)).toEqual([]);
    expect(integratorOptions(DTO)).toEqual([]); // bu sürümde adaptör yok
    const withList = {
      ...DTO,
      eFaturaEntegratorSecenekleri: [{ anahtar: 'test-entegrator', ad: 'Test' }],
    } as SettingsDto;
    expect(integratorOptions(withList)).toEqual([{ deger: 'test-entegrator', etiket: 'Test' }]);
    const removed = { ...DTO, eFaturaEntegrator: 'eski-adaptor' } as SettingsDto;
    expect(integratorOptions(removed)).toEqual([{ deger: 'eski-adaptor', etiket: 'eski-adaptor' }]);
    expect(EINVOICE_SCENARIOS).toEqual(['TEMELFATURA', 'TICARIFATURA']);
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
