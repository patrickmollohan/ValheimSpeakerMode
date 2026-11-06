using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valheim.SettingsGui;

using GameAudioSettings = Valheim.SettingsGui.AudioSettings;

namespace ValheimSpeakerMode {
    [HarmonyPatch(typeof(GameAudioSettings), "Initialize")]
    internal static class AudioSettingsInitializePatch {
        private static void Postfix(GameAudioSettings __instance, Toggle ___m_continousMusic) {
            try {
                AudioModeDropdown.Attach(__instance, ___m_continousMusic);
            } catch (Exception ex) {
                Plugin.Log.LogError("Could not inject audio settings dropdowns: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(GameAudioSettings), "OnTabOpen")]
    internal static class AudioSettingsTabOpenPatch {
        private static void Postfix(GameAudioSettings __instance, Button backButton, Button okButton) {
            AudioModeDropdown dropdowns = __instance.GetComponent<AudioModeDropdown>();

            if (dropdowns != null) {
                dropdowns.RefreshSelections();
                dropdowns.UpdateNavigation(backButton, okButton);
            }
        }
    }

    [HarmonyPatch(typeof(GameAudioSettings), "OnOkAsync")]
    internal static class AudioSettingsOkPatch {
        private static void Prefix() {
            if (Plugin.Instance != null) Plugin.Instance.CommitAudioSettings();
        }
    }

    [HarmonyPatch(typeof(GameAudioSettings), "OnBack")]
    internal static class AudioSettingsBackPatch {
        private static void Postfix() {
            if (Plugin.Instance != null) Plugin.Instance.RevertAudioSettings();
        }
    }

    internal sealed class AudioModeDropdown : MonoBehaviour {

        private static readonly int[] ModeValues = {
            (int)AudioSpeakerMode.Mono,
            (int)AudioSpeakerMode.Stereo,
            (int)AudioSpeakerMode.Quad,
            (int)AudioSpeakerMode.Surround,
            (int)AudioSpeakerMode.Mode5point1,
            (int)AudioSpeakerMode.Mode7point1,
            (int)AudioSpeakerMode.Prologic
        };

        private static readonly int[] StandardSampleRates = {
            11025,
            22050,
            44100,
            48000,
            88200,
            96000
        };

        private static readonly int[] StandardDspBufferSizes = {
            64,
            128,
            256,
            512,
            1024,
            2048,
            4096
        };

        private TMP_Dropdown _speakerModeDropdown;
        private TMP_Dropdown _sampleRateDropdown;
        private TMP_Dropdown _dspBufferSizeDropdown;

        private Toggle _lastVanillaControl;

        private List<int> _sampleRateValues;
        private List<int> _dspBufferSizeValues;

        internal static void Attach(GameAudioSettings audio, Toggle lastVanillaControl) {
            if (audio == null || lastVanillaControl == null) return;
            if (audio.GetComponent<AudioModeDropdown>() != null) return;

            Component template = FindNativeDropdown();
            if (template == null) throw new InvalidOperationException("Could not find Valheim's native GuiDropdown template.");

            Transform layoutParent = lastVanillaControl.transform.parent;
            if (layoutParent == null || layoutParent.GetComponent<VerticalLayoutGroup>() == null) throw new InvalidOperationException("Could not find the Audio Settings VerticalLayoutGroup.");

            Transform masterVolume = layoutParent.Find("MasterVolume");
            if (masterVolume == null) throw new InvalidOperationException("Could not find MasterVolume row.");

            Transform labelTransform = masterVolume.Find("Label");
            if (labelTransform == null) throw new InvalidOperationException("Could not find MasterVolume Label.");

            TMP_Text labelSource = labelTransform.GetComponent<TMP_Text>();
            if (labelSource == null) throw new InvalidOperationException("MasterVolume Label has no TMP_Text component.");

            AudioModeDropdown controller = audio.gameObject.AddComponent<AudioModeDropdown>();
            controller._lastVanillaControl = lastVanillaControl;
            controller.Build(layoutParent, masterVolume, labelSource, template);
        }

        private static Component FindNativeDropdown() {
            Type graphicsType = AccessTools.TypeByName("Valheim.SettingsGui.GraphicsSettings");
            if (graphicsType == null) graphicsType = AccessTools.TypeByName("GraphicsSettings");
            if (graphicsType == null) return null;

            FieldInfo resolutionField = AccessTools.Field(graphicsType, "m_resolutionDropdown");
            if (resolutionField == null) return null;

            UnityEngine.Object[] panels = Resources.FindObjectsOfTypeAll(graphicsType);
            foreach (UnityEngine.Object candidate in panels) {
                Component panel = candidate as Component;
                if (panel == null || !panel.gameObject.scene.IsValid()) continue;

                Component dropdown = resolutionField.GetValue(panel) as Component;
                if (dropdown != null && dropdown.GetComponent<TMP_Dropdown>() != null) return dropdown;
            }

            foreach (UnityEngine.Object candidate in panels) {
                Component panel = candidate as Component;
                if (panel == null) continue;

                Component dropdown = resolutionField.GetValue(panel) as Component;
                if (dropdown != null && dropdown.GetComponent<TMP_Dropdown>() != null) return dropdown;
            }

            return null;
        }

        private void Build(Transform layoutParent, Transform rowTemplate, TMP_Text labelSource, Component dropdownTemplate) {
            _sampleRateValues = new List<int>(StandardSampleRates);

            AudioConfiguration initialConfiguration = UnityEngine.AudioSettings.GetConfiguration();

            int initialSampleRate = Plugin.Instance != null ? Plugin.Instance.PreviewSampleRate : initialConfiguration.sampleRate;
            if (initialSampleRate > 0 && !_sampleRateValues.Contains(initialSampleRate)) {
                _sampleRateValues.Add(initialSampleRate);
                _sampleRateValues.Sort();
            }

            _dspBufferSizeValues = new List<int>(StandardDspBufferSizes);

            int initialDspBufferSize = Plugin.Instance != null ? Plugin.Instance.PreviewDspBufferSize : initialConfiguration.dspBufferSize;
            if (initialDspBufferSize > 0 && !_dspBufferSizeValues.Contains(initialDspBufferSize)) {
                _dspBufferSizeValues.Add(initialDspBufferSize);
                _dspBufferSizeValues.Sort();
            }

            _speakerModeDropdown = CreateDropdownRow(layoutParent, rowTemplate, dropdownTemplate, "SpeakerMode", "Speaker mode",
                new List<string> {
                    "Mono",
                    "Stereo",
                    "Quad",
                    "5.0 Surround",
                    "5.1 Surround",
                    "7.1 Surround",
                    "Prologic"
                });

            List<string> sampleRateOptions = _sampleRateValues.Select(rate => rate.ToString("N0") + " Hz").ToList();
            _sampleRateDropdown = CreateDropdownRow(layoutParent, rowTemplate, dropdownTemplate, "SampleRate", "Sample rate", sampleRateOptions);

            List<string> dspBufferOptions = _dspBufferSizeValues.Select(size => size.ToString()).ToList();
            _dspBufferSizeDropdown = AddSecondDropdown(_sampleRateDropdown, dropdownTemplate, "DspBufferSize", "DSP buffer size", dspBufferOptions);

            _speakerModeDropdown.onValueChanged.AddListener(OnSpeakerModeSelected);
            _sampleRateDropdown.onValueChanged.AddListener(OnSampleRateSelected);
            _dspBufferSizeDropdown.onValueChanged.AddListener(OnDspBufferSizeSelected);

            RefreshSelections();

            _speakerModeDropdown.transform.parent.SetSiblingIndex(_lastVanillaControl.transform.GetSiblingIndex());
            _sampleRateDropdown.transform.parent.SetSiblingIndex(_lastVanillaControl.transform.GetSiblingIndex());

            _speakerModeDropdown.transform.parent.gameObject.SetActive(true);
            _sampleRateDropdown.transform.parent.gameObject.SetActive(true);

            LayoutRebuilder.ForceRebuildLayoutImmediate(layoutParent.GetComponent<RectTransform>());
        }

        private const float SecondLabelRightEdge = 365f;
        private const float SecondDropdownX = 380f;
        private const float SecondLabelWidth = 175f;

        private static TMP_Dropdown CreateDropdownControl(Transform row, Component dropdownTemplate, string objectName, List<string> options, float xPosition) {
            GameObject dropdownObject = Instantiate(dropdownTemplate.gameObject, row, false);
            dropdownObject.name = objectName;
            dropdownObject.SetActive(true);

            TMP_Dropdown dropdown = dropdownObject.GetComponent<TMP_Dropdown>();
            if (dropdown == null) throw new InvalidOperationException("Cloned native dropdown has no TMP_Dropdown.");

            RectTransform dropdownRect = dropdownObject.GetComponent<RectTransform>();
            dropdownRect.anchorMin = new Vector2(0f, 0.5f);
            dropdownRect.anchorMax = new Vector2(0f, 0.5f);
            dropdownRect.pivot = new Vector2(0f, 0.5f);
            dropdownRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 180f);
            dropdownRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 46f);
            dropdownRect.anchoredPosition = new Vector2(xPosition, 0f);
            dropdownRect.localScale = Vector3.one;

            LayoutElement dropdownLayout = dropdownObject.GetComponent<LayoutElement>();
            if (dropdownLayout == null) dropdownLayout = dropdownObject.AddComponent<LayoutElement>();
            dropdownLayout.ignoreLayout = true;

            dropdown.ClearOptions();
            dropdown.AddOptions(options);

            return dropdown;
        }

        private static TMP_Dropdown AddSecondDropdown(TMP_Dropdown firstDropdown, Component dropdownTemplate, string rowName, string labelText, List<string> options) {
            Transform row = firstDropdown.transform.parent;

            Transform firstLabel = row.Find("Label");
            if (firstLabel == null) throw new InvalidOperationException("Row has no Label to clone for second dropdown.");

            GameObject labelObject = Instantiate(firstLabel.gameObject, row, false);
            labelObject.name = rowName + "Label";

            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(0f, 1f);
            labelRect.pivot = new Vector2(1f, 0.5f);
            labelRect.sizeDelta = new Vector2(SecondLabelWidth, 0f);
            labelRect.anchoredPosition = new Vector2(SecondLabelRightEdge, 0f);
            labelRect.localScale = Vector3.one;

            TMP_Text label = labelObject.GetComponent<TMP_Text>();
            label.text = labelText;
            label.alignment = TextAlignmentOptions.MidlineRight;
            label.raycastTarget = false;

            return CreateDropdownControl(row, dropdownTemplate, rowName + "Dropdown", options, SecondDropdownX);
        }

        private TMP_Dropdown CreateDropdownRow(Transform layoutParent, Transform rowTemplate, Component dropdownTemplate, string rowName, string labelText, List<string> options) {
            GameObject row = Instantiate(rowTemplate.gameObject, layoutParent, false);
            row.name = rowName;

            Transform background = row.transform.Find("Background");
            Transform fillArea = row.transform.Find("Fill Area");
            Transform handleArea = row.transform.Find("Handle Slide Area");
            Transform value = row.transform.Find("Value");

            if (background != null) Destroy(background.gameObject);
            if (fillArea != null) Destroy(fillArea.gameObject);
            if (handleArea != null) Destroy(handleArea.gameObject);
            if (value != null) Destroy(value.gameObject);

            Selectable selectable = row.GetComponent<Selectable>();
            if (selectable != null) Destroy(selectable);

            Slider slider = row.GetComponent<Slider>();
            if (slider != null) Destroy(slider);

            Component[] components = row.GetComponents<Component>();
            foreach (Component component in components) {
                if (component == null) continue;
                if (component.GetType().Name == "GuiSlider") Destroy(component);
            }

            Transform labelTransform = row.transform.Find("Label");
            if (labelTransform == null) throw new InvalidOperationException("Cloned slider row has no Label.");

            TMP_Text label = labelTransform.GetComponent<TMP_Text>();
            if (label == null) throw new InvalidOperationException("Cloned slider label has no TMP_Text.");
            label.text = labelText;
            label.raycastTarget = false;

            LayoutElement rowLayout = row.GetComponent<LayoutElement>();
            if (rowLayout == null) rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.minHeight = 46f;
            rowLayout.preferredHeight = 46f;
            rowLayout.flexibleHeight = 0f;

            RectTransform rowRect = row.GetComponent<RectTransform>();
            rowRect.localScale = Vector3.one;

            TMP_Dropdown dropdown = CreateDropdownControl(row.transform, dropdownTemplate, rowName + "Dropdown", options, 0f);

            row.SetActive(false);

            return dropdown;
        }

        private void OnSpeakerModeSelected(int index) {
            if (index < 0 || index >= ModeValues.Length || Plugin.Instance == null) return;
            Plugin.Instance.PreviewSpeakerMode(ModeValues[index]);
        }

        private void OnSampleRateSelected(int index) {
            if (_sampleRateValues == null || index < 0 || index >= _sampleRateValues.Count || Plugin.Instance == null) return;
            Plugin.Instance.PreviewSampleRateSetting(_sampleRateValues[index]);
        }

        private void OnDspBufferSizeSelected(int index) {
            if (_dspBufferSizeValues == null || index < 0 || index >= _dspBufferSizeValues.Count || Plugin.Instance == null) return;
            Plugin.Instance.PreviewDspBufferSizeSetting(_dspBufferSizeValues[index]);
        }

        internal void RefreshSelections() {
            if (Plugin.Instance == null) return;

            if (_speakerModeDropdown != null) {
                _speakerModeDropdown.SetValueWithoutNotify(IndexForMode(Plugin.Instance.PreviewMode));
                _speakerModeDropdown.RefreshShownValue();
            }

            if (_sampleRateDropdown != null) {
                _sampleRateDropdown.SetValueWithoutNotify(IndexForSampleRate(Plugin.Instance.PreviewSampleRate));
                _sampleRateDropdown.RefreshShownValue();
            }

            if (_dspBufferSizeDropdown != null) {
                _dspBufferSizeDropdown.SetValueWithoutNotify(IndexForDspBufferSize(Plugin.Instance.PreviewDspBufferSize));
                _dspBufferSizeDropdown.RefreshShownValue();
            }
        }

        internal void UpdateNavigation(Button backButton, Button okButton) {
            if (_speakerModeDropdown == null || _sampleRateDropdown == null || _dspBufferSizeDropdown == null || _lastVanillaControl == null) return;

            Navigation vanillaNavigation = _lastVanillaControl.navigation;
            vanillaNavigation.mode = Navigation.Mode.Explicit;
            vanillaNavigation.selectOnDown = _speakerModeDropdown;
            _lastVanillaControl.navigation = vanillaNavigation;

            Navigation speakerNavigation = _speakerModeDropdown.navigation;
            speakerNavigation.mode = Navigation.Mode.Explicit;
            speakerNavigation.selectOnUp = _lastVanillaControl;
            speakerNavigation.selectOnDown = _sampleRateDropdown;
            _speakerModeDropdown.navigation = speakerNavigation;

            Navigation sampleRateNavigation = _sampleRateDropdown.navigation;
            sampleRateNavigation.mode = Navigation.Mode.Explicit;
            sampleRateNavigation.selectOnUp = _speakerModeDropdown;
            sampleRateNavigation.selectOnRight = _dspBufferSizeDropdown;
            if (backButton != null) sampleRateNavigation.selectOnDown = backButton;
            _sampleRateDropdown.navigation = sampleRateNavigation;

            Navigation dspBufferNavigation = _dspBufferSizeDropdown.navigation;
            dspBufferNavigation.mode = Navigation.Mode.Explicit;
            dspBufferNavigation.selectOnUp = _speakerModeDropdown;
            dspBufferNavigation.selectOnLeft = _sampleRateDropdown;
            if (okButton != null) {
                dspBufferNavigation.selectOnDown = okButton;
            } else if (backButton != null) {
                dspBufferNavigation.selectOnDown = backButton;
            }
            _dspBufferSizeDropdown.navigation = dspBufferNavigation;

            if (backButton != null) {
                Navigation navigation = backButton.navigation;
                navigation.mode = Navigation.Mode.Explicit;
                navigation.selectOnUp = _sampleRateDropdown;
                backButton.navigation = navigation;
            }

            if (okButton != null) {
                Navigation navigation = okButton.navigation;
                navigation.mode = Navigation.Mode.Explicit;
                navigation.selectOnUp = _dspBufferSizeDropdown;
                okButton.navigation = navigation;
            }
        }

        private static int IndexForMode(int mode) {
            for (int i = 0; i < ModeValues.Length; i++) {
                if (ModeValues[i] == mode) return i;
            }
            return 0;
        }

        private int IndexForSampleRate(int sampleRate) {
            if (_sampleRateValues == null) return 0;
            int index = _sampleRateValues.IndexOf(sampleRate);
            return index >= 0 ? index : 0;
        }

        private int IndexForDspBufferSize(int bufferSize) {
            if (_dspBufferSizeValues == null) return 0;
            int index = _dspBufferSizeValues.IndexOf(bufferSize);
            return index >= 0 ? index : 0;
        }
    }
}
