import { inject, Injectable, Injector } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { OturumServisi } from './oturum-servisi';

/** Yeniden giriş diyaloğuna giden veri: bilinen firma/kullanıcı kilitlenir, yalnız parola sorulur. */
export interface YenidenGirisVerisi {
  readonly firma: string | null;
  readonly kullanici: string | null;
  /** Önceki oturumun kullanıcısı: başka kimlikle girilirse istek TEKRARLANMAZ. */
  readonly kullaniciId: string | null;
}

/**
 * `oturum_yok` gelince SAYFADAN AYRILMADAN giriş ister (roadmap tuzağı "401'de gezinme → 172 alanlı form
 * kaybolur"). Aynı anda düşen istekler TEK diyaloğu paylaşır; giriş başarılıysa hepsi tekrarlanır.
 *
 * Diyalog ve CDK tembel yüklenir (ilk paket küçük kalır). Sonuç `true` = aynı kullanıcıyla yeniden
 * giriş yapıldı, `false` = vazgeçildi (istek `oturum_yok` ile düşer, form yerinde kalır).
 */
@Injectable({ providedIn: 'root' })
export class YenidenGirisServisi {
  private readonly enjektor = inject(Injector);
  private readonly oturum = inject(OturumServisi);
  private bekleyen: Promise<boolean> | null = null;
  private kapat: (() => void) | null = null;

  constructor() {
    // Çıkışta/firma kapanınca açık diyalog kalmasın.
    this.oturum.temizlikKaydet(() => this.kapat?.());
  }

  iste(): Promise<boolean> {
    this.bekleyen ??= this.ac().finally(() => {
      this.bekleyen = null;
      this.kapat = null;
    });
    return this.bekleyen;
  }

  private async ac(): Promise<boolean> {
    const [{ Dialog }, { YenidenGirisDiyalogu }] = await Promise.all([
      import('@angular/cdk/dialog'),
      import('@shared/yeniden-giris-diyalogu/yeniden-giris-diyalogu'),
    ]);
    const ben = this.oturum.ben();
    const veri: YenidenGirisVerisi = {
      firma: ben?.kiraci.kod ?? null,
      kullanici: ben?.kullanici.kullaniciAdi ?? null,
      kullaniciId: ben?.kullanici.id ?? null,
    };
    const ref = this.enjektor.get(Dialog).open<boolean, YenidenGirisVerisi>(YenidenGirisDiyalogu, {
      data: veri,
      // Perdeye tıklamak kapatmaz (yanlışlıkla); Esc diyalogun kendisinde "vazgeç" sayılır.
      disableClose: true,
      ariaLabelledBy: 'rc-yeniden-giris-baslik',
      ariaDescribedBy: 'rc-yeniden-giris-aciklama',
      ariaModal: true,
      panelClass: 'rc-diyalog-paneli',
      backdropClass: 'rc-diyalog-perdesi',
      autoFocus: veri.kullanici ? '#rc-yeniden-giris-sifre' : 'first-tabbable',
      restoreFocus: true,
    });
    this.kapat = () => ref.close(false);
    return (await firstValueFrom(ref.closed)) === true;
  }
}
