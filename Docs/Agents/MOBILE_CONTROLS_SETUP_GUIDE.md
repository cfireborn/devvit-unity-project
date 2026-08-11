# Mobile Controls Setup Guide

## Current Shipping Baseline

As serialized in `Assets/UI/UI.prefab`:

- `MobileInputManager.autoCreateTouchReceiver` is enabled.
- `VirtualJoystick.useDynamicPosition` is enabled.
- `VirtualJoystick.useScreenZones` is disabled.
- `MobileInputManager.showOnDesktopForTesting` and `forceEnableOnWebGL` are enabled.

The automatic full-screen receiver therefore lets the dynamic joystick activate anywhere in the Game view. `DialogueUI` also advances on any primary touch/click while mobile controls are active, so the same touch can move the joystick and advance an open dialogue.

The steps below describe an **optional zoned configuration**, not the current shipping baseline. Enabling `useScreenZones` limits joystick activation to the bottom zone, but it does not yet stop `DialogueUI` from reacting to that bottom-zone touch.

## The Problem This Setup Addresses

Unity's UI event system only detects touches on visible UI elements (Images, Buttons, etc.). If your BigCircle is small, touches outside it won't be detected. This breaks screen zones!

## The Intended Zoned Setup

Use the existing invisible full-screen receiver to forward touches to `VirtualJoystick`, then enable `useScreenZones` to restrict joystick activation. The receiver solves touch coverage; the setting performs the zoning.

---

## Step-by-Step Optional Setup

The shipping prefab already has `autoCreateTouchReceiver` enabled. In that mode, `MobileInputManager` creates `TouchReceiver_Auto` directly under the Canvas at runtime. Do not also author a receiver in the scene.

If you prefer an explicit scene-authored receiver, first disable `autoCreateTouchReceiver`, then follow steps 1–2. Otherwise skip directly to step 3.

### 1. Create an explicit UI hierarchy (only when auto-create is disabled)

In your Canvas, create this structure:

```
Canvas
├── TouchReceiver (GameObject) ← NEW! First Canvas child; receives all touches
└── MobileUI (GameObject)
    └── VirtualJoystick (GameObject)
        ├── BigCircle (Image)
        └── SmallCircle (Image - child of BigCircle)
```

### 2. Set up the explicit TouchReceiver

**Create the TouchReceiver GameObject:**
1. Right-click directly on the Canvas → Create Empty
2. Rename it to "TouchReceiver"
3. Add **MobileTouchReceiver.cs** component
4. Add **Image** component (if not auto-added)

**Configure TouchReceiver:**
- **RectTransform:**
  - Anchors: Stretch/Stretch (full screen)
  - Left: 0, Top: 0, Right: 0, Bottom: 0
  - Width/Height: Should auto-adjust to full canvas size

- **Image Component:**
  - Color: `rgba(0, 0, 0, 0)` - Fully transparent!
  - **Raycast Target**: ✅ MUST BE CHECKED (critical!)
  - Source Image: None (leave empty)

- **MobileTouchReceiver Component:**
  - Virtual Joystick: Drag VirtualJoystick GameObject here (or leave empty for auto-find)
  - Auto Configure On Start: ✅ Checked (handles setup automatically)

**Important:** Keep TouchReceiver directly under the Canvas and make it the **FIRST Canvas child**. `MobileUI`/`InputAndUIManager` has a fixed-size RectTransform in the current prefab, so a stretched child beneath it would cover only that small parent instead of the screen. First-sibling placement keeps the receiver behind other Canvas UI.

### 3. Enable optional joystick zoning

**On the VirtualJoystick GameObject:**
- **Use Screen Zones**: ✅ Checked
- **Use Dynamic Position**: ✅ Checked
- **Joystick Zone Height**: 0.33 (bottom 1/3 of screen)
- **Return To Origin On Release**: ✅ Checked

**Important:** BigCircle position doesn't matter anymore! It will move dynamically.

### 4. Verify MobileInputManager

On your MobileUI GameObject (or wherever):
- Add **MobileInputManager.cs** if not already there
- Virtual Joystick: Reference the VirtualJoystick GameObject
- Mobile UI Container: Reference the MobileUI GameObject
- **Force Enable On WebGL**: ✅ Checked
- **Show On Desktop For Testing**: ✅ Checked (current prefab baseline; disable for a production configuration that should hide mobile UI on desktop)
- **Auto Create Touch Receiver**: ✅ Checked when using the shipping automatic setup; unchecked only when using the explicit receiver from steps 1–2

### 5. Test in Unity Editor

1. Click Play
2. Enable "Show On Desktop For Testing" if testing on desktop
3. **Test Bottom Third:**
   - Click anywhere in bottom 1/3 of Game View
   - Joystick should appear at mouse position
   - Drag to test movement
4. **Test Top Two-Thirds:**
   - Click anywhere in top 2/3 of Game View
   - Should NOT activate joystick
   - (Dialogue will advance if dialogue is showing)

---

## Explicit-Receiver Hierarchy Visual

When auto-create is disabled and you intentionally author the receiver, the hierarchy should look like this:

```
Canvas
  GraphicRaycaster ← Must be present!

  TouchReceiver (GameObject) ← First Canvas child (bottom of render order)
    MobileTouchReceiver.cs
    Image (Color: transparent, Raycast Target: ON)
    RectTransform (Anchors: Stretch, Offsets: 0)

  MobileUI (GameObject)
    MobileInputManager ← Manages mobile detection

    VirtualJoystick (GameObject)
      VirtualJoystick.cs
      RectTransform

      BigCircle (Image)
        Image (Your joystick circle sprite)
        SmallCircle (Image - child)
          Image (Your joystick knob sprite)
```

---

## How It Works

### Without TouchReceiver (Broken):
```
Touch at (100, 500) ← Top of screen
  ↓
Is touch over BigCircle's small bounds? NO
  ↓
Nothing happens! ❌
```

### With TouchReceiver and `useScreenZones` enabled:
```
Touch at (100, 500) ← Top of screen
  ↓
TouchReceiver (full-screen) receives touch
  ↓
Forwards to VirtualJoystick.OnPointerDown()
  ↓
VirtualJoystick checks: IsInJoystickZone(500)?
  ↓
Y position 500 > Screen.height * 0.33? YES (top zone)
  ↓
Ignores touch for joystick activation
```

`DialogueUI` still sees the primary touch independently; “ignored by the joystick” does not currently mean “dialogue-only.”

---

## Common Issues & Fixes

### Issue: Touches not detected anywhere
**Fix:**
- Check Canvas has **GraphicRaycaster** component
- Check TouchReceiver **Image** has **Raycast Target = true**
- Check EventSystem exists in scene (should auto-create with Canvas)
- Make sure TouchReceiver RectTransform is full-screen (anchors stretch/stretch)

### Issue: Joystick activates in top portion of screen
**Fix:**
- Check "Use Screen Zones" is enabled in VirtualJoystick
- Verify "Joystick Zone Height" is 0.33 or lower
- Test by tapping very bottom of screen (should work) vs very top (should not work)

### Issue: TouchReceiver blocks other UI
**Fix:**
- Make sure TouchReceiver is the FIRST direct child of the Canvas (in Hierarchy, drag to top)
- This puts it behind other UI elements in render order
- Other UI elements will receive clicks first

### Issue: BigCircle not appearing when touched
**Fix:**
- Check "Use Dynamic Position" is enabled
- Verify BigCircle has Image component with a sprite
- Check initial alpha is 0 (hidden) in Start()
- Make sure TouchReceiver is forwarding calls to VirtualJoystick

### Issue: Can't see where screen zones are
**Add visual debug line (temporary):**
```csharp
void OnGUI()
{
    if (useScreenZones)
    {
        float dividerY = Screen.height * (1f - joystickZoneHeight);
        GUI.Box(new Rect(0, dividerY - 2, Screen.width, 4), "");
    }
}
```
Add this to VirtualJoystick.cs to see the zone divider line.

---

## Quick Verification Checklist

Before testing:
- [ ] Canvas has GraphicRaycaster
- [ ] EventSystem exists in scene
- [ ] TouchReceiver is full-screen (anchors: stretch/stretch)
- [ ] TouchReceiver Image has raycastTarget = true
- [ ] TouchReceiver Image is transparent (alpha = 0)
- [ ] Automatic `TouchReceiver_Auto` or the explicit TouchReceiver is the first direct child of the Canvas; do not keep both
- [ ] VirtualJoystick has "Use Screen Zones" enabled
- [ ] MobileInputManager is configured with references

After enabling the optional zoned setup:
- [ ] Touch bottom of screen → joystick appears
- [ ] Touch top of screen → joystick does NOT appear
- [ ] Drag in bottom zone → player moves
- [ ] Tap top zone → dialogue advances (when dialogue showing)
- [ ] While dialogue is open, confirm and record that a bottom-zone touch may also advance it; this is the known limitation above

---

## Why This Works

1. **TouchReceiver is full-screen** → Receives ALL touches
2. **TouchReceiver forwards to VirtualJoystick** → Centralized input handling
3. **VirtualJoystick checks zones** → Bottom = joystick, Top = ignore
4. **DialogueUI does not currently check zones** → Any primary touch/click may advance an open dialogue
5. **Result** → Joystick activation is zoned correctly, but dialogue input still needs an explicit zone check before the controls are fully isolated

---

## Existing Automatic Setup

`MobileInputManager` already implements `CreateTouchReceiver()` and calls it from `Start()` when `autoCreateTouchReceiver` is enabled and mobile controls are active. Do not paste in another `Start()` or `CreateTouchReceiver()`; duplicate methods will cause compilation errors.

To use the existing automatic path:

1. Keep `autoCreateTouchReceiver` enabled on `MobileInputManager`.
2. Enter Play Mode and confirm a runtime `TouchReceiver_Auto` appears directly under the Canvas.
3. Confirm it is behind interactive UI and that its transparent `Image` receives raycasts.
4. Configure `VirtualJoystick.useScreenZones` independently according to the behavior you want.
5. Test with dialogue open, because dialogue touch filtering is still independent of the joystick setting.
