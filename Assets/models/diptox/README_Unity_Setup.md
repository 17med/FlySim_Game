# Diptox Aerosol Spray Can — 3D Asset Package for Unity

## Included Files
- `diptox_can.glb`: Self-contained binary glTF 2.0 3D model with embedded PBR textures (Albedo, Metallic-Roughness, Normal, Ambient Occlusion).
- `diptox_can.obj` & `diptox_can.mtl`: Wavefront OBJ mesh for native drag-and-drop import into any version of Unity without extra packages.
- `Textures/diptox_albedo.png`: 2048x2048 Base Color / Albedo UV map.
- `Textures/diptox_metallic_smoothness.png`: 2048x2048 Unity Metallic-Smoothness map (RGB = Metallic, Alpha = Smoothness).
- `Textures/diptox_normal.png`: 2048x2048 OpenGL/Unity tangent-space Normal map (mark as "Normal map" in Unity Texture Import Settings).
- `Textures/diptox_ao.png`: 2048x2048 Ambient Occlusion map.

## Mesh Specifications
- **Real-World Scale (Unity Units / Meters)**: Diameter = `0.065 m` (6.5 cm), Height = `0.280 m` (28.0 cm)
- **Pivot Point**: Bottom center `(0, 0, 0)` for easy surface placement
- **Forward Axis**: Front label faces `+Z`
- **Single Unified Material**: Uses a single non-overlapping UV layout across the cap, body, neck seam, and bottom rim so only 1 Material draw call is required.
