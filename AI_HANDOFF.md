# AI Handoff

## Current objective

完整檢查程式，修正已知問題，完成可重現的驗證後發佈到 GitHub。

## Repository state

- 工作目錄原先不是 Git 儲存庫；2026-07-23 已初始化為 `main`。
- 初始提交為 `d049844`（`完整檢查並強化鍵盤滑鼠診斷工具`）；提交後工作樹乾淨，尚無遠端。
- 專案為單一 `KeyboardMonitor.csproj`（.NET 8 Windows Forms）與 `KeyboardMonitor.cs`。
- `KeyboardMonitor.cs.bak`、`.vs/`、`bin/`、`obj/` 是既有本機檔案；尚未納入 Git。
- 原先缺少 `AGENTS.md` 與 `AI_HANDOFF.md`，已依協作規範建立。

## Evidence and findings

- 2026-07-23：`dotnet build KeyboardMonitor.csproj -c Release --no-restore` 成功，0 警告、0 錯誤。
- 2026-07-23：確認並修正 KPS 計時器每秒清零後 getter 又讀取已清零欄位，導致當前 KPS 固定顯示 0。
- 2026-07-23：新增 `KeyboardInput`，修正通用 Shift/Ctrl/Alt 左右鍵解析，以及 NumLock 關閉時數字鍵盤與導覽鍵共用虛擬鍵碼的誤判。
- 2026-07-23：滑鼠偵測由只接收本程式視窗訊息的 `IMessageFilter` 改為 `WH_MOUSE_LL` 全域低階 Hook。
- 2026-07-23：Hook 安裝失敗現在會顯示 Win32 錯誤並安全關閉；關閉流程會解除鍵盤／滑鼠 Hook 並停止、釋放 Timer，不再呼叫 `Environment.Exit(0)`。
- 2026-07-23：新增無外部測試套件的回歸執行器，涵蓋基本鍵、左右修飾鍵、導覽／數字鍵盤、OEM 鍵、WPM 與 KPS 取樣。
- 2026-07-23：嚴格 Release 建置（warnings as errors）成功，0 警告、0 錯誤；回歸測試 6/6 組通過。
- 2026-07-23：`.NET 8 recommended analyzers` 搭配 warnings as errors 與 whitespace format check 均通過。
- 2026-07-23：第一次 Windows UI 啟動成功，100% 鍵盤、滑鼠、打字、日誌面板均可見；畫面顯示底部提示略遭裁切。
- 2026-07-23：實際輸入驗證被使用者實體 Escape 鍵中止，不能視為完成。
- 2026-07-23：已依初次畫面把視窗高度由 780 調整為 810、移除不必要的 `Premium Mode` 標題，且配置切換不再縮窄整個視窗，以避免 80%／60% 模式讓固定寬度的下方診斷面板嚴重裁切；同時補上鍵盤配置與按鍵的無障礙名稱。
- 2026-07-23：修正版通過 format check、Release build（0 warnings / 0 errors）與 6/6 回歸測試。
- 2026-07-23：自包含 win-x64 單檔發佈成功，輸出僅 `KeyboardMonitor.exe` 一個檔案，大小 177,798,963 bytes，SHA-256 `62494CC4A157E21AF4C907E1234F261903A16035D3A671EE378B73BBBE976706`。
- 2026-07-23：Windows 實機驗證完成。全域鍵盤 Hook 正確記錄 A、CTRL_L、SHIFT_L、B、NUM_8；畫面顯示當前 KPS 3、最高 KPS 3、WPM 2、持續時間 65ms。
- 2026-07-23：全域滑鼠 Hook 正確記錄 L_BUTTON 按下／放開及滾輪向上。
- 2026-07-23：100%、80% TKL、60% 三種配置均已實際切換並截圖檢查；修正後下方面板與提示文字無裁切。
- 2026-07-23：清除重設已實測，日誌回到單一重設事件、按鍵狀態清空；關閉按鈕可正常退出且程序消失。
- 靜態掃描未發現網路、檔案寫入、登錄、剪貼簿或外部程序啟動；原生呼叫僅限 System32 的 user32/kernel32 Hook API。
- GitHub CLI 2.95.0 已安裝，Git identity 為 `Jackie Chen <nojackno2@hotmail.com.tw>`；`nojackno2-ctrl/KeyboardMonitor` 尚不存在或目前無法存取。
- 2026-07-23：使用者完成 GitHub 裝置登入；沙箱外 `gh auth status` 已確認 `nojackno2-ctrl` 為作用中帳號，具備 `repo` 與 `workflow` scope。
- 使用者指定建立公開儲存庫 `nojackno2-ctrl/KeyboardMonitor`。
- 2026-07-23：已建立並推送公開儲存庫 `https://github.com/nojackno2-ctrl/KeyboardMonitor`，預設分支為 `main`，本機 `main` 追蹤 `origin/main`。
- 首次遠端 CI run `29989550172` 全綠：Restore、Build、6/6 回歸測試與格式檢查均通過。
- 遠端提交 `449f2df` 的 CI run `29989658649` 亦全綠，確認最後程式碼與交接更新在 GitHub runner 上可建置、測試並通過格式檢查。
- 遠端提交 `d42a6e8` 的 CI run `29989746345` 功能全綠，但 GitHub 標註 `actions/checkout@v4` 與 `actions/setup-dotnet@v4` 使用已淘汰的 Node.js 20。
- 官方最新 release 與 major tag 已確認為 `actions/checkout@v7`、`actions/setup-dotnet@v6`；工作流程已升級，須以最新 CI 確認警告消失。

## Constraints

- 不直接刪除既有 `.bak` 或輸出目錄；以 `.gitignore` 排除即可。
- 發佈前必須實際完成 Release 建置、自動測試與 Windows UI 操作驗證。
- GitHub 發佈須先確認 GitHub CLI 可用、已登入，以及新儲存庫名稱／可見性或既有目標。

## Next actions

1. 提交並推送 action major 升級。
2. 確認最新 CI 全綠且無 Node.js 20 annotation。
