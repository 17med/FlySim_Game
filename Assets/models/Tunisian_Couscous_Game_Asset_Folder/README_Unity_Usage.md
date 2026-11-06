# Tunisian Couscous Dish (Tebsi Kosksi) — 3D Game Asset Folder (100% down to 0% Animation & Separated Parts)

## Folder Structure
1. **`01_Animated_Dish_100_to_0/`**
   - `Tunisian_Couscous_Animated_100_to_0.fbx`: Kaydara Binary FBX 7400 containing all 14 separated child objects (`00_Plate_Tebsi_Ceramic` through `13_Veg_Left_Pumpkin_and_Zucchini`) plus the embedded animation clip `Eat_100_to_0_Percent` (0.0s = 100% Full -> 0.5s = 90% -> ... -> 5.0s = 0% Empty Plate).
   - `Tunisian_Couscous_Animated_100_to_0.glb`: Self-contained glTF 2.0 binary with embedded PBR textures and the `Eat_Dish_100_to_0_Percent` keyframe animation.
2. **`02_Stages_100_to_0_Percent/`**
   - 11 individual stage models in both `.fbx` and `.obj` formats (`100%`, `90%`, `80%`, `70%`, `60%`, `50%`, `40%`, `30%`, `20%`, `10%`, and `0% Empty Plate`).
3. **`03_Separated_Modular_Parts/`**
   - `Tunisian_Couscous_All_14_Parts_Separated.fbx`: Multi-object FBX hierarchy where every single ingredient is a separate child GameObject in Unity.
   - 14 standalone `.obj` meshes (`00_Plate_Tebsi_Ceramic.obj`, `01_Couscous_Semolina_Mound.obj`, `02_Meat_Lamb_Shank_Main.obj`, etc.).
4. **`04_Unity_Scripts/`**
   - `CouscousDishController.cs`: Ready-to-use Unity C# component with a `dishPercentage` `[Range(0f, 100f)]` slider and `EatPortion(10f)` method to smoothly deplete the dish from 100% to 90%, 80%, ..., 0% in real time.
5. **`05_Textures/`**
   - `Tunisian_Couscous_Albedo.png` (2048x2048)
   - `Tunisian_Couscous_Normal.png` (2048x2048)
   - `Tunisian_Couscous_MetallicSmoothness.png` (2048x2048)
   - `Tunisian_Couscous_AO.png` (2048x2048)
