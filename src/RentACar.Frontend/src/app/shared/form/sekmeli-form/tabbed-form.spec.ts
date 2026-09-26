import { ChangeDetectionStrategy, Component, viewChild } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Observable, Subject } from 'rxjs';
import { ApiHatasi } from '@core/api/api-hatasi';
import { provideTranslation } from '@core/i18n/ceviri';
import { Alan } from '../alan/alan';
import { formSubmission } from '../form-submission';
import { FormErrors } from '../form-errors';
import { TextInput } from '../kontroller/text-input';
import { TabPanel, TabbedForm } from './tabbed-form';

@Component({
  selector: 'rc-deneme-sekmeli',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, Alan, TextInput, TabbedForm, TabPanel, FormErrors],
  template: `
    <form [formGroup]="form" (ngSubmit)="kaydet()">
      <rc-sekmeli-form [sekmeler]="tabs" etiket="Bölümler">
        <section rcSekmePaneli="genel" baslik="Genel">
          <rc-alan etiket="Ad"><rc-metin-girdisi formControlName="ad" /></rc-alan>
        </section>
        <section rcSekmePaneli="odeme" baslik="Ödeme">
          <rc-alan etiket="IBAN"><rc-metin-girdisi formControlName="iban" /></rc-alan>
        </section>
        <div rcYanPanel>
          <rc-form-hatalari [hatalar]="submission.genelHatalar()" />
          <button type="submit" [disabled]="submission.gonderiliyor()">Kaydet</button>
        </div>
      </rc-sekmeli-form>
    </form>
  `,
})
class TabbedTestHost {
  readonly sekmeli = viewChild.required(TabbedForm);
  readonly tabs = [
    { kimlik: 'genel', etiket: 'Genel' },
    { kimlik: 'odeme', etiket: 'Ödeme' },
  ];
  readonly form = new FormGroup({
    ad: new FormControl<string | null>('Ayşe', Validators.required),
    iban: new FormControl<string | null>(null, Validators.required),
  });
  readonly submission = formSubmission();
  requests: { anahtar: string; yanit: Subject<unknown> }[] = [];

  kaydet(): void {
    this.submission.gonder(
      this.form,
      (key): Observable<unknown> => {
        const response = new Subject<unknown>();
        this.requests.push({ anahtar: key, yanit: response });
        return response;
      },
      { gecersiz: () => this.sekmeli().goToFirstInvalid() },
    );
  }
}

async function exchangeRate(hash = '') {
  history.replaceState(null, '', `/app/vitrin${hash}`);
  TestBed.configureTestingModule({ providers: [...provideTranslation()] });
  const fixture = TestBed.createComponent(TabbedTestHost);
  document.body.appendChild(fixture.nativeElement as HTMLElement);
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  const tab = (name: string) =>
    [...root.querySelectorAll<HTMLButtonElement>('[role="tab"]')].find((s) =>
      s.textContent?.includes(name),
    );
  const panel = (k: string) => root.querySelector<HTMLElement>(`[data-rc-sekme="${k}"]`);
  const gonder = async () => {
    root.querySelector<HTMLButtonElement>('button[type="submit"]')?.click();
    await fixture.whenStable();
    await fixture.whenStable();
  };
  return { fixture, kok: root, sekme: tab, panel, gonder, b: fixture.componentInstance };
}

describe('rc-sekmeli-form', () => {
  it('ilk sekme etkin; tıklama paneli değiştirir ve #sekme= yazar (yol korunur)', async () => {
    const { fixture, sekme, panel } = await exchangeRate();
    expect(sekme('Genel')?.getAttribute('aria-selected')).toBe('true');
    expect(panel('odeme')?.hidden).toBe(true);
    sekme('Ödeme')?.click();
    await fixture.whenStable();
    expect(panel('odeme')?.hidden).toBe(false);
    expect(panel('genel')?.hidden).toBe(true);
    expect(location.pathname).toBe('/app/vitrin');
    expect(location.hash).toBe('#sekme=odeme');
    expect(panel('odeme')?.getAttribute('aria-labelledby')).toBe(sekme('Ödeme')?.id);
  });

  it('derin bağlantı: #sekme=odeme ile açılır; ok tuşlarıyla gezinir', async () => {
    const { fixture, sekme, panel } = await exchangeRate('#sekme=odeme');
    expect(panel('odeme')?.hidden).toBe(false);
    sekme('Ödeme')?.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
    await fixture.whenStable();
    expect(sekme('Genel')?.getAttribute('aria-selected')).toBe('true');
  });

  it('geçersiz alan gizli sekmedeyse o sekmeye geçer, alana odaklanır; istek GİTMEZ', async () => {
    const { fixture, kok, sekme, gonder, b } = await exchangeRate();
    await gonder();
    await new Promise((r) => setTimeout(r, 0));
    await fixture.whenStable();
    expect(b.requests).toHaveLength(0);
    expect(sekme('Ödeme')?.getAttribute('aria-selected')).toBe('true');
    const iban = kok.querySelector<HTMLInputElement>('[data-rc-sekme="odeme"] input');
    expect(document.activeElement).toBe(iban);
    expect(sekme('Ödeme')?.textContent).toContain('hatalı alan var');
  });

  it('gönderim kilidi: uçarken ikinci gönderim yok; 400 alanlar değerleri korur ve alanı işaretler', async () => {
    const { fixture, kok, gonder, b } = await exchangeRate();
    b.form.controls.iban.setValue('TR12');
    await gonder();
    await gonder(); // çift tık
    expect(b.requests).toHaveLength(1);
    expect(kok.querySelector<HTMLButtonElement>('button[type="submit"]')?.disabled).toBe(true);
    const first = b.requests[0];
    first?.yanit.error(
      new ApiHatasi({
        status: 400,
        kod: 'dogrulama',
        detay: 'IBAN geçersiz.',
        alanlar: { Iban: ['IBAN geçersiz.'], Genel: ['Şube kapalı.'] },
      }),
    );
    await fixture.whenStable();
    expect(b.form.getRawValue()).toEqual({ ad: 'Ayşe', iban: 'TR12' });
    expect(kok.querySelector('[data-rc-sekme="odeme"]')?.textContent).toContain('IBAN geçersiz.');
    expect(kok.querySelector('.rc-form-hatalari')?.textContent).toContain('Şube kapalı.');
    // Aynı gönderimin tekrarı aynı anahtarla; 2xx sonrası form temiz ve anahtar yenilenir.
    b.form.controls.iban.setValue('TR34');
    b.form.markAsDirty();
    await gonder();
    expect(b.requests[1]?.anahtar).toBe(first?.anahtar);
    b.requests[1]?.yanit.next({});
    b.requests[1]?.yanit.complete();
    await fixture.whenStable();
    expect(b.form.dirty).toBe(false);
    await gonder();
    expect(b.requests[2]?.anahtar).not.toBe(first?.anahtar);
  });

  it('alansız hata detayı form düzeyinde gösterilir', async () => {
    const { fixture, kok, gonder, b } = await exchangeRate();
    b.form.controls.iban.setValue('TR12');
    await gonder();
    b.requests[0]?.yanit.error(new ApiHatasi({ status: 409, kod: 'cakisma', detay: 'Araç dolu.' }));
    await fixture.whenStable();
    expect(kok.querySelector('.rc-form-hatalari')?.textContent).toContain('Araç dolu.');
    expect(b.form.controls.iban.value).toBe('TR12');
  });
});
