import { TestBed } from '@angular/core/testing';

import { WarningBannerService } from './warning-banner-service';

describe('UyariBandiServisi', () => {
  let service: WarningBannerService;
  beforeEach(() => (service = TestBed.inject(WarningBannerService)));

  it('gezinmeden ÖNCE gösterilen bant gezinme tamamlanınca kapanır', () => {
    service.show({ tur: 'uyari', mesaj: 'eski sayfanın bandı' });
    service.navigationStarted();
    service.navigationEnded('tamamlandi');
    expect(service.bant()).toBeNull();
  });

  it('gezinme zinciri sırasında gösterilen bant (guard yönlendirmesi) yeni sayfada kalır', () => {
    service.navigationStarted();
    service.show({ tur: 'uyari', mesaj: 'Yetkiniz yok.', kod: 'yetki_yok' });
    service.navigationEnded('yonlendirme');
    service.navigationStarted(); // yönlendirmenin yeni gezinmesi
    service.navigationEnded('tamamlandi');
    expect(service.bant()?.mesaj).toBe('Yetkiniz yok.');
  });

  it('kalıcı bant gezinmede kapanmaz; iptal edilen gezinme bandı kapatmaz', () => {
    service.show({ tur: 'uyari', mesaj: 'pilot değil', kalici: true });
    service.navigationStarted();
    service.navigationEnded('tamamlandi');
    expect(service.bant()?.mesaj).toBe('pilot değil');

    service.show({ tur: 'uyari', mesaj: 'geçici' });
    service.navigationStarted();
    service.navigationEnded('iptal');
    expect(service.bant()?.mesaj).toBe('geçici');
  });
});
