汉化支持

# Moulman's EFT DMA Radar

Fork of [Lone EFT DMA Radar](https://github.com/lone-dma/Lone-EFT-DMA-Radar) with additional ESP, aimbot, and memory-write features.

## Disclaimer ⚠️  
This app has been tested on 🪟 Windows 11 25H2 (Game) and 🪟 Windows 11 23H2 (Radar).  
⚠️ Older versions of Windows (e.g., Windows 10) may not work properly and are not officially supported.

**Note:** All current testing is done with both the radar and game running at **1920x1080** resolution.  

## Features ✨

- 🛰️ ESP Fuser DX9 overlay
- 🎯 Device Aimbot / Kmbox integration
- 🕵️‍♂️ Silent aim (memory aim)
- 💪 No recoil, no sway, and infinite stamina
- 🧼 Clean UI

##  Common Issues ⚠️

### DX Overlay/D3DX Errors ("DX overlay init failed", "ESP DX init failed: System.DllNotFoundException: Unable to load DLL 'd3dx943.dll'...")

If you see an error like:

```
DX overlay init failed

ESP DX init failed: System.DllNotFoundException: Unable to load DLL 'd3dx943.dll' or one of its dependencies: The specified module could not be found
```

This means your PC does **not** have the required legacy DirectX 9 *D3DX* runtime (specifically `d3dx9_43.dll`). Modern Windows installs (Windows 10/11) **do not include** this file by default.

**How to fix:**

1. **On your Radar PC**, download and run Microsoft’s official installer:

   👉 [DirectX End-User Runtime (June 2010)](https://www.microsoft.com/en-us/download/details.aspx?id=8109)

   > This will add the required DirectX 9 component (`d3dx9_43.dll`) and several others needed by the overlay.

2. **Follow the install prompts** to complete setup.

3. **Restart the radar app.** A full PC reboot may help but is usually not required.

**Do NOT** attempt to download `d3dx9_43.dll` from random third-party DLL sites. Use only Microsoft’s official installer.


##  Contributing 🤝

Send PRs if you wish to participate. Contributions are welcome!

- Please fork the repository and create pull requests for features or fixes.
- Test your changes before submitting a PR.
- If you are submitting a significant change, consider opening an issue to discuss it first.
