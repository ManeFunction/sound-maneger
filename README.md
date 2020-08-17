# sound-maneger
Just a neat sound manager for Unity. Have separated channels, playlists, ducking and some other amazing features.

From version 1.0.4 finally with **npm packages**!  
What means **Unity Package Manager** support with versions and updates!

To add this or any other of my packages to your project,  
paste my packages feed address to **manifest.json**
```json
  "scopedRegistries": [
    {
      "name": "Mane Function",
      "url": "https://pkgs.dev.azure.com/manefunction/unity-mane/_packaging/unity/npm/registry/",
      "scopes": [
        "com.manefunction"
      ]
    }
  ],
```
and add this project dependency (version can vary)
```json
  "dependencies": {
    "com.manefunction.sound-maneger": "1.0.4", 
    
    }
```
**If you are using Unity 2018 or earlier, use my legacy feed:**  
https://pkgs.dev.azure.com/manefunction/unity-mane/_packaging/unity-2018/npm/registry/
