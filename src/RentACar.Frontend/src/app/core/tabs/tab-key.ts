import type { ActivatedRouteSnapshot } from '@angular/router';

/**
 * Kabuk rotasının `data` işareti (`data: { kabuk: true }`): altındaki bileşenli yaprak sayfalar sekme
 * olur. Kabuk dışı (giriş sayfası) ve yönlendirme rotaları sekme değildir.
 */
export const SHELL_MARKER = 'kabuk';

/**
 * Sayfa rotası `data: { sekme: false }` ile sekme dışında kalır: arka planda tutulmaz, her girişte
 * taze kurulur (ör. tek seferlik sihirbaz). Varsayılan: kabuk altındaki her sayfa sekmedir.
 */
export const TAB_DATA = 'sekme';

/** Rotanın tam deseni, parametre adlarıyla: `/kiralar/:id`; ana sayfa `/`. */
export function routePattern(snapshot: ActivatedRouteSnapshot): string {
  const parts = snapshot.pathFromRoot.map((r) => r.routeConfig?.path ?? '').filter((p) => p !== '');
  return `/${parts.join('/')}`;
}

/** Kabuğun altındaki, bileşeni olan yaprak sayfa mı (sekme adayı)? */
export function isTabPage(snapshot: ActivatedRouteSnapshot): boolean {
  const route = snapshot.routeConfig;
  if (!route || route.children?.length || route.loadChildren) return false;
  if (!route.component && !route.loadComponent) return false;
  if (route.data?.[TAB_DATA] === false) return false;
  return snapshot.pathFromRoot.some((r) => r.routeConfig?.data?.[SHELL_MARKER] === true);
}

/**
 * Sekmenin kimliği: rota deseni + yol parametreleri (`/kiralar/:id?id=5`). Sorgu dizesi ve fragment
 * dahil DEĞİL — aynı kaydın filtre/sekme değişimi aynı sekmede kalır; farklı kayıt ayrı sekmedir.
 * Sekme sayfası değilse `null`.
 */
export function tabKey(snapshot: ActivatedRouteSnapshot): string | null {
  if (!isTabPage(snapshot)) return null;
  return generateKey(routePattern(snapshot), snapshot.params);
}

/** Desen + parametrelerden anahtar (depodan geri yüklenen sekme de aynı anahtarı üretmeli). */
export function generateKey(pattern: string, parameters: Readonly<Record<string, string>>): string {
  const duplicate = Object.keys(parameters)
    .sort()
    .map((name) => `${name}=${parameters[name]}`)
    .join('&');
  return duplicate ? `${pattern}?${duplicate}` : pattern;
}

/** Yaprak rota (en derin birincil çocuk). */
export function lastLeaf(snapshot: ActivatedRouteSnapshot): ActivatedRouteSnapshot {
  let leaf = snapshot;
  while (leaf.firstChild) leaf = leaf.firstChild;
  return leaf;
}
