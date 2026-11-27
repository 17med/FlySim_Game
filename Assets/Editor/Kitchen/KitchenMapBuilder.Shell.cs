using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// The shell every room shares: one floor slab, the ceiling, the outer walls with their windows and
// the interior walls with their doorways, plus the ceiling and pendant lamps the rooms hang from it.
// Plan, in house metres (x west to east, z south to north; the outer walls stand just outside):
//
//   z  3.5  +-------------+-------------+-------------+
//           |   KITCHEN   |    PANTRY   | LIVING ROOM |
//           |             D             D             |   D = doorway, 1.2 m wide, 2.1 m high
//   z  0.6  +----D--------+-----D-------+--D----------+
//   z  0.5  |                                         |
//           |               HUB / DINING              |
//   z -3.5  +-----------------------------------------+
//         x -7          x -2          x 2           x 7
//
// The three north rooms each open onto the hub and onto their neighbours, so every room can be
// walked through in a loop and none of them has a single doorway a player can be trapped behind.
// Every interior wall is built in two half-thick layers, one per room, so each room paints its own side.
public partial class KitchenMapBuilder
{
    const float HouseWest = -7f;
    const float HouseEast = 7f;
    const float HouseSouth = -3.5f;
    const float HouseNorth = 3.5f;
    const float WallThickness = 0.1f;
    // The hub wall runs the width of the house: its south face is the hub's north edge, its north
    // face the north rooms' south edge.
    const float HubNorth = 0.5f;
    const float NorthRoomsSouth = HubNorth + WallThickness;
    const float HubWallMiddle = HubNorth + WallThickness * 0.5f;
    // The two cross walls between the north rooms, centred on x = -2 and x = 2.
    const float KitchenEast = -2.05f;
    const float PantryWest = KitchenEast + WallThickness;
    const float KitchenPantryMiddle = KitchenEast + WallThickness * 0.5f;
    const float PantryEast = 1.95f;
    const float LivingWest = PantryEast + WallThickness;
    const float PantryLivingMiddle = PantryEast + WallThickness * 0.5f;
    // Doorways, by their west (hub wall) or south (cross walls) edge.
    const float DoorwayWidth = 1.2f;
    const float DoorwayHeight = 2.1f;
    const float KitchenDoorwayWest = -3.4f;
    const float PantryDoorwayWest = -0.6f;
    const float LivingDoorwayWest = 2.2f;
    const float CrossDoorwaySouth = 1.4f;
    // Window centres along their walls.
    const float HubWindowWestX = -4f;
    const float HubWindowEastX = 4f;
    const float KitchenWindowX = -4.5f;
    const float LivingWindowX = 4.6f;
    const float TrimWidth = 0.07f;
    const float TrimDepth = 0.02f;
    const float WindowHalfWidth = 0.6f;
    // Extra ground pressed down around the house's outer walls, so a hill never shows past the slab's edge.
    const float TerrainClearance = 0.3f;
    // Where the pressed-down ground ends up: just under the floor slab's top, out of sight beneath it.
    const float TerrainUnderFloor = -0.05f;
    // One step of a 16-bit terrain height, as a share of the terrain's full height.
    const float HeightmapStep = 2f / 65535f;
    const float WindowTop = 2f;
    const float SillHeight = 1.05f;
    const float SillDepth = 0.2f;
    const float BaseboardHeight = 0.1f;
    const float BaseboardDepth = 0.015f;
    // 2 mm: high enough above the floor slab not to flicker against it, too low to trip over.
    const float InlayHeight = 0.002f;
    const float CeilingLightIntensity = 160f;
    const float PendantIntensity = 80f;
    // Above the floor, not below the ceiling: a pendant hangs at the height of the table it lights,
    // whatever the room height is.
    const float PendantShadeBottom = 1.8f;
    static readonly Color HubWallColor = new Color(0.88f, 0.77f, 0.7f);
    static readonly Color KitchenWallColor = new Color(0.93f, 0.87f, 0.76f);
    static readonly Color PantryWallColor = new Color(0.86f, 0.81f, 0.66f);
    static readonly Color LivingWallColor = new Color(0.66f, 0.74f, 0.72f);
    static readonly Color SeamColor = new Color(0.2f, 0.2f, 0.2f);
    static readonly Color[] PlankColors =
    {
        new Color(0.62f, 0.43f, 0.28f), new Color(0.57f, 0.39f, 0.25f), new Color(0.66f, 0.47f, 0.31f), new Color(0.6f, 0.41f, 0.27f),
        new Color(0.55f, 0.37f, 0.24f), new Color(0.64f, 0.45f, 0.3f), new Color(0.58f, 0.4f, 0.26f), new Color(0.63f, 0.44f, 0.29f),
    };

    void BuildShell(Transform shell)
    {
        float t = WallThickness;
        BuildFloor(shell);
        // A direct child of Shell named Ceiling: KitchenCeilingRaiser finds it by that path.
        GameObject ceiling = Box(shell, "Ceiling", new Vector3(HouseWest - t, RoomHeight, HouseSouth - t),
            new Vector3(HouseEast + t, RoomHeight + t, HouseNorth + t), PaintSurface("Ceiling", new Color(0.96f, 0.95f, 0.92f)));
        // A roof shadow would put the whole interior in the toon shader's shadow band; letting the sun
        // through is the usual stylized-interior cheat, and nobody sees the ceiling from above.
        ceiling.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        BuildOuterWalls(Group(shell, "OuterWalls"));
        BuildInteriorWalls(Group(shell, "InteriorWalls"));
    }

    // One slab under the whole house carries the floor's collider, so nothing can fall through a seam
    // between rooms; the kitchen and the pantry get tiles laid on top of it.
    void BuildFloor(Transform shell)
    {
        float t = WallThickness;
        Vector3 min = new Vector3(HouseWest - t, -t, HouseSouth - t), max = new Vector3(HouseEast + t, 0f, HouseNorth + t);
        // Eight 20 cm planks running north-south, repeating every 1.6 m.
        Box(shell, "Floor", min, max, PaintPattern("Planks", PlankColors, PlankColors.Length, new Vector2((max.x - min.x) / 1.6f, 1f)));

        // 40 cm checker tiles in the kitchen, 30 cm terracotta in the pantry: the 2x2 pattern spans two tiles.
        Color cream = new Color(0.92f, 0.89f, 0.82f), teal = new Color(0.4f, 0.55f, 0.55f);
        Vector2 kitchenSize = new Vector2(KitchenEast - HouseWest, HouseNorth - NorthRoomsSouth);
        FloorInlay(shell, new Vector2(HouseWest, NorthRoomsSouth), new Vector2(KitchenEast, HouseNorth),
            PaintPattern("KitchenTiles", new[] { cream, teal, teal, cream }, 2, kitchenSize / 0.8f));
        Color clay = new Color(0.72f, 0.42f, 0.3f), sand = new Color(0.82f, 0.56f, 0.4f);
        Vector2 pantrySize = new Vector2(PantryEast - PantryWest, HouseNorth - NorthRoomsSouth);
        FloorInlay(shell, new Vector2(PantryWest, NorthRoomsSouth), new Vector2(PantryEast, HouseNorth),
            PaintPattern("PantryTiles", new[] { clay, sand, sand, clay }, 2, pantrySize / 0.6f));
    }

    // A thin painted layer on the floor slab, between the given (x, z) corners. It has no collider:
    // the slab under it is the floor, and a second floor 2 mm up would only snag feet at the doorways.
    static void FloorInlay(Transform parent, Vector2 min, Vector2 max, Material tiles)
    {
        GameObject inlay = Box(parent, "FloorInlay", new Vector3(min.x, 0f, min.y), new Vector3(max.x, InlayHeight, max.y), tiles);
        Object.DestroyImmediate(inlay.GetComponent<BoxCollider>());
    }

    // Each outer wall is cut where the rooms meet, so each length is painted for the room it faces.
    // The south and north lengths run past the corners to close them.
    void BuildOuterWalls(Transform parent)
    {
        float t = WallThickness;
        Material hub = PaintSurface("HubWall", HubWallColor), kitchen = PaintSurface("KitchenWall", KitchenWallColor);
        Material pantry = PaintSurface("PantryWall", PantryWallColor), living = PaintSurface("LivingWall", LivingWallColor);
        // Yaw 180: these frames run west from their origin.
        BuildWindowWall(WallFrame(parent, "Wall_South_East", new Vector3(HouseEast + t, 0f, HouseSouth), 180f), HouseEast + t, HouseEast + t - HubWindowEastX, hub);
        BuildWindowWall(WallFrame(parent, "Wall_South_West", new Vector3(0f, 0f, HouseSouth), 180f), -(HouseWest - t), -HubWindowWestX, hub);
        // Yaw 0: these run east.
        BuildWindowWall(WallFrame(parent, "Wall_North_Kitchen", new Vector3(HouseWest - t, 0f, HouseNorth), 0f), KitchenPantryMiddle - (HouseWest - t), KitchenWindowX - (HouseWest - t), kitchen);
        BuildSolidWall(WallFrame(parent, "Wall_North_Pantry", new Vector3(KitchenPantryMiddle, 0f, HouseNorth), 0f), PantryLivingMiddle - KitchenPantryMiddle, pantry);
        BuildWindowWall(WallFrame(parent, "Wall_North_Living", new Vector3(PantryLivingMiddle, 0f, HouseNorth), 0f), HouseEast + t - PantryLivingMiddle, LivingWindowX - PantryLivingMiddle, living);
        // Yaw -90: these run north.
        BuildSolidWall(WallFrame(parent, "Wall_West_Hub", new Vector3(HouseWest, 0f, HouseSouth), -90f), HubWallMiddle - HouseSouth, hub);
        BuildSolidWall(WallFrame(parent, "Wall_West_Kitchen", new Vector3(HouseWest, 0f, HubWallMiddle), -90f), HouseNorth - HubWallMiddle, kitchen);
        // Yaw 90: these run south.
        BuildSolidWall(WallFrame(parent, "Wall_East_Living", new Vector3(HouseEast, 0f, HouseNorth), 90f), HouseNorth - HubWallMiddle, living);
        BuildSolidWall(WallFrame(parent, "Wall_East_Hub", new Vector3(HouseEast, 0f, HubWallMiddle), 90f), HubWallMiddle - HouseSouth, hub);
    }

    // The hub wall is built in three lengths, one per north room, each with that room's doorway; the
    // cross walls run south from the north wall with their front faces to the west.
    void BuildInteriorWalls(Transform parent)
    {
        Material hub = PaintSurface("HubWall", HubWallColor), kitchen = PaintSurface("KitchenWall", KitchenWallColor);
        Material pantry = PaintSurface("PantryWall", PantryWallColor), living = PaintSurface("LivingWall", LivingWallColor);
        BuildDoorwayWall(WallFrame(parent, "Wall_Hub_Kitchen", new Vector3(HouseWest, 0f, HubNorth), 0f),
            KitchenPantryMiddle - HouseWest, KitchenDoorwayWest - HouseWest, hub, kitchen);
        BuildDoorwayWall(WallFrame(parent, "Wall_Hub_Pantry", new Vector3(KitchenPantryMiddle, 0f, HubNorth), 0f),
            PantryLivingMiddle - KitchenPantryMiddle, PantryDoorwayWest - KitchenPantryMiddle, hub, pantry);
        BuildDoorwayWall(WallFrame(parent, "Wall_Hub_Living", new Vector3(PantryLivingMiddle, 0f, HubNorth), 0f),
            HouseEast - PantryLivingMiddle, LivingDoorwayWest - PantryLivingMiddle, hub, living);

        float crossLength = HouseNorth - NorthRoomsSouth, crossDoorway = HouseNorth - (CrossDoorwaySouth + DoorwayWidth);
        BuildDoorwayWall(WallFrame(parent, "Wall_Kitchen_Pantry", new Vector3(KitchenEast, 0f, HouseNorth), 90f), crossLength, crossDoorway, kitchen, pantry);
        BuildDoorwayWall(WallFrame(parent, "Wall_Pantry_Living", new Vector3(PantryEast, 0f, HouseNorth), 90f), crossLength, crossDoorway, pantry, living);
    }

    // An interior wall with one doorway, in its own frame (see BuildWindowWall): local x runs along the
    // wall, the front face is at z = 0 and the back face at z = WallThickness. Both faces get door
    // trims and baseboards; the lintel fills the doorway from DoorwayHeight up to the ceiling.
    // Five parameters (over the usual four): the two paints are what makes it one wall between two
    // rooms, and bundling them would only move the pair one call further away.
    void BuildDoorwayWall(Transform frame, float length, float doorwayStart, Material front, Material back)
    {
        float doorwayEnd = doorwayStart + DoorwayWidth, t = WallThickness, half = t * 0.5f;
        foreach ((float z0, float z1, Material paint) in new[] { (0f, half, front), (half, t, back) })
        {
            Box(frame, "Wall_BeforeDoorway", new Vector3(0f, 0f, z0), new Vector3(doorwayStart, RoomHeight, z1), paint);
            Box(frame, "Wall_AfterDoorway", new Vector3(doorwayEnd, 0f, z0), new Vector3(length, RoomHeight, z1), paint);
            Box(frame, "Lintel", new Vector3(doorwayStart, DoorwayHeight, z0), new Vector3(doorwayEnd, RoomHeight, z1), paint);
        }
        DoorTrim(frame, doorwayStart, 0f, -1f);
        DoorTrim(frame, doorwayStart, t, 1f);

        // The front face's room is on -z, so its baseboards are walked towards -x (see Baseboard).
        float beforeTrim = doorwayStart - TrimWidth, afterTrim = doorwayEnd + TrimWidth;
        Baseboard(frame, new Vector3(beforeTrim, 0f, 0f), Vector3.zero);
        Baseboard(frame, new Vector3(length, 0f, 0f), new Vector3(afterTrim, 0f, 0f));
        Baseboard(frame, new Vector3(0f, 0f, t), new Vector3(beforeTrim, 0f, t));
        Baseboard(frame, new Vector3(afterTrim, 0f, t), new Vector3(length, 0f, t));
    }

    // The white frame round a doorway on one face of a wall frame. outward is -1 for the front face
    // (z = 0) and +1 for the back face (z = WallThickness): the side the trim stands proud on.
    void DoorTrim(Transform frame, float doorwayStart, float faceZ, float outward)
    {
        Material trim = PaintSurface("Trim", TrimWhite, 0.4f);
        float z0 = Mathf.Min(faceZ, faceZ + outward * TrimDepth), z1 = Mathf.Max(faceZ, faceZ + outward * TrimDepth);
        float doorwayEnd = doorwayStart + DoorwayWidth, top = DoorwayHeight + TrimWidth;
        Box(frame, "DoorTrim_Start", new Vector3(doorwayStart - TrimWidth, 0f, z0), new Vector3(doorwayStart, top, z1), trim);
        Box(frame, "DoorTrim_End", new Vector3(doorwayEnd, 0f, z0), new Vector3(doorwayEnd + TrimWidth, top, z1), trim);
        Box(frame, "DoorTrim_Head", new Vector3(doorwayStart - TrimWidth, DoorwayHeight, z0), new Vector3(doorwayEnd + TrimWidth, top, z1), trim);
    }

    // An outer wall without a window, in its own frame (see BuildWindowWall).
    void BuildSolidWall(Transform frame, float length, Material wall)
    {
        Box(frame, "Wall", Vector3.zero, new Vector3(length, RoomHeight, WallThickness), wall);
        Baseboard(frame, new Vector3(length, 0f, 0f), Vector3.zero);
    }

    // An outer wall with a window, in its own frame: local x runs along the wall, z = 0 is the
    // room-side face and +z points outside. The glass is an invisible collider, so the fly stays in.
    void BuildWindowWall(Transform frame, float length, float windowCenter, Material wall)
    {
        float left = windowCenter - WindowHalfWidth, right = windowCenter + WindowHalfWidth, t = WallThickness;
        Box(frame, "Wall_Left", new Vector3(0f, 0f, 0f), new Vector3(left, RoomHeight, t), wall);
        Box(frame, "Wall_Right", new Vector3(right, 0f, 0f), new Vector3(length, RoomHeight, t), wall);
        Box(frame, "Wall_BelowWindow", new Vector3(left, 0f, 0f), new Vector3(right, SillHeight, t), wall);
        Box(frame, "Wall_AboveWindow", new Vector3(left, WindowTop, 0f), new Vector3(right, RoomHeight, t), wall);
        Baseboard(frame, new Vector3(length, 0f, 0f), Vector3.zero);

        Material trim = PaintSurface("Trim", TrimWhite, 0.4f);
        Box(frame, "WindowSill", new Vector3(left - 0.08f, SillHeight - 0.03f, -SillDepth), new Vector3(right + 0.08f, SillHeight, t), trim);
        Box(frame, "WindowTrim_Left", new Vector3(left - TrimWidth, SillHeight, -TrimDepth), new Vector3(left, WindowTop + TrimWidth, 0f), trim);
        Box(frame, "WindowTrim_Right", new Vector3(right, SillHeight, -TrimDepth), new Vector3(right + TrimWidth, WindowTop + TrimWidth, 0f), trim);
        Box(frame, "WindowTrim_Head", new Vector3(left - TrimWidth, WindowTop, -TrimDepth), new Vector3(right + TrimWidth, WindowTop + TrimWidth, 0f), trim);
        Box(frame, "WindowMullion", new Vector3(windowCenter - 0.02f, SillHeight, t * 0.3f), new Vector3(windowCenter + 0.02f, WindowTop, t * 0.7f), trim);
        Box(frame, "WindowTransom", new Vector3(left, 1.62f, t * 0.3f), new Vector3(right, 1.66f, t * 0.7f), trim);
        GameObject glass = Box(frame, "WindowGlass", new Vector3(left, SillHeight, 0f), new Vector3(right, WindowTop, t), trim);
        Object.DestroyImmediate(glass.GetComponent<MeshRenderer>());
    }

    // A wall frame placed at origin with the given compass yaw (see BuildWindowWall).
    static Transform WallFrame(Transform parent, string name, Vector3 origin, float yaw)
    {
        Transform frame = Group(parent, name);
        frame.localPosition = origin;
        frame.localRotation = Quaternion.Euler(0f, yaw, 0f);
        return frame;
    }

    // A baseboard along a wall face at floor level, on the left of the walking direction, which is
    // the room side when the face is walked counter-clockwise round its room, seen from above.
    void Baseboard(Transform parent, Vector3 start, Vector3 end)
    {
        Vector3 direction = (end - start).normalized;
        Vector3 left = new Vector3(-direction.z, 0f, direction.x) * BaseboardDepth;
        Vector3 min = Vector3.Min(start, end + left);
        Vector3 max = Vector3.Max(start, end + left) + Vector3.up * BaseboardHeight;
        Box(parent, "Baseboard", min, max, PaintSurface("Trim", TrimWhite, 0.4f));
    }

    // A flush light in the middle of a room's ceiling. KitchenCeilingRaiser moves anything named
    // CeilingLamp or CeilingLight up with the ceiling, so the names stay.
    void CeilingLamp(Transform parent, float x, float z)
    {
        Cylinder(parent, "CeilingLamp", new Vector3(x, RoomHeight - 0.08f, z), new Vector3(0.5f, 0.08f, 0.5f), PaintGlow("CeilingLamp", new Color(1f, 0.92f, 0.78f)));
        Light light = NewLight(parent, "CeilingLight", new Vector3(x, RoomHeight - 0.25f, z), LightType.Point);
        light.range = 7f * WorldScale;
        light.intensity = CeilingLightIntensity;
    }

    // A lamp on a cord from the ceiling, its shade PendantShadeBottom above the floor.
    void Pendant(Transform parent, float x, float z)
    {
        Vector3 spot = new Vector3(x, 0f, z);
        float shadeHeight = 0.2f;
        Box(parent, "PendantCord", spot + new Vector3(-0.005f, PendantShadeBottom + shadeHeight, -0.005f), spot + new Vector3(0.005f, RoomHeight, 0.005f), Paint("Seam", SeamColor));
        Cylinder(parent, "PendantShade", spot + Vector3.up * PendantShadeBottom, new Vector3(0.36f, shadeHeight, 0.36f), PaintGlow("PendantShade", new Color(0.98f, 0.8f, 0.45f)));
        Light light = NewLight(parent, "PendantLight", spot + Vector3.up * (PendantShadeBottom - 0.05f), LightType.Point);
        light.range = 5f * WorldScale;
        light.intensity = PendantIntensity;
    }

    // The outdoor ground is a hand-made Terrain sculpted around the old, smaller house. The new house
    // reaches 4 m further west, and one of its hills came up through the kitchen floor and the west
    // counter (seen in the first top-down build). The ground under the floor slab is pressed down to
    // just below the floor - nobody can see it there - and everything outside the house stays as it
    // was sculpted. Saved straight away, because the heights live in the TerrainData asset, which
    // saving the scene does not write.
    static void FlattenTerrainUnderHouse(Scene scene)
    {
        foreach (GameObject sceneRoot in scene.GetRootGameObjects())
        {
            foreach (Terrain terrain in sceneRoot.GetComponentsInChildren<Terrain>(true))
                FlattenUnderFootprint(terrain);
        }
    }

    static void FlattenUnderFootprint(Terrain terrain)
    {
        TerrainData data = terrain.terrainData;
        // Fails open: a Terrain with no data has no ground to push through the floor.
        if (data == null)
            return;
        float edge = WallThickness + TerrainClearance;
        Vector3 origin = terrain.transform.position;
        int resolution = data.heightmapResolution;
        float x0 = HeightmapPosition((HouseWest - edge) * WorldScale - origin.x, data.size.x, resolution);
        float x1 = HeightmapPosition((HouseEast + edge) * WorldScale - origin.x, data.size.x, resolution);
        float z0 = HeightmapPosition((HouseSouth - edge) * WorldScale - origin.z, data.size.z, resolution);
        float z1 = HeightmapPosition((HouseNorth + edge) * WorldScale - origin.z, data.size.z, resolution);
        // A house beside the terrain rather than over it has nothing of it to press down.
        if (x1 < 0f || z1 < 0f || x0 > resolution - 1 || z0 > resolution - 1)
            return;
        int left = Mathf.Clamp(Mathf.FloorToInt(x0), 0, resolution - 1), right = Mathf.Clamp(Mathf.CeilToInt(x1), 0, resolution - 1);
        int bottom = Mathf.Clamp(Mathf.FloorToInt(z0), 0, resolution - 1), top = Mathf.Clamp(Mathf.CeilToInt(z1), 0, resolution - 1);
        float under = Mathf.Clamp01((TerrainUnderFloor * WorldScale - origin.y) / data.size.y);
        float[,] heights = data.GetHeights(left, bottom, right - left + 1, top - bottom + 1);
        int lowered = 0;
        for (int z = 0; z < heights.GetLength(0); z++)
        {
            for (int x = 0; x < heights.GetLength(1); x++)
            {
                // A sample's worth of slack: heights are stored in 16 bits, so ground pressed down on an
                // earlier build reads back a hair above 'under' and would be counted as lowered again.
                if (heights[z, x] <= under + HeightmapStep)
                    continue;
                heights[z, x] = under;
                lowered++;
            }
        }
        if (lowered == 0)
            return;
        Undo.RegisterCompleteObjectUndo(data, "Flatten Terrain Under House");
        data.SetHeights(left, bottom, heights);
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssetIfDirty(data);
        Debug.Log($"KitchenMapBuilder: pressed {lowered} terrain height samples of '{terrain.name}' down under the house floor, so no hill comes up through it.", terrain);
    }

    // Where a distance along the terrain falls on its heightmap, in samples; not clamped, so the caller
    // can tell a house beside the terrain from one over its edge.
    static float HeightmapPosition(float distance, float size, int resolution) => distance / size * (resolution - 1);
}
