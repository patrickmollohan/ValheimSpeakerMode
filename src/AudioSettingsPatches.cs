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
                Plugin.Log.LogError("Could not inject speaker mode dropdown: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(GameAudioSettings), "OnTabOpen")]
    internal static class AudioSettingsTabOpenPatch {
        private static void Postfix(GameAudioSettings __instance, Button backButton, Button okButton) {
            AudioModeDropdown dropdown = __instance.GetComponent<AudioModeDropdown>();

            if (dropdown != null) {
                dropdown.RefreshSelection();
                dropdown.UpdateNavigation(backButton, okButton);
            }
        }
    }

    [HarmonyPatch(typeof(GameAudioSettings), "OnOkAsync")]
    internal static class AudioSettingsOkPatch {
        private static void Prefix() {
            if (Plugin.Instance != null) Plugin.Instance.CommitSpeakerMode();
        }
    }

    [HarmonyPatch(typeof(GameAudioSettings), "OnBack")]
    internal static class AudioSettingsBackPatch {
        private static void Postfix() {
            if (Plugin.Instance != null) Plugin.Instance.RevertSpeakerMode();
        }
    }

    internal sealed class AudioModeDropdown : MonoBehaviour {
        private static readonly int[] ModeValues = {
            (int)AudioSpeakerMode.Stereo,
            (int)AudioSpeakerMode.Quad,
            (int)AudioSpeakerMode.Mode5point1,
            (int)AudioSpeakerMode.Mode7point1
        };

        private TMP_Dropdown _dropdown;
        private Toggle _lastVanillaControl;

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
            GameObject row = Instantiate(rowTemplate.gameObject, layoutParent, false);
            row.name = "SpeakerMode";

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
            label.text = "Speaker mode";
            label.raycastTarget = false;

            LayoutElement rowLayout = row.GetComponent<LayoutElement>();
            if (rowLayout == null) rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.minHeight = 46f;
            rowLayout.preferredHeight = 46f;
            rowLayout.flexibleHeight = 0f;

            RectTransform rowRect = row.GetComponent<RectTransform>();
            rowRect.localScale = Vector3.one;

            GameObject dropdownObject = Instantiate(dropdownTemplate.gameObject, row.transform, false);
            dropdownObject.name = "SpeakerModeDropdown";
            dropdownObject.SetActive(true);

            _dropdown = dropdownObject.GetComponent<TMP_Dropdown>();
            if (_dropdown == null) throw new InvalidOperationException("Cloned native dropdown has no TMP_Dropdown.");

            RectTransform dropdownRect = dropdownObject.GetComponent<RectTransform>();
            dropdownRect.anchorMin = new Vector2(0f, 0.5f);
            dropdownRect.anchorMax = new Vector2(0f, 0.5f);
            dropdownRect.pivot = new Vector2(0f, 0.5f);
            dropdownRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 180f);
            dropdownRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 46f);
            dropdownRect.anchoredPosition = Vector2.zero;
            dropdownRect.localScale = Vector3.one;

            LayoutElement dropdownLayout = dropdownObject.GetComponent<LayoutElement>();
            if (dropdownLayout == null) dropdownLayout = dropdownObject.AddComponent<LayoutElement>();
            dropdownLayout.ignoreLayout = true;

            _dropdown.ClearOptions();
            _dropdown.AddOptions(new List<string> {
                "Stereo",
                "Quad",
                "5.1 Surround",
                "7.1 Surround"
            });
            _dropdown.onValueChanged.AddListener(OnModeSelected);

            RefreshSelection();

            row.transform.SetSiblingIndex(_lastVanillaControl.transform.GetSiblingIndex());
            row.SetActive(true);

            LayoutRebuilder.ForceRebuildLayoutImmediate(layoutParent.GetComponent<RectTransform>());
        }

        private void OnModeSelected(int index) {
            if (index < 0 || index >= ModeValues.Length || Plugin.Instance == null) return;
            Plugin.Instance.PreviewSpeakerMode(ModeValues[index]);
        }

        internal void RefreshSelection() {
            if (_dropdown == null || Plugin.Instance == null) return;
            _dropdown.SetValueWithoutNotify(IndexForMode(Plugin.Instance.PreviewMode));
            _dropdown.RefreshShownValue();
        }

        internal void UpdateNavigation(Button backButton, Button okButton) {
            if (_dropdown == null || _lastVanillaControl == null) return;

            Navigation toggleNavigation = _lastVanillaControl.navigation;
            toggleNavigation.mode = Navigation.Mode.Explicit;
            toggleNavigation.selectOnDown = _dropdown;

            _lastVanillaControl.navigation = toggleNavigation;

            Navigation dropdownNavigation = _dropdown.navigation;
            dropdownNavigation.mode = Navigation.Mode.Explicit;
            dropdownNavigation.selectOnUp = _lastVanillaControl;

            if (backButton != null) dropdownNavigation.selectOnDown = backButton;

            _dropdown.navigation = dropdownNavigation;

            if (backButton != null) {
                Navigation navigation = backButton.navigation;
                navigation.mode = Navigation.Mode.Explicit;
                navigation.selectOnUp = _dropdown;

                backButton.navigation = navigation;
            }

            if (okButton != null) {
                Navigation navigation = okButton.navigation;
                navigation.mode = Navigation.Mode.Explicit;
                navigation.selectOnUp = _dropdown;

                okButton.navigation = navigation;
            }
        }

        private static int IndexForMode(int mode) {
            for (int i = 0; i < ModeValues.Length; i++) {
                if (ModeValues[i] == mode) return i;
            }
            return 0;
        }
    }
}
