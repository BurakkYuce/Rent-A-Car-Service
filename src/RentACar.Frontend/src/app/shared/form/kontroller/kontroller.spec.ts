import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { provideTranslation } from '@core/i18n/ceviri';
import { SERVER_ERROR, applyServerErrors } from '@core/form/sunucu-hatalari';
import type { GunAraligi } from '@core/form/tarih-girdisi';
import { Alan } from '../alan/alan';
import { DateTimePicker } from '../tarih/date-time-picker';
import { DatePicker } from '../tarih/date-picker';
import { TextArea } from './text-area';
import { TextInput } from './text-input';
import { Anahtar, Checkbox } from './checkbox';
import { MoneyInput } from './money-input';
import { RadioGroup } from './radio-group';
import { NumberInput } from './number-input';
import { Selection } from './selection';

@Component({
  selector: 'rc-deneme-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    Alan,
    TextInput,
    TextArea,
    MoneyInput,
    NumberInput,
    Selection,
    Checkbox,
    Anahtar,
    RadioGroup,
    DatePicker,
    DateTimePicker,
  ],
  template: `
    <form [formGroup]="form">
      <rc-alan etiket="Plaka" ipucu="34 ABC 123" data-t="plaka">
        <rc-metin-girdisi formControlName="plaka" />
      </rc-alan>
      <rc-alan etiket="Açıklama" data-t="aciklama">
        <rc-metin-alani formControlName="aciklama" />
      </rc-alan>
      <rc-alan etiket="Tutar" data-t="tutar">
        <rc-para-girdisi formControlName="tutar" negatif />
      </rc-alan>
      <rc-alan etiket="Adet" data-t="adet">
        <rc-sayi-girdisi formControlName="adet" />
      </rc-alan>
      <rc-alan etiket="Durum" data-t="durum">
        <rc-secim formControlName="durum" [secenekler]="statuses" />
      </rc-alan>
      <rc-alan etiket="Kasko" data-t="kasko">
        <rc-onay-kutusu formControlName="kasko">Kasko dahil</rc-onay-kutusu>
      </rc-alan>
      <rc-alan etiket="Fatura" data-t="fatura">
        <rc-anahtar formControlName="fatura">Otomatik</rc-anahtar>
      </rc-alan>
      <rc-alan etiket="Ödeme" grup data-t="odeme">
        <rc-radyo-grubu formControlName="odeme" [secenekler]="payments" />
      </rc-alan>
      <rc-alan etiket="Çıkış" data-t="cikis">
        <rc-tarih-secici formControlName="cikis" />
      </rc-alan>
      <rc-alan etiket="Dönem" data-t="donem">
        <rc-tarih-secici formControlName="donem" aralik />
      </rc-alan>
      <rc-alan etiket="Dönüş" data-t="donus">
        <rc-tarih-saat-secici formControlName="donus" />
      </rc-alan>
    </form>
  `,
})
class FormTestHost {
  readonly statuses = [
    { deger: { kod: 1 }, etiket: 'Aktif' },
    { deger: { kod: 2 }, etiket: 'Pasif' },
  ];
  readonly payments = [
    { deger: 'nakit', etiket: 'Nakit' },
    { deger: 'kart', etiket: 'Kart' },
  ];
  readonly form = new FormGroup({
    plaka: new FormControl<string | null>(null, Validators.required),
    aciklama: new FormControl<string | null>(null),
    tutar: new FormControl<string | number | null>(1234.5),
    adet: new FormControl<number | null>(3),
    durum: new FormControl<{ kod: number } | null>(null),
    kasko: new FormControl<boolean | null>(false),
    fatura: new FormControl<boolean | null>(true),
    odeme: new FormControl<string | null>('nakit'),
    cikis: new FormControl<string | null>('2026-09-22', Validators.required),
    donem: new FormControl<GunAraligi | null>(null),
    donus: new FormControl<string | null>('2026-09-22T11:30:00+00:00'),
  });
}

@Component({
  selector: 'rc-odakta-sec-deneme',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, MoneyInput],
  template: `<rc-para-girdisi [formControl]="amount" />`,
})
class SelectOnFocusTest {
  readonly amount = new FormControl<string | number | null>(1250.5);
}

async function exchangeRate() {
  TestBed.configureTestingModule({ providers: [...provideTranslation()] });
  const fixture = TestBed.createComponent(FormTestHost);
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  const alan = (t: string): HTMLElement => {
    const a = root.querySelector<HTMLElement>(`[data-t="${t}"]`);
    if (!a) throw new Error(t);
    return a;
  };
  const input = (t: string, order = 0): HTMLInputElement => {
    const g = alan(t).querySelectorAll<HTMLInputElement>('input, textarea, select')[order];
    if (!g) throw new Error(`${t} girdisi`);
    return g;
  };
  const write = async (t: string, text: string, order = 0): Promise<void> => {
    const g = input(t, order);
    g.value = text;
    g.dispatchEvent(new Event('input'));
    await fixture.whenStable();
  };
  const release = async (t: string, order = 0): Promise<void> => {
    input(t, order).dispatchEvent(new Event('blur'));
    await fixture.whenStable();
  };
  return {
    fixture,
    kok: root,
    form: fixture.componentInstance.form,
    alan,
    girdi: input,
    yaz: write,
    birak: release,
  };
}

describe('CVA kontroller', () => {
  it('ön doldurma formu kirletmez; her kontrol kendi biçiminde gösterir', async () => {
    const { form, girdi } = await exchangeRate();
    expect(form.dirty).toBe(false);
    expect(girdi('tutar').value).toBe('1.234,50');
    expect(girdi('adet').value).toBe('3');
    expect(girdi('cikis').value).toBe('22.09.2026');
    expect(girdi('donus', 0).value).toBe('22.09.2026');
    expect(girdi('donus', 1).value).toBe('14:30'); // 11:30 UTC = 14:30 İstanbul
    expect(girdi('fatura').checked).toBe(true);
    expect(girdi('odeme', 0).checked).toBe(true);
  });

  it('metin: yazılan değer; boş metin null', async () => {
    const { form, yaz } = await exchangeRate();
    await yaz('plaka', '34 ABC 123');
    expect(form.value.plaka).toBe('34 ABC 123');
    expect(form.dirty).toBe(true);
    await yaz('plaka', '');
    expect(form.value.plaka).toBeNull();
    await yaz('aciklama', 'iki\nsatır');
    expect(form.value.aciklama).toBe('iki\nsatır');
  });

  it('para: tr yazım → invariant metin; odak gruplamasız, bırakınca biçimli', async () => {
    const { form, girdi, yaz, birak } = await exchangeRate();
    girdi('tutar').dispatchEvent(new Event('focus'));
    await Promise.resolve();
    await yaz('tutar', '1.234,56');
    expect(form.value.tutar).toBe('1234.56');
    await birak('tutar');
    expect(girdi('tutar').value).toBe('1.234,56');
    expect(form.controls.tutar.touched).toBe(true);
    await yaz('tutar', '-12,5');
    expect(form.value.tutar).toBe('-12.50');
  });

  it('para: Tab/otomatik doldurma odağında düzenleme yazımı EŞZAMANLI, tümü seçili (yazılan sona eklenmez)', async () => {
    const { form, girdi, yaz, birak } = await exchangeRate();
    await yaz('tutar', '2.600,00');
    await birak('tutar');
    expect(girdi('tutar').value).toBe('2.600,00');
    // Tab'la gelme / otomatik doldurma: önce tüm metin seçili, sonra odak.
    girdi('tutar').select();
    girdi('tutar').dispatchEvent(new Event('focus'));
    // Değişiklik algılaması BEKLENMEDEN: DOM düzenleme yazımında ve TAMAMI hâlâ seçili (yazılan yerine geçer).
    expect(girdi('tutar').value).toBe('2600,00');
    expect(girdi('tutar').selectionStart).toBe(0);
    expect(girdi('tutar').selectionEnd).toBe('2600,00'.length);
    await yaz('tutar', '500');
    expect(form.value.tutar).toBe('500.00');
  });

  it('para: YAZILAN 2’den fazla anlamlı ondalık yuvarlanmaz, alan hatası; programatik değer yuvarlanır', async () => {
    const { fixture, form, alan, girdi, yaz, birak } = await exchangeRate();
    // 3 hane → hata, değer yok (sessiz yuvarlama niyet dışı tutar gönderirdi).
    await yaz('tutar', '1,555');
    await birak('tutar');
    expect(form.value.tutar).toBeNull();
    expect(form.controls.tutar.hasError('paraFazlaHane')).toBe(true);
    expect(form.controls.tutar.hasError('paraGecersiz')).toBe(false);
    expect(girdi('tutar').value).toBe('1,555');
    expect(alan('tutar').textContent).toContain('En fazla 2 ondalık hane girilebilir.');
    // Önceden dolu öneri + sonuna yazılan rakamlar (adversarial F3).
    await yaz('tutar', '1250,5090');
    expect(form.controls.tutar.hasError('paraFazlaHane')).toBe(true);
    await yaz('tutar', '0,005');
    expect(form.value.tutar).toBeNull();
    // 2 hane ve sondaki sıfırlar geçerli.
    await yaz('tutar', '1,55');
    expect(form.value.tutar).toBe('1.55');
    expect(form.controls.tutar.valid).toBe(true);
    await yaz('tutar', '1,500');
    expect(form.value.tutar).toBe('1.50');
    // Programatik 4 hane (sunucudan numeric(19,4)) → yuvarlanır, hata yok.
    form.controls.tutar.setValue('1250.5050');
    await fixture.whenStable();
    expect(girdi('tutar').value).toBe('1.250,51');
    expect(form.controls.tutar.valid).toBe(true);
  });

  it('para (varsayılan): programatik odakta tüm metin seçili; yazılan önerinin yerine geçer', async () => {
    TestBed.configureTestingModule({ providers: [...provideTranslation()] });
    const fixture = TestBed.createComponent(SelectOnFocusTest);
    await fixture.whenStable();
    const g = (fixture.nativeElement as HTMLElement).querySelector('input');
    if (g === null) throw new Error('girdi yok');
    g.focus();
    await fixture.whenStable();
    expect(g.value).toBe('1250,50');
    expect([g.selectionStart, g.selectionEnd]).toEqual([0, g.value.length]);
  });

  it('para: KULLANICININ YAZDIĞI tutarda fare odağı seçmez (imleç yerinde); klavye odağı seçer', async () => {
    const { girdi, yaz, birak } = await exchangeRate();
    await yaz('tutar', '1.234,56'); // kullanıcı yazdı
    await birak('tutar');
    const g = girdi('tutar');
    g.dispatchEvent(new Event('pointerdown'));
    g.setSelectionRange(2, 2); // tarayıcının tık konumu
    g.dispatchEvent(new Event('focus'));
    expect(g.value).toBe('1234,56');
    expect(g.selectionEnd! - g.selectionStart!).toBe(0); // seçim yok
    await birak('tutar');
    // Sonraki klavye (Tab) odağı yine tümünü seçer — bayrak tek seferliktir.
    g.select();
    g.dispatchEvent(new Event('focus'));
    expect([g.selectionStart, g.selectionEnd]).toEqual([0, g.value.length]);
  });

  it('para (3. tur M-B): DOKUNULMAMIŞ ön-dolu tutarda fare odağı da TÜMÜNÜ seçer (sola tıklayıp yazan sona/başa eklemez)', async () => {
    const { fixture, form, girdi } = await exchangeRate();
    form.controls.tutar.setValue('2600'); // programatik ön-doldurma
    await fixture.whenStable();
    const g = girdi('tutar');
    g.dispatchEvent(new Event('pointerdown'));
    g.setSelectionRange(0, 0); // metnin soluna tık
    g.dispatchEvent(new Event('focus'));
    expect(g.value).toBe('2600,00');
    expect([g.selectionStart, g.selectionEnd]).toEqual([0, g.value.length]);
  });

  it('para (Q1): basış odak üretmeden iptal edilirse (dokunmatik kaydırma) sonraki Tab odağı tümünü seçer', async () => {
    const { girdi, yaz, birak } = await exchangeRate();
    await yaz('tutar', '1.234,56');
    await birak('tutar');
    const g = girdi('tutar');
    g.dispatchEvent(new Event('pointerdown'));
    g.dispatchEvent(new Event('pointercancel'));
    g.select();
    g.dispatchEvent(new Event('focus'));
    expect([g.selectionStart, g.selectionEnd]).toEqual([0, g.value.length]);
  });

  it('para: anlaşılmayan yazım değer değil hata; metin ekranda kalır, mesaj alanın altında', async () => {
    const { form, alan, girdi, yaz, birak } = await exchangeRate();
    await yaz('tutar', '12,3a');
    await birak('tutar');
    expect(form.value.tutar).toBeNull();
    expect(form.controls.tutar.hasError('paraGecersiz')).toBe(true);
    expect(girdi('tutar').value).toBe('12,3a');
    expect(girdi('tutar').getAttribute('aria-invalid')).toBe('true');
    expect(alan('tutar').textContent).toContain('Geçerli bir tutar girin');
    await yaz('tutar', '12,30');
    expect(form.controls.tutar.valid).toBe(true);
  });

  it('sayı: binlik nokta, fazla kesir reddedilir', async () => {
    const { form, yaz } = await exchangeRate();
    await yaz('adet', '1.234');
    expect(form.value.adet).toBe(1234);
    await yaz('adet', '12,5');
    expect(form.value.adet).toBeNull();
    expect(form.controls.adet.hasError('sayiGecersiz')).toBe(true);
  });

  it('seçim: nesne değer sıra numarasıyla taşınır', async () => {
    const { form, fixture, girdi } = await exchangeRate();
    const selection = girdi('durum') as unknown as HTMLSelectElement;
    selection.value = '1';
    selection.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    expect(form.value.durum).toEqual({ kod: 2 });
    form.controls.durum.setValue(fixture.componentInstance.statuses[0]?.deger ?? null);
    await fixture.whenStable();
    expect(selection.value).toBe('0');
  });

  it('onay kutusu, anahtar ve radyo', async () => {
    const { form, fixture, girdi } = await exchangeRate();
    girdi('kasko').click();
    girdi('fatura').click();
    girdi('odeme', 1).click();
    await fixture.whenStable();
    expect(form.value.kasko).toBe(true);
    expect(form.value.fatura).toBe(false);
    expect(form.value.odeme).toBe('kart');
    expect(girdi('fatura').getAttribute('role')).toBe('switch');
  });

  it('tarih: gün metni, geçersiz gün hata, gidiş-dönüş kaymasız', async () => {
    const { form, girdi, yaz, birak } = await exchangeRate();
    await yaz('cikis', '1.10.2026');
    expect(form.value.cikis).toBe('2026-10-01');
    await birak('cikis');
    expect(girdi('cikis').value).toBe('01.10.2026');
    await yaz('cikis', '31.02.2026');
    expect(form.controls.cikis.hasError('tarihGecersiz')).toBe(true);
    // Kaydet → yeniden aç → kaydet: aynı gün.
    form.controls.cikis.setValue('2026-03-29');
    await birak('cikis');
    expect(girdi('cikis').value).toBe('29.03.2026');
    await yaz('cikis', girdi('cikis').value);
    expect(form.value.cikis).toBe('2026-03-29');
  });

  it('tarih aralığı: metin ve sıra hatası', async () => {
    const { form, yaz } = await exchangeRate();
    await yaz('donem', '01.09.2026 – 30.09.2026');
    expect(form.value.donem).toEqual({ baslangic: '2026-09-01', bitis: '2026-09-30' });
    await yaz('donem', '30.09.2026 – 01.09.2026');
    expect(form.controls.donem.hasError('tarihSirasi')).toBe(true);
  });

  it('tarih: takvimden gün seçimi', async () => {
    const { form, fixture, alan } = await exchangeRate();
    alan('cikis').querySelector<HTMLButtonElement>('button')?.click();
    await fixture.whenStable();
    const day = document.querySelector<HTMLButtonElement>('[data-gun="2026-09-25"]');
    expect(day).not.toBeNull();
    day?.click();
    await fixture.whenStable();
    expect(form.value.cikis).toBe('2026-09-25');
    expect(document.querySelector('[data-gun]')).toBeNull(); // panel kapandı
  });

  it('tarih-saat: UTC an; saat eksikse hata; iki kez kaydetmede kayma yok', async () => {
    const { form, girdi, yaz } = await exchangeRate();
    await yaz('donus', '15:00', 1);
    expect(form.value.donus).toBe('2026-09-22T12:00:00.000Z');
    await yaz('donus', '', 1);
    expect(form.controls.donus.hasError('saatGecersiz')).toBe(true);
    await yaz('donus', '01:00', 1);
    expect(form.value.donus).toBe('2026-09-21T22:00:00.000Z');
    // Kaydedilen değer forma geri yazılır → aynı görüntü → yeniden kaydet aynı an.
    form.controls.donus.setValue(form.value.donus ?? null);
    await yaz('donus', girdi('donus', 1).value, 1);
    expect(form.value.donus).toBe('2026-09-21T22:00:00.000Z');
    expect(girdi('donus', 0).value).toBe('22.09.2026');
  });

  it('pasif kontrol', async () => {
    const { form, fixture, girdi } = await exchangeRate();
    form.controls.tutar.disable();
    form.controls.odeme.disable();
    await fixture.whenStable();
    expect(girdi('tutar').disabled).toBe(true);
    expect(girdi('odeme', 1).disabled).toBe(true);
  });
});

describe('rc-alan', () => {
  it('etiket kontrolü gösterir; zorunlu işareti doğrulayıcıdan; ipucu describedby', async () => {
    const { alan, girdi } = await exchangeRate();
    const label = alan('plaka').querySelector('label');
    expect(label?.getAttribute('for')).toBe(girdi('plaka').id);
    expect(label?.textContent).toContain('*');
    expect(alan('aciklama').querySelector('.rc-alan__zorunlu')).toBeNull();
    expect(girdi('plaka').getAttribute('aria-required')).toBe('true');
    const hint = alan('plaka').querySelector('.rc-alan__ipucu');
    expect(girdi('plaka').getAttribute('aria-describedby')).toBe(hint?.id);
  });

  it('istemci hatası dokunulunca görünür; aria-invalid + describedby hata kimliği', async () => {
    const { alan, girdi, birak } = await exchangeRate();
    expect(alan('plaka').querySelector('.rc-alan__hata')?.textContent?.trim()).toBe('');
    expect(girdi('plaka').getAttribute('aria-invalid')).toBeNull();
    await birak('plaka');
    const error = alan('plaka').querySelector('.rc-alan__hata');
    expect(error?.textContent).toContain('Bu alan zorunlu.');
    expect(girdi('plaka').getAttribute('aria-invalid')).toBe('true');
    expect(girdi('plaka').getAttribute('aria-describedby')?.split(' ')).toContain(error?.id);
  });

  it('sunucu hatası hemen görünür, değer korunur, yazınca kalkar', async () => {
    const { form, fixture, alan, yaz } = await exchangeRate();
    await yaz('plaka', '34 ABC 123');
    applyServerErrors(form, { Plaka: ['Bu plaka zaten kayıtlı.'] });
    await fixture.whenStable();
    expect(alan('plaka').textContent).toContain('Bu plaka zaten kayıtlı.');
    expect(form.value.plaka).toBe('34 ABC 123');
    await yaz('plaka', '34 ABC 124');
    expect(form.controls.plaka.hasError(SERVER_ERROR)).toBe(false);
    expect(alan('plaka').textContent).not.toContain('Bu plaka zaten kayıtlı.');
  });

  it('radyo grubu alan etiketiyle adlandırılır', async () => {
    const { alan } = await exchangeRate();
    const group = alan('odeme').querySelector('[role="radiogroup"]');
    const label = alan('odeme').querySelector('.rc-alan__etiket');
    expect(group?.getAttribute('aria-labelledby')).toBe(label?.id);
  });
});
