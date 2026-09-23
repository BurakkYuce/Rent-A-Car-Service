# Roadmap değişiklik kaydı

Faz sırası ve kapsamı kilitlidir ([README](README.md) "Sıra kuralı"). İş sürerken bir fazı öne ya da arkaya
almak, bir sayfayı başka faza taşımak ya da bir fazın kapsamını daraltmak **yalnız kullanıcının açık kararıyla**
olur ve buraya yazılır. Kayıt olmadan yapılan sıra değişikliği geçersizdir.

Çekirdekte eksik çıkması sıra değişikliği değildir: o fazın içine "çekirdek eki" PR'ı olarak eklenir ve
buraya yalnız bilgi olarak not edilir.

| Tarih | Faz | Değişiklik | Gerekçe | Karar veren |
|---|---|---|---|---|
| 2026-09-21 | — | Roadmap açıldı: G0 + F0–F13, 60 PR. Sayfa envanteri: 148 dosya / 149 `@page` rotası (plan metnindeki "148 `@page`" dosya sayısıdır; `KiraForm` iki rotalıdır). | Başlangıç | Kullanıcı (plan onayı) |
| 2026-09-21 | F1.2 | PR #242 kapatıldı, yerine tek commit'lik #243 (sıra değişmedi) | GitGuardian: testte sabit kimlik bilgisi (canlı bir hesapla aynı) | — (bilgi) |
| 2026-09-21 | F1.2 | Sözleşme eki: 9. hata kodu `xsrf_gecersiz` (400) | CSRF reddi form hatası (`dogrulama`) ya da yetki bandı (`yetki_yok`) gibi görünmesin | — (bilgi, eklemeli) |
| 2026-09-21 | F1.4 → F3.3 | SPA kuralı: 409 `mukerrer` "farklı içerik"te yeni anahtarla OTOMATİK yeniden gönderim yok; kayıt yeniden yüklenir | Adversarial LOW-A: kur çağrı anında çözülünce birebir tekrar "farklı içerik" sayılabilir; otomatik tekrar çift kayıt üretirdi | — (bilgi, F3.3 kapsamında) |
| 2026-09-21 | F1 | Faz içi PR'lar paralel geliştirildi; merge sırası 238, 239, 240, 243, 245, 244 | Hız (kullanıcı isteği); faz sırası korunmuştur | Kullanıcı ("hızlı olsun") |
| 2026-09-22 | F2 → F3 | F3, F2 Exit'inin sunucu maddeleri (production `/app/` 200 + 401/`pilot_degil`, geri alma provası, yanlış SHA/checksum reddi) beklenmeden başladı. F2.2 kodu (#249) ve CI kapıları merge edilmişti; sunucu adımları kullanıcının sunucu erişimine bağlı (`docs/ops/f2-2-sunucu-adimlari.md`) | Hız (kullanıcı: "hepsini hemen başlat, bu gece bitirelim"); F3 yalnız istemci çekirdeği, üretime veri açmaz (pilot kapısı F1.2'de) | Kullanıcı ("hızlı olsun") |
| 2026-09-22 | F3 → F4 | F4.1 (kira + panel uçları) ve F4.4'ün backend yarısı (sabit panel finans uçları, `/api/ui/v1/finans`) F3.7 merge edilmeden başladı; F3 içindeki PR'lar da paralel geliştirildi (merge sırası 248, 250, 251, 252, 253, 254, 255) | Hız; bu uçlar F3'ün istemci çekirdeğine bağlı değil. Para kuralı değişmedi: her ikisi de merge öncesi bağımsız adversarial inceleme | Kullanıcı ("hızlı olsun") |
| 2026-09-22 | F4.4 | F4.4 iki PR'a bölündü: F4.4a backend finans uçları (F4.1 ile paralel), F4.4 frontend (kira formu II, F4.3 sonrası). Faz PR sayısı ve sırası değişmedi; F4.4a F4.4'ün önden gelen parçası | Backend uçları F3'e bağımlı değil; F8 aynı uçları yeniden kullanır | — (bilgi) |
| 2026-09-22 | F4.3 | F4.3 iki PR'a bölündü: F4.3 kira formu I (#261) ve F4.3b parite ekleri (#262, maskeli müşteri özeti + sunucu toplamları + kimlikle seçim etiketi + ek hizmet matrisi + paylaşım). Faz kapsamı ve sırası değişmedi | F4 Exit "parite %100" için gereken alanlar tek PR'a sığmıyordu; ikisi paralel yürüdü | — (bilgi) |
| 2026-09-22 | F4 (çekirdek eki) | Rota bazlı tembel çeviri PR'ı (#268) F4 içinde açıldı: `tr.json` ilk paketten çıkarıldı (395 → 372,5 kB) | Çekirdekte eksik çıktı: çeviriler ilk pakete gömülüydü, F5'te bütçe hata eşiği aşılacaktı. Sıra kuralı gereği fazın içine "çekirdek eki" olarak girdi | — (bilgi) |
| 2026-09-23 | F4 | F4'ün KODU tamamlandı (#264 ile). Exit'in "pilotta 10 iş günü P1 yok" ve "F4 listesindeki `@page` = 0" maddeleri açık; Blazor kira/Panel sayfalarının silinmesi ayrı bir PR'a (F4.6b) alındı ve pilot doğrulamasından sonraya bırakıldı | Pilot, kullanıcının F2.2 sunucu adımlarına bağlı; kod tarafını bekletmemek için ayrıldı. F5'in bu pilot süresini beklemeden başlayıp başlamayacağı kullanıcı kararı olarak AÇIK | — (bilgi; F5 kararı kullanıcıda) |

