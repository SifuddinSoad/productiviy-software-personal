# FocusLock থেকে উদ্ধারের উপায়

> **কোথা থেকে চালাচ্ছেন সেটা জরুরি।** Shared folder (`\\VBoxSvr\build\...`) থেকে চালালে
> restart-এর পর session ফিরে আসবে না — Windows logon-এর সময় shared folder তখনো তৈরি হয় না।
> `scripts\install.ps1` চালিয়ে আগে এই PC-তে install করে নিন।

Lock কোনো bug-এর কারণে শেষ না হলে এই ক্রমে চেষ্টা করুন। প্রতিটা উপায় আগেরটার চেয়ে কঠিন।

## ১. App-এর ভেতরেই Emergency exit

Top bar-এ **Emergency exit** → screen-এর code হুবহু type করুন।

## ২. Safe Mode + `--cleanup` (সবচেয়ে সহজ বাইরের রাস্তা)

Safe Mode-এ HKCU `Run` entry চলে না, আর guard service-ও চলে না (`start= auto` service Safe
Mode-এ ওঠে না)। তাই FocusLock নিজে থেকে খুলবে না।

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

5. **Guard service install করা থাকলে** — administrator PowerShell-এ:

```
powershell -ExecutionPolicy Bypass -File "C:\Program Files\FocusLock\scripts\uninstall-service.ps1"
```

Admin না থাকলেও ক্ষতি নেই: উপরের `--cleanup` inbox-এ "ছেড়ে দাও" লিখে রাখে, service পরের
normal boot-এ সেটা পড়ে নিজেই ছেড়ে দেয় — কিছু lock হওয়ার আগেই।

6. স্বাভাবিকভাবে restart

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

**Guard state মুছে ফেলা** (service install করা থাকলে):

```
del X:\ProgramData\FocusLock\guard.json
del X:\ProgramData\FocusLock\guard.json.bak
```

## ৪. Guard service বন্ধ করা (WinRE)

WinRE command prompt-এ:

```
reg load HKLM\SYS X:\Windows\System32\config\SYSTEM
reg add "HKLM\SYS\ControlSet001\Services\FocusLockGuard" /v Start /t REG_DWORD /d 4 /f
reg unload HKLM\SYS
```

`Start = 4` মানে service disabled।

---

## Service আসলে কী করে, কী করে না

- **করে:** session চলাকালে app বন্ধ হয়ে গেলে ~১ সেকেন্ডে ফিরিয়ে আনে; restart-এর পর logon-এর
  সাথে সাথেই চালু করে; state রাখে `C:\ProgramData\FocusLock`-এ, যেখানে সাধারণ user পড়তে পারে
  কিন্তু বদলাতে পারে না।
- **করে না:** নিজে কিছুই block করে না। সব locking app-এর কাজ, তাই service ভুল করলেও বড়জোর
  একটা program চালু করবে — screen ধরে রাখতে পারবে না।
- **নিজেই ছেড়ে দেয়:** planned সময় শেষ হলে, app বারবার চালু হয়ে বন্ধ হতে থাকলে (১ মিনিটে ৬ বার),
  বা state file পড়া না গেলে।
- **যা ঠেকায় না:** app আপনার account-এই চলে, তাই `inbox` folder-এ হাতে একটা "end" command লিখে
  দিলে service ছেড়ে দেবে। Safe Mode-এর মতোই — জেনে-বুঝে বের হতে চাইলে পথ আছে, ভুল করে বা
  তাড়াহুড়োয় বের হওয়া যাবে না।
