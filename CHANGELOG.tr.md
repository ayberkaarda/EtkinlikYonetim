# Değişiklik Günlüğü

🇬🇧 [English](CHANGELOG.md) · 🇹🇷 **Türkçe**

Bu projedeki tüm önemli değişiklikler bu dosyada belgelenir.
Biçim [Keep a Changelog](https://keepachangelog.com/tr-TR/1.1.0/) esas alınarak hazırlanmıştır.

## [Yayımlanmamış]

### Eklendi

- Serilog ile `%LOCALAPPDATA%\EtkinlikYonetim\logs` altında günlük dönen log dosyaları: 30 günden eski dosyalar
  silinir ve en fazla 30 dosya tutulur (testler bunu tarihli dosyalarla denetler). Hata kayıtları yalnızca istisna
  türlerini, HResult kodlarını ve metot adlarını içerir.
- Anonim denetim kayıtları: yalnızca sabit bir kod ve bir rol, asla bir hesap. Her başarısız giriş (bilinmeyen hesap,
  yanlış şifre, kilitli hesap) aynı kodu yazar: `auth.sign_in.failed`.
- Kayıt, giriş, ilk yönetici, katılımcı, etkinlik, etkinlik kaydı ve iptal kuralları için merkezi FluentValidation
  doğrulayıcıları; kurallar ve Türkçe mesajlar değişmedi.
- Daha fazla Moq ve SQLite testi: kapasite sınırları, geçmiş tarihler, iptal ve yeniden kayıt, IDOR yanıtları, kilit
  süresi ve T.C. Kimlik No kontrol basamakları; log dosyalarının kişisel veri veya anahtar içermediğini denetleyen
  testler.
- Mimari diyagramlı İngilizce ve Türkçe README, tehdit modeli (`docs/THREAT_MODEL.md`) ve bu değişiklik günlüğü.
- CI'da sürüm işi: `v*` etiketlerinde etiket adı denetlenir, win-x64 derlemesi yayımlanır ve zip çıktısı olarak
  yüklenir.

### Değişti

- Hatalar izleme çıktısı yerine log dosyasına yazılır; hata pencereleri değişmedi.

### Güvenlik

- Kurulum anahtarının süresi, anahtarın kullanıldığı anda veritabanının saatine göre belirlenir: son `UPDATE`
  `SonKullanma > SYSUTCDATETIME()` koşulunu arar ve `TuketimTarihi` değerini veritabanının saatiyle yazar;
  kullanılabilir anahtarların seçimi de artık bilgisayarın saatine bağlı değildir. Bir kurulum beklerken süresi
  dolan ya da yalnızca geri kalmış bir saatin kabul edeceği anahtar reddedilir ve kullanılmamış kalır.
- Adlar, e-posta adresleri, kimlik numaraları, şifreler, kurulum anahtarları ve istisna mesajları loga asla yazılmaz.

## Temel

Özgün uygulamanın yeniden yazımı; bu değişiklik günlüğü başlamadan önce birleştirildi.

### Eklendi

- EF Core, bağımlılık enjeksiyonu ve otomatik migration'larla katmanlı .NET 10 çözümü (Domain, Business, Data, UI);
  özgün veritabanı verileri korunarak yerinde yükseltilir ve katılımcının tek etkinliği biletli bir kayda dönüşür.
- Bilet kodlu kayıtlar, iptal; kapasite ve çift kayıt denetimleri veritabanı işleminin içinde.
- Boş veritabanı için ilk kurulum; hesapları olan ama yöneticisi olmayan veritabanı için yalnızca veritabanı
  sahibinin `docs/issue-setup-key.sql` ile ürettiği tek kullanımlık kurulum anahtarı ve uygulama oturumu için
  `docs/grant-app-login.sql` örnek yetkileri.
- Varsayılan olarak kapalı, isteğe bağlı demo modu (`ETKINLIK_DEMO`, `Seed:Demo`).
- SQLite üzerinde servis testleri; SQL Server'da yarış, kurulum anahtarı ve eski şema yükseltmesi testleri; SQL
  Server işi olan CI.

### Güvenlik

- PBKDF2 şifre özetleri ve düz metin şifrelerin bir sonraki girişte yükseltilmesi; 5 hatalı denemeden sonra 15
  dakikalık kilit ve her hatada aynı yanıt.
- Her serviste rol ve sahiplik denetimi; kimlik numaraları hesap başına benzersiz ve yönetim listelerinde maskeli.
- Varsayılan kurulumda bilinen şifreli hesap yok.
