## This project is built with Unity 6.2 (6000.2.8f1)

## Instructions
1. Once downloaded, open this project via Unity Hub.
2. Navigate to `Assets/Scenes/SampleScene` and open that scene.
3. Press Play to test the scene in the Unity environment.

## Exporting for Devvit
Here are the steps to export this project for the configured Devvit Unity Starter Template:

1. In Unity, select **File > Build Profiles** and switch the active platform to **Web**.
2. In the Build Profiles window, select **Player Settings** to open the Player Settings window.
3. In Player Settings, scroll down to **Publishing Settings** and ensure **Decompression Fallback** is selected.
4. Now we are ready to export, but the project needs to be built twice:

   a. Set the **Compression Format** to **GZip** and select **Build** in the Build Profiles window.  
   b. Copy the following files into your `src/client/Public/Build` folder of the Devvit Starter Template:  
      - `exportName.data.unityweb`  
      - `exportName.wasm.unityweb`  
   c. Next, in Publishing Settings, set **Compression Format** to **Disabled** and select **Build** again.  
   d. Copy the following file into your `src/client/Public/Build` folder:  
      - `exportName.framework.js`

5. Run `npm run dev` in your Devvit project to update the Unity app running within Reddit.

## Automated Devvit export (macOS)
You can run the entire double-build pipeline from the terminal with the included helper script:

```bash
./tools/export_devvit.sh \
  --output ../devvit-unity-starter/src/client/Public/Build \
  --name DevvitWebGL
```

- The script calls Unity in batch mode, sets **Decompression Fallback**, performs the GZip and Disabled builds, and copies the resulting `.data.unityweb`, `.wasm.unityweb`, and `.framework.js` files into the destination folder you specify.
- If Unity is installed in a non-default location, pass `--unity /path/to/Unity.app/Contents/MacOS/Unity` (or set the `UNITY_PATH` environment variable) when running the script.
- The `--name` argument controls the base filename for the copied artifacts; omit it to reuse the Unity `Product Name`.
- The script creates a local `DevvitExports` folder by default, so you can point it at any Devvit Starter Template folder or keep local artifacts for manual copying.
