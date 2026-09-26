import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { Schema } from '@core/api/ui-tipleri';
import { TemelStore } from '@core/veri/temel-store';

import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { BLOG_ROOT } from './blog-page';

type BlogPreview = Schema<'BlogPreviewDto'>;

/**
 * F11.2b blog önizleme (Blazor `BlogOnizleme`): sitedeki ayrıştırma kuralıyla üretilmiş bloklar (Paragraf / Baslik2 /
 * Baslik3) ve arama sonucu künyesi. Bloklar DÜZ METİN olarak basılır — yazıdaki `<script>` ekranda metin görünür.
 */
@Component({
  selector: 'rc-blog-preview-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageBand, RouterLink, TranslocoPipe],
  styleUrl: '../system.scss',
  templateUrl: './blog-preview-page.html',
})
export class BlogPreviewPage {
  private readonly api = inject(ApiIstemcisi);
  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';
  protected readonly preview = new TemelStore<BlogPreview>(() =>
    this.api.get<BlogPreview>(`${BLOG_ROOT}/${encodeURIComponent(this.id)}/onizleme`),
  );

  constructor() {
    this.preview.yukle();
  }
}
