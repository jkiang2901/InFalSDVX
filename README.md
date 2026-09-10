# InFalSDVX - In Falsus Rhythm Game Input Mapper

Simple and lightweight keyboard-to-mouse input remapper for the rhythm game **In Falsus**.

---

## 🚀 Quick Start Guide

1. Open folder `c:\Users\ADMIN\Desktop\InFalSDVX`.
2. Run **`InFalSDVX.exe`**.
3. Default controls when active:
   - **Key `Q`**: Move mouse Left (Hold = smooth movement; Quick tap = Left / Yellow Flick).
   - **Key `E`**: Move mouse Right (Hold = smooth movement; Quick tap = Right / Green Flick).
   - **Key `F8`**: Toggle remapper ON / OFF.
4. Start **In Falsus** game and play!

---

## ⚙️ Features

- **Custom Keybindings**: Click any key button in the app UI and press a new key to rebind.
- **Mouse Speed Slider**: Adjust mouse movement speed (1 to 60 px/tick).
- **Built-in Debug Log**: View real-time keypresses and mouse movement events in the app log panel.
- **Low Latency (<1ms)**: Built with Windows native Win32 hooks (`WH_KEYBOARD_LL` & `SendInput API`). Works in Fullscreen and Borderless modes.

---

## 📝 Debug Log Example

When you press keys or change settings, the debug log will show:

```text
[13:53:15] InFalSDVX initialized. Remapper is ACTIVE.
[13:53:16] Key [Q] DOWN -> Move Left
[13:53:16] Key [Q] UP
[13:53:18] Key [E] DOWN -> Move Right
[13:53:18] Key [E] UP
[13:53:20] Mouse speed changed to: 20 px/tick
[13:53:22] Remapper set to DISABLED
```

---

## 🛠️ Rebuilding from Source

If you edit `InFalSDVX.cs`, double-click **`build.bat`** to recompile `InFalSDVX.exe` instantly.
