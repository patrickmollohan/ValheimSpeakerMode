# Valheim Speaker Mode
A BepInEx mod that adds a speaker mode dropdown to Valheim's audio settings page. It currently only changes Unity's global speaker mode; it does **not** modify AudioSources, spatialBlend, 3D positioning, or sound assets.

## Usage
- Dropdown injected into the vanilla audio settings page.
- Modes: Stereo, Quad, 5.1 Surround, and 7.1 Surround.
- Immediate preview when the dropdown changes.
- "OK" commits the selection to the BepInEx config; "Back" restores the last committed mode.
- Startup applies the saved mode.

## Requirements
- Valheim
- BepInEx
- .NET SDK with `netstandard2.1` targeting support

The project targets .NET Standard 2.1 to match current Valheim Unity assemblies and references the assemblies from your local game installation. It does not bundle BepInEx, HarmonyX, Unity, or Valheim DLLs.

## Building and Installation
Open a terminal in this project directory.

Windows PowerShell:
```powershell
dotnet build .\ValheimSpeakerMode.csproj -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\Valheim"
```

Bash:
```bash
dotnet build ./ValheimSpeakerMode.csproj -c Release -p:GameDir="$HOME/.steam/steam/steamapps/common/Valheim"
```

If your Steam library is elsewhere, set `GameDir` to the folder containing
`valheim.exe` (or the Valheim Linux executable).

The build output is:

```text
bin/Release/netstandard2.1/ValheimSpeakerMode.dll
```

Copy that DLL into:

```text
Valheim/BepInEx/plugins/ValheimSpeakerMode/
```

Then launch Valheim with BepInEx.

## Config
After the first launch, the plugin creates:

```text
Valheim/BepInEx/config/com.patrickmollohan.valheimspeakermode.cfg
```

**SpeakerMode** values:

| Value | Mode |
|---:|---|
| 2 | Stereo |
| 4 | Quad |
| 6 | 5.1 |
| 7 | 7.1 |
