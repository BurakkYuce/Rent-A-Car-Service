import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { Schema } from '@core/api/ui-tipleri';
import { TemelStore } from '@core/veri/temel-store';
import { Alan } from '@shared/form/alan/alan';
import { TextInput } from '@shared/form/kontroller/text-input';
import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';

type SearchHit = Schema<'SearchHitDto'>;

const SEARCH_MAX_LENGTH = 100;

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

const GUID = '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}';

/** Arama hedefi (Blazor adresi) → SPA rotası (`/app` öneksiz). Araç hedefi Blazor'da detay sayfasıdır. */
const HIT_ROUTES: readonly (readonly [RegExp, (m: RegExpExecArray) => string])[] = [
  [new RegExp(`^/araclar/(${GUID})$`), (m) => `/araclar/${m[1] ?? ''}/detay`],
  [new RegExp(`^/(cariler|kiralar)/(${GUID})$`), (m) => `/${m[1] ?? ''}/${m[2] ?? ''}`],
  [/^\/(rezervasyonlar|faturalar)$/, (m) => `/${m[1] ?? ''}`],
];

/**
 * F11.3: güvenli arama hedefinin (`safeHitUrl`'den geçmiş) SPA karşılığı — sonuç bağlantısı router'la gider (tam sayfa
 * yüklemesi ve sunucu yönlendirmesi yok). Karşılığı olmayan yol `null` (bağlantı düz adresle kalır). Sorgu/parça yok
 * sayılmaz: hedefte varsa eşleşme olmaz.
 */
export function hitRoute(url: string): string | null {
  for (const [pattern, target] of HIT_ROUTES) {
    const m = pattern.exec(url);
    if (m) return target(m);
  }
  return null;
}

/**
 * F11.2b genel arama (Blazor `Ara`; oturum yeter, şube kapsamı ve KVKK — anonim cari yalnız etiketiyle — sunucuda).
 * Aranan metin URL'ye ve tarayıcı deposuna yazılmaz (kişi adı olabilir). Sonuç bağlantıları SPA rotasına router'la
 * gider (`hitRoute`, F11.3); karşılığı olmayan hedef ekranın kendi adresiyle (tam sayfa) kalır.
 */
@Component({
  selector: 'rc-search-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageBand, ReactiveFormsModule, RouterLink, TranslocoPipe, Alan, TextInput],
  styleUrl: '../system.scss',
  templateUrl: './search-page.html',
})
export class SearchPage {
  private readonly api = inject(ApiIstemcisi);

  protected readonly form = new FormGroup({
    q: new FormControl<string | null>(null, [
      Validators.required,
      Validators.maxLength(SEARCH_MAX_LENGTH),
    ]),
  });
  protected readonly searched = signal(false);
  protected readonly results = new TemelStore<readonly SearchHit[], string>((q) =>
    this.api.get<readonly SearchHit[]>('/api/ui/v1/ara', { parametreler: { q } }),
  );
  protected readonly safeHitUrl = safeHitUrl;
  protected readonly hitRoute = hitRoute;

  constructor() {
    // F11.3: eski arayüzün arama kutusu (`GET /ara?q=…`) pilot firmada buraya yönlenir. Metin bir kez okunur, arama
    // yapılır ve sorgu adresten silinir (yerinde değiştirme: aranan metin URL'de ve geçmişte kalmaz).
    const route = inject(ActivatedRoute);
    const incoming = route.snapshot.queryParamMap.get('q');
    if (incoming !== null) {
      const q = incoming.trim();
      if (q.length > 0 && q.length <= SEARCH_MAX_LENGTH) {
        this.form.controls.q.setValue(q);
        this.search();
      }
      void inject(Router).navigate([], {
        relativeTo: route,
        queryParams: { q: null },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      });
    }
  }

  protected search(): void {
    this.form.markAllAsTouched();
    const q = this.form.getRawValue().q?.trim() ?? '';
    if (this.form.invalid || q.length === 0) return;
    this.searched.set(true);
    this.results.yukle(q);
  }
}
