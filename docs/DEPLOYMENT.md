# ContextRelay Extension - Deployment and Verification Guide

## Current Status
- ✅ VSIX build: Complete and ready (`ContextRelay.VSExtension.vsix` - 139.43 MB)
- ✅ Settings implementation: JSON-based, fully functional
- ✅ In-proc package: Registered as a legacy Tools > Options page
- ✅ Architecture: Hybrid (out-of-proc net8.0 + in-proc net48)

## Deployment Target
- Visual Studio 2026 Insider (18.x)
- Visual Studio 2022 Enterprise (17.9+)
- Both amd64 and arm64 architectures supported

## Pre-Installation Checklist
- [ ] Visual Studio 2026 Insider or 2022 Enterprise is installed
- [ ] VSIX file location: `src\ContextRelay.VSExtension\bin\Debug\net8.0-windows10.0.22621.0\ContextRelay.VSExtension.vsix` (relative to repo root)
- [ ] Settings file exists: `%AppData%\ContextRelay\settings.json` (auto-created on first load)
- [ ] No previous ContextRelay extension is installed

## Installation Methods

### Method 1: Experimental Instance (Development/Testing)
Recommended for internal testing and validation.

```powershell
# Visual Studio 2026 Insider
& 'C:\Program Files\Microsoft Visual Studio\18\Insiders\Common7\IDE\devenv.exe' `
  /rootsuffix Exp `
  /Install '<repo-root>\src\ContextRelay.VSExtension\bin\Debug\net8.0-windows10.0.22621.0\ContextRelay.VSExtension.vsix'

# Visual Studio 2022 Enterprise (if available)
& 'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\devenv.exe' `
  /rootsuffix Exp `
  /Install '<repo-root>\src\ContextRelay.VSExtension\bin\Debug\net8.0-windows10.0.22621.0\ContextRelay.VSExtension.vsix'
```

### Method 2: Main Instance
For production or direct validation in the main VS instance.

```powershell
# Visual Studio 2026 Insider
& 'C:\Program Files\Microsoft Visual Studio\18\Insiders\Common7\IDE\devenv.exe' `
  /Install '<repo-root>\src\ContextRelay.VSExtension\bin\Debug\net8.0-windows10.0.22621.0\ContextRelay.VSExtension.vsix'
```

### Method 3: Extension Manager (GUI)
1. Open Visual Studio
2. Go to **Extensions** > **Manage Extensions** (Ctrl+Shift+X)
3. Click the **⚙️** (Settings) button at the top-right
4. Select **Install from VSIX**
5. Navigate to `ContextRelay.VSExtension.vsix` and open
6. Click **Install** and restart Visual Studio when prompted

## Post-Installation Verification

### Step 1: Verify Options Page Registration
1. Open Visual Studio
2. Go to **Tools** > **Options** (or press Ctrl+Shift+Alt+O)
3. In the left tree, expand and look for **ContextRelay**
4. Click **ContextRelay** > **General**
5. Verify the following properties are visible:
   - Max Results (current: 10)
   - Output Directory (current: .contextrelay)
   - Enable Chat Preview (current: true)
   - UI Language (current: ja)
   - And 14 additional settings

**Expected Result**: Options page displays all 18 settings with proper categorization

### Step 2: Verify Options Search
1. Go to **Tools** > **Options**
2. In the search box at the top-left, type "contextrelay"
3. Look for entries appearing in the search results
4. Click on results to navigate to them

**Expected Result**: Search returns "ContextRelay > General" and related entries in the Tools > Options experience

### Step 3: Verify JSON Settings Persistence
1. In the Options page, change one setting (e.g., set **Max Results** to 20)
2. Click **OK** to save
3. Open **File Explorer** and navigate to: `%AppData%\ContextRelay\`
4. Open `settings.json` with a text editor
5. Look for the changed value: `"MaxResults": 20`
6. Close and reopen Visual Studio
7. Return to **Tools** > **Options** > **ContextRelay** > **General**
8. Verify the setting retained the value (Max Results = 20)

**Expected Result**: Settings are persisted to JSON and survive VS restart

### Step 4: Verify Language Switching
1. In Options page, change **UI Language** from "ja" (Japanese) to "en" (English)
2. Click **OK**
3. Watch the Visual Studio UI for language changes in tool windows
4. Return to **Tools** > **Options** > **ContextRelay** > **General**
5. Verify **UI Language** now shows "en"

**Expected Result**: Language changes apply immediately; no restart needed

### Step 5: Verify Tool Window Integration
1. Open **View** > **Other Windows** > **ContextRelay**
2. Verify the ContextRelay tool window displays
3. Change a setting in **Tools** > **Options** > **ContextRelay** > **General**
4. Observe if the tool window reflects the setting change (if applicable)
5. Use the language toggle buttons in the tool window (if present)

**Expected Result**: Tool window loads without errors and reflects settings

### Step 6: Inspect ActivityLog for Errors
If any step above fails, inspect the Visual Studio Activity Log for diagnostics:

```powershell
# For main instance
$activityLog = 'C:\Users\KazushiKamegawa\AppData\Local\Microsoft\VisualStudio\18.0_*\ActivityLog.xml'
$log = Get-Item (Resolve-Path $activityLog)[0]
Write-Host "Activity Log: $($log.FullName)"

# Search for ContextRelay-related entries
Get-Content $log | Select-String -Pattern "ContextRelay|Package|Error" | Select-Object -First 20
```

## Troubleshooting

### Issue: Options Page Does Not Appear
**Symptoms**: ContextRelay is not visible in Tools > Options tree

**Diagnostic Steps**:
1. Check ActivityLog for package load errors
2. Verify VSIX extraction completed successfully
3. Uninstall extension and perform clean reinstall
4. Check `%AppData%\ContextRelay\` folder permissions (should be writable)

**Resolution**:
```powershell
# Uninstall from main instance
& 'C:\Program Files\Microsoft Visual Studio\18\Insiders\Common7\IDE\devenv.exe' /Uninstall-Extension ContextRelay.VSExtension

# Reinstall
& 'C:\Program Files\Microsoft Visual Studio\18\Insiders\Common7\IDE\devenv.exe' `
  /Install '<repo-root>\src\ContextRelay.VSExtension\bin\Debug\net8.0-windows10.0.22621.0\ContextRelay.VSExtension.vsix'
```

### Issue: Settings Changes Are Not Saved
**Symptoms**: Changing values in Options page does not persist in settings.json

**Diagnostic Steps**:
1. Verify folder exists: `%AppData%\ContextRelay\`
2. Verify write permissions on folder
3. Check for corrupted JSON: `Get-Content -Raw $env:APPDATA\ContextRelay\settings.json | ConvertFrom-Json`
4. Check ActivityLog for save errors

**Resolution**:
```powershell
# Backup existing settings
Copy-Item "$env:APPDATA\ContextRelay\settings.json" "$env:APPDATA\ContextRelay\settings.json.bak"

# Delete corrupted file (will be regenerated with defaults)
Remove-Item "$env:APPDATA\ContextRelay\settings.json"

# Restart Visual Studio - settings.json will be recreated
```

### Issue: UI Language Does Not Change
**Symptoms**: Language setting changes but UI does not update

**Diagnostic Steps**:
1. Verify UI Language value is valid ("en", "ja", "auto")
2. Check ActivityLog for language initialization errors
3. Verify ContextRelayLocalizedStrings is being called

**Resolution**:
1. Close all Visual Studio windows
2. Delete `settings.json` to reset to defaults (UI Language: "auto")
3. Restart Visual Studio
4. In Options, set UI Language to specific value ("en" or "ja")
5. Restart Visual Studio to apply

### Issue: VSIX Installation Fails
**Symptoms**: Installation command returns error or hangs

**Diagnostic Steps**:
1. Verify VSIX file exists and is not corrupted: `(Get-Item <vsix-path>).Length -gt 50MB`
2. Verify file is not in use: `Get-Process devenv -ErrorAction SilentlyContinue`
3. Check disk space: `Get-Volume | Where-Object {$_.DriveLetter -eq 'C'} | Select-Object SizeRemaining`

**Resolution**:
```powershell
# Close all VS instances
Get-Process devenv | Stop-Process -Force

# Clean temp files
Remove-Item "$env:LOCALAPPDATA\Microsoft\VisualStudio\*\ComponentModelCache" -Recurse -Force -ErrorAction SilentlyContinue

# Restart installation
& 'C:\Program Files\Microsoft Visual Studio\18\Insiders\Common7\IDE\devenv.exe' /Repair
```

## Rollback Procedure

If the extension causes issues, uninstall using:

```powershell
# Using devenv.exe
& 'C:\Program Files\Microsoft Visual Studio\18\Insiders\Common7\IDE\devenv.exe' /Uninstall-Extension ContextRelay.VSExtension

# Or using Extension Manager
# Tools > Extensions > Manage Extensions > Search "ContextRelay" > Uninstall > Restart
```

## Next Steps After Installation

1. **Validate all verification steps** to ensure proper registration
2. **Test feature functionality** by using the ContextRelay tool window
3. **Collect feedback** on Options page usability and settings
4. **Report issues** with exact error messages and ActivityLog excerpts
5. **Performance testing** if needed for large workspaces

## Support Resources

- Visual Studio Extensibility Documentation: https://learn.microsoft.com/en-us/visualstudio/extensibility/
- VSSDK Options Registration: https://learn.microsoft.com/en-us/visualstudio/extensibility/registering-and-unregistering-vspackages
- Unified Settings Documentation: https://learn.microsoft.com/en-us/visualstudio/extensibility/unified-settings

---

**Last Updated**: 2024
**Extension Version**: Hybrid Architecture (out-of-proc net8.0 + in-proc net48)
**Supported Versions**: VS 2022 17.9+, VS 2026 Insider
