import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { Sema } from '@core/api/ui-tipleri';
import { TemelStore } from '@core/veri/temel-store';
import { Alan } from '@shared/form/alan/alan';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';

type SearchHit = Sema<'SearchHitDto'>;

/** Sunucunun verdiği hedef yalnız site-içi kök-göreli yol olabilir (`//evil` ya da şema taşıyan adres düşer). */
export function safeHitUrl(url: string): string | null {
  return url.startsWith('/') && !url.startsWith('//') && !url.includes('\\') ? url : null;
}

/**
 * F11.2b genel arama (Blazor `Ara`; oturum yeter, şube kapsamı ve KVKK — anonim cari yalnız etiketiyle — sunucuda).
 * Aranan metin URL'ye ve tarayıcı deposuna yazılmaz (kişi adı olabilir). Sonuç bağlantıları ekranın kendi adresidir
 * (tam sayfa geçiş; taşınan ekranlar sunucuda yeni arayüze yönlenir).
 */
@Component({
  selector: 'rc-search-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, TranslocoPipe, Alan, MetinGirdisi],
  styleUrl: '../system.scss',
  templateUrl: './search-page.html',
})
export class SearchPage {
  private readonly api = inject(ApiIstemcisi);

  protected readonly form = new FormGroup({
    q: new FormControl<string | null>(null, [Validators.required, Validators.maxLength(100)]),
  });
  protected readonly searched = signal(false);
  protected readonly results = new TemelStore<readonly SearchHit[], string>((q) =>
    this.api.get<readonly SearchHit[]>('/api/ui/v1/ara', { parametreler: { q } }),
  );
  protected readonly safeHitUrl = safeHitUrl;

  protected search(): void {
    this.form.markAllAsTouched();
    const q = this.form.getRawValue().q?.trim() ?? '';
    if (this.form.invalid || q.length === 0) return;
    this.searched.set(true);
    this.results.yukle(q);
  }
}
