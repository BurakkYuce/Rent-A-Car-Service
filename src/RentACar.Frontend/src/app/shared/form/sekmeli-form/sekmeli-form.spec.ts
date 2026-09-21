import { ChangeDetectionStrategy, Component, viewChild } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Observable, Subject } from 'rxjs';
import { ApiHatasi } from '@core/api/api-hatasi';
import { provideCeviri } from '@core/i18n/ceviri';
import { Alan } from '../alan/alan';
import { formGonderimi } from '../form-gonderimi';
import { FormHatalari } from '../form-hatalari';
import { MetinGirdisi } from '../kontroller/metin-girdisi';
import { SekmePaneli, SekmeliForm } from './sekmeli-form';

@Component({
  selector: 'rc-deneme-sekmeli',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, Alan, MetinGirdisi, SekmeliForm, SekmePaneli, FormHatalari],
  template: `
    <form [formGroup]="form" (ngSubmit)="kaydet()">
      <rc-sekmeli-form [sekmeler]="sekmeler" etiket="Bölümler">
        <section rcSekmePaneli="genel" baslik="Genel">
          <rc-alan etiket="Ad"><rc-metin-girdisi formControlName="ad" /></rc-alan>
        </section>
        <section rcSekmePaneli="odeme" baslik="Ödeme">
          <rc-alan etiket="IBAN"><rc-metin-girdisi formControlName="iban" /></rc-alan>
        </section>
        <div rcYanPanel>
          <rc-form-hatalari [hatalar]="gonderim.genelHatalar()" />
          <button type="submit" [disabled]="gonderim.gonderiliyor()">Kaydet</button>
        </div>
      </rc-sekmeli-form>
    </form>
  `,
})
class DenemeSekmeli {
  readonly sekmeli = viewChild.required(SekmeliForm);
  readonly sekmeler = [
    { kimlik: 'genel', etiket: 'Genel' },
    { kimlik: 'odeme', etiket: 'Ödeme' },
  ];
  readonly form = new FormGroup({
    ad: new FormControl<string | null>('Ayşe', Validators.required),
    iban: new FormControl<string | null>(null, Validators.required),
  });
  readonly gonderim = formGonderimi();
  istekler: { anahtar: string; yanit: Subject<unknown> }[] = [];

  kaydet(): void {
    this.gonderim.gonder(
      this.form,
      (anahtar): Observable<unknown> => {
        const yanit = new Subject<unknown>();
        this.istekler.push({ anahtar, yanit });
        return yanit;
      },
      { gecersiz: () => this.sekmeli().ilkGecersizeGit() },
    );
  }
}

async function kur(hash = '') {
  history.replaceState(null, '', `/app/vitrin${hash}`);
  TestBed.configureTestingModule({ providers: [...provideCeviri()] });
  const fixture = TestBed.createComponent(DenemeSekmeli);
  document.body.appendChild(fixture.nativeElement as HTMLElement);
  await fixture.whenStable();
  const kok = fixture.nativeElement as HTMLElement;
  const sekme = (ad: string) =>
    [...kok.querySelectorAll<HTMLButtonElement>('[role="tab"]')].find((s) =>
      s.textContent?.includes(ad),
    );
  const panel = (k: string) => kok.querySelector<HTMLElement>(`[data-rc-sekme="${k}"]`);
  const gonder = async () => {
    kok.querySelector<HTMLButtonElement>('button[type="submit"]')?.click();
    await fixture.whenStable();
    await fixture.whenStable();
  };
  return { fixture, kok, sekme, panel, gonder, b: fixture.componentInstance };
}

describe('rc-sekmeli-form', () => {
  it('ilk sekme etkin; tıklama paneli değiştirir ve #sekme= yazar (yol korunur)', async () => {
    const { fixture, sekme, panel } = await kur();
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
    const { fixture, sekme, panel } = await kur('#sekme=odeme');
    expect(panel('odeme')?.hidden).toBe(false);
    sekme('Ödeme')?.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
    await fixture.whenStable();
    expect(sekme('Genel')?.getAttribute('aria-selected')).toBe('true');
  });

  it('geçersiz alan gizli sekmedeyse o sekmeye geçer, alana odaklanır; istek GİTMEZ', async () => {
    const { fixture, kok, sekme, gonder, b } = await kur();
    await gonder();
    await new Promise((r) => setTimeout(r, 0));
    await fixture.whenStable();
    expect(b.istekler).toHaveLength(0);
    expect(sekme('Ödeme')?.getAttribute('aria-selected')).toBe('true');
    const iban = kok.querySelector<HTMLInputElement>('[data-rc-sekme="odeme"] input');
    expect(document.activeElement).toBe(iban);
    expect(sekme('Ödeme')?.textContent).toContain('hatalı alan var');
  });

  it('gönderim kilidi: uçarken ikinci gönderim yok; 400 alanlar değerleri korur ve alanı işaretler', async () => {
    const { fixture, kok, gonder, b } = await kur();
    b.form.controls.iban.setValue('TR12');
    await gonder();
    await gonder(); // çift tık
    expect(b.istekler).toHaveLength(1);
    expect(kok.querySelector<HTMLButtonElement>('button[type="submit"]')?.disabled).toBe(true);
    const ilk = b.istekler[0];
    ilk?.yanit.error(
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
    expect(b.istekler[1]?.anahtar).toBe(ilk?.anahtar);
    b.istekler[1]?.yanit.next({});
    b.istekler[1]?.yanit.complete();
    await fixture.whenStable();
    expect(b.form.dirty).toBe(false);
    await gonder();
    expect(b.istekler[2]?.anahtar).not.toBe(ilk?.anahtar);
  });

  it('alansız hata detayı form düzeyinde gösterilir', async () => {
    const { fixture, kok, gonder, b } = await kur();
    b.form.controls.iban.setValue('TR12');
    await gonder();
    b.istekler[0]?.yanit.error(new ApiHatasi({ status: 409, kod: 'cakisma', detay: 'Araç dolu.' }));
    await fixture.whenStable();
    expect(kok.querySelector('.rc-form-hatalari')?.textContent).toContain('Araç dolu.');
    expect(b.form.controls.iban.value).toBe('TR12');
  });
});
