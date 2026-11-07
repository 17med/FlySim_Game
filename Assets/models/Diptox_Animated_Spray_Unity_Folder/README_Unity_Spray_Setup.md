# Diptox Aerosol Spray Can — Button Click Animation & Particle System for Unity

## Folder Contents
1. **`01_Animated_Spray_FBX_and_GLB/`**
   - `Diptox_Spray_Animated_With_Particles.fbx` & `.glb`: Contains the separated Can Body (`00_Diptox_Can_Body`), Red Spray Collar (`01_Red_Actuator_Hood`), Pressable Nozzle Button (`02_Pressable_Spray_Button`), and 3D Animated Spray Mist Cone + 8 Aerosol Particle Puffs (`03_Spray_Mist_Cone`, `04_Spray_Particles_Puff_01..08`) with the embedded animation clip **`Click_Button_And_Spray_Particles`**.
2. **`02_Separated_Can_and_Button_Model/`**
   - `Diptox_Separated_Pressable_Button.fbx` & `.glb`: Clean separated hierarchy (`00_Diptox_Can_Body`, `01_Red_Actuator_Hood`, `02_Pressable_Spray_Button`) designed to be paired directly with `DiptoxSprayController.cs` and Unity's native Particle System.
3. **`03_Unity_Scripts/DiptoxSprayController.cs`**
   - Attach `DiptoxSprayController.cs` to the root of the Diptox prefab in Unity.
   - When you **Left-Click** (or press **Space**, or trigger `StartSpray()` / `StopSpray()` from a UI button):
     1. `02_Pressable_Spray_Button` smoothly depresses downward by `4.5 mm` (`-Y`) and springs back up when released.
     2. A full **Unity ParticleSystem** automatically spawns at the front nozzle hole (`+Z`) and emits a high-velocity cone of expanding, turbulent insecticide mist particles using `Diptox_Spray_Particle_Sprite.png`.
     3. Includes built-in `Physics.SphereCastAll` hit detection (`OnHitByDiptox`) to damage/kill insects in the spray cone.
4. **`04_Textures_and_Particle_Sprite/`**
   - `Diptox_Spray_Particle_Sprite.png` (512x512 RGBA volumetric mist & micro-droplet particle sprite)
   - `Diptox_Albedo.png`, `Diptox_Normal.png`, `Diptox_MetallicSmoothness.png`, `Diptox_AO.png` (2048x2048 PBR maps)
