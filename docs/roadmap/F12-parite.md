# F12 — Parite ve kesiş (Platform konsolu)

> Kalıp adım 3–4. Kaynak: `F12.md` envanterindeki 4 Blazor sayfası (`Components/Pages/Platform/…`) ile SPA platform
> ağacı (`src/RentACar.Frontend/src/app/features/platform`, #293), uçlar `/api/ui/v1/platform/*` (#281).
> **Canlı parite kontrolü YAPILMADI** — kullanıcı kararı (2026-09-26, `DEGISIKLIKLER.md`): kesiş ve F13 söküm canlı
> doğrulama beklenmeden yapılır; canlıya yayın F2.2 sunucu adımları ve `/app` üretim doğrulamasından sonra.

## Envanter kontrolü

4 sayfanın 4'ü SPA'da (ayrı rota ağacı, kendi oturumu ve kabuğu; firma menüsü yok):

| Blazor | SPA | Uçlar (#281) | Not |
|---|---|---|---|
| `/platform/login` | `/app/platform/giris` | `platform/oturum/giris`, `cikis`, `ben` | yanlış parola genel mesaj |
| `/platform/tenants` | `/app/platform/kiracilar` | `platform/kiracilar` (liste, oluştur) | + yeni özet ekranı `/app/platform` (`platform/ozet`) |
| `/platform/tenants/{id}` | `/app/platform/kiracilar/:id` | detay, güncelle, aç/kapa, kapat (kod onaylı), yeniden aç, logo, web sitesi modülü, pilot | pilot anahtarı F13.1b'de kalkar |
| `/platform/belgeler` | `/app/platform/belgeler` | `platform/belgeler` (yükle, sürüm, durum, sil, indir) | PDF sınırı uçta |

Taşınmamış platform ekranı yok. `PlatformLayout` (kabuk) SPA'da `PlatformShell`.

## Bilinçli farklar

- Blazor'un `?ok=1` / `?hata=` sorgu bildirimleri SPA'da sayfa içi durum mesajı; sorgu yok sayılır (zararsız).
- Platform oturumu firma oturumundan ayrı SPA durumu; tek çerez değiştiği için platform girişi firma durumunu siler
  (Blazor'da da tek çerezdi).

## Kesiş

- **`Spa/Cutover.cs`:** F12 bloğu haritanın SONUNA. Dört şablon, SPA adları farklı (`tenants` → `kiracilar`, `login` →
  `giris`). **Pilot kapısı YOK:** platform bir kiracı değildir; blok her oturumda (oturumsuz, platform operatörü, firma
  kullanıcısı) yönlenir (`Cutover.IsPlatformConsolePath`, segment eşleşmesi).
- Korumalı sayfanın oturumsuz isteği önce cookie challenge'ı alır: `/platform/tenants` → `/platform/login` (dönüşsüz)
  → `/app/platform/giris`. Firma kullanıcısı 403 → aynı zincir. Döngü yok (`/app` anonim).
- Platform operatörünün firma sayfası isteği (`PlatformIsolationMiddleware`) ve girişli `/login` isteği artık tek
  adımda `/app/platform/kiracilar`.
- Dosya GET'leri (`/platform/tenants/{id}/logo`, `/platform/belgeler/{id}/indir`) ve Blazor POST'ları yönlenmez; F13.1a'da
  POST'lar silinir.

## Devredilen testler

- `IlkKesisTests`: F12 pozitif/negatif harita, envanter (F12.md, kabuk satırı hariç), SPA rota dosyaları, segment
  eşleşmesi; host: her oturumda 302 (sorgu AYNEN), oturumsuz ve firma kullanıcısı zinciri, platform operatörünün tek
  adımı, pilot anahtarı testi detay sayfası yerine yönlendirmeyi doğrular.
- `PlatformIsolationTests`: hedef `/app/platform/kiracilar`.
- e2e `kesis-f12.spec.ts` (yeni): eski adresler SPA'ya (sorgu + fragment), konsol ekranlarında Blazor platform
  bağlantısı yok.
