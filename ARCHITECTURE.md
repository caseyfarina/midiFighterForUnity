# Architecture

```mermaid
graph TD

    subgraph HW ["Hardware"]
        MF64["Midi Fighter 64<br/>8×8 button grid · notes 36–99"]
        MIDIMIX["Akai MIDI Mix<br/>24 knobs · 9 faders · 24 buttons"]
    end

    MINIS["Minis  jp.keijiro.minis<br/>Unity Input System device driver"]

    subgraph CORE ["Core  Runtime/"]
        MEM["MidiEventManager  MonoBehaviour<br/>───────────────────────────<br/>OnNoteOn(note, velocity 0–1)<br/>OnNoteOff(note)<br/>OnControlChange(cc, value 0–1)<br/>DeviceName"]
        UMTD["UnityMainThreadDispatcher  MonoBehaviour<br/>───────────────────────────<br/>Enqueue(Action)<br/>Drains queue each Update()"]
    end

    subgraph MF64PATH ["Midi Fighter 64  Runtime/"]
        MF64MAP["MidiFighter64InputMap  static class<br/>───────────────────────────<br/>IsInRange(note) → bool<br/>FromNote(note) → GridButton<br/>NOTE_OFFSET 36 · NOTE_MAX 99 · GRID_SIZE 8"]
        GRIDBTN["GridButton  struct<br/>───────────────────────────<br/>row 1–8 · col 1–8<br/>linearIndex 0–63 · noteNumber<br/>IsValid"]
        MGR["MidiGridRouter  MonoBehaviour<br/>───────────────────────────<br/>OnRow1(col)<br/>OnGridPreset(row, col)<br/>OnGridRandomize(row)<br/>OnRow5(col)<br/>OnSlotToggle(slot 1–24, isNoteOn)<br/>OnGridButton(GridButton, isNoteOn)<br/>override RouteButton()"]
        MFBR["MidiFighterButtonRouter  MonoBehaviour<br/>───────────────────────────<br/>Button vs Toggle per pad (config or inline grid)<br/>OnButtonPress · OnButtonHold · OnButtonRelease<br/>OnToggle(btn, state)<br/>Mirrors state to MidiFighterOutput LEDs"]
        MFO["MidiFighterOutput  MonoBehaviour  Singleton<br/>───────────────────────────<br/>SetLED(note, velocity 0–127)<br/>ClearLED(note) · ClearAllLEDs()<br/>ledChannelIndex 0–3  Blue Purple Red White<br/>Windows and Unity Editor only"]
    end

    subgraph MMPATH ["MIDI Mix  Runtime/"]
        MMMAP["MidiMixInputMap  static class<br/>───────────────────────────<br/>TryGetKnob(cc) → MixKnob<br/>TryGetFader(cc) → MixFader<br/>TryGetButton(note) → MixButton<br/>IsBankLeft/IsBankRight/IsSoloModifier(note)"]
        MMSTRUCTS["Structs and Enum<br/>───────────────────────────<br/>MixKnob  channel · row · ccNumber<br/>MixFader  channel · isMaster · ccNumber<br/>MixButton  channel · type · noteNumber<br/>MidiMixButton  Mute Solo RecArm"]
        MMR["MidiMixRouter  MonoBehaviour<br/>───────────────────────────<br/>OnKnob(ch, row, val)<br/>OnChannelFader(ch, val) · OnMasterFader(val)<br/>OnMute · OnSolo · OnRecArm (ch, bool)<br/>OnSoloModifier(isDown) · IsSoloHeld<br/>OnBankLeft · OnBankRight<br/>OnKnobRaw · OnFaderRaw · OnButtonRaw<br/>override RouteCC() · RouteNote()"]
    end

    subgraph DRAWER ["On-screen readout  Runtime/UI/"]
        MSD["MidiStatusDrawer  MonoBehaviour<br/>───────────────────────────<br/>UI Toolkit overlay, one UIDocument per display<br/>Mirrors 64 pads · 24 knobs · 9 faders · toggles<br/>Read-only: every widget ignores picking"]
        PC["PadCell / KnobDisplay  VisualElement<br/>───────────────────────────<br/>Painter2D custom elements, no textures<br/>All radii are fractions of element size"]
    end

    MF64    -->|USB MIDI| MINIS
    MIDIMIX -->|USB MIDI| MINIS
    MINIS   --> MEM

    MEM -->|"OnNoteOn, OnNoteOff"| MGR
    MEM -->|"OnNoteOn, OnNoteOff, OnControlChange"| MMR

    MGR --> MF64MAP
    MF64MAP --> GRIDBTN

    MMR --> MMMAP
    MMMAP --> MMSTRUCTS

    MFBR -->|typed static events| MSD
    MMR  -->|typed static events| MSD
    MSD  --> PC

    MFBR -->|SetLED, ClearLED| MFO
    MFO  -. winmm.dll .-> MF64

    UMTD -. available to any subscriber .-> CORE
```
