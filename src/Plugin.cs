using System;
using System.Collections;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ValheimSpeakerMode {
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin {
        public const string PluginGuid = "com.patrickmollohan.valheimspeakermode";
        public const string PluginName = "Valheim Speaker Mode";
        public const string PluginVersion = "1.0.1";

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }
        internal ConfigEntry<int> SavedSpeakerMode { get; private set; }

        private Harmony _harmony;
        private int _committedMode;
        private int _previewMode;

        private MusicSnapshot _musicSnapshot;
        private Coroutine _musicRestoreCoroutine;

        private sealed class MusicSnapshot {
            public object NamedMusic;
            public string MusicName;
            public int TimeSamples;
            public bool WasPlaying;
        }

        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo MusicManSourceField = typeof(MusicMan).GetField("m_musicSource", PrivateInstance);
        private static readonly FieldInfo MusicManCurrentField = typeof(MusicMan).GetField("m_currentMusic", PrivateInstance);
        private static readonly FieldInfo MusicManQueuedField = typeof(MusicMan).GetField("m_queuedMusic", PrivateInstance);
        private static readonly FieldInfo MusicManStopField = typeof(MusicMan).GetField("m_stopMusic", PrivateInstance);
        private static readonly FieldInfo NamedMusicNameField = typeof(MusicMan.NamedMusic).GetField("m_name");
        private static readonly FieldInfo NamedMusicResumeField = typeof(MusicMan.NamedMusic).GetField("m_resume");
        private static readonly FieldInfo NamedMusicLoopField = typeof(MusicMan.NamedMusic).GetField("m_loop");
        private static readonly FieldInfo NamedMusicSavedPositionField = typeof(MusicMan.NamedMusic).GetField("m_savedPlaybackPos");
        private static readonly FieldInfo NamedMusicLastPlayedField = typeof(MusicMan.NamedMusic).GetField("m_lastPlayedTime");

        private void Awake() {
            Instance = this;
            Log = Logger;

            SavedSpeakerMode = Config.Bind("Audio", "SpeakerMode", (int)AudioSpeakerMode.Stereo, "Unity speaker mode.");
            _committedMode = SavedSpeakerMode.Value;
            _previewMode = _committedMode;

            _harmony = new Harmony(PluginGuid);

            try {
                ApplySpeakerMode(_committedMode, "startup");
            } catch (Exception ex) {
                Logger.LogError("Could not apply the saved speaker mode: " + ex);
            }

            _harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        internal int PreviewMode {
            get { return _previewMode; }
        }

        internal void PreviewSpeakerMode(int requestedMode) {
            _previewMode = requestedMode;
            ApplySpeakerMode(requestedMode, "preview");
        }

        internal void CommitSpeakerMode() {
            _committedMode = _previewMode;
            SavedSpeakerMode.Value = _committedMode;
            Config.Save();

            Logger.LogInfo("Saved speaker mode: " + ModeName(_committedMode));
        }

        internal void RevertSpeakerMode() {
            _previewMode = _committedMode;
            ApplySpeakerMode(_committedMode, "revert");
        }

        private void ApplySpeakerMode(int requestedMode, string reason) {
            if (!Enum.IsDefined(typeof(AudioSpeakerMode), requestedMode)) {
                Logger.LogWarning("Ignoring invalid AudioSpeakerMode value: " + requestedMode);
                return;
            }

            AudioSpeakerMode requested = (AudioSpeakerMode)requestedMode;
            AudioConfiguration configuration = AudioSettings.GetConfiguration();

            if (configuration.speakerMode == requested) {
                Logger.LogInfo("Speaker mode already " + requested + " (" + reason + ").");
                return;
            }

            CaptureMusicState();

            configuration.speakerMode = requested;

            bool resetSucceeded = AudioSettings.Reset(configuration);
            AudioSpeakerMode actual = AudioSettings.GetConfiguration().speakerMode;

            Logger.LogInfo("Speaker mode " + reason + ": requested " + requested + ", actual " + actual + ", Reset returned " + resetSucceeded + ".");

            if (!resetSucceeded || actual != requested) Logger.LogWarning("The audio driver did not accept " + requested + ". Check the operating system's speaker layout and audio device capabilities.");

            RestoreMusicStateAfterReset();
        }

        private void CaptureMusicState() {
            _musicSnapshot = null;

            try {
                MusicMan musicMan = MusicMan.instance;
                if (musicMan == null) return;

                AudioSource source = MusicManSourceField?.GetValue(musicMan) as AudioSource;
                object currentMusic = MusicManCurrentField?.GetValue(musicMan);
                object queuedMusic = MusicManQueuedField?.GetValue(musicMan);

                object namedMusic = queuedMusic ?? currentMusic;
                if (namedMusic == null) return;

                string musicName = NamedMusicNameField?.GetValue(namedMusic) as string;
                if (string.IsNullOrEmpty(musicName)) return;

                bool wasPlaying = source != null && source.isPlaying;
                int timeSamples = wasPlaying ? source.timeSamples : 0;

                _musicSnapshot = new MusicSnapshot {
                    NamedMusic = namedMusic,
                    MusicName = musicName,
                    TimeSamples = timeSamples,
                    WasPlaying = wasPlaying
                };

                Logger.LogInfo("Captured music before audio reset: " + musicName + ", playing=" + wasPlaying + ", samples=" + timeSamples);
            } catch (Exception ex) {
                Logger.LogWarning("Could not capture music state before audio reset: " + ex);
            }
        }

        private void RestoreMusicStateAfterReset() {
            if (_musicSnapshot == null || !_musicSnapshot.WasPlaying) return;

            if (_musicRestoreCoroutine != null) StopCoroutine(_musicRestoreCoroutine);
            _musicRestoreCoroutine = StartCoroutine(RestoreMusicAfterDelay());
        }

        private IEnumerator RestoreMusicAfterDelay() {
            yield return null;
            yield return null;

            try {
                MusicMan musicMan = MusicMan.instance;
                if (musicMan == null) yield break;

                AudioSource source = MusicManSourceField?.GetValue(musicMan) as AudioSource;
                if (source == null) yield break;

                object namedMusic = _musicSnapshot.NamedMusic;
                if (namedMusic == null) yield break;

                MusicManQueuedField?.SetValue(musicMan, namedMusic);
                MusicManStopField?.SetValue(musicMan, false);

                bool loop = NamedMusicLoopField != null && (bool)NamedMusicLoopField.GetValue(namedMusic);
                bool resume = NamedMusicResumeField != null && (bool)NamedMusicResumeField.GetValue(namedMusic);
                if (loop && resume) {
                    NamedMusicSavedPositionField?.SetValue(namedMusic, Mathf.Max(0, _musicSnapshot.TimeSamples));
                    NamedMusicLastPlayedField?.SetValue(namedMusic, Time.time);
                }

                Logger.LogInfo("Queued music restoration: " + _musicSnapshot.MusicName + ", resume=" + (loop && resume) + ", samples=" + _musicSnapshot.TimeSamples);

                _musicSnapshot = null;
            } catch (Exception ex) {
                Logger.LogWarning("Could not restore music after audio reset: " + ex);
                _musicSnapshot = null;
            } finally {
                _musicRestoreCoroutine = null;
            }
        }

        internal static string ModeName(int mode) {
            switch ((AudioSpeakerMode)mode) {
				case AudioSpeakerMode.Mono: return "Mono";
                case AudioSpeakerMode.Stereo: return "Stereo";
                case AudioSpeakerMode.Quad: return "Quad";
				case AudioSpeakerMode.Surround: return "5.0 Surround";
                case AudioSpeakerMode.Mode5point1: return "5.1 Surround";
                case AudioSpeakerMode.Mode7point1: return "7.1 Surround";
				case AudioSpeakerMode.Prologic: return "Prologic";
                default: return "Unknown (" + mode + ")";
            }
        }

        private void OnDestroy() {
            if (_musicRestoreCoroutine != null) StopCoroutine(_musicRestoreCoroutine);
            if (_harmony != null) _harmony.UnpatchSelf();
            if (Instance == this) Instance = null;
        }
    }
}
