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
        public const string PluginVersion = "1.1.0";

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }
        internal ConfigEntry<int> SavedSpeakerMode { get; private set; }
        internal ConfigEntry<int> SavedSampleRate { get; private set; }
        internal ConfigEntry<int> SavedDspBufferSize { get; private set; }

        private Harmony _harmony;
        private int _committedMode;
        private int _previewMode;
        private int _committedSampleRate;
        private int _previewSampleRate;
        private int _committedDspBufferSize;
        private int _previewDspBufferSize;

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

            AudioConfiguration initialConfiguration = AudioSettings.GetConfiguration();
            Logger.LogInfo("Initial Unity audio configuration: sampleRate=" + initialConfiguration.sampleRate + " Hz, speakerMode=" + initialConfiguration.speakerMode + ", dspBufferSize=" + initialConfiguration.dspBufferSize + ".");

            SavedSpeakerMode = Config.Bind("Audio", "SpeakerMode", (int)initialConfiguration.speakerMode, "Unity speaker mode.");
            SavedSampleRate = Config.Bind("Audio", "SampleRate", initialConfiguration.sampleRate, "Unity audio output sample rate in Hz.");
            SavedDspBufferSize = Config.Bind("Audio", "DspBufferSize", initialConfiguration.dspBufferSize, "Unity DSP buffer size in samples.");

            _committedMode = SavedSpeakerMode.Value;
            _previewMode = _committedMode;

            _committedSampleRate = SavedSampleRate.Value > 0 ? SavedSampleRate.Value : initialConfiguration.sampleRate;
            _previewSampleRate = _committedSampleRate;

            _committedDspBufferSize = SavedDspBufferSize.Value > 0 ? SavedDspBufferSize.Value : initialConfiguration.dspBufferSize;
            _previewDspBufferSize = _committedDspBufferSize;

            _harmony = new Harmony(PluginGuid);

            try {
                ApplyAudioSettings(_committedMode, _committedSampleRate, _committedDspBufferSize, "startup");
            } catch (Exception ex) {
                Logger.LogError("Could not apply the saved audio settings: " + ex);
            }

            _harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        internal int PreviewMode {
            get { return _previewMode; }
        }

        internal int PreviewSampleRate {
            get { return _previewSampleRate; }
        }

        internal int PreviewDspBufferSize {
            get { return _previewDspBufferSize; }
        }

        internal void PreviewSpeakerMode(int requestedMode) {
            _previewMode = requestedMode;
            ApplyAudioSettings(_previewMode, _previewSampleRate, _previewDspBufferSize, "speaker mode preview");
        }

        internal void PreviewSampleRateSetting(int requestedSampleRate) {
            _previewSampleRate = requestedSampleRate;
            ApplyAudioSettings(_previewMode, _previewSampleRate, _previewDspBufferSize, "sample rate preview");
        }

        internal void PreviewDspBufferSizeSetting(int requestedBufferSize) {
            _previewDspBufferSize = requestedBufferSize;
            ApplyAudioSettings(_previewMode, _previewSampleRate, _previewDspBufferSize, "DSP buffer size preview");
        }

        internal void CommitAudioSettings() {
            _committedMode = _previewMode;
            _committedSampleRate = _previewSampleRate;
            _committedDspBufferSize = _previewDspBufferSize;

            SavedSpeakerMode.Value = _committedMode;
            SavedSampleRate.Value = _committedSampleRate;
            SavedDspBufferSize.Value = _committedDspBufferSize;

            Config.Save();
            Logger.LogInfo("Saved audio settings: speakerMode=" + ModeName(_committedMode) + ", sampleRate=" + _committedSampleRate + " Hz, dspBufferSize=" + _committedDspBufferSize + ".");
        }

        internal void RevertAudioSettings() {
            _previewMode = _committedMode;
            _previewSampleRate = _committedSampleRate;
            _previewDspBufferSize = _committedDspBufferSize;

            ApplyAudioSettings(_committedMode, _committedSampleRate, _committedDspBufferSize, "revert");
        }

        private void ApplyAudioSettings(int requestedMode, int requestedSampleRate, int requestedDspBufferSize, string reason) {
            if (!Enum.IsDefined(typeof(AudioSpeakerMode), requestedMode)) {
                Logger.LogWarning("Ignoring invalid AudioSpeakerMode value: " + requestedMode);
                return;
            }

            if (requestedSampleRate <= 0) {
                Logger.LogWarning("Ignoring invalid audio sample rate: " + requestedSampleRate);
                return;
            }

            if (requestedDspBufferSize <= 0) {
                Logger.LogWarning("Ignoring invalid DSP buffer size: " + requestedDspBufferSize);
                return;
            }

            AudioSpeakerMode requestedSpeakerMode = (AudioSpeakerMode)requestedMode;

            AudioConfiguration configuration = AudioSettings.GetConfiguration();
            if (configuration.speakerMode == requestedSpeakerMode && configuration.sampleRate == requestedSampleRate && configuration.dspBufferSize == requestedDspBufferSize) {
                Logger.LogInfo("Audio settings already match the request (" + reason + "): " + requestedSpeakerMode + ", " + requestedSampleRate + " Hz, DSP buffer " + requestedDspBufferSize + ".");
                return;
            }

            CaptureMusicState();

            configuration.speakerMode = requestedSpeakerMode;
            configuration.sampleRate = requestedSampleRate;
            configuration.dspBufferSize = requestedDspBufferSize;

            bool resetSucceeded = AudioSettings.Reset(configuration);

            AudioConfiguration actual = AudioSettings.GetConfiguration();

            Logger.LogInfo("Audio settings " + reason + ": requested speakerMode=" + requestedSpeakerMode + ", sampleRate=" + requestedSampleRate + " Hz, dspBufferSize=" + requestedDspBufferSize + "; actual speakerMode=" + actual.speakerMode + ", sampleRate=" + actual.sampleRate + " Hz, dspBufferSize=" + actual.dspBufferSize + "; Reset returned " + resetSucceeded + ".");

            if (!resetSucceeded || actual.speakerMode != requestedSpeakerMode || actual.sampleRate != requestedSampleRate || actual.dspBufferSize != requestedDspBufferSize) Logger.LogWarning("The audio device did not accept all requested settings. Unity may have selected the closest supported configuration.");

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
