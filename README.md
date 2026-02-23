# GTAPhoneHudFix
Phone Hud Fix is a lightweight .Net utility script that fixes the annoying GTA V phone UI desync bug where hud elements like contact pictures, camera control text, UI overlays, and scaleform layers randomly disappear. This script automatically refreshes the phone scripts and UI assets to restore missing elements.

## [ FEATURES ]
- Automatic repair runs once on startup to immediately correct existing UI issues.
- Configurable manual fix command (fixphonehud) for instant on-demand repair.
- Optional periodic background repair at configurable intervals
- Staged reset process to safely reload phone scripts and UI assets without restarting the game
- Recreates mobile phone object and reloads Scaleform UI layers
- Lightweight and performance-friendly (runs only when needed)
- Fully configurable via .ini


## [ REQUIREMENTS ]
- Latest ScriptHookV
Enhanced:
- Latest ScriptHookVDotNet v3 Enhanced
Legacy:
- Latest ScriptHookVDotNet v3 Nightly

## [ INSTALLATION ]
- Install ScriptHookV and ScriptHookVDotNet v3
- Place PhoneHudFix.dll into your GTA V/scripts/ folder
- (Optional) Edit PhoneHudFix.ini to customize behavior
- Enjoy

## [ Incompatibilities ]
- None. Works great with iFruitAddon2

## [ Credits & Acknowledgements ]
- Alexander Blade - for ScriptHookV
- crosire - for ScriptHookVDotNet
- Chiheb-Bacha - for ScriptHookVDotNet Enhanced

Without their foundational tools, this mod would not be possible.

