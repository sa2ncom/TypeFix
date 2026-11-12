# TypeFix Build, Test & Release Guide

A complete guide for development, daily testing, multi-architecture publishing (x64, x86, ARM64), and generating installers.

---

## 🇬🇧 English

### 0. Quick Cheat Sheet (Which Command Should I Run?)

| Goal | Command | Notes |
| :--- | :--- | :--- |
| **Quick test while coding** | `dotnet build`<br>`dotnet run` | Or `.\bin\Debug\net10.0-windows\TypeFix.exe --tray` to test silent tray startup |
| **Portable `.exe` only (no Setup)** | `dotnet publish -p:PublishProfile=win-x64` | Outputs `publish\win-x64\TypeFix.exe` |
| **Final Setup Installers (x64, x86, ARM64)** | `.\installer\build-installer.ps1` | Builds **everything** in one step into `installer\output\` |

> [!IMPORTANT]
> **Do NOT run `dotnet publish` before `.\installer\build-installer.ps1`!**
> `.\installer\build-installer.ps1` already terminates running `TypeFix` instances, runs `dotnet publish` for `win-x64`, `win-x86`, and `win-arm64`, and compiles all 3 Setup installers automatically. Running `dotnet publish` first only builds everything twice.

---

### 1. Daily Development Workflow

> [!NOTE]
> **Do I need to build the installer after every code change?**
> **No!** Building the setup installer is time-consuming and only intended for final releases. During daily development, focus only on building and running the project directly.

#### Standard Development Cycle:
1. **Make code changes** in your editor.
2. **Compile check (fast syntax & build verification):**
   ```powershell
   dotnet build
   ```
3. **Run & Test locally:**
   ```powershell
   dotnet run
   ```
   *(Or press `F5` / `Ctrl+F5` in Visual Studio).*
4. **Test silent background / Windows startup mode (`--tray`):**
   ```powershell
   .\bin\Debug\net10.0-windows\TypeFix.exe --tray
   ```
   - No window should open; the icon appears directly in the System Tray and `F10` works immediately.
   - Launching `TypeFix.exe` again without `--tray` (or clicking the tray icon) opens the main window.
   - Check the registered Windows startup command anytime with:
     ```powershell
     Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "TypeFix"
     ```

> [!IMPORTANT]
> **Testing Tip for TypeFix:**
> - TypeFix runs as a background tray application. Look for its icon in the Windows Notification Area / System Tray.
> - **Always close any existing running instance** before building or launching a new one, otherwise Windows will lock the executable file:
>   ```powershell
>   Get-Process -Name "TypeFix" -ErrorAction SilentlyContinue | Stop-Process -Force
>   ```

---

### 2. Publishing All 3 Architectures (x64, x86, ARM64)

TypeFix uses self-contained, single-file publish profiles located in `Properties/PublishProfiles/`. These outputs bundle the .NET runtime and do not require .NET to be pre-installed on the target machine.

#### One-Liner Command to Publish All 3 Architectures:
Run this PowerShell command from the repository root:

```powershell
foreach ($p in 'win-x64','win-x86','win-arm64') { dotnet publish -p:PublishProfile=$p; if ($LASTEXITCODE -ne 0) { break } }
```

#### Individual Publish Commands:
| Architecture | Command | Output Path | Target System |
| :--- | :--- | :--- | :--- |
| **Windows 64-bit (x64)** | `dotnet publish -p:PublishProfile=win-x64` | `publish\win-x64\TypeFix.exe` | Standard 64-bit PCs (Recommended) |
| **Windows 32-bit (x86)** | `dotnet publish -p:PublishProfile=win-x86` | `publish\win-x86\TypeFix.exe` | 32-bit Windows systems |
| **Windows ARM64** | `dotnet publish -p:PublishProfile=win-arm64` | `publish\win-arm64\TypeFix.exe` | Windows on ARM (Surface Pro X, Snapdragon X) |

---

### 3. How to Test Each Build on Your Machine

Your current machine is a standard 64-bit Windows OS:

- **Testing x64:** Directly testable via `dotnet run` or by executing `.\publish\win-x64\TypeFix.exe`.
- **Testing x86:** 64-bit Windows has built-in 32-bit compatibility (WOW64). You can directly run and test `.\publish\win-x86\TypeFix.exe`.
- **Testing ARM64:** Compiles cross-platform on any Windows PC without errors. However, to execute and test the binary, you need a physical ARM-based Windows device or an ARM64 virtual machine.

---

### 4. Building Setup Installers for Release

When your code changes and tests are complete and you are ready to produce final distribution packages, run this **single command** *(no need to run `dotnet publish` beforehand)*:

```powershell
.\installer\build-installer.ps1
```

*Requirements: [Inno Setup 6](https://jrsoftware.org/isdl.php)*

This script automatically:
1. Terminates any running `TypeFix` processes so output files are not locked.
2. Publishes self-contained builds for `win-x64`, `win-x86`, and `win-arm64`.
3. Compiles a setup installer for each architecture into `installer\output\`:
   - `installer\output\TypeFix-1.0.1-Setup-x64.exe`
   - `installer\output\TypeFix-1.0.1-Setup-x86.exe`
   - `installer\output\TypeFix-1.0.1-Setup-arm64.exe`

---
---

## 🇮🇷 فارسی

<div dir="rtl">

### ۰. خلاصه سریع (کدام دستور را بزنم؟)

| هدف شما | دستور لازم | توضیح کوتاه |
| :--- | :--- | :--- |
| **تست سریع موقع کدنویسی** | `dotnet build`<br>`dotnet run` | یا `.\bin\Debug\net10.0-windows\TypeFix.exe --tray` برای تست استارتاپ در Tray |
| **فقط خروجی تک‌فایل `.exe` (بدون Setup)** | `dotnet publish -p:PublishProfile=win-x64` | خروجی در `publish\win-x64\TypeFix.exe` |
| **ساخت آخرین نسخه + فایل نصبی (x64, x86, ARM64)** | `.\installer\build-installer.ps1` | همه‌چیز را یکجا در `installer\output\` می‌سازد |

> [!IMPORTANT]
> **قبل از `.\installer\build-installer.ps1` نیازی به اجرای `dotnet publish` نیست!**
> اسکریپت `.\installer\build-installer.ps1` خودش به‌صورت خودکار اول پروسس‌های بازِ `TypeFix` را می‌بندد، سپس برای هر سه معماری (`win-x64`، `win-x86` و `win-arm64`) دستور `dotnet publish` را اجرا می‌کند و در نهایت هر ۳ فایل Setup را می‌سازد. زدن `dotnet publish` قبل از آن فقط بیلد را دو بار انجام می‌دهد.

---

### ۱. چرخه توسعه روزمره

> [!NOTE]
> **آیا لازمه بعد از هر تغییر Installer هم ساخته بشه؟**
> **خیر!** ساخت فایل نصبی فرآیندی زمان‌بر است و فقط مخصوص زمان انتشار نسخه نهایی (Release) است. در زمان برنامه‌نویسی و کارهای روزمره، فقط کامپایل و اجرای مستقیم کافی است.

#### چرخه بهینه توسعه:
۱. **اعمال تغییرات در کد**.
۲. **بررسی سریع خطاها (کامپایل):**
   ```powershell
   dotnet build
   ```
۳. **اجرا و تست محلی:**
   ```powershell
   dotnet run
   ```
   *(یا فشردن کلید `F5` / `Ctrl+F5` در Visual Studio).*
۴. **تست اجرای پس‌زمینه / استارتاپ ویندوز (`--tray`):**
   ```powershell
   .\bin\Debug\net10.0-windows\TypeFix.exe --tray
   ```
   - پنجرهٔ اصلی باز نمی‌شود؛ آیکون برنامه مستقیم کنار ساعت (System Tray) می‌رود و کلید `F10` بلافاصله کار می‌کند.
   - اگر `TypeFix.exe` را دوباره بدون `--tray` اجرا کنید (یا روی آیکون Tray کلیک کنید)، پنجرهٔ اصلی ظاهر می‌شود.
   - برای دیدن دستور ثبت‌شده در استارتاپ ویندوز:
     ```powershell
     Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "TypeFix"
     ```

> [!IMPORTANT]
> **نکته مهم هنگام تست TypeFix:**
> - برنامه TypeFix در تسک‌بار (System Tray کنار ساعت ویندوز) می‌نشیند. عملکرد آیکون و کلیدهای میانبر را چک کنید.
> - **حتماً قبل از کامپایل یا اجرای دوباره**، نسخه در حال اجرا را از تسک‌بار ببندید (Exit) تا فایل قفل نباشد:
>   ```powershell
>   Get-Process -Name "TypeFix" -ErrorAction SilentlyContinue | Stop-Process -Force
>   ```

---

### ۲. ساخت خروجی هر ۳ معماری (x64, x86, ARM64)

پروژه‌ی TypeFix از پروفایل‌های انتشار مستقل (`Self-Contained` و `Single-File`) واقع در `Properties/PublishProfiles/` استفاده می‌کند. این خروجی‌ها بدون نیاز به نصب بودن دات‌نت روی سیستم مقصد اجرا می‌شوند.

#### دستور تک‌خطی برای خروجی گرفتن هر ۳ معماری به‌صورت هم‌زمان:
این دستور پاورشل را در ریشه پروژه اجرا کنید:

```powershell
foreach ($p in 'win-x64','win-x86','win-arm64') { dotnet publish -p:PublishProfile=$p; if ($LASTEXITCODE -ne 0) { break } }
```

#### دستورات جداگانه برای هر معماری:
| معماری | دستور ساخت (PowerShell) | مسیر فایل خروجی | کاربرد |
| :--- | :--- | :--- | :--- |
| **ویندوز ۶۴ بیتی (x64)** | `dotnet publish -p:PublishProfile=win-x64` | `publish\win-x64\TypeFix.exe` | سیستم‌های استاندارد ۶۴ بیتی (پیشنهادی) |
| **ویندوز ۳۲ بیتی (x86)** | `dotnet publish -p:PublishProfile=win-x86` | `publish\win-x86\TypeFix.exe` | سیستم‌های قدیمی ۳۲ بیتی |
| **ویندوز آرم (ARM64)** | `dotnet publish -p:PublishProfile=win-arm64` | `publish\win-arm64\TypeFix.exe` | دستگاه‌های مجهز به پردازنده ARM (مانند سرفیس پرو X یا اسنپ‌دراگون) |

---

### ۳. روش تست نسخه‌ها روی سیستم شما

سیستم فعلی شما یک ویندوز استاندارد ۶۴ بیتی است:

- **تست نسخه x64:** مستقیماً با دستور `dotnet run` یا اجرای فایل `publish\win-x64\TypeFix.exe` تست می‌شود.
- **تست نسخه x86:** ویندوز ۶۴ بیتی شما دارای قابلیت داخلی اجرای برنامه‌های ۳۲ بیتی (WOW64) است؛ بنابراین می‌توانید فایل `publish\win-x86\TypeFix.exe` را مستقیم اجرا و تست کنید.
- **تست نسخه ARM64:** بیلد آن روی سیستم شما با موفقیت انجام می‌شود، اما برای اجرای فایل اجرایی نیاز به یک دستگاه مبتنی بر ARM یا شبیه‌ساز ARM64 دارید.

---

### ۴. ساخت اینستالر نهایی برای انتشار (Release)

زمانی که تغییرات کامل شد و قصد انتشار نسخه رسمی را داشتید، فقط همین **یک دستور** را بزنید *(نیازی به زدن `dotnet publish` قبل از آن نیست)*:

```powershell
.\installer\build-installer.ps1
```

*پیش‌نیاز: نصب بودن [Inno Setup 6](https://jrsoftware.org/isdl.php)*

این اسکریپت به‌صورت خودکار کارهای زیر را انجام می‌دهد:
۱. پروسس‌های در حال اجرای `TypeFix` را می‌بندد تا فایل‌ها قفل نباشند.
۲. خروجی هر سه نسخه `win-x64`، `win-x86` و `win-arm64` را می‌گیرد.
۳. با Inno Setup سه فایل نصبی کامل در پوشه `installer\output\` تولید می‌کند:
   - `installer\output\TypeFix-1.0.1-Setup-x64.exe`
   - `installer\output\TypeFix-1.0.1-Setup-x86.exe`
   - `installer\output\TypeFix-1.0.1-Setup-arm64.exe`

</div>
