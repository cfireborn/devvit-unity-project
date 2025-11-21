This project is built with Unity 6.2 (6000.2.8f1)

## Instructions: 
1. Once downloaded, open this project via Unity Hub.
2. Navigate to Assets/Scenes/SampleScene and open that Scene. 
3. Press Play to test the scene in the Unity environment



## Exporting for Devvit:
Here are the steps to export this project for the configured Devvit Unity Starter Template:
1. In Unity, Select File > Build Profiles and Switch the active Platform to Web.
2. Still within the Build Profiles window, select Player Settings to navigate to the Player Settings window
3. In Player Settings, scroll down to Publishing Settings and ensure Decompression Fallback is selected.
4. Now we are ready to export, but will need to build the project twice:
    a. Set the Compression Format to GZip and select Build in the Build Profiles window.
    b. Copy the following files into your src/client/Public/Build folder of the Devvit Starter Template: 
        - exportName.data.unityweb
        - exportName.wasm.unityweb
   c. Next, in Publishing Settings, set Compression Format to Disabled  and select Build in the Build Profiles window
   d. Copy the following files into your src/client/Public/Build folder of the Devvit Starter Template:
       exportName.framework.js

5. Run `npm run dev` in your devvit project to update the Unity app running within Reddit. 
   
