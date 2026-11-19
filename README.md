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

4. Tabloları elle oluşturmanız gerekmez: uygulama ilk açılışta EF Core migration'larını uygular. Varsayılan kurulum hiçbir hesap oluşturmaz ve hiçbir hazır şifre içermez; veritabanı tamamen boşsa (hiç hesap yoksa) açılan **İlk Kurulum** ekranında ilk yönetici hesabını kendi belirlediğiniz şifreyle oluşturursunuz. Eski sürümün oluşturduğu bir veritabanı verileri korunarak yerinde yükseltilir.
   - **Hesapları olan ama yöneticisi olmayan veritabanı** (örneğin yükseltilmiş eski bir veritabanı): uygulama normal giriş ekranıyla açılır; yönetici yokluğu tek başına kimseye yetki vermez. İlk yönetici yalnızca **kurulum anahtarı** ile oluşturulur: giriş ekranındaki **İlk yönetici hesabını oluştur** bağlantısı kurulum formunu açar ve anahtarı ister.
   - Anahtarı veritabanının kurucusu, aynı bağlantı ayarıyla bir komut isteminde üretir: `EtkinlikYonetim.exe --create-setup-key` (kaynaktan: `dotnet run --project src/EtkinlikYonetim.UI -- --create-setup-key`). Komut pencere açmaz; anahtarı yalnızca bir kez konsola yazar. Veritabanında anahtarın kendisi değil yalnızca özeti (SHA-256) tutulur; anahtar hiçbir yere loglanmaz.
   - Anahtar yalnızca veritabanında hiç yönetici yokken üretilebilir; yönetici varken komut reddedilir. Yeni anahtar, kullanılmamış eski anahtarı geçersiz kılar.
   - Anahtarı formdaki **Kurulum Anahtarı** alanına yazın (tireler, boşluklar ve büyük/küçük harf önemli değildir) ya da `ETKINLIK_SETUP_KEY` ortam değişkeniyle verin; ortam değişkeni yalnızca alanı doldurur, değer yine veritabanındaki kayıtla doğrulanır. İstemcide seçilen veya üretilen bir değer yetki sayılmaz.
   - Anahtar 24 saat geçerlidir ve tek kullanımlıktır: yönetici oluşturulduğu işlemle birlikte veritabanında kullanıldı olarak işaretlenir, bu nedenle başka bir profil veya bilgisayardan yeniden kullanılamaz; yazma başarısız olursa anahtar harcanmaz. 5 hatalı anahtar denemesi veritabanında sayılır ve anahtarı geçersiz kılar; bu durumda yeni anahtar üretin.
   - Demo modu isteğe bağlıdır ve varsayılan olarak kapalıdır: `ETKINLIK_DEMO=1` ortam değişkeni ya da `appsettings.json` içinde `"Seed": { "Demo": true }` yalnızca tamamen boş bir veritabanına kurgusal örnek veriler ekler. Yalnızca demo modu açıksa şu hesaplar oluşur: `admin` / `Demo.Admin1` ve `ornek` / `Demo.Kullanici1`. Demo modunu gerçek bir kurulumda açmayın.

5. Uygulamayı çalıştırın (`dotnet run --project src/EtkinlikYonetim.UI`) ve testleri `dotnet test` ile çalıştırın; SQL Server gerektiren testleri atlamak için `ETKINLIK_SKIP_SQL_TESTS=1` tanımlayın.

## 📌 Notlar
- TC Kimlik numarası doğrulaması yalnızca sistem içi kontrolle yapılmaktadır.
- Kullanıcıların sisteme birden fazla etkinlik kaydı yapması mümkündür.
- Admin yetkisi giriş ekranında kontrol edilmektedir.
- **Güvenlik notu:** Bu sürümden önceki ara sürümlerle kurulmuş veritabanlarında `admin` ve `ornek` örnek hesapları, yukarıda yayımlanmış şifreleriyle bulunabilir. Böyle bir veritabanında bu hesapları (sahip oldukları örnek katılımcı ve kayıtlarla birlikte) silin ya da şifrelerini değiştirin. Hiç yönetici hesabı kalmazsa yeni yönetici, giriş ekranından yukarıdaki kurulum anahtarıyla oluşturulur.

## 📧 İletişim

Proje hakkında sorularınız için benimle iletişime geçebilirsiniz.
