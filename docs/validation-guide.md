# Visual Studio Options Registration - Validation Guide

## Prerequisites
- Visual Studio 2022 (version 17.9+) or Visual Studio 2026 Insider
- The generated VSIX file: `ContextRelay.VSExtension.vsix`

## Installation Steps

### Option 1: Using Visual Studio Extension Manager
1. Open Visual Studio
2. Go to **Extensions** > **Manage Extensions**
3. Click **⚙️ Settings** (gear icon) in the top right
4. Select **Install from VSIX**
5. Navigate to `C:\temp\ContextRelay.VSExtension.vsix` and select it
6. Follow the prompts to install and restart VS

### Option 2: Using Visual Studio Experimental Instance (Development)
```powershell
$vsixPath = 'C:\temp\ContextRelay.VSExtension.vsix'
$expInstanceId = '18.0_24b3d224Exp'
$vsDevCmd = 'C:\Program Files\Microsoft Visual Studio\2026\Enterprise\Common7\IDE\devenv.exe'

# Install the extension to the Experimental Instance
& $vsDevCmd /rootsuffix Exp /Install $vsixPath
```

## Verification Checklist

### 1. Options Page Visibility
- [ ] Open Visual Studio
- [ ] Go to **Tools** > **Options**
- [ ] Expand the tree on the left
- [ ] Verify **ContextRelay** appears as a category
- [ ] Verify **General** sub-item appears under ContextRelay
- [ ] Click on ContextRelay > General and verify the property grid displays

### 2. Settings Properties
Verify all settings are displayed in the Options page:
- [ ] **General**: Max Results, Output Directory, Enable Chat Preview, UI Language
- [ ] **Diagnostics**: Enable Graph Debug Logging, Enable Work IQ Debug Logging
- [ ] **Authentication**: Client ID, Tenant ID, Cloud Environment, Custom Endpoints, Use Broker
- [ ] **Caching**: Cache TTL, Cache Max Entries, Persist Workspace State
- [ ] **Features**: Mail, Teams, SharePoint, OneDrive, Connectors, OneNote, Planner, Todo enabled/disabled toggles

### 3. Tools > Options Registration
- [ ] Open **Tools** > **Options** > **Search**
- [ ] Type "contextrelay" or "settings" in the search box
- [ ] Verify **ContextRelay > General** appears in search results
- [ ] Verify the description mentions "ContextRelay options"
- > **Note**: The extension uses the legacy Tools > Options page (not Unified Settings). `IsInUnifiedSettings` is forced to `0` to keep the page visible in Visual Studio 18.

### 4. JSON Settings Persistence
- [ ] In the Options page, change at least one setting (e.g., Max Results to 20)
- [ ] Click **OK** to save
- [ ] Open **File Explorer** and navigate to `%AppData%\ContextRelay\`
- [ ] Verify `settings.json` file exists
- [ ] Open `settings.json` and verify the changed setting is persisted (e.g., `"MaxResults": 20`)
- [ ] Close and reopen Visual Studio
- [ ] Go to **Tools** > **Options** > **ContextRelay** > **General**
- [ ] Verify the setting retained the value from the JSON file

### 5. ActivityLog Diagnostics
If the Options page does not appear, diagnose using ActivityLog:

```powershell
# For the Experimental Instance
$activityLog = 'C:\Users\KazushiKamegawa\AppData\Local\Microsoft\VisualStudio\18.0_24b3d224Exp\ActivityLog.xml'

# Or for the main instance
$activityLog = 'C:\Users\KazushiKamegawa\AppData\Local\Microsoft\VisualStudio\18.0_e706f682\ActivityLog.xml'

# Open and search for errors
Get-Content $activityLog | Select-String -Pattern "Error|ContextRelay|Package"
```

Look for:
- [ ] No "Package not loaded" errors for ContextRelay
- [ ] No "Failed to load" errors for ContextRelayOptionsPackage
- [ ] No registry access errors for `HKCU\Software\Microsoft\VisualStudio\18.0\ToolsOptionsPages\ContextRelay`

### 6. Tool Window Integration
- [ ] Verify that the ContextRelay tool window still displays (**View** > **Other Windows** > **ContextRelay**)
- [ ] Verify that changes made in Options page do not break tool window functionality
- [ ] Make a change in the Options page, save, and verify the tool window reflects the change

## Common Issues and Troubleshooting

### Issue: ContextRelay does not appear in Options
**Cause**: The in-proc package may not be loading
**Solution**:
1. Check ActivityLog.xml for package load errors
2. Verify VSIX contains `ContextRelay.VSExtension.Package.pkgdef`
3. Check registry: `HKEY_CURRENT_USER\Software\Microsoft\VisualStudio\18.0_*\ToolsOptionsPages\ContextRelay`
4. Restart Visual Studio with `/SafeMode` and re-install

### Issue: Settings changes are not persisted
**Cause**: JSON file may not be writable or path incorrect
**Solution**:
1. Verify write permissions on `%AppData%\ContextRelay\` folder
2. Check for JSON file format errors: `Get-Content -Raw $env:APPDATA\ContextRelay\settings.json | ConvertFrom-Json`
3. Manually delete `settings.json` and restart VS to regenerate with defaults

### Issue: UI Language doesn't change
**Cause**: UI Language normalization or cache issue
**Solution**:
1. Verify `UiLanguage` value in `settings.json` is valid ("en", "ja", "auto", etc.)
2. Restart Visual Studio to apply UI language changes
3. Check ActivityLog for language initialization errors

## Additional Documentation
- [Microsoft Learn: Create Extensions](https://learn.microsoft.com/en-us/visualstudio/extensibility/creating-an-extension-with-a-vspackage)
- [VSSDK: ProvideOptionPage attribute](https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.shell.provideoptionpageattribute)
- [VSSDK: Unified Settings](https://learn.microsoft.com/en-us/visualstudio/extensibility/unified-settings)
