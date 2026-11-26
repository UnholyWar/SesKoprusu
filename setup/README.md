# Windows x64 kurulumu

Derleme makinesinde .NET 10 SDK ve Inno Setup 6 kurulu olmalı. ISCC.exe dosyasını PATH'e ekleyin. Resmî VB-CABLE Windows paketini https://vb-audio.com/Cable/ adresinden indirip bir klasöre çıkarın. Paketi başkalarına dağıtmadan önce sürücünün dağıtım koşullarını kontrol edin.

~~~powershell
.\setup\Build.ps1 -CablePackageDirectory C:\Drivers\VB-CABLE -CodexDirectory C:\Tools\Codex-x64
~~~

CodexDirectory için resmî Windows x64 Codex CLI paketinin codex.exe ve yardımcı dosyalarını içeren klasörü kullanın. Uygulama açılınca Codex Ayarları üzerinden cihaz koduyla giriş yapın. Parola ve ikinci doğrulama adımı tarayıcıdaki ChatGPT giriş sayfasında tamamlanır. Oturum SesKöprüsü için ayrı tutulur.

setup/Output/SesKoprusu-Setup.exe uygulama ile sürücü kurulumunu içerir. Sürücü sihirbazını tamamlayıp Windows'u yeniden başlatın. İlk çalıştırmada Windows varsayılan çıkışını dinleyeceğiniz fiziksel aygıta ayarlayın. Seslendirme için uygulamayı başlatmadan önce OPENAI_API_KEY ortam değişkenini tanımlayın. SESKOPRUSU_CODEX_MODEL ile hesabınızdaki CLI uyumlu Codex modelini seçebilirsiniz.

## Windows kabul testi

1. Windows ses panelinde varsayılan oynatma ve kayıt aygıtlarını, ayrıca iletişim varsayılanlarını kaydedin. Tarayıcıyı sistemin varsayılan çıkışına ayarlayın.
2. YouTube'da konuşma içeren bir video açın. Oynatma sürerken anahtarı açın. Windows varsayılanlarının CABLE Input ve CABLE Output olduğunu ve kayıt panelinde CABLE Output göstergesinin videodaki sesle hareket ettiğini kontrol edin. Konuşmanın önceki hoparlör veya kulaklıktan duyulduğunu dinleyerek doğrulayın.
3. Videoyu durdurmadan anahtarı kapatın. Dört varsayılanı ilk kayıtla karşılaştırın ve sesi dinleyin. İşlemi tekrarlayıp anahtar açıkken uygulamayı kapatın ve aynı karşılaştırmayı yapın.
4. Anahtar açıkken Görev Yöneticisi ile uygulamayı sonlandırın. Yeniden açılışta kayıtlı aygıtların geri geldiğini kontrol edin.
5. Codex Ayarları'ndan cihaz koduyla giriş yapın. Bağlantı testi gerçek bir kısa Codex isteği gönderir. Başarılı olduğunda ana ekranda “Codex bağlı” görünür. Visual Studio ile çalıştırırken yerleşik veya PATH üzerindeki Codex kullanılabilir. Bulunamazsa “CLI indir” ile resmî Windows paketini açın ve çıkarılan çalıştırılabilir dosyayı “CLI seç” ile seçin.
6. Çeviri için ayarlardaki maskeli alana OpenAI API anahtarını girin. Girilen anahtar pencere kapatılınca silinir. Alternatif olarak OPENAI_API_KEY kullanılabilir. Gelen ses için VB-CABLE, giden çeviri için ayrıca VB-CABLE A gerekir. Giden çeviriyi kullanacaksanız VB-Audio'nun A+B paketini kurun. İki yönün hedef dilini ayrı seçin. Anahtarı açtığınızda seçilen yönün sesi dört saniyelik parçalar halinde OpenAI ses API’sine gönderilir. Makine çevirisi seslendirilir. Dil seçimini değiştirmek için anahtarı kapatın.
7. Gelen sesi kulaklıkta, giden sesi ayrı bir görüşme istemcisinde dinleyin. Oturumu kapatıp yeniden açınca iki dil tercihini doğrulayın.
