# Keyboard Monitor (鍵盤與滑鼠診斷工具)

[![CI](https://github.com/nojackno2-ctrl/KeyboardMonitor/actions/workflows/ci.yml/badge.svg)](https://github.com/nojackno2-ctrl/KeyboardMonitor/actions/workflows/ci.yml)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-blue.svg)](https://dotnet.microsoft.com/)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

專為 Windows 打造的現代化、輕量級全域鍵盤與滑鼠診斷工具。採用極簡現代深色主題（Dark Mode）與 Per-Monitor V2 高 DPI 支援，提供即時按鍵可視化、卡鍵警示、全域滑鼠偵測、按鍵延遲／持續時間測量與打字速率統計（WPM / KPS）。

---

## 🌟 核心特色

- **🎯 多種鍵盤配置切換**
  - **100% 全尺寸 (Full Size)**：標準 104/108 鍵完整佈局。
  - **80% TKL (Tenkeyless)**：無數字鍵區的精簡配置。
  - **60% 緊湊型 (Compact)**：極簡打字佈局。
- **⚡ 全域低階 Hook (Low-Level Hooks)**
  - 支援視窗焦點外（背景模式 / 遊戲中）即時捕捉鍵盤與滑鼠輸入事件。
- **🔍 精準按鍵與修飾鍵識別**
  - 精確區分左右修飾鍵（`Left Shift` / `Right Shift`、`Left Ctrl` / `Right Ctrl`、`Left Alt` / `Right Alt`）。
  - 精確區別主鍵盤導覽鍵（`Insert` / `Delete` / 方向鍵）與數字鍵盤（NumPad）共用虛擬碼。
- **⚠️ 智慧卡鍵診斷 (Stuck Key Warning)**
  - 按鍵持續按壓超過 2 秒即自動觸發卡鍵警示（琥珀色高亮），協助快速檢測軸體回彈不良或微動開關故障。
- **🖱️ 全方位滑鼠功能測試**
  - 即時檢測左鍵、右鍵、中鍵滾輪、側鍵（XButton 1 / 2）。
  - 支援滾輪滾動方向與數值、點擊座標與雙擊測試。
- **📊 效能數據與打字測速**
  - **KPS (Keys Per Second)**：即時每秒按鍵數與歷史最高峰值（Peak KPS）。
  - **按鍵持續時間 (Press Duration / Latency)**：精確至毫秒（ms）的按鍵觸發時間測量。
  - **WPM (Words Per Minute)**：打字速度即時計算，附帶專屬測試文字輸入區。
- **📜 診斷事件日誌**
  - 即時保留最近 100 筆輸入事件日誌，方便回溯除錯與硬體驗證。
- **🔒 隱私安全承諾**
  - **100% 離線運作**：無任何網路傳輸或外部請求。
  - **無背景儲存**：所有事件僅在記憶體中做即時 UI 渲染，不留硬碟紀錄檔、不記錄機密輸入。

---

## 🖥️ 介面預覽與狀態說明

### 按鍵顏色狀態

| 狀態 | 視覺外觀 | 說明 |
| :--- | :--- | :--- |
| **預設常態 (Idle)** | 深灰底色、淺灰邊框 | 尚未觸發或已重設之按鍵 |
| **目前按下 (Pressed)** | 亮青綠色 (Cyan) 高亮 | 實體按鍵正在按壓中 |
| **已觸發過 (Triggered)** | 靛藍色 (Indigo) 標記 | 本次測試階段曾被成功觸發 |
| **卡鍵警告 (Stuck Key)** | 琥珀黃/紅 (Amber) 警示 | 按鍵持續按壓超過 2 秒，提示可能卡鍵 |

### 四大診斷面板

1. **滑鼠檢測區 (Mouse Tester)**：視覺化按鈕與滾輪狀態，支援五鍵滑鼠與雙擊測試。
2. **打字測試區 (Typing Area)**：可自由輸入文字並即時換算 WPM 打字速度。
3. **效能統計區 (Metrics)**：即時顯示目前 KPS、Peak KPS 與最後按鍵持續時間（ms）。
4. **即時日誌區 (Event Log)**：依時間序列顯示最近 100 筆按鍵/滑鼠事件。

---

## 📥 下載與安裝

### 方式一：下載獨立執行檔 (推薦)
前往 [GitHub Releases](https://github.com/nojackno2-ctrl/KeyboardMonitor/releases) 頁面下載最新版本的 `KeyboardMonitor.exe`：
- **免安裝任何環境**：內建 .NET 8 執行環境（Self-Contained）。
- **單一執行檔**：下載後直接雙擊即可執行。
- 提供 `SHA256SUMS.txt` 供安全性驗證。

### 方式二：本機自行編譯
若您已安裝 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)，可直接透過原始碼編譯執行。

---

## 🛠️ 開發與建置指南

### 系統需求
- **作業系統**：Windows 10 / Windows 11 (x64)
- **開發環境**：.NET 8.0 SDK 或更高版本

### 建置專案
```powershell
# 還原相依套件 (win-x64)
dotnet restore .\KeyboardMonitor.csproj -r win-x64

# 建置 Release 版本
dotnet build .\KeyboardMonitor.csproj -c Release --no-restore
```

### 執行自動化測試
專案包含內建的回歸測試套件，涵蓋按鍵解析、修飾鍵辨識、數字鍵盤共用鍵、WPM/KPS 計算、卡鍵偵測與生命週期：
```powershell
dotnet run --project .\tests\KeyboardMonitor.Tests\KeyboardMonitor.Tests.csproj -c Release
```

### 程式碼格式驗證
```powershell
dotnet format .\KeyboardMonitor.csproj whitespace --verify-no-changes --no-restore
dotnet format .\tests\KeyboardMonitor.Tests\KeyboardMonitor.Tests.csproj whitespace --verify-no-changes --no-restore
```

### 打包自包含單一執行檔
```powershell
dotnet publish .\KeyboardMonitor.csproj -c Release -r win-x64 `
  --self-contained true --no-restore `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None -p:DebugSymbols=false `
  -o .\publish\win-x64
```

---

## 🏗️ 專案架構與模組設計

```
KeyboardMonitor/
├── KeyboardMonitor.cs         # WinForms 介面呈現、佈局繪製與使用者互動
├── GlobalInputHook.cs          # Windows 全域低階 Hook (WH_KEYBOARD_LL / WH_MOUSE_LL)
├── KeyStateTracker.cs         # 執行緒安全的按鍵狀態追蹤、計時與卡鍵判定
├── InputMetrics.cs            # KPS (每秒按鍵數) 與 WPM (打字測速) 統計服務
├── KeyboardInput.cs           # Windows 虛擬鍵碼 (VK) 與掃描碼 (ScanCode) 解析引擎
├── MonotonicClock.cs          # 高精度單調時鐘（基於 Stopwatch），避免系統時鐘跳變干擾
├── KeyboardMonitor.csproj     # .NET 8 專案設定檔 (嚴格編譯模式與程式碼分析)
├── .github/workflows/ci.yml   # GitHub Actions CI/CD 自動化建置與發布流程
└── tests/
    └── KeyboardMonitor.Tests/ # 零外部相依的完整回歸測試專案
```

---

## ❓ 常見問題 (FAQ)

<details>
<summary><b>Q1: 為什麼程式需要全域 Hook 權限？會不會造成防毒軟體誤報？</b></summary>
<br>
本程式採用 Windows 標準的 <code>SetWindowsHookEx (WH_KEYBOARD_LL / WH_MOUSE_LL)</code> API，目的是為了讓您在切換到全螢幕遊戲、瀏覽器或其他應用程式時，依然能在後台診斷鍵盤與滑鼠的輸入狀況。本專案為 100% 開源，絕無鍵盤側錄、硬碟記錄或網路連線行為。若防毒軟體提示攔截，請安心將其加入信任名單。
</details>

<details>
<summary><b>Q2: 關閉視窗後是否會在背景殘留行程？</b></summary>
<br>
不會。程式實作了嚴謹的 <code>IDisposable</code> 生命週期管理機制，視窗關閉時會立即呼叫 <code>UnhookWindowsHookEx</code> 卸載底層 Hook 並停止所有計時器，完全釋放系統資源。
</details>

<details>
<summary><b>Q3: 如何重設所有的按鍵測試紀錄？</b></summary>
<br>
點擊右上角的「重設 (Reset)」按鈕，即可將所有按鍵恢復至初始狀態，並清空日誌與 KPS 統計數據。
</details>

---

## 📄 授權條款 (License)

本專案基於 [MIT License](LICENSE) 開源授權，歡迎自由使用、修改與分享。
