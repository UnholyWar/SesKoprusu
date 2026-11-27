# SesKöprüsü

**[Windows x64 kurulumunu indir / Download the Windows x64 installer (1.0.9)](https://github.com/UnholyWar/SesKoprusu/releases/download/setup-test-v1.0.9/SesKoprusu-Setup.exe)**

[Test sürümleri / Test releases](https://github.com/UnholyWar/SesKoprusu/releases) · [Türkçe](#türkçe) · [English](#english)

## Türkçe

SesKöprüsü, gelen konuşmayı seçtiğin dilde kulaklığına verir. Kendi sesini de başka bir dile çevirip görüşme uygulamana mikrofon sesi olarak iletebilir. İki yönü ayrı ayrı veya birlikte kullanabilirsin.

### Kurulum ve ilk kullanım

1. Yukarıdaki **Windows x64 kurulumunu indir** bağlantısından `SesKoprusu-Setup.exe` dosyasını indir, çalıştır ve kurulum sihirbazını tamamla.
2. SesKöprüsü’nde **Codex Settings → Account → Sign in with a device code** düğmesine bas. Tarayıcıda ChatGPT hesabına giriş yapıp uygulamanın gösterdiği tek kullanımlık kodu gir. **Codex connected** yazısını gördüğünde hesabın bağlıdır. Sesli çeviri için hesabının Codex gerçek zamanlı ses erişimi olmalı.
3. Köprü kapalıyken Windows’ta kullandığın kulaklığı veya hoparlörü varsayılan **çıkış**, fiziksel mikrofonunu varsayılan **giriş** olarak seç. Ana ekranda **Incoming audio** ile duyacağın dili, **Outgoing audio** ile karşı tarafa iletilecek dili seç.

   | Kullanım | Incoming audio | Outgoing audio |
   | --- | --- | --- |
   | İngilizce videoyu Türkçe dinlemek | Turkish | Keep original |
   | Türkçe konuşup sesini İngilizce göndermek | Keep original | English |
   | Karşı tarafı Türkçe duymak ve İngilizce yanıt iletmek | Turkish | English |

4. **Audio bridge** anahtarını aç. **Devices** sekmesi açılırsa gerekli görünen sürücüyü buradan kur. Tek yönde çeviri için **VB-CABLE**, çift yönde ise ek olarak **Hi-Fi Cable** gerekir. Kurulumdan sonra Windows yeniden başlatma isterse tamamla, **Refresh devices**’a bas ve köprüyü tekrar aç. Hi-Fi Cable için örnekleme hızı uyarısı alırsan Windows Ses ayarlarında **Hi-Fi Cable Input** ve **Hi-Fi Cable Output** için aynı hızı seç.
5. Görüşme uygulamasında ses aygıtlarını **Sistem varsayılanı** olarak bırakabilirsin. Çift yönlü çeviride elle seçim gerekiyorsa hoparlör için **CABLE Input**, mikrofon için **Hi-Fi Cable Output** seç. Yalnızca mikrofonunu çeviriyorsan kayıt girişi **CABLE Output** olur. Köprüyü kapattığında önceki varsayılan aygıtların geri yüklenir.

**Güncelleme:** Köprüyü kapatıp **Codex Settings → Updates → Check for updates** düğmesine bas. Yeni sürüm varsa **Download and install** ile kur.

## English

SesKöprüsü plays incoming speech through your headphones in the language you choose. It can also translate your own speech and send it to a call as microphone input. You can use either direction on its own or both together.

### Installation and first use

1. Use the **Download the Windows x64 installer** link above, run `SesKoprusu-Setup.exe`, and complete the setup wizard.
2. In SesKöprüsü, open **Codex Settings → Account → Sign in with a device code**. Sign in to ChatGPT in your browser and enter the one-time code shown in the app. Look for **Codex connected**. Voice translation requires an account with access to Codex realtime voice.
3. With the bridge off, set your headphones or speakers as the default Windows **output** and your physical microphone as the default **input**. On the home screen, **Incoming audio** sets the language you hear. **Outgoing audio** sets the language others hear.

   | Use case | Incoming audio | Outgoing audio |
   | --- | --- | --- |
   | Listen to an English video in Turkish | Turkish | Keep original |
   | Speak Turkish and send your voice in English | Keep original | English |
   | Hear the caller in Turkish and send your replies in English | Turkish | English |

4. Turn on **Audio bridge**. If the **Devices** tab opens, install the driver marked as required. One-way translation uses **VB-CABLE**. Two-way translation also needs **Hi-Fi Cable**. Restart Windows if prompted, select **Refresh devices**, and turn the bridge on again. If you see a sample-rate warning, set **Hi-Fi Cable Input** and **Hi-Fi Cable Output** to the same rate in Windows Sound settings.
5. Your calling app can use the **system default** devices. If it needs explicit devices for two-way translation, select **CABLE Input** as its speaker and **Hi-Fi Cable Output** as its microphone. For microphone-only translation, the recording input is **CABLE Output**. Turning off the bridge restores your previous Windows defaults.

**Updates:** Turn off the bridge and open **Codex Settings → Updates → Check for updates**. If a release is available, select **Download and install**.
