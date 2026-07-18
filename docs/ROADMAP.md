# Bistable — Vizyon Yol Haritası (Plan of Record)

**Tarih:** 2026-07-17
**Kaynak analiz:** [VISION_GAP_ANALYSIS.md](VISION_GAP_ANALYSIS.md)
**Statü:** Bu dosya, projenin **tek geçerli plan kaydıdır**. Yeni iş önerileri
önce bu sıraya vurulur; sıradaki faz kapanmadan sonrakine geçilmez
(sahibin çalışma ilkesi).

## Vizyon (özet)

Yaz → Gör → Sür → Anla döngüsü: SystemVerilog yazarken şematik anında güncellenir;
şematik üzerinden giriş sürülür, **iç sinyaller canlı** izlenir, hata yerinde
görülür. Ek eksenler: tek-tık testbench, şematik export, sentezli hâlde de canlı
test, extension destekli profesyonel açık kaynak araç.

## Faz defteri

### Tarihsel fazlar (tamamlandı / eritildi — dosyaları kayıt olarak kalır)

| Faz | Konu | Durum |
|---|---|---|
| 0 – 6.5 | Test altyapısı, AST, statik şematik, worker protokol v2, canlı değerler, RV32I hedefi, Yosys sentez + gate viewer | **Tamamlandı** (PHASE-6.5 resmî kapanış ölçümleri → yeni **Faz 13**) |
| 2.7 (duraklatılmış) | Şematik UX & kalıcılık | Export → **Faz 10**; kalan UX kalemleri → **Faz 14** değerlendirmesi |
| 2.8 (ertelenmiş) | Performans & ölçek | Artımlı düzen/önbellek → **Faz 9**; çizim/ölçüm performansı → **Faz 12** |
| SCHEMATIC_REWRITE_PLAN "Faz 7" (SVG) | Export | → **Faz 10**'a eritildi |
| RTL_SCHEMATIC_VISUAL_ISSUES | Görsel kusurlar | Issue 1–5 kapandı; Issue 4 Stage 2 → **Faz 12** (P12-8) |

### Vizyon fazları (yeni — sıra bağlayıcıdır)

| Faz | Başlık | Öncelik | Önkoşul | Vizyon hedefi |
|---|---|---|---|---|
| **7** ✓ | [Şematik Dürüstlüğü — `always_comb` decode + coverage sözleşmesi](PHASES/PHASE-7.md) | **P0** | — | H2 (güven) |
| **8** ✓ | [Canlı Değer Kanalı — `ReadSignals` batch IPC](PHASES/PHASE-8.md) | **P0** | — | H2 (ölçek) |
| **9** ◐ | [Canlı Döngü — izle → artımlı elaborasyon → tazele](PHASES/PHASE-9.md) | **P0** | 7, 8 | **Backend kapıları tamam; Avalonia IDE yüzeyi sahibi tarafından reddedildi** |
| **9.5** ▶ | [Theia Workbench Geçişi — .NET Engine Host](PHASES/PHASE-9.5.md) | **P0 uygulama kapısı** | 9 backend | H1 + H6 (ürün kabuğu) |
| **10** | [Şematik Export — SVG/PNG](PHASES/PHASE-10.md) | P1 | 7 (yumuşak) | H4 |
| **11** | [Testbench Akışı — tek tık derle & koştur](PHASES/PHASE-11.md) | P1 | 9 (yumuşak) | H3 |
| **12** | [Çekirdek Sağlık — ayrıştırma, temizlik, performans](PHASES/PHASE-12.md) | P1/P2 | — | H6 (zemin) |
| **13** | [Gate-Level Kapanış — 6.5 ölçümleri + Expand Cone](PHASES/PHASE-13.md) | P2 | — | H5 |
| **14** | [Platformlaşma — Extension API, XAML, dağıtım](PHASES/PHASE-14.md) | P3 | 12 | H6 |

### Bağımlılık gerekçeleri

- **7 her şeyden önce:** Şematik `always_comb`'u göstermezken (VISION_GAP_ANALYSIS §2,
  `zero` vakası) üstüne kurulan her canlılık özelliği yanlış resmi canlandırır.
- **8, 9'dan önce:** Canlı döngü değer basacaksa frame başına tek IPC şart;
  aksi hâlde 9'un "canlı" vaadi büyük görünümde tıkanır.
- **9 = vizyonun kalbi:** 7+8 bitmeden başlamaz; bittiğinde ürünün varlık sebebi
  çalışıyor olur.
- **9.5 sahibi kararıyla eklendi (2026-07-17):** Faz 9'un watcher/diff/hot-swap
  backend'i kabul edilebilir; ancak Avalonia Source/dock prototipi profesyonel
  IDE kapısını geçmedi. UI-ağır Faz 10–11 işlerine devam etmeden Theia tabanlı
  ürün kabuğu uygulanır. Ürün sahibi Theia yönünü kabul etti; mevcut Avalonia
  uygulaması Electron/live-loop/schematic/performance geçiş kapıları kapanana
  kadar silinmez. Şematik UX için birincil referans AMD Vivado'dur; semantik
  pin/ölçülü label sözleşmesinden sonraki bağlayıcı dilim seçici hiyerarşi
  expand/collapse ve modül document navigation'dır. Manuel simülasyon UX'inde
  Logisim Evolution ve Digital referans alınır; Poke/Drive modu ve çok-bit
  popover sözleşmesi `docs/SIMULATION_INTERACTION_UX.md` içinde kayıtlıdır ve
  hiyerarşi diliminden sonra uygulanır.
- **10 ve 11 paralel edilebilir** (birbirinden bağımsız); sıra: küçük/moral (10),
  sonra 11.
- **12, 14'ün ön şartı:** Extension API ancak servis sınırlarından tanımlanır;
  4.400 satırlık ViewModel'den API çıkmaz.
- **13 bağımsız:** Mevcut olgun gate hattının resmî kapanışı; P0'ları bloklamasın
  diye arkaya alındı.

## Çalışma kuralları (tüm fazlar için)

1. Faz kapısı sağlanmadan sonraki faza geçilmez; kapı = otomatik test + (varsa)
   sahibin görsel/manuel kabulü.
2. Her görev testiyle gelir; her bug regresyon testiyle kapanır (`docs/TESTING.md`).
3. Davranış değiştiren her iş, aynı değişiklikte ilgili dokümanı ve gerekiyorsa
   `AGENTS.md`'yi günceller.
4. Performans dokunuşları ölçümle gelir (önce/sonra); "hissediyorum" kabul değildir.
5. Görsel cila talepleri Faz 7–9 kapanmadan reddedilir (sahibin talimatı).
