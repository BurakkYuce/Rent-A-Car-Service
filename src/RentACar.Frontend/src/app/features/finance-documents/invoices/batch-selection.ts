/**
 * Toplu faturalama seçimi yalnız EKRANDA görünen adayları tutar (#300 L5): yeniden yükleme sonrası listede olmayan
 * (faturalanmış, iptal edilmiş, başka süzgeçte kalan) kira seçimde kalıp "N kira faturalanacak" onayına sayılmamalı.
 * Değişiklik yoksa AYNI küme döner (sinyal gereksiz yere tetiklenmesin).
 */
export function pruneSelection(
  selection: ReadonlySet<string>,
  visibleIds: readonly string[],
): ReadonlySet<string> {
  const visible = new Set(visibleIds);
  const kept = [...selection].filter((id) => visible.has(id));
  return kept.length === selection.size ? selection : new Set(kept);
}
