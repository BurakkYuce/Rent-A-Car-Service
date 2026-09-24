import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { Sema } from '@core/api/ui-tipleri';
import { TemelStore } from '@core/veri/temel-store';
import { Alan } from '@shared/form/alan/alan';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';

type SearchHit = Sema<'SearchHitDto'>;

/** Genel aramanın ürettiği hedef yolların kökleri (`SearchRepository`); başka yol bağlantı olmaz. */
export const SEARCH_HIT_PREFIXES = [
  '/araclar',
  '/cariler',
  '/kiralar',
  '/rezervasyonlar',
  '/faturalar',
] as const;

/** Kontrol karakteri (TAB/LF/CR dahil — URL ayrıştırıcısı bunları siler: `/\t/evil` → `//evil`), boşluk, ters bölü. */
function hasUnsafeChar(url: string): boolean {
  for (let i = 0; i < url.length; i++) {
    const c = url.charCodeAt(i);
    if (c <= 0x20 || (c >= 0x7f && c <= 0x9f) || c === 0x5c) return true;
  }
  return false;
}

/**
 * Sunucunun verdiği hedef yalnız site-içi, bilinen köklerden biriyle başlayan yol olabilir (F11.2b güvenlik L1):
 * `//evil`, şema taşıyan adres, kontrol karakteri/boşluk/ters bölü içeren girdi ya da çözüldüğünde başka kökene giden
 * yol `null` döner (bağlantı yerine düz metin basılır).
 */
export function safeHitUrl(
  url: string,
  origin: string = globalThis.location.origin,
): string | null {
  if (!url.startsWith('/') || url.startsWith('//') || hasUnsafeChar(url)) return null;
  let resolved: URL;
  try {
    resolved = new URL(url, origin);
  } catch {
    return null;
  }
  if (resolved.origin !== new URL(origin).origin) return null;
  const path = resolved.pathname;
  return SEARCH_HIT_PREFIXES.some((p) => path === p || path.startsWith(`${p}/`)) ? url : null;
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
