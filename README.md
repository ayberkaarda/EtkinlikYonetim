# 🎉 EtkinlikYonetim

**C# ve SQL ile geliştirilmiş bir Etkinlik Yönetim Sistemi**

Bu proje, etkinlik organizasyonlarının dijital ortamda kolayca yönetilmesini sağlar. Kullanıcılar sisteme kaydolup etkinliklere katılabilirken, yöneticiler (admin) yeni etkinlikler oluşturabilir, katılımcıları görüntüleyebilir ve sistemin genel yönetimini gerçekleştirebilir.

## 🚀 Özellikler

### 👤 Kullanıcı Paneli
- Kullanıcı, sisteme TC Kimlik No, Ad Soyad, E-posta gibi bilgilerle kayıt olur.
- Aktif etkinlikleri görüntüler.
- TC Kimlik No ile doğrulama yaptıktan sonra istediği etkinliğe katılabilir.

### 🔐 Admin Paneli
- Yeni etkinlik oluşturabilir.
- Katılımcı ekleyebilir veya silebilir.
- Bilet düzenleyebilir.
- Her etkinlik için kayıtlı katılımcı listesini görüntüleyebilir.

## 🖥️ Ekran Görüntüleri

### Giriş Ekranı
![Giriş Ekranı](screenshots/login.png)

### Kullanıcı Kayıt Ekranı
![Kullanıcı Kayıt](screenshots/user_register.png)

### Etkinliğe Katılım Ekranı
![Etkinliğe Katılım](screenshots/event_register.png)

### Admin Paneli
![Admin Paneli](screenshots/admin_panel.png)

### Katılımcı Listesi
![Katılımcı Listesi](screenshots/shows_members.png)

## 🛠️ Kullanılan Teknolojiler

- C# (Windows Forms)
- MSSQL (SQL Server)
- ADO.NET ile veritabanı bağlantısı

## 📦 Kurulum ve Kullanım

1. Bu projeyi GitHub'dan klonlayın:
   ```bash
   git clone https://github.com/kullaniciadi/EtkinlikYonetim.git
   ```

2. .NET 10 SDK ve SQL Server (Express yeterli) kurulu olmalıdır. `EtkinlikYonetim.sln` dosyasını Visual Studio'da açın ya da `dotnet build` çalıştırın.

3. Bağlantı bilgisini verin (birini seçin):
   - `src/EtkinlikYonetim.UI/appsettings.example.json` dosyasını aynı klasöre `appsettings.json` adıyla kopyalayıp düzenleyin (bu dosya git'e eklenmez),
   - `dotnet user-secrets set "ConnectionStrings:EtkinlikYonetim" "<bağlantı>" --project src/EtkinlikYonetim.UI`,
   - ya da `ETKINLIK_CONNECTION` ortam değişkenini tanımlayın.

4. Tabloları elle oluşturmanız gerekmez: uygulama ilk açılışta EF Core migration'larını uygular. Varsayılan kurulum hiçbir hesap oluşturmaz ve hiçbir hazır şifre içermez; veritabanında yönetici yoksa açılan **İlk Kurulum** ekranında ilk yönetici hesabını kendi belirlediğiniz şifreyle oluşturursunuz. Eski sürümün oluşturduğu bir veritabanı verileri korunarak yerinde yükseltilir.
   - Demo modu isteğe bağlıdır ve varsayılan olarak kapalıdır: `ETKINLIK_DEMO=1` ortam değişkeni ya da `appsettings.json` içinde `"Seed": { "Demo": true }` yalnızca tamamen boş bir veritabanına kurgusal örnek veriler ekler. Yalnızca demo modu açıksa şu hesaplar oluşur: `admin` / `Demo.Admin1` ve `ornek` / `Demo.Kullanici1`. Demo modunu gerçek bir kurulumda açmayın.

5. Uygulamayı çalıştırın (`dotnet run --project src/EtkinlikYonetim.UI`) ve testleri `dotnet test` ile çalıştırın; SQL Server gerektiren testleri atlamak için `ETKINLIK_SKIP_SQL_TESTS=1` tanımlayın.

## 📌 Notlar
- TC Kimlik numarası doğrulaması yalnızca sistem içi kontrolle yapılmaktadır.
- Kullanıcıların sisteme birden fazla etkinlik kaydı yapması mümkündür.
- Admin yetkisi giriş ekranında kontrol edilmektedir.
- **Güvenlik notu:** Bu sürümden önceki ara sürümlerle kurulmuş veritabanlarında `admin` ve `ornek` örnek hesapları, yukarıda yayımlanmış şifreleriyle bulunabilir. Böyle bir veritabanında bu hesapları (sahip oldukları örnek katılımcı ve kayıtlarla birlikte) silin ya da şifrelerini değiştirin. Hiç yönetici hesabı kalmazsa uygulama bir sonraki açılışta İlk Kurulum ekranını gösterir.

## 📧 İletişim

Proje hakkında sorularınız için benimle iletişime geçebilirsiniz.
