# Manuel Simülasyon Etkileşimi — UX Sözleşmesi

**Durum:** Poke/Drive, scalar toggle ve multi-bit popover uygulandı; manuel kabul bekleniyor

**Karar tarihi:** 2026-07-17; uygulama önceliği sahibi tarafından 2026-07-18'de öne alındı

**Görsel referans:** AMD Vivado

**Manuel simülasyon referansları:** Logisim Evolution ve Digital

## Ürün hedefi

Bistable, SystemVerilog'u Vivado kalitesinde okunabilir bir RTL şematiğe
dönüştürürken girişlerin elle sürülmesini Logisim Evolution ve Digital kadar
doğrudan hâle getirecektir. Görsel gezinme ve simülasyon mutasyonu iki ayrı
etkileşimdir: yanlışlıkla giriş sürmemek için **Select** yalnız seçim yapar;
**Poke/Drive** modu giriş değerini değiştirir.

## Referans ürünlerde doğrulanan davranış

### Logisim Evolution

- Poke aracı devre elemanları ve hatlarla etkileşir. Bir input pininin binary
  gösteriminde bit tıklanarak değiştirilir; octal/hex digit tıklaması değeri
  artırır, decimal/float gösteriminde değer diyaloğu açılır. Output pinine Poke
  tıklaması değeri değiştirmez.
- Simülasyon zaten çalışır durumdadır. Input değişince propagasyon hemen olur;
  hat ve output rengi yeni değeri gösterir.
- Hatta Poke ile tıklamak özellikle çok bitli değeri geçici olarak gösterir;
  gösterim radixi ayarlanabilir.

Kaynaklar: [Poke aracı](https://www.baillifard.com/logisim/en/html/guide/tutorial/tutor-test.html),
[Pin davranışı](https://www.baillifard.com/logisim/en/html/libs/wiring/pin.html),
[Hat değeri](https://www.baillifard.com/logisim/en/html/libs/base/wiring.html).

### Digital

- Tek bit `InputShape` tıklaması 0/1 arasında toggle eder. Çok bitli input
  tıklaması, tıklanan noktada yeniden kullanılabilir bir `SingleValueDialog`
  açar.
- Bu editör non-modal'dir; radix/format seçimi, metin/spinner alanı, bit
  checkbox'ları, **Apply** ve **OK** taşır. Apply devreyi günceller ve editörü
  açık tutar; OK uygular ve kapatır; Escape kapatır.
- Clock fareyle elle toggle edilebilir; momentary button basılıyken aktif olup
  bırakılınca eski durumuna döner. Yapısal düzenleme ile çalışan simülasyon
  etkileşimi ayrıdır.

Kaynaklar: [InputShape](https://github.com/hneemann/Digital/blob/master/src/main/java/de/neemann/digital/draw/shapes/InputShape.java),
[SingleValueDialog](https://github.com/hneemann/Digital/blob/master/src/main/java/de/neemann/digital/gui/components/SingleValueDialog.java),
[ClockShape](https://github.com/hneemann/Digital/blob/master/src/main/java/de/neemann/digital/draw/shapes/ClockShape.java),
[ButtonShape](https://github.com/hneemann/Digital/blob/master/src/main/java/de/neemann/digital/draw/shapes/ButtonShape.java),
[Digital README](https://github.com/hneemann/Digital).

## Bistable etkileşim sözleşmesi

### 1. Seçim her zaman güvenlidir

- Port sembolü, pin, hat ve constant literal kutusu exact sinyal kimliğini
  seçer. Görünür/elide edilmiş etiketten net adı türetilmez.
- Constant seçimi Inspector'da `constant · read only` olarak görünür. Constant
  kutusu hiçbir modda sürülemez.
- Output ve internal net normal akışta read-only'dir. Gelecekte force/release
  gerekirse açık adlı, geri alınabilir ayrı bir komut olur; Poke davranışına
  gizlenmez.
- Seçili hat ve canlı değer aynı ELK geometrisi üzerinde vurgulanır; değer
  değişimi layout çalıştırmaz.

### 1.5 Hiyerarşik document'larda okuma-yalnız sözleşme (P9.5-10)

- Child module document'ındaki boundary port, internal net ve output'lar
  yalnız seçilebilir/izlenebilir. Bir child sinyalinin adı bir top-level input
  ile çakışsa bile `simulation.setInput` child document'tan erişilemez;
  sürülebilir port çözümü tek noktadan (`topLevelDrivePort`) yapılır ve root
  olmayan document'larda daima boş döner (`check-schematic-hierarchy.mjs`
  regresyonu).
- Poke/Drive modu yalnız root (top-module) document'ında etkinleşir.

### 2. Ayrı Poke/Drive modu

- Araç çubuğunda Hand ve Select'in yanında açık durum göstergeli Poke/Drive
  modu bulunur; klavye kısayolu `P` olur.
- Poke yalnız top-level input portlarında mutasyon yapar. Select modunda aynı
  port tıklaması sadece seçer ve Inspector'ı açar.
- Input'un clock/reset/button rolü adından (`clk`, `rst` vb.) tahmin edilmez.
  Özel etkileşim ancak proje/port metadata'sında açıkça tanımlanır.
- Poke modu ancak native simulation worker hazırken etkinleşir. Hand modunda
  sembol/pin üzerinde başlayan sürükleme pan yapar; Select hiçbir devre
  mutasyonu üretmez.

### 3. Tek bit giriş

- Poke tıklaması 0↔1 toggle eder; ilk sürme öncesinde mevcut worker değeri
  temel alınır.
- Tek kullanıcı eylemi `SetInput → Eval → tek batched ReadSignals` turudur.
  Hatlar, output'lar ve Inspector aynı frame'de güncellenir.
- İlk sürüm Verilator'ın mevcut iki-durumlu sözleşmesini korur. X/Z ancak engine
  ve değer doğrulayıcı açıkça desteklediğinde eklenir.

### 4. Çok bitli giriş popover'ı

Input sembolüne Poke tıklaması modal pencere yerine sembole bağlı, viewport
içinde tutulan bir Theia popover'ı açar:

- tam hierarchical path, width, signedness ve mevcut değer;
- Binary / Hex / Unsigned Decimal / Signed Decimal radix seçimi;
- doğrulamalı metin alanı;
- makul genişliklerde bit toggle satırı; büyük bus'larda sanallaştırılmış veya
  isteğe bağlı açılan bit görünümü;
- **Apply** (uygula, açık kal), **OK** (uygula, kapat), **Escape** (değiştirmeden
  kapat);
- hata halinde inline açıklama; geçersiz değer worker IPC'sine gönderilmez.

Son radix tercihi korunur. Apply, mevcut canlı döngünün tek batched refresh
sözleşmesini kullanır ve ELK layout'u tekrar çalıştırmaz.

Uygulanan ilk sürüm bit düğmelerini MSB→LSB dizer. 64 bite kadar her bit
görünür; daha geniş bus'larda DOM maliyetini sınırlamak için en düşük 64 bit
gösterilir ve tam değer radix alanından girilir. Sayısal dönüşüm JavaScript
`Number` yerine `BigInt` ile yapılır; 32/64-bit değerlerde precision kaybı yoktur.

### 5. Clock ve momentary kontrol

- Şimdilik toolbar `Tick` clock semantiğinin tek yetkili yoludur. Port adından
  clock tahmini yapılmaz.
- Açık input-role metadata eklendiğinde clock glyph tıklaması tanımlı edge/tick
  üretir; momentary button pointer-down'da aktif, pointer-up/cancel'da eski
  değerine döner. Focus kaybı stuck-high bırakmamalıdır.

## Uygulama sırası

1. **Tamamlandı:** Port ve constant gövdesi exact net seçimi; constant read-only
   Inspector davranışı.
2. **Uygulandı, manuel kabul bekliyor:** Poke/Drive modu, tek-bit toggle ve
   çok-bit anchored non-modal popover; BIN/HEX/UDEC/SDEC, bit toggle,
   Apply/OK/Escape.
3. **Sıradaki bağlayıcı iş:** P9.5-10 Vivado-tarzı hiyerarşik aç/kapa, module
   document ve breadcrumb.
4. Açık input-role metadata'sı sonrası clock ve momentary-button etkileşimi.

## Kabul kapısı

1. Constant literal kutusunun görünen alanına tıklamak exact output netini
   seçer; Inspector read-only gösterir ve kutu sürülemez.
2. Poke modunda 1-bit input tek tıkla değişir; output ve görünür probe'lar tek
   batch sonucunda güncellenir.
3. Çok-bit popover Apply/OK/Escape, radix ve inline validation sözleşmesini
   karşılar; viewport dışına taşmaz ve klavyeyle kullanılabilir.
4. Select modunda hiçbir tıklama devre durumunu değiştirmez.
5. Her davranış per-bit kimliği, session generation/stale-frame koruması ve
   relayout-yok performans guardrail'lerini koruyan regresyon testiyle gelir.

## Uygulama notu — 2026-07-18

Ürün sahibi P9.5-11'in temel manuel sürme dilimini P9.5-10'dan önce açıkça öne
aldı. Bu istisna yalnız burada tanımlı Poke/Drive davranışını kapsar; kalan
hiyerarşi işi yine sıradaki bağlayıcı dilimdir.
