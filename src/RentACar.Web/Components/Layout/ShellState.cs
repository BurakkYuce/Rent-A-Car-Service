namespace RentACar.Web.Components.Layout;

/// <summary>
/// Kabuk (shell) durumu — sidebar collapse + aktif menü gibi layout-seviyesi durum tek yerden yönetilir
/// (her sayfa kendi çözmez). Scoped: static SSR'da istek başına, interaktif circuit'te circuit başına.
/// Static shell'de collapse şimdilik CSS/&lt;details&gt; ile; bu servis ileride interaktif shell'e geçilirse
/// hazır seam (implement genişletilir).
/// </summary>
public sealed class ShellState
{
    public bool SidebarCollapsed { get; private set; }

    public event Action? Changed;

    public void ToggleSidebar()
    {
        SidebarCollapsed = !SidebarCollapsed;
        Changed?.Invoke();
    }
}
