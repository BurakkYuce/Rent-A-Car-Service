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
