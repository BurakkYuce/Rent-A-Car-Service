# FAZ-83 — Şifre Değiştirme (Self-Service)

| | |
|---|---|
| **Desen** | D2 — kural taşıyan master |
| **Efor** | 0,5 gün |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `sifre_degistir.aspx` |
| **Risk** | düşük — ama yetki-yükseltme/CSRF açısından DİKKAT gerektiren bir kimlik ucu |

## Amaç
Giriş yapmış herhangi bir kullanıcı (rolü fark etmez), kendi parolasını eski parolasını
doğrulayarak, admin'e ihtiyaç duymadan değiştirebilir hâle gelir.

## Neden (kanıt)
Bugün SADECE admin'in başka bir kullanıcının parolasını sıfırladığı akış var:
`src/RentACar.Web/Users/UserEndpoints.cs` satır 15-17, `/kullanicilar` grubu
`.RequireAuthorization(p => p.RequireRole(nameof(UserRole.Admin)))` ile TAMAMEN admin-kilitli;
satır 28-29 `/sifre` ucu `[FromForm] Guid id, [FromForm] string password` alıyor — eski parola
sorgusu YOK, yalnız admin başka birinin parolasını resetler
(`UserService.ResetPasswordAsync`, satır 62-69, `RequireAdmin()` guard'lı). Self-service (kendi
parolanı eski parolanla değiştirme) akışı hiçbir yerde yok. `IPasswordHasher.Verify(hash,
password)` (`src/RentACar.Application/Common/IPasswordHasher.cs`) zaten mevcut ama
`UserService.cs`'te HİÇ çağrılmıyor (grep doğrulandı) — yalnız `Hash()` kullanılıyor.

## Yapılacaklar
1. `src/RentACar.Application/Users/UserService.cs` — yeni metod:
   ```
   public async Task ChangeOwnPasswordAsync(string eskiSifre, string yeniSifre, CancellationToken ct = default)
   ```
   Guard YOK (`RequireAdmin()` ÇAĞRILMAZ — herhangi rol kendi parolasını değiştirebilir); kullanıcı
   kimliği **FORM'DAN DEĞİL** `_currentUser.UserId`'den alınır (yetki-yükseltme guard'ı — başka
   kullanıcı id'si post edilse bile HER ZAMAN `_currentUser.UserId` kullanılır, form'da id alanı
   HİÇ olmaz). Akış: `_currentUser.UserId is not { } uid` → `ValidationException`("Oturum
   bulunamadı"); `_repository.FindAsync(uid)` ile kullanıcı bulunur; `_hasher.Verify(user.
   PasswordHash, eskiSifre)` `false` ise `ValidationException("Mevcut parola hatalı.")`; yeni
   parola için AYNI kural `ResetPasswordAsync`'teki gibi (`newPassword.Length < 6` →
   `ValidationException("Parola en az 6 karakter olmalıdır.")`); `_repository.UpdateAsync(uid, u
   => u.PasswordHash = _hasher.Hash(yeniSifre))`.
2. `IUserRepository.cs`'e yeni metod GEREKMEZ — `FindAsync(Guid id)` ve `UpdateAsync(Guid id,
   Action<User> apply)` zaten var, ikisi de yeterli.
3. Yeni sayfa `src/RentACar.Web/Components/Pages/Users/SifreDegistir.razor` —
   `@page "/profil/sifre-degistir"`, `@attribute [Authorize]` (rol kısıtı YOK — herhangi giriş
   yapmış kullanıcı), form: `<input type="password" name="eskiSifre">`,
   `<input type="password" name="yeniSifre">`, `<input type="password" name="yeniSifreTekrar">`
   (yalnız CLIENT-side eşleşme kontrolü — sunucuya İKİ alan gönderilmez, sunucu tek `yeniSifre`
   görür; tekrar-doğrulama UX amaçlı, güvenlik sınırı DEĞİL).
4. Yeni endpoint dosyası `src/RentACar.Web/Identity/ProfileEndpoints.cs` —
   `app.MapGroup("/profil").RequireAuthorization().AntiforgeryByEnv()`;
   `MapPost("/sifre-degistir", async (UserService svc, [FromForm] string eskiSifre, [FromForm]
   string yeniSifre) => ...)` — `id` PARAMETRESİ YOKTUR (adım 1'deki guard'ı web ucunda da
   TEKRARLAMAK için — form'dan asla id alınmaz).
5. `src/RentACar.Web/Program.cs` — `app.MapProfileEndpoints();` eklenir (mevcut
   `app.MapUserEndpoints();` satırının (459) yanına).
6. `src/RentACar.Web/Components/Layout/MainLayout.razor` — satır ~248-257 arası (kullanıcı adı +
   tema + çıkış formu), Çıkış formundan ÖNCE `<a href="/profil/sifre-degistir">Şifre Değiştir</a>`
   linki eklenir.
7. `src/RentACar.Web/_Imports.razor` — yeni sayfa için ek using GEREKMEZ (mevcut namespace'ler
   yeterli, `UserService` zaten `RentACar.Application.Users` altında, diğer sayfalar da
   kullanıyor).

## Dokunulacak dosyalar
- `src/RentACar.Application/Users/UserService.cs` — yeni metod
- (yeni) `src/RentACar.Web/Components/Pages/Users/SifreDegistir.razor`
- (yeni) `src/RentACar.Web/Identity/ProfileEndpoints.cs`
- `src/RentACar.Web/Program.cs` — `MapProfileEndpoints()` eklenir
- `src/RentACar.Web/Components/Layout/MainLayout.razor` — link

## Migration
Yok — mevcut `Users.PasswordHash` kolonu kullanılır, şema değişikliği yok.

## Test
- `tests/RentACar.IntegrationTests/UserServiceTests.cs` (mevcut dosyaya ekleme, yoksa yeni test
  dosyası) — **bağımsız oracle**:
  1. `Create` ile bilinen bir parola (`"ilkSifre1"`) verilen kullanıcı oluşturulur.
  2. O kullanıcı kimliğiyle `host.ScopeFor(tenant, userId: o kullanıcının id'si, ...)` açılıp
     `ChangeOwnPasswordAsync("ilkSifre1", "yeniSifre2")` çağrılır → başarı.
  3. Yanlış eski parola (`"yanlisSifre"`) ile çağrı → `ValidationException("Mevcut parola
     hatalı.")` (mesaj testte elle yazılır).
  4. Yeni parola 5 karakter (`"kısa1"`) → `ValidationException("Parola en az 6 karakter
     olmalıdır.")`.
  5. **Yetki-yükseltme testi (kritik):** kullanıcı A oturumuyla, kullanıcı B'nin id'sini
     ENDPOINT'e post etmenin (varsa herhangi bir gizli/alternatif form alanı) B'nin parolasını
     DEĞİŞTİRMEDİĞİ — çünkü endpoint hiç id parametresi almıyor; bu test aslında endpoint
     imzasının id parametresi TAŞIMADIĞINI derleme-zamanı olarak da garantiler.
  6. Değişiklik sonrası eski parolayla `LoginService.ValidateAsync` başarısız, yeni parolayla
     başarılı (uçtan-uca doğrulama).

## Exit
- [ ] `/profil/sifre-degistir` sayfası herhangi rol için erişilebilir, admin gerektirmiyor
- [ ] Eski parola yanlışsa değişiklik reddediliyor
- [ ] Yeni parola < 6 karakterse reddediliyor
- [ ] Endpoint FORM'dan kullanıcı id'si ALMIYOR (kod incelemesiyle + testle doğrulanır)
- [ ] Üst-bar "Şifre Değiştir" linki her rolde görünüyor
- [ ] Tam suite yeşil

## Notlar
CSRF/yetki-yükseltme guard'ı bu fazın TEK gerçek riski: eski `/kullanicilar/sifre` ucu id form'dan
geliyordu ama o uç ZATEN Admin-kilitli olduğundan risksizdi; YENİ uç herhangi role açıldığından
kimliği KESİNLİKLE `ICurrentUser.UserId`'den almalı, aksi hâlde herhangi bir giriş yapmış kullanıcı
başka birinin parolasını (id'sini tahmin ederek) değiştirebilir — bu, plan metninde de açıkça
uyarılmış ("Endpoint kullanıcı kimliğini FORM'dan değil ICurrentUser.UserId'den alır").
