import { TestBed } from '@angular/core/testing';

import { UyariBandiServisi } from './uyari-bandi-servisi';

describe('UyariBandiServisi', () => {
  let servis: UyariBandiServisi;
  beforeEach(() => (servis = TestBed.inject(UyariBandiServisi)));

  it('gezinmeden ÖNCE gösterilen bant gezinme tamamlanınca kapanır', () => {
    servis.goster({ tur: 'uyari', mesaj: 'eski sayfanın bandı' });
    servis.gezinmeBasladi();
    servis.gezinmeBitti('tamamlandi');
    expect(servis.bant()).toBeNull();
  });

  it('gezinme zinciri sırasında gösterilen bant (guard yönlendirmesi) yeni sayfada kalır', () => {
    servis.gezinmeBasladi();
    servis.goster({ tur: 'uyari', mesaj: 'Yetkiniz yok.', kod: 'yetki_yok' });
    servis.gezinmeBitti('yonlendirme');
    servis.gezinmeBasladi(); // yönlendirmenin yeni gezinmesi
    servis.gezinmeBitti('tamamlandi');
    expect(servis.bant()?.mesaj).toBe('Yetkiniz yok.');
  });

  it('kalıcı bant gezinmede kapanmaz; iptal edilen gezinme bandı kapatmaz', () => {
    servis.goster({ tur: 'uyari', mesaj: 'pilot değil', kalici: true });
    servis.gezinmeBasladi();
    servis.gezinmeBitti('tamamlandi');
    expect(servis.bant()?.mesaj).toBe('pilot değil');

    servis.goster({ tur: 'uyari', mesaj: 'geçici' });
    servis.gezinmeBasladi();
    servis.gezinmeBitti('iptal');
    expect(servis.bant()?.mesaj).toBe('geçici');
  });
});
