# FocusLock থেকে উদ্ধারের উপায়

> **কোথা থেকে চালাচ্ছেন সেটা জরুরি।** Shared folder (`\\VBoxSvr\build\...`) থেকে চালালে
> restart-এর পর session ফিরে আসবে না — Windows logon-এর সময় shared folder তখনো তৈরি হয় না।
> `scripts\install.ps1` চালিয়ে আগে এই PC-তে install করে নিন।

Lock কোনো bug-এর কারণে শেষ না হলে এই ক্রমে চেষ্টা করুন। প্রতিটা উপায় আগেরটার চেয়ে কঠিন।

## ১. App-এর ভেতরেই Emergency exit

Top bar-এ **Emergency exit** → screen-এর code হুবহু type করুন।

## ২. Safe Mode + `--cleanup` (সবচেয়ে সহজ বাইরের রাস্তা)

Safe Mode-এ HKCU `Run` entry চলে না, তাই FocusLock নিজে থেকে খুলবে না।

1. Login screen-এ ডান-নিচের **Power** → **Shift** চেপে ধরে **Restart**
2. **Troubleshoot → Advanced options → Startup Settings → Restart** → **4** (Safe Mode)
3. যে account lock হয়েছিল সেই account দিয়ে login
4. `Win + R` → নিচের যেকোনো একটা:

```
"C:\Program Files\FocusLock\FocusLock.App.exe" --cleanup
```

অথবা app ফাইল না চললে:

```
powershell -ExecutionPolicy Bypass -File "C:\Program Files\FocusLock\scripts\uninstall.ps1"
```

(App অন্য folder-এ থাকলে সেই path দিন।)

5. স্বাভাবিকভাবে restart

## ৩. WinRE command prompt (Windows-এ ঢোকাই যাচ্ছে না)

WinRE-তে ঢোকা: boot-এর সময় ৩ বার force shutdown → **Troubleshoot → Advanced options → Command Prompt**।

WinRE-তে drive letter বদলে যায়। আগে Windows কোন drive-এ আছে বের করুন:

```
dir C:\Users
dir D:\Users
```

যেটায় আপনার user folder দেখায়, নিচে সেটাকেই `X:` ধরা হয়েছে। `USERNAME` = যে account lock হয়েছিল।

**Active session মুছে ফেলা:**

```
del X:\Users\USERNAME\AppData\Local\FocusLock\active.json
```

**User-এর registry load করে startup entry আর Task Manager policy মুছে ফেলা:**

```
reg load HKU\FL X:\Users\USERNAME\NTUSER.DAT
reg delete "HKU\FL\Software\Microsoft\Windows\CurrentVersion\Run" /v FocusLock /f
reg delete "HKU\FL\Software\Microsoft\Windows\CurrentVersion\Policies\System" /v DisableTaskMgr /f
reg unload HKU\FL
```

`exit` → **Continue**।

## ৪. (Phase 3-এ service যোগ হওয়ার পর) Service বন্ধ করা

WinRE command prompt-এ:

```
reg load HKLM\SYS X:\Windows\System32\config\SYSTEM
reg add "HKLM\SYS\ControlSet001\Services\FocusLockService" /v Start /t REG_DWORD /d 4 /f
reg unload HKLM\SYS
```

`Start = 4` মানে service disabled।
