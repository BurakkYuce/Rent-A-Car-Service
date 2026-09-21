import type { ActivatedRouteSnapshot } from '@angular/router';

/**
 * Kabuk rotasının `data` işareti (`data: { kabuk: true }`): altındaki bileşenli yaprak sayfalar sekme
 * olur. Kabuk dışı (giriş sayfası) ve yönlendirme rotaları sekme değildir.
 */
export const KABUK_ISARETI = 'kabuk';

/**
 * Sayfa rotası `data: { sekme: false }` ile sekme dışında kalır: arka planda tutulmaz, her girişte
 * taze kurulur (ör. tek seferlik sihirbaz). Varsayılan: kabuk altındaki her sayfa sekmedir.
 */
export const SEKME_VERISI = 'sekme';

/** Rotanın tam deseni, parametre adlarıyla: `/kiralar/:id`; ana sayfa `/`. */
export function rotaDeseni(snapshot: ActivatedRouteSnapshot): string {
  const parcalar = snapshot.pathFromRoot
    .map((r) => r.routeConfig?.path ?? '')
    .filter((p) => p !== '');
  return `/${parcalar.join('/')}`;
}

/** Kabuğun altındaki, bileşeni olan yaprak sayfa mı (sekme adayı)? */
export function sekmeSayfasiMi(snapshot: ActivatedRouteSnapshot): boolean {
  const rota = snapshot.routeConfig;
  if (!rota || rota.children?.length || rota.loadChildren) return false;
  if (!rota.component && !rota.loadComponent) return false;
  if (rota.data?.[SEKME_VERISI] === false) return false;
  return snapshot.pathFromRoot.some((r) => r.routeConfig?.data?.[KABUK_ISARETI] === true);
}

/**
 * Sekmenin kimliği: rota deseni + yol parametreleri (`/kiralar/:id?id=5`). Sorgu dizesi ve fragment
 * dahil DEĞİL — aynı kaydın filtre/sekme değişimi aynı sekmede kalır; farklı kayıt ayrı sekmedir.
 * Sekme sayfası değilse `null`.
 */
export function sekmeAnahtari(snapshot: ActivatedRouteSnapshot): string | null {
  if (!sekmeSayfasiMi(snapshot)) return null;
  return anahtarUret(rotaDeseni(snapshot), snapshot.params);
}

/** Desen + parametrelerden anahtar (depodan geri yüklenen sekme de aynı anahtarı üretmeli). */
export function anahtarUret(desen: string, parametreler: Readonly<Record<string, string>>): string {
  const cift = Object.keys(parametreler)
    .sort()
    .map((ad) => `${ad}=${parametreler[ad]}`)
    .join('&');
  return cift ? `${desen}?${cift}` : desen;
}

/** Yaprak rota (en derin birincil çocuk). */
export function sonYaprak(snapshot: ActivatedRouteSnapshot): ActivatedRouteSnapshot {
  let yaprak = snapshot;
  while (yaprak.firstChild) yaprak = yaprak.firstChild;
  return yaprak;
}
