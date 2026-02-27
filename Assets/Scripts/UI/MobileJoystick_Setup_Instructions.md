# Mobile Joystick Setup Instructions

This guide explains how to set up the virtual joystick for mobile controls in Unity.

## Files Created

1. **VirtualJoystick.cs** - The joystick UI component that handles touch input
2. **MobileInputManager.cs** - Manages mobile detection and joystick visibility
3. **PlayerControllerM.cs** - Modified to accept mobile input alongside keyboard/gamepad

## Quick Start: Screen Zones Setup (Recommended)

**This is the easiest and cleanest approach!**

### What You Get:
- Bottom 1/3 of screen = Movement (joystick appears anywhere you touch)
- Top 2/3 of screen = Dialogue/Interaction (tap to advance)
- No conflicts, no accidental triggers

### Setup in 3 Steps:

1. **Create the UI** (see Step 1 below)
2. **Configure VirtualJoystick:**
   - ✅ Check "Use Screen Zones"
   - ✅ Check "Use Dynamic Position"
   - Set "Joystick Zone Height" to **0.33** (bottom third)
3. **Done!** Test by tapping top/bottom of screen

### How to Adjust:
- Want more joystick area? Increase "Joystick Zone Height" to 0.4 or 0.5
- Want less? Decrease to 0.25 or 0.2
- The line dividing zones is at: `Screen.height × joystickZoneHeight`

**No need to position BigCircle!** It will appear wherever you touch in the bottom zone.

---

## Unity Setup Steps

### Step 1: Create Joystick UI Hierarchy

1. In your UI Canvas (or create one if needed), create the following hierarchy:

```
Canvas
└── MobileUI (GameObject)
    └── VirtualJoystick (GameObject)
        ├── BigCircle (Image)
        └── SmallCircle (Image - child of BigCircle)
```

### Step 2: Configure Components

#### MobileUI GameObject:
- Add **MobileInputManager.cs** component
- Set **Virtual Joystick** field to the VirtualJoystick GameObject
- Set **Mobile UI Container** field to the MobileUI GameObject itself
- Check **Force Enable On WebGL** if you want joystick in WebGL builds
- Check **Show On Desktop For Testing** to test in Unity Editor

#### VirtualJoystick GameObject:
- Add **VirtualJoystick.cs** component
- Set **Big Circle** to the BigCircle RectTransform
- Set **Small Circle** to the SmallCircle RectTransform
- Set **Big Circle Image** to the BigCircle Image component
- Set **Small Circle Image** to the SmallCircle Image component
- Set **Max Distance** to ~100 (radius of joystick movement)
- **Joystick Behavior:**
  - **Use Dynamic Position**: ✅ Check this (Recommended! Joystick appears where you touch)
    - Unchecked = Fixed position like donkeytetris
    - Checked = Floating joystick (appears anywhere you touch)
  - **Return To Origin On Release**: ✅ Check this (joystick returns to original position when released)
- **Screen Zones:**
  - **Use Screen Zones**: ✅ Check this (Recommended! Clean screen division)
    - Bottom portion = joystick zone (movement)
    - Top portion = dialogue zone (tap to advance)
  - **Joystick Zone Height**: 0.33 (bottom 1/3 of screen for joystick)
    - 0.33 = bottom third (recommended)
    - 0.5 = bottom half
    - Adjust based on your game's needs
- Set **Max Alpha** to 0.8 (transparency when fully moved)
- Set **Min Alpha** to 0.3 (transparency when idle)

#### BigCircle (Image):
- **RectTransform:**
  - Anchor: Bottom-Left (or wherever you want the joystick)
  - Position: X: 150, Y: 150 (adjust to your preference)
  - Width: 200, Height: 200
- **Image:**
  - Sprite: Use a circular sprite (white circle with soft edges)
  - Color: White with alpha ~0.3
  - Raycast Target: **Checked** (important for touch detection!)

#### SmallCircle (Image - child of BigCircle):
- **RectTransform:**
  - Anchor: Center
  - Position: X: 0, Y: 0
  - Width: 80, Height: 80
- **Image:**
  - Sprite: Use a circular sprite (solid circle)
  - Color: White with alpha ~0.8
  - Raycast Target: **Unchecked** (big circle handles raycasts)

### Step 3: Create Sprites (if needed)

If you don't have joystick sprites:

1. **Option A:** Use Unity's built-in sprites:
   - Right-click in Project → Create → Sprites → Circle
   - Use UISprite (from UI package) or Knob sprite

2. **Option B:** Create simple circles in an image editor:
   - BigCircle: 256x256 white circle with soft fade at edges
   - SmallCircle: 128x128 solid white circle
   - Import as Sprite (2D and UI)

### Step 4: Test in Unity Editor

1. Enable **Show On Desktop For Testing** in MobileInputManager
2. Click Play
3. You should see the joystick in the bottom-left (semi-transparent)
4. Click and drag on the big circle - the small circle should follow
5. The squirrel should move based on joystick direction
6. Push joystick upward to make the squirrel jump
7. Hold joystick upward while falling to glide

### Step 5: Mobile/WebGL Testing

1. Disable **Show On Desktop For Testing**
2. Build for WebGL or deploy to mobile device
3. The joystick should automatically appear on mobile platforms
4. Test touch controls:
   - Touch and drag to move
   - Push up to jump
   - Hold up while airborne to glide

## Screen Layout System (Recommended)

### Screen Zones Mode ⭐ **NEW & RECOMMENDED**
**Behavior:** Screen is divided into movement zone (bottom) and interaction zone (top)

```
┌─────────────────────────┐
│                         │ ← Top 2/3 of screen
│   TAP TO ADVANCE        │   Tap anywhere = Advance dialogue
│   DIALOGUE ZONE         │   No accidental joystick activation
│                         │
├─────────────────────────┤ ← Divider line (adjustable)
│   🕹️ JOYSTICK ZONE     │ ← Bottom 1/3 of screen
│   Touch anywhere here   │   Joystick appears at touch point
│   to activate movement  │   Drag to move
└─────────────────────────┘
```

**Advantages:**
- ✅ **Clean separation**: Movement bottom, interaction top
- ✅ **No accidental triggers**: Can't accidentally move while tapping dialogue
- ✅ **Comfortable**: Place thumb anywhere in bottom third
- ✅ **Screen real estate**: Entire top area is usable for UI/dialogue
- ✅ **Simple logic**: No complex bounds checking needed

**Settings:**
- `Use Screen Zones`: ✅ Enable this
- `Joystick Zone Height`: 0.33 (bottom 1/3) - adjustable from 0.1 to 0.9

---

## Legacy Joystick Modes

### Dynamic Position (No Zones)
**Behavior:** Joystick appears wherever you first touch
- Touch anywhere on screen → Joystick appears at touch point
- Requires bounds checking to avoid dialogue conflicts
- **Best for:** If you don't want screen zones

### Fixed Position (donkeytetris style)
**Behavior:** Joystick stays in one fixed position (bottom-left corner)
- Joystick is always visible at fixed location
- Only activates if you touch within its radius
- **Best for:** Consistency across sessions
- **Implementation matches:** ✅ donkeytetris.gd

**To switch modes:**
- Screen zones: Toggle "Use Screen Zones" in VirtualJoystick
- Fixed vs Dynamic: Toggle "Use Dynamic Position" in VirtualJoystick

### Comparison Table

| Feature | Fixed | Dynamic (No Zones) | **Screen Zones** ⭐ |
|---------|-------|-------------------|-------------------|
| Joystick appears | Always visible | Anywhere touched | Bottom third only |
| Dialogue advance | Everywhere except joystick | Everywhere except joystick | **Top 2/3 only** |
| Accidental conflicts | Low | Medium | **None** ✅ |
| Screen division | None | None | **Clear zones** ✅ |
| Best for | Arcade feel | Flexible placement | **Mobile games** ✅ |
| Setup complexity | Low | Medium | **Low** ✅ |

## How It Works

### Input Flow:
1. **VirtualJoystick** detects touch input using Unity UI events (IPointerDown, IDrag, IPointerUp)
2. Calculates normalized input vector (-1 to 1 for X and Y)
3. **MobileInputManager** queries VirtualJoystick and provides input to game
4. **PlayerControllerM** reads mobile input in `ReadInput()` and combines with keyboard/gamepad input
5. Stronger input wins (if both mobile and keyboard pressed, use the one with higher magnitude)

### Movement Capabilities (matches WASD controls):

**Horizontal Movement:**
- Joystick left/right = A/D keys ✅
- Smooth analog control with normalized values

**Vertical Movement (Ladders):**
- Joystick up/down = W/S keys ✅
- When on a ladder, vertical input climbs up/down
- Works seamlessly with existing ladder system

**Jump:**
- Push joystick up (Y > 0.5) = Press Space ✅
- Uses edge detection: only triggers once when first pushed up
- Won't conflict with ladder climbing (game logic handles it correctly)

**Glide:**
- Hold joystick up while falling = Hold Space ✅
- Glide stays active as long as joystick is held up (Y > 0)
- This matches the Godot reference behavior

**Dialogue Advance:**
- Tap anywhere on screen EXCEPT joystick = Press Enter ✅
- Advances dialogue text automatically
- Joystick touches are excluded (won't accidentally advance dialogue while moving)
- Built into DialogueUI.cs (no separate button needed)
- **Smart detection**: In dynamic mode, only active joystick area is excluded

### Platform Detection:
- **Android/iOS:** Always shows joystick
- **WebGL:** Shows if `forceEnableOnWebGL` is true (recommended for mobile browsers)
- **Desktop:** Only shows if `showOnDesktopForTesting` is true

## Customization

### Visual Tweaks:
- Adjust **maxDistance** in VirtualJoystick to change joystick sensitivity
- Adjust **maxAlpha** and **minAlpha** for transparency feedback
- Change sprite colors to match your game's theme
- Add an outer ring sprite for better visual feedback

### Positioning:
- Move the BigCircle RectTransform to reposition joystick
- Common positions:
  - Bottom-Left: X: 150, Y: 150
  - Bottom-Right: X: -150, Y: 150 (anchor right)

### Sensitivity:
- In VirtualJoystick, **maxDistance** controls the range
- Smaller values = more sensitive
- Larger values = requires more drag to reach max speed

## Recent Improvements

### Screen Zones System ⭐ **NEW - RECOMMENDED**
- Screen divided into bottom (joystick) and top (dialogue/interaction) zones
- **Bottom 1/3**: Touch anywhere to activate joystick, drag to move
- **Top 2/3**: Tap anywhere to advance dialogue
- **Zero conflicts**: Movement and interaction zones are completely separate
- **Adjustable**: Change zone height from 10% to 90% of screen
- **Better UX**: Cleaner than bounds checking, more intuitive for players
- **Implementation**: Uses simple Y-coordinate check (Screen.height × zoneHeight)

### How Screen Zones Work:
```
Touch detected at position (x, y)
  ↓
Is y <= (Screen.height × 0.33)?
  ↓
  YES → Joystick Zone: Activate joystick at touch position
  NO  → Dialogue Zone: Advance dialogue
```

### Edge Detection for Jump (Fixed)
- Jump now only triggers ONCE when joystick first pushed up (like pressing Space key)
- Previously would trigger continuously while held up
- Now properly mimics keyboard behavior with edge detection

### Legacy: Smart Joystick Exclusion (Still Supported)
- If Screen Zones disabled, falls back to bounds checking
- Tap anywhere on screen to advance dialogue (like pressing Enter)
- **Smart exclusion**: Joystick touches DON'T advance dialogue
  - Fixed mode: Excludes the fixed joystick area
  - Dynamic mode: Only excludes when joystick is visible/active
- No accidental dialogue advancement while moving!

### Ladder Support (Confirmed Working)
- Joystick vertical input works perfectly with ladders
- Push up to climb, push down to descend
- Game logic automatically handles ladder vs jump behavior based on `isOnLadder` state

## Troubleshooting

**Joystick not responding:**
- Make sure Canvas has GraphicRaycaster component
- Verify BigCircle Image has **Raycast Target** checked
- Check EventSystem exists in scene (Auto-created with Canvas)

**Player not moving:**
- Check MobileInputManager is assigned to VirtualJoystick
- Verify PlayerControllerM is using InputSystem_Actions.inputactions asset
- Enable **Show On Desktop For Testing** to test in editor

**Joystick visible on desktop builds:**
- Disable **Show On Desktop For Testing**
- Rebuild

**Jump triggering multiple times:**
- This is now fixed with edge detection in VirtualJoystick.cs
- Jump only triggers on first frame when Y > 0.5

**Glide not working:**
- Push joystick more than halfway up (Y > 0.5)
- Make sure player is falling (not grounded)
- Check that jumpHeld is being set in PlayerControllerM

**Dialogue not advancing on mobile:**
- Tap anywhere EXCEPT the joystick
- Make sure MobileInputManager.Instance is active
- Check DialogueUI.cs Update() method is running
- Verify VirtualJoystick is found in DialogueUI.Start() (_virtualJoystick is not null)

**Dialogue advances when touching joystick:**
- Check that DialogueUI found VirtualJoystick correctly
- Verify `IsScreenPositionOverJoystick()` is being called
- In dynamic mode: Joystick should only exclude area when visible
- Make sure RectTransform bounds are correct for joystick

**Dialogue won't advance even when tapping away from joystick:**
- Check that `IsTouchOverJoystick()` returns false for your tap position
- Verify Canvas render mode (should be ScreenSpaceOverlay or has Camera assigned)
- Try tapping in the top-right corner (far from joystick) to test
- Enable debug logging in DialogueUI to see touch detection

**Joystick not appearing (dynamic mode):**
- Check "Use Dynamic Position" is enabled
- Verify BigCircle starts with alpha 0 (hidden until touched)
- Make sure parent Canvas is set correctly
- Touch should make it appear at touch position

**Joystick appears in wrong position (dynamic mode):**
- Check that BigCircle parent is the MobileUI container (not Canvas root)
- Verify RectTransform anchors are set correctly
- Parent should use anchors that work with screen-to-local conversion

**Joystick doesn't return to origin:**
- Check "Return To Origin On Release" is enabled
- Verify bigCircleOriginalPos is set correctly in Start()
- This only works in dynamic mode

## Integration with Multiplayer

When implementing multiplayer (Fishnet):
- Mobile input is local-only (doesn't need network sync)
- NetworkPlayerController will read from PlayerControllerM
- Each client handles their own joystick input
- No changes needed to mobile code for multiplayer
