import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { type Observable, map } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { Sema } from '@core/api/ui-tipleri';
import { sayfaTerkKorumasi } from '@core/form/kaydedilmemis-degisiklik';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { Alan } from '@shared/form/alan/alan';
import { Secim } from '@shared/form/kontroller/secim';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { TanimCrud } from '@shared/form/tanim-crud/tanim-crud';
import {
  type TanimAlani,
  type TanimKaynagi,
  type TanimSatiri,
  restTanimKaynagi,
} from '@shared/form/tanim-crud/tanim-kaynagi';
import { SayfaBandi } from '../../../kabuk/sayfa-bandi/sayfa-bandi';

type BlogList = Sema<'SayfaOfBlogRowDto'>;

export const BLOG_ROOT = '/api/ui/v1/blog-yonetim' as const;
/** Sunucu sınırı: 2 MB (asıl kural serviste, içerikten tür tespiti). */
export const MAX_COVER_BYTES = 2 * 1024 * 1024;

type Translate = (key: CeviriAnahtari) => string;

/** Blog yazısı alanları (`BlogRequest` sınırları). İçerik düz metin: boş satır paragraf, `##`/`###` ara başlık. */
export function blogFields(t: Translate): readonly TanimAlani[] {
  const l = (k: string) => t(`sistem.blog.alan.${k}` as CeviriAnahtari);
  const text = (ad: string, max: number, extra: Partial<TanimAlani> = {}): TanimAlani => ({
    ad,
    etiket: l(ad),
    tur: 'metin',
    azamiUzunluk: max,
    inList: false,
    ...extra,
  });
  return [
    text('baslik', 200, { zorunlu: true, inList: true }),
    text('slug', 200, { inList: true }),
    {
      ad: 'durum',
      etiket: l('durum'),
      tur: 'secim',
      zorunlu: true,
      defaultValue: 'Taslak',
      secenekler: [
        { deger: 'Taslak', etiket: t('sistem.blog.taslak') },
        { deger: 'Yayinda', etiket: t('sistem.blog.yayinda') },
      ],
    },
    text('altBaslik', 300),
    text('yazar', 160),
    text('ozet', 500, { tur: 'textarea' }),
    text('seoBaslik', 300),
    text('metaAciklama', 500),
    text('anahtarKelimeler', 500),
    text('kapakAlt', 300),
    { ad: 'aramaDisi', etiket: l('aramaDisi'), tur: 'onay', inList: false },
    text('icerik', 20000, { tur: 'textarea', zorunlu: true }),
  ];
}

/**
 * F11.2b blog yönetimi (Blazor `BlogList`; OperationsWrite): yazılar tanım CRUD'uyla (sürüm/409 birleştirme çekirdekte),
 * kapak görseli ve önizleme ayrı bölümde. İçerik DÜZ METİN — ekran hiçbir yerde HTML olarak yorumlamaz.
 */
@Component({
  selector: 'rc-blog-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SayfaBandi, ReactiveFormsModule, RouterLink, TranslocoPipe, TanimCrud, Alan, Secim],
  styleUrl: '../system.scss',
  templateUrl: './blog-page.html',
})
export class BlogPage {
  private readonly api = inject(ApiIstemcisi);
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();
  private readonly crud = viewChild(TanimCrud);
  private readonly coverInput = viewChild<ElementRef<HTMLInputElement>>('coverInput');

  protected readonly fields = blogFields(this.t);
  protected readonly source: TanimKaynagi = {
    ...restTanimKaynagi(BLOG_ROOT),
    listele: () =>
      this.api
        .get<BlogList>(BLOG_ROOT, { parametreler: { boyut: 200, sirala: 'baslik' } })
        .pipe(map((p) => p.kayitlar as unknown as readonly TanimSatiri[])),
  };
  protected readonly posts = computed(() => this.crud()?.rows() ?? []);
  protected readonly postOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    this.posts().map((p) => ({ deger: p.id, etiket: String(p['baslik'] ?? '') })),
  );
  protected readonly selectedId = new FormControl<string | null>(null);
  private readonly selectedValue = signal<string | null>(null);
  protected readonly selected = computed(
    () => this.posts().find((p) => p.id === this.selectedValue()) ?? null,
  );
  /** Önbellek kırıcı: kapak değişince görsel yeniden istenir. */
  protected readonly coverVersion = signal(0);
  protected readonly coverBusy = signal(false);
  protected readonly coverError = signal<string | null>(null);

  constructor() {
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
    this.selectedId.valueChanges.pipe(takeUntilDestroyed()).subscribe((id) => {
      // Yazı değişince seçili dosya da düşer (@if örneği korunur: eski yazının dosyası yenisine gitmesin).
      const el = this.coverInput()?.nativeElement;
      if (el) el.value = '';
      this.coverError.set(null);
      this.selectedValue.set(id);
    });
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.crud()?.kaydedilmemisDegisiklikVar() ?? false;
  }

  protected coverSrc(id: string): string {
    return `${BLOG_ROOT}/${encodeURIComponent(id)}/kapak/kucuk?v=${this.coverVersion()}`;
  }

  protected uploadCover(id: string): void {
    const file = this.coverInput()?.nativeElement.files?.[0] ?? null;
    this.coverError.set(null);
    if (!file) {
      this.coverError.set(this.t('sistem.blog.kapak.secilmedi'));
      return;
    }
    if (file.size > MAX_COVER_BYTES) {
      this.coverError.set(this.t('sistem.blog.kapak.buyuk'));
      return;
    }
    const body = new FormData();
    body.append('kapak', file, file.name);
    this.coverRequest(this.api.post(`${BLOG_ROOT}/${encodeURIComponent(id)}/kapak`, body), () => {
      const el = this.coverInput()?.nativeElement;
      if (el) el.value = '';
    });
  }

  protected async removeCover(id: string): Promise<void> {
    const yes = await this.confirm.sor({
      baslik: this.t('sistem.blog.kapak.kaldirBaslik'),
      mesaj: this.t('sistem.blog.kapak.kaldirMesaj'),
      onayEtiketi: this.t('sistem.blog.kapak.kaldir'),
      tehlikeli: true,
    });
    if (yes) this.coverRequest(this.api.delete(`${BLOG_ROOT}/${encodeURIComponent(id)}/kapak`));
  }

  private coverRequest(request: Observable<unknown>, after?: () => void): void {
    if (this.coverBusy()) return;
    this.coverBusy.set(true);
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.coverBusy.set(false);
        after?.();
        this.coverVersion.update((v) => v + 1);
        this.toast.basari(this.t('sistem.ortak.kaydedildi'));
        this.crud()?.reload();
      },
      error: (e: unknown) => {
        this.coverBusy.set(false);
        const h = apiHatasinaCevir(e);
        this.coverError.set(h.alanlar?.['kapak']?.[0] ?? h.detay);
      },
    });
  }
}
