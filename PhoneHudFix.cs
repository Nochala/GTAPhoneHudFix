using System;
using System.IO;
using GTA;
using GTA.Native;
using GTA.UI;

namespace PhoneHudFix
{
	public class PhoneHudFix : Script
	{
		private const string CELLPHONE_CONTROLLER = "cellphone_controller";
		private const string CELLPHONE_FLASHHAND  = "cellphone_flashhand";
		private const string PHONE_TXD            = "cellphone_ifruit";

		private const ulong NATIVE_HAS_CHEAT_STRING_JUST_BEEN_ENTERED = 0x557E43C447E700A8;

		private readonly int _fixCheatHash;

		private readonly bool _logEnabled;
		private readonly int  _logLevel; 
		private readonly bool _logResetOnStart;
		private readonly string _logFileName;
		private readonly string _sessionId;
		private StreamWriter _logWriter;

		private readonly bool _pinPhoneAssetsEnabled;
		private readonly bool _refreshBlipsOnPhoneCloseEnabled;
		private readonly bool _refreshBlipsOnMissionEndEnabled;

		private readonly int _blipRefreshBatchSize;
		private readonly int _blipRefreshPassesOnPhoneClose;
		private readonly int _blipCacheRebuildIntervalMs;

		private bool _wasPhoneOpen;
		private bool _wasMissionActive;

		private int _pendingBlipRefreshPasses;
		private Blip[] _blipCache = Array.Empty<Blip>();
		private int _blipCacheIndex = 0;
		private int _nextBlipCacheRebuildAt = 0;

		private int  _fixStep = 0;
		private int  _stepTimer = 0;
		private bool _reopenAfterFix = false;

		private string _pendingScriptName = null;
		private int    _pendingScriptStack = 0;

		private int _sfCellphone1 = 0;
		private int _sfCellphone2 = 0;

		private bool _audioFixPending = false;
		private int _audioFixAt = -1;
		private readonly bool _audioFixEnabled;
		private readonly int _audioFixDelayMs;

		public PhoneHudFix()
		{
			// INI
			string cheatStr = Settings.GetValue("SETTINGS", "FIX_CHEAT_STRING", "fixphonehud");
			_fixCheatHash = Function.Call<int>(Hash.GET_HASH_KEY, cheatStr);

			_logEnabled = Settings.GetValue("SETTINGS", "LOG_ENABLED", false);

			int lvl = Settings.GetValue("SETTINGS", "LOG_LEVEL", 1);

			if (lvl < 0) lvl = 0;
			if (lvl > 2) lvl = 2;

			_logLevel = lvl;

			_logResetOnStart = Settings.GetValue("SETTINGS", "LOG_RESET_ON_START", true);
			_logFileName = Settings.GetValue("SETTINGS", "LOG_FILE_NAME", "PhoneHudFix.log");
			_sessionId = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
			InitLogger();

			_pinPhoneAssetsEnabled = Settings.GetValue("SETTINGS", "PIN_PHONE_ASSETS_ENABLED", true);

			_audioFixEnabled = Settings.GetValue("SETTINGS", "AUDIO_FIX_ENABLED", true);
			_audioFixDelayMs = Settings.GetValue("SETTINGS", "AUDIO_FIX_DELAY_MS", 900);
			_refreshBlipsOnPhoneCloseEnabled = Settings.GetValue("SETTINGS", "REFRESH_BLIPS_ON_PHONE_CLOSE_ENABLED", true);
			_refreshBlipsOnMissionEndEnabled = Settings.GetValue("SETTINGS", "REFRESH_BLIPS_ON_MISSION_END_ENABLED", true);
			_blipRefreshBatchSize = Math.Max(1, Settings.GetValue("SETTINGS", "BLIP_REFRESH_BATCH_SIZE", 24));
			_blipRefreshPassesOnPhoneClose = Math.Max(1, Settings.GetValue("SETTINGS", "BLIP_REFRESH_PASSES_ON_PHONE_CLOSE", 6));
			_blipCacheRebuildIntervalMs = Math.Max(1000, Settings.GetValue("SETTINGS", "BLIP_CACHE_REBUILD_INTERVAL_MS", 3000));

			_wasPhoneOpen = IsPhoneOpen();
			_wasMissionActive = IsMissionActive();
			_pendingBlipRefreshPasses = 0;
			_nextBlipCacheRebuildAt = Game.GameTime + 250;

			LogInfo($"Started. session={_sessionId} cheat=\"{cheatStr}\" pinAssets={_pinPhoneAssetsEnabled} blipsOnClose={_refreshBlipsOnPhoneCloseEnabled} blipsOnMissionEnd={_refreshBlipsOnMissionEndEnabled}");

			Interval = 0;
			Tick += OnTick;
		}

		private void OnTick(object sender, EventArgs e)
		{
			// Manual trigger via cheat string
			if (HasCheatJustBeenEntered(_fixCheatHash))
			{
				_reopenAfterFix = IsPhoneOpen();
				LogInfo($"Cheat trigger detected. reopenAfterFix={_reopenAfterFix}");
				StartFixPipeline();
			}

			// Mission transitions (event-driven): refresh blips after mission ends
			bool missionActiveNow = IsMissionActive();
			if (_refreshBlipsOnMissionEndEnabled && _wasMissionActive && !missionActiveNow)
			{
				if (CanDoLightWork())
				{
					_pendingBlipRefreshPasses = Math.Max(_pendingBlipRefreshPasses, _blipRefreshPassesOnPhoneClose);
					LogInfo($"Mission ended -> scheduled blip refresh passes={_pendingBlipRefreshPasses}");
				}
			}
			_wasMissionActive = missionActiveNow;

			bool phoneOpenNow = IsPhoneOpen();

			if (phoneOpenNow && !_wasPhoneOpen)
			{
				if (_pinPhoneAssetsEnabled && CanDoLightWork())
				{
					LogDebug("Phone opened -> pinning phone assets");
					PinPhoneAssets();
				}
			}

			if (!phoneOpenNow && _wasPhoneOpen)
			{
				if (_refreshBlipsOnPhoneCloseEnabled && CanDoLightWork())
				{
					_pendingBlipRefreshPasses = _blipRefreshPassesOnPhoneClose;
					LogDebug($"Phone closed -> scheduled blip refresh passes={_pendingBlipRefreshPasses}");
				}
			}

			_wasPhoneOpen = phoneOpenNow;

			if (_fixStep == 0 && _pendingBlipRefreshPasses > 0 && CanDoLightWork())
			{
				LogTrace($"Blip refresh pass. remaining(before)={_pendingBlipRefreshPasses}");
				RefreshBlipsBatch();
				_pendingBlipRefreshPasses--;
			}

			if (_fixStep != 0)
				RunFixPipeline();
		}

		private bool HasCheatJustBeenEntered(int cheatHash)
		{
			return SafeCallBool(() => Function.Call<bool>((Hash)NATIVE_HAS_CHEAT_STRING_JUST_BEEN_ENTERED, cheatHash), false);
		}

		private bool CanDoLightWork()
		{
			if (Game.IsPaused)
				return false;

			if (SafeCallBool(() => Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE), false) ||
				SafeCallBool(() => Function.Call<bool>(Hash.IS_CUTSCENE_PLAYING), false))
				return false;

			return true;
		}

		// =========================
		// Manual heavy fix pipeline
		// =========================
		private void StartFixPipeline()
		{
			if (_fixStep != 0) return;

			_fixStep = 1;
			_stepTimer = 0;
			_pendingScriptName = null;

			Screen.ShowSubtitle("Repairing phone UI...");
			LogInfo("Heavy fix pipeline started");

			if (_audioFixEnabled)
			{
				_audioFixPending = true;
				_audioFixAt = Game.GameTime + _audioFixDelayMs;
			}

		}

		private void RunFixPipeline()
		{
			_stepTimer++;

			switch (_fixStep)
			{
				case 1:
					// Close phone to avoid mid-app resets
					ClosePhone();
					if (_stepTimer > 10) NextStep();
					break;

				case 2:
					// Kill phone scripts to reset state
					SafeCallVoid(() => Function.Call(Hash.TERMINATE_ALL_SCRIPTS_WITH_THIS_NAME, CELLPHONE_FLASHHAND));
					SafeCallVoid(() => Function.Call(Hash.TERMINATE_ALL_SCRIPTS_WITH_THIS_NAME, CELLPHONE_CONTROLLER));
					LogDebug("Terminated cellphone scripts");
					if (_stepTimer > 5) NextStep();
					break;

				case 3:
					// Recreate phone object
					SafeCallVoid(() => Function.Call(Hash.DESTROY_MOBILE_PHONE));
					if (_stepTimer > 2)
					{
						SafeCallVoid(() => Function.Call(Hash.CREATE_MOBILE_PHONE, 0));
						LogDebug("Recreated mobile phone");
						NextStep();
					}
					break;

				case 4:

					if (_pinPhoneAssetsEnabled) PinPhoneAssets();
					else RequestPhoneUiAssets();
					LogDebug(_pinPhoneAssetsEnabled ? "Pinned phone assets" : "Requested phone UI assets");
					if (_stepTimer > 30) NextStep();
					break;

				case 5:

					if (!IsScriptRunning(CELLPHONE_CONTROLLER))
					{
						if (_pendingScriptName == null || _pendingScriptName != CELLPHONE_CONTROLLER)
							RequestStartScriptNonBlocking(CELLPHONE_CONTROLLER, 1424);

						TryStartPendingScriptNonBlocking();
						// REMEMBER TO WRITE WORKAROUND ON THIS YA FUCKIN COCK
						break;
					}

					if (!IsScriptRunning(CELLPHONE_FLASHHAND))
					{
						if (_pendingScriptName == null || _pendingScriptName != CELLPHONE_FLASHHAND)
							RequestStartScriptNonBlocking(CELLPHONE_FLASHHAND, 1424);

						TryStartPendingScriptNonBlocking();
						break;
					}

					if (_audioFixPending && Game.GameTime >= _audioFixAt)
					{
						_audioFixPending = false;
						_audioFixAt = -1;
						RecoverPhoneAudio();
					}

					// Both are running???
					if (_stepTimer > 30) NextStep();
					break;

				case 6:
					if (_reopenAfterFix)
						NudgePhoneOpen();

					Screen.ShowSubtitle("Phone UI refresh complete");
					LogInfo("Heavy fix pipeline complete");
					_fixStep = 0;
					_pendingScriptName = null;
					break;

			}
		}

		private void NextStep()
		{
			_fixStep++;
			_stepTimer = 0;
			_pendingScriptName = null;
			LogTrace($"Heavy fix -> next step={_fixStep}");
		}

		private bool IsScriptRunning(string scriptName)
		{
			int hash = SafeCallInt(() => Function.Call<int>(Hash.GET_HASH_KEY, scriptName), 0);
			if (hash == 0) return false;

			// This returns the number of script threads currently running for that script hash.
			int threads = SafeCallInt(() => Function.Call<int>(Hash.GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH, hash), 0);
			return threads > 0;
		}

private void RequestStartScriptNonBlocking(string scriptName, int stackSize)
		{
			_pendingScriptName = scriptName;
			_pendingScriptStack = stackSize;
			SafeCallVoid(() => Function.Call(Hash.REQUEST_SCRIPT, _pendingScriptName));
			LogDebug($"Requested script: {_pendingScriptName}");
		}

		private void TryStartPendingScriptNonBlocking()
		{
			if (_pendingScriptName == null) return;

			bool loaded = SafeCallBool(() => Function.Call<bool>(Hash.HAS_SCRIPT_LOADED, _pendingScriptName), false);

			if (!loaded)
			{
				SafeCallVoid(() => Function.Call(Hash.REQUEST_SCRIPT, _pendingScriptName));
				return;
			}

			SafeCallVoid(() => Function.Call(Hash.START_NEW_SCRIPT, _pendingScriptName, _pendingScriptStack));
			SafeCallVoid(() => Function.Call(Hash.SET_SCRIPT_AS_NO_LONGER_NEEDED, _pendingScriptName));
			LogDebug($"Started script: {_pendingScriptName}");

			_pendingScriptName = null;
			_pendingScriptStack = 0;
		}

		private void PinPhoneAssets()
		{
			SafeCallVoid(() => Function.Call(Hash.REQUEST_STREAMED_TEXTURE_DICT, PHONE_TXD, true));

			if (_sfCellphone1 == 0)
				_sfCellphone1 = SafeCallInt(() => Function.Call<int>(Hash.REQUEST_SCALEFORM_MOVIE, "cellphone_ifruit"), 0);
			else
			{
				bool loaded = SafeCallBool(() => Function.Call<bool>(Hash.HAS_SCALEFORM_MOVIE_LOADED, _sfCellphone1), true);
				if (!loaded)
					_sfCellphone1 = SafeCallInt(() => Function.Call<int>(Hash.REQUEST_SCALEFORM_MOVIE, "cellphone_ifruit"), 0);
			}

			if (_sfCellphone2 == 0)
				_sfCellphone2 = SafeCallInt(() => Function.Call<int>(Hash.REQUEST_SCALEFORM_MOVIE, "cellphone_ifruit_2"), 0);
			else
			{
				bool loaded = SafeCallBool(() => Function.Call<bool>(Hash.HAS_SCALEFORM_MOVIE_LOADED, _sfCellphone2), true);
				if (!loaded)
					_sfCellphone2 = SafeCallInt(() => Function.Call<int>(Hash.REQUEST_SCALEFORM_MOVIE, "cellphone_ifruit_2"), 0);
			}
		}

		private void RequestPhoneUiAssets()
		{
			SafeCallVoid(() => Function.Call(Hash.REQUEST_STREAMED_TEXTURE_DICT, PHONE_TXD, true));
			SafeCallInt(() => Function.Call<int>(Hash.REQUEST_SCALEFORM_MOVIE, "cellphone_ifruit"), 0);
			SafeCallInt(() => Function.Call<int>(Hash.REQUEST_SCALEFORM_MOVIE, "cellphone_ifruit_2"), 0);
		}

		private void RecoverPhoneAudio()
		{
			// Flags for audio routing
			SafeCallVoid(() => Function.Call(Hash.SET_AUDIO_FLAG, "FrontendRadioDisabled", false));
			SafeCallVoid(() => Function.Call(Hash.SET_AUDIO_FLAG, "AllowRadioOverScreenFade", true));
			SafeCallVoid(() => Function.Call(Hash.SET_AUDIO_FLAG, "AllowScoreAndRadio", true));

			// Nudge mobile phone radio state (harmless; can unstick phone audio routing)
			SafeCallVoid(() => Function.Call(Hash.SET_MOBILE_PHONE_RADIO_STATE, false));
			SafeCallVoid(() => Function.Call(Hash.SET_MOBILE_PHONE_RADIO_STATE, true));
			SafeCallVoid(() => Function.Call(Hash.SET_MOBILE_PHONE_RADIO_STATE, false));
		}

		private bool IsPhoneOpen()
		{

			return SafeCallBool(() => Function.Call<bool>(Hash.IS_MOBILE_PHONE_RADIO_ACTIVE), false);
		}

		private bool IsMissionActive()
		{

			return Game.IsMissionActive;
		}

		private void ClosePhone()
		{
			SafeCallVoid(() => Function.Call(Hash.CELL_CAM_ACTIVATE, false, false));
			SafeCallVoid(() => Function.Call(Hash.SET_MOBILE_PHONE_RADIO_STATE, false));
			SafeCallVoid(() => Function.Call(Hash.SET_FRONTEND_ACTIVE, false));
			SafeCallVoid(() => Function.Call(Hash.SET_PAUSE_MENU_ACTIVE, false));
		}

		private void NudgePhoneOpen()
		{
			SafeCallVoid(() => Function.Call(Hash.ENABLE_CONTROL_ACTION, 0, (int)Control.Phone, true));
		}

		private void RefreshBlipsBatch()
		{

			if (Game.GameTime >= _nextBlipCacheRebuildAt || _blipCache == null || _blipCache.Length == 0)
			{
				_nextBlipCacheRebuildAt = Game.GameTime + _blipCacheRebuildIntervalMs;
				_blipCache = World.GetAllBlips();
				_blipCacheIndex = 0;
			}

			if (_blipCache == null || _blipCache.Length == 0)
				return;

			int touched = 0;
			int safeGuard = _blipCache.Length; // avoid infinite looping on invalid blips

			while (touched < _blipRefreshBatchSize && safeGuard-- > 0)
			{
				if (_blipCacheIndex >= _blipCache.Length)
					_blipCacheIndex = 0;

				Blip b = _blipCache[_blipCacheIndex++];
				if (b == null || !b.Exists())
					continue;

				int h = b.Handle;
				if (h == 0)
					continue;


				int display = SafeCallInt(() => Function.Call<int>(Hash.GET_BLIP_INFO_ID_DISPLAY, h), 0);
				int alpha   = SafeCallInt(() => Function.Call<int>(Hash.GET_BLIP_ALPHA, h), 255);


				float scale =  1.0f;

				bool shortRange = SafeCallBool(() => Function.Call<bool>(Hash.IS_BLIP_SHORT_RANGE, h), false);

				SafeCallVoid(() => Function.Call(Hash.SET_BLIP_DISPLAY, h, display));
				SafeCallVoid(() => Function.Call(Hash.SET_BLIP_ALPHA, h, alpha));
				SafeCallVoid(() => Function.Call(Hash.SET_BLIP_SCALE, h, scale));
				SafeCallVoid(() => Function.Call(Hash.SET_BLIP_AS_SHORT_RANGE, h, shortRange));

				touched++;
			}

			LogTrace($"Refreshed blips batch touched={touched} cache={_blipCache.Length}");
		}

		// =========================
		// Optional logging
		// =========================

		private string GetScriptsDirectory()
		{
			try
			{
				string baseDir = AppDomain.CurrentDomain.BaseDirectory;

				
				if (!baseDir.EndsWith("\\"))
					baseDir += "\\";

				
				if (baseDir.EndsWith("scripts\\", StringComparison.OrdinalIgnoreCase))
					return baseDir.TrimEnd('\\');

			
				string scriptsPath = System.IO.Path.Combine(baseDir, "scripts");

				return scriptsPath;
			}
			catch
			{
				return "scripts";
			}
		}
		private void InitLogger()
		{
			if (!_logEnabled) return;

			try
			{
				string scriptsDir = GetScriptsDirectory();
				Directory.CreateDirectory(scriptsDir);
				string filePath = Path.Combine(scriptsDir, _logFileName);

				if (_logResetOnStart && File.Exists(filePath))
					File.Delete(filePath);

				_logWriter = new StreamWriter(filePath, append: true) { AutoFlush = true };
				WriteLogLine(1, $"Log started. session={_sessionId}");
			}
			catch
			{
				_logWriter = null;
			}
		}

		private void WriteLogLine(int level, string msg)
		{
			if (!_logEnabled || _logWriter == null) return;
			if (level > _logLevel) return;

			string lvl;

			switch (level)
			{
				case 0:
					lvl = "ERROR";
					break;

				case 1:
					lvl = "INFO";
					break;

				case 2:
					lvl = "DEBUG";
					break;

				default:
					lvl = "UNKNOWN";
					break;
			}

			try
			{
				_logWriter.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{lvl}] {msg}");
			}
			catch { /* ignore */ }
		}

		private void LogError(string msg) => WriteLogLine(0, msg);
		private void LogInfo(string msg)  => WriteLogLine(1, msg);
		private void LogDebug(string msg) => WriteLogLine(2, msg);
		private void LogTrace(string msg) => WriteLogLine(3, msg);

	
		// Safe-call helpers 
	
		private static void SafeCallVoid(Action a)
		{
			try { a(); } catch { /* swallow */ }
		}

		private static bool SafeCallBool(Func<bool> f, bool defaultValue)
		{
			try { return f(); } catch { return defaultValue; }
		}

		private static int SafeCallInt(Func<int> f, int defaultValue)
		{
			try { return f(); } catch { return defaultValue; }
		}

		private static float SafeCallFloat(Func<float> f, float defaultValue)
		{
			try { return f(); } catch { return defaultValue; }
		}
	}
}
