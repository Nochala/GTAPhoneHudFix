# GTAPhoneHudFix
Phone Hud Fix is a lightweight .Net utility script that fixes the annoying GTA V phone UI desync bug where hud elements like contact pictures, camera control text, UI overlays, and scaleform layers randomly disappear. This script automatically refreshes the phone scripts and UI assets to restore missing elements.

### Note on Future Updates
**Development on this mod is largely complete, so future updates are unlikely unless a significant issue comes up.**

## [ FEATURES ]
- Automatic repair runs once on startup to immediately correct existing UI issues.
- Configurable manual fix command (fixphonehud) for instant on-demand repair.
- Optional periodic background repair at configurable intervals
- Staged reset process to safely reload phone scripts and UI assets without restarting the game
- Recreates mobile phone object and reloads Scaleform UI layers
- Lightweight and performance-friendly (runs only when needed)
- Fully configurable via .ini


## [ REQUIREMENTS ]
- [Latest ScriptHookV](https://www.dev-c.com/gtav/scripthookv/)

Enhanced:
- [Latest ScriptHookVDotNet v3 Enhanced](https://www.gta5-mods.com/tools/script-hook-v-net-enhanced)
  
Legacy:
- [Latest ScriptHookVDotNet v3 Nightly](https://www.gta5-mods.com/tools/scripthookv-net)
  

## [ INSTALLATION ]
- Install ScriptHookV and ScriptHookVDotNet v3
- Place PhoneHudFix.dll & PhoneHudFix.ini into your GTA V/scripts/ folder
- (Optional) Edit PhoneHudFix.ini to customize behavior
- Enjoy


## [ Incompatibilities ]
- None. Works great with iFruitAddon2

## [ Credits & Acknowledgements ]
- Alexander Blade - for ScriptHookV
- crosire - for ScriptHookVDotNet
- Chiheb-Bacha - for ScriptHookVDotNet Enhanced

Without their foundational tools, this mod would not be possible.

### [ Mod Mirrors ]
[GTA5-Mods](https://www.gta5-mods.com/scripts/phone-hud-fix#description_tab)

[NexusMods Enhanced Page](https://www.nexusmods.com/gta5enhanced/mods/422)

[NexusMods Legacy Page](https://www.nexusmods.com/gta5/mods/1643)
