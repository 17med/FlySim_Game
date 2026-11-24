# Fluted Glass Coffee Mug — Unity 3D Asset & 0→100 Drink Animation

## Contents
- `Meshes/FlutedGlassMug.obj` (+ `.mtl`) — 36-flute ribbed clear glass mug with rounded handle (1 Unity unit = 1 meter).
- `Meshes/CoffeeLiquid.obj` (+ `.mtl`) — Espresso body + frothy crema top disk (pivot at bottom for clean 0→100% scaling).
- `Textures/` — PBR textures extracted and synthesized from your reference photo:
  - `Coffee_Crema_Albedo.png` (1024x1024)
  - `Coffee_Crema_Normal.png` (1024x1024 micro-foam normal map)
  - `Coffee_Crema_Roughness.png` (1024x1024)
  - `Coffee_Body_Albedo.png`
- `Unity/CoffeeDrinkController.cs` — Ready-to-use C# script that animates the coffee from `0` (full) to `100` (empty).
- `Blender/build_coffee_fbx.py` — Optional Blender script if you want to bake the meshes + `Drink_0_to_100` Shape Key into a single `.fbx`.

## Quick Setup in Unity
1. Drag the `Meshes/`, `Textures/`, and `Unity/` folders into your Unity `Assets/` directory.
2. Place `FlutedGlassMug` in your scene, then drag `CoffeeLiquid` as a child of `FlutedGlassMug` at local position `(0, 0.01, 0)`.
3. On the `ClearFlutedGlass` material:
   - Set **Surface Type** to **Transparent** (URP) or **Rendering Mode = Transparent** (Built-in).
   - Set **Smoothness** to `0.95` and **Alpha** to `0.22` for realistic clear glass.
4. On `CoffeeCremaMat`:
   - Assign `Coffee_Crema_Albedo` to Base Map/Albedo and `Coffee_Crema_Normal` to Normal Map.
5. Attach `CoffeeDrinkController.cs` to `FlutedGlassMug`, assign `CoffeeLiquid` to `Coffee Liquid Transform`, and scrub `Drink Progress (0–100)` or call `PlayDrinkAnimation()` in your game code.

## Optional: Convert to Single Animated `.fbx` in Blender
If you have Blender installed locally, run:
```bash
blender --background --python Blender/build_coffee_fbx.py
```
This outputs `FlutedCoffeeMug_Animated.fbx` with a `Drink_0_to_100` BlendShape/Shape Key animated from frame 1 to 100.
