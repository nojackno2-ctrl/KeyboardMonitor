# Keyboard Monitor

Windows 鍵盤與滑鼠診斷工具，可即時顯示實體按鍵狀態、卡鍵、按壓持續時間、每秒按鍵數、打字速度，以及滑鼠按鍵與滾輪事件。

## 功能

- 支援 100%、80% TKL 與 60% 鍵盤配置。
- 使用 Windows 全域低階 Hook 偵測鍵盤與滑鼠事件。
- 區分左右 Shift、Ctrl、Alt，以及主鍵盤／數字鍵盤共用鍵碼。
- 顯示目前與最高 KPS、WPM、按鍵持續時間及最近 100 筆事件。
- 按鍵持續超過 2 秒時標示為可能卡鍵。
- 所有輸入事件只在本機記憶體中用於即時顯示；程式不儲存或傳送輸入內容。

## 系統需求

- Windows 10 或 Windows 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)，或使用自包含發佈版本

## 建置與驗證

```powershell
dotnet build .\KeyboardMonitor.csproj -c Release
dotnet run --project .\tests\KeyboardMonitor.Tests\KeyboardMonitor.Tests.csproj -c Release
dotnet format .\KeyboardMonitor.csproj whitespace --verify-no-changes --no-restore
```

回歸執行器不依賴外部測試套件，涵蓋鍵碼解析、修飾鍵、導覽鍵／數字鍵盤、WPM 與 KPS 計算。

## 執行

```powershell
dotnet run --project .\KeyboardMonitor.csproj -c Release
```

程式會監控目前 Windows 桌面工作階段的全域鍵盤與滑鼠事件。關閉視窗後，程式會立即解除 Hook 並釋放計時器資源。

## 建立自包含單一執行檔

```powershell
dotnet publish .\KeyboardMonitor.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
  -o .\publish\win-x64
```
