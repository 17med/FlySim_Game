using UnityEngine;

// The kitchen: an L of counters along the north and west walls with the stove, sink and window, the
// fridge in the north-east corner, a dining table in the middle and a prep table by the doorway.
public partial class KitchenMapBuilder
{
    const float CounterHeight = 0.9f;
    const float CounterDepth = 0.6f;
    const float CounterFront = HalfDepth - CounterDepth;
    const float WestCounterFront = KitchenWest + CounterDepth;
    const float NorthCounterEast = 1.8f;
    const float StoveWest = -2.2f;
    const float StoveEast = -1.6f;
    const float FridgeWest = 2.2f;
    const float FridgeFront = 1.8f;
    const float UpperCabinetBottom = 1.45f;
    const float UpperCabinetTop = 2.15f;
    const float UpperCabinetDepth = 0.35f;
    const float DoorSouth = -2.2f;
    const float DoorNorth = -1.3f;
    static readonly Vector3 DiningTableSpot = new Vector3(-0.4f, 0f, 0.1f);
    // The prep table stands long side east-west against the south wall.
    static readonly Vector3 PrepTableSpot = new Vector3(1.35f, 0f, -1.75f);
    const float PrepTableTop = 1.009f;
    // Hovering beside the dining table, facing the counters and the window.
    static readonly Vector3 FlySpawn = new Vector3(-0.4f, 1.4f, -0.6f);
    const float PendantIntensity = 80f;
    static readonly float[] KitchenShelfHeights = { 1.3f, 1.7f };
    static readonly Color KitchenWallColor = new Color(0.93f, 0.87f, 0.76f);
    static readonly Color CabinetColor = new Color(0.56f, 0.66f, 0.55f);
    static readonly Color MetalColor = new Color(0.75f, 0.75f, 0.72f);

    static readonly Prop[] NorthCounterProps =
    {
        new("SM_FoodPot_01", -2.85f, 2.32f), new("SM_FoodPot_04", -2.7f, 2.32f), new("SM_FoodPot_07", -2.55f, 2.32f),
        new("SM_FoodPot_06", -2.4f, 2.32f),
        new("SM_KitchenUstensil_Roll", -1.15f, 2.05f), new("SM_Bottles_01", -0.85f, 2.32f), new("SM_Bottles_03", -0.73f, 2.36f),
        new("SM_Sponge_01", 0.47f, 2.15f, 10f), new("SM_Soap_01", 0.5f, 2.36f), new("SM_Glove", 0.72f, 2.25f, 25f),
        new("SM_FoodPot_10", 1.0f, 2.32f), new("SM_FoodPot_13", 1.14f, 2.32f), new("SM_FoodPot_11", 1.27f, 2.32f),
        new("SM_KitchenUstensil_Whip", 1.45f, 2.08f, 15f), new("SM_Food_SM_Lemon", 1.66f, 2.32f),
    };

    // The microwave faces east, into the room. Everything here stays under 0.55 m: the wall cabinets
    // start at 1.45 m, so the standing ladle (0.65 m) is left out.
    static readonly Prop[] WestCounterProps =
    {
        new("SM_Microwave", -2.72f, 1.35f, 90f), new("SM_Bottles_05", -2.85f, 0.55f), new("SM_Bottles_08", -2.85f, 0.75f), new("SM_BreakfastProps_Bread", -2.65f, 0.25f, 75f),
        new("SM_Food_SM_Onion", -2.62f, -0.12f), new("SM_Food_SM_Garlic", -2.75f, -0.22f),
        new("SM_Food_SM_Carrot_02", -2.6f, -0.4f, 20f),
    };

    static readonly Prop[] KitchenSillProps =
    {
        new("SM_FlowerPot_02", -0.35f, HalfDepth - SillDepth * 0.5f), new("SM_FlowersPlant_01", 0.35f, HalfDepth - SillDepth * 0.5f),
    };

    // Along and across the dining table's long side, from its centre.
    static readonly Prop[] DiningTableProps =
    {
        new("SM_Food_SM_Apple_01", 0.05f, 0.04f), new("SM_Food_SM_Apple_02", -0.06f, 0.1f), new("SM_Food_SM_Orange", 0.13f, -0.05f),
        new("SM_Food_SM_Pear", -0.11f, -0.07f), new("SM_Food_SM_Banana", 0f, 0.19f, 30f), new("SM_Food_SM_Clementine", 0.2f, 0.11f),
        new("SM_Candle", 0.55f, 0f), new("SM_Candle", -0.55f, 0f),
        new("SM_BookOpen_01", -0.85f, 0.25f, 15f), new("SM_Bottles_07", 0.85f, -0.25f),
        new("SM_Food_SM_Waffle", 0.82f, 0.22f, -10f), new("SM_BreakfastProps_Bread", -0.35f, -0.28f, 40f),
    };

    static readonly Prop[] PrepTableProps =
    {
        new("SM_FoodPot_14", 0.55f, -2.15f), new("SM_KitchenUstensil_Roll", 0.95f, -1.6f, 80f),
        new("SM_KitchenUstensil_Spoon", 1.3f, -1.55f, 95f), new("SM_KitchenUstensil_Knife", 1.55f, -1.5f, 100f),
        new("SM_Food_SM_Pepper_01", 1.1f, -2.0f), new("SM_Food_SM_Pepper_02", 1.22f, -2.08f), new("SM_Food_SM_Pepper_03", 1.32f, -1.98f),
        new("SM_Food_SM_Carrot_01", 1.65f, -1.95f, 15f), new("SM_Food_SM_Potato", 1.82f, -1.85f), new("SM_Food_SM_Onion", 1.95f, -2.05f),
        new("SM_FlowerPot_10", 2.12f, -2.15f),
    };

    // One list per shelf, bottom first, matching KitchenShelfHeights; on the kitchen side of the partition.
    static readonly Prop[][] KitchenShelfProps =
    {
        new[] { new Prop("SM_FoodPot_02", 2.88f, 0.25f, -90f), new Prop("SM_FoodPot_03", 2.88f, 0.45f), new Prop("SM_FoodPot_05", 2.88f, 0.62f),
                new Prop("SM_FoodPot_08", 2.88f, 0.82f), new Prop("SM_FoodPot_09", 2.88f, 1.0f) },
        new[] { new Prop("SM_FlowersPlant_03", 2.88f, 0.3f), new Prop("SM_Bottles_02", 2.88f, 0.6f), new Prop("SM_Bottles_04", 2.88f, 0.75f),
                new Prop("SM_FoodPot_12", 2.88f, 1.0f), new Prop("SM_FoodPot_15", 2.88f, 1.25f) },
    };

    void BuildKitchen(Transform parent)
    {
        BuildKitchenShell(Group(parent, "Shell"));
        BuildCounters(Group(parent, "Counters"));
        BuildStove(Group(parent, "Stove"));
        BuildFridgeAndSink(Group(parent, "FridgeAndSink"));
        BuildDiningTable(Group(parent, "Dining"));
        BuildPrepTable(Group(parent, "PrepTable"));
        BuildKitchenShelves(Group(parent, "Shelves"));
    }

    void BuildKitchenShell(Transform parent)
    {
        float t = WallThickness, d = HalfDepth + t;
        Material wall = PaintSurface("KitchenWall", KitchenWallColor);
        Color cream = new Color(0.92f, 0.89f, 0.82f), teal = new Color(0.4f, 0.55f, 0.55f);
        // 40 cm checker tiles: the 2x2 pattern repeats every 80 cm.
        Vector2 tiling = new Vector2(PartitionMiddle - (KitchenWest - t), 2f * d) / 0.8f;
        Material tiles = PaintPattern("KitchenTiles", new[] { cream, teal, teal, cream }, 2, tiling);
        Box(parent, "Floor", new Vector3(KitchenWest - t, -t, -d), new Vector3(PartitionMiddle, 0f, d), tiles);
        Box(parent, "Wall_South", new Vector3(KitchenWest - t, 0f, -d), new Vector3(PartitionMiddle, RoomHeight, -HalfDepth), wall);
        Box(parent, "Wall_West", new Vector3(KitchenWest - t, 0f, -HalfDepth), new Vector3(KitchenWest, RoomHeight, HalfDepth), wall);
        Transform north = WallFrame(parent, "Wall_North", new Vector3(KitchenWest - t, 0f, HalfDepth), 0f);
        // The window is centred over the sink at x = 0.
        BuildWindowWall(north, PartitionMiddle - (KitchenWest - t), -(KitchenWest - t), wall);
        PartitionHalf(parent, PartitionX, PartitionMiddle, wall);
        PlaceProps(Group(parent, "SillProps"), KitchenSillProps, SillHeight);

        Material door = Paint("Door", new Color(0.45f, 0.3f, 0.2f));
        float x = KitchenWest;
        Box(parent, "Door", new Vector3(x, 0f, DoorSouth), new Vector3(x + 0.04f, 2.05f, DoorNorth), door);
        Box(parent, "DoorPanel_Low", new Vector3(x + 0.04f, 0.2f, DoorSouth + 0.12f), new Vector3(x + 0.055f, 0.9f, DoorNorth - 0.12f), door);
        Box(parent, "DoorPanel_High", new Vector3(x + 0.04f, 1.1f, DoorSouth + 0.12f), new Vector3(x + 0.055f, 1.85f, DoorNorth - 0.12f), door);
        Box(parent, "DoorKnob", new Vector3(x + 0.04f, 0.95f, DoorNorth - 0.12f), new Vector3(x + 0.1f, 1f, DoorNorth - 0.07f), Paint("Metal", MetalColor, 0.8f));

        // Counter-clockwise from the south-west corner; baseboards hidden behind counters are skipped.
        Baseboard(parent, new Vector3(KitchenWest, 0f, -HalfDepth), new Vector3(PartitionX, 0f, -HalfDepth));
        Baseboard(parent, new Vector3(PartitionX, 0f, -HalfDepth), new Vector3(PartitionX, 0f, DoorwaySouth - TrimWidth));
        Baseboard(parent, new Vector3(PartitionX, 0f, DoorwayNorth + TrimWidth), new Vector3(PartitionX, 0f, FridgeFront));
        Baseboard(parent, new Vector3(KitchenWest, 0f, -0.8f), new Vector3(KitchenWest, 0f, DoorNorth));
        Baseboard(parent, new Vector3(KitchenWest, 0f, DoorSouth), new Vector3(KitchenWest, 0f, -HalfDepth));

        Transform pictures = Group(parent, "Pictures");
        HangPicture(pictures, "SM_Frames_02", new Vector3(1.6f, 1.4f, -HalfDepth), 0f);
        HangPicture(pictures, "SM_Frames_06", new Vector3(0.6f, 1.5f, -HalfDepth), 0f);
    }

    void BuildCounters(Transform parent)
    {
        Counter(parent, "Counter_North_A", new Vector3(KitchenWest, 0f, CounterFront), new Vector3(StoveWest, 0f, HalfDepth), Vector3.back);
        Counter(parent, "Counter_North_B", new Vector3(StoveEast, 0f, CounterFront), new Vector3(NorthCounterEast, 0f, HalfDepth), Vector3.back);
        Counter(parent, "Counter_West", new Vector3(KitchenWest, 0f, -0.8f), new Vector3(WestCounterFront, 0f, CounterFront), Vector3.right);

        Material cabinet = Paint("Cabinet", CabinetColor);
        float back = HalfDepth, west = KitchenWest;
        Box(parent, "UpperCabinet_North", new Vector3(west, UpperCabinetBottom, back - UpperCabinetDepth), new Vector3(StoveWest - 0.05f, UpperCabinetTop, back), cabinet);
        Box(parent, "UpperCabinet_West", new Vector3(west, UpperCabinetBottom, -0.8f), new Vector3(west + UpperCabinetDepth, UpperCabinetTop, back - UpperCabinetDepth), cabinet);

        Transform props = Group(parent, "Props");
        PlaceProps(props, NorthCounterProps, CounterHeight);
        PlaceProps(props, WestCounterProps, CounterHeight);
    }

    // A run of base cabinets with an overhanging worktop, a dark plinth and a handle every 0.5 m
    // along the side that faces `front`. min and max are the footprint corners; their y is ignored.
    // Five parameters for the same reason as Box.
    void Counter(Transform parent, string name, Vector3 min, Vector3 max, Vector3 front)
    {
        Transform run = Group(parent, name);
        // The plinth is set back 6 cm from the doors, like a real toe kick.
        Vector3 recess = -front * 0.06f;
        Box(run, "Plinth", new Vector3(min.x, 0f, min.z) + Vector3.Max(recess, Vector3.zero), new Vector3(max.x, 0.1f, max.z) + Vector3.Min(recess, Vector3.zero), Paint("Plinth", new Color(0.25f, 0.27f, 0.25f)));
        Box(run, "Cabinets", new Vector3(min.x, 0.1f, min.z), new Vector3(max.x, CounterHeight - 0.04f, max.z), Paint("Cabinet", CabinetColor));
        Vector3 overhang = front * 0.02f;
        Vector3 topMin = new Vector3(min.x, CounterHeight - 0.04f, min.z) + Vector3.Min(overhang, Vector3.zero);
        Vector3 topMax = new Vector3(max.x, CounterHeight, max.z) + Vector3.Max(overhang, Vector3.zero);
        Box(run, "Worktop", topMin, topMax, Paint("Worktop", new Color(0.92f, 0.9f, 0.86f), 0.5f));

        Vector3 along = new Vector3(Mathf.Abs(front.z), 0f, Mathf.Abs(front.x));
        float length = Vector3.Dot(max - min, along);
        Vector3 faceCenter = (min + max) * 0.5f + Vector3.Scale(front, (max - min) * 0.5f);
        faceCenter.y = CounterHeight - 0.12f;
        Vector3 handleSize = along * 0.12f + Vector3.up * 0.02f + new Vector3(Mathf.Abs(front.x), 0f, Mathf.Abs(front.z)) * 0.03f;
        Material metal = Paint("Metal", MetalColor, 0.8f);
        for (float s = 0.25f; s < length; s += 0.5f)
        {
            Vector3 center = faceCenter + along * (s - length * 0.5f) + front * 0.015f;
            Box(run, "Handle", center - handleSize * 0.5f, center + handleSize * 0.5f, metal);
        }
    }

    void BuildStove(Transform parent)
    {
        float front = CounterFront, back = HalfDepth;
        Material dark = Paint("StoveBody", new Color(0.18f, 0.18f, 0.19f), 0.6f);
        Material black = Paint("Burner", new Color(0.05f, 0.05f, 0.05f), 0.3f);
        Box(parent, "Body", new Vector3(StoveWest, 0f, front), new Vector3(StoveEast, CounterHeight, back), dark);
        Box(parent, "OvenDoor", new Vector3(StoveWest + 0.05f, 0.12f, front - 0.01f), new Vector3(StoveEast - 0.05f, 0.7f, front), black);
        Box(parent, "OvenHandle", new Vector3(StoveWest + 0.08f, 0.74f, front - 0.04f), new Vector3(StoveEast - 0.08f, 0.76f, front - 0.01f), Paint("Metal", MetalColor, 0.8f));
        foreach (float x in new[] { StoveWest + 0.15f, StoveEast - 0.15f })
        {
            foreach (float z in new[] { front + 0.15f, back - 0.15f })
                Cylinder(parent, "Burner", new Vector3(x, CounterHeight, z), new Vector3(0.18f, 0.01f, 0.18f), black);
        }

        Material metal = Paint("Metal", MetalColor, 0.8f);
        Box(parent, "Hood", new Vector3(StoveWest - 0.05f, 1.6f, back - 0.5f), new Vector3(StoveEast + 0.05f, 1.75f, back), metal);
        Box(parent, "HoodChimney", new Vector3(StoveWest + 0.15f, 1.75f, back - 0.3f), new Vector3(StoveEast - 0.15f, RoomHeight, back), metal);
        // On the back-left burner with its handle over the counter to the west, out of the fly's way.
        PlaceModel(parent, "SM_SaucePan_Black_Pan_01", new Vector3(StoveWest + 0.15f, CounterHeight + 0.01f, back - 0.15f), 0f);
    }

    void BuildFridgeAndSink(Transform parent)
    {
        float front = CounterFront, back = HalfDepth;
        Material metal = Paint("Metal", MetalColor, 0.8f);
        Box(parent, "Sink", new Vector3(-0.35f, CounterHeight, front + 0.1f), new Vector3(0.35f, CounterHeight + 0.005f, back - 0.1f), metal);
        Box(parent, "Faucet", new Vector3(-0.02f, CounterHeight, back - 0.08f), new Vector3(0.02f, CounterHeight + 0.3f, back - 0.04f), metal);
        Box(parent, "FaucetSpout", new Vector3(-0.02f, CounterHeight + 0.26f, back - 0.25f), new Vector3(0.02f, CounterHeight + 0.3f, back - 0.08f), metal);

        Material white = Paint("Fridge", new Color(0.95f, 0.95f, 0.93f), 0.6f);
        Material seam = Paint("Seam", new Color(0.2f, 0.2f, 0.2f));
        Box(parent, "Fridge", new Vector3(FridgeWest, 0f, FridgeFront), new Vector3(PartitionX, 1.85f, back), white);
        Box(parent, "FridgeDoorSplit", new Vector3(FridgeWest, 1.18f, FridgeFront - 0.01f), new Vector3(PartitionX, 1.2f, FridgeFront), seam);
        Box(parent, "FridgeHandle_Low", new Vector3(FridgeWest + 0.08f, 0.5f, FridgeFront - 0.05f), new Vector3(FridgeWest + 0.11f, 1.1f, FridgeFront - 0.01f), metal);
        Box(parent, "FridgeHandle_High", new Vector3(FridgeWest + 0.08f, 1.28f, FridgeFront - 0.05f), new Vector3(FridgeWest + 0.11f, 1.7f, FridgeFront - 0.01f), metal);
        PlaceModel(parent, "SM_FlowerPot_05", new Vector3(2.6f, 1.85f, 2.15f), 0f);
    }

    void BuildDiningTable(Transform parent)
    {
        GameObject table = PlaceModel(parent, "SM_MainTable", DiningTableSpot, 0f);
        if (table == null)
            return;
        Bounds bounds = LocalBounds(table);
        // The table is longer north-south; chairs and props follow its measured bounds.
        Vector3 center = new Vector3(bounds.center.x, 0f, bounds.center.z);
        float acrossOffset = bounds.extents.x + 0.22f;
        foreach (float along in new[] { -0.7f, 0f, 0.7f })
        {
            Chair(parent, center + new Vector3(-acrossOffset, 0f, along), Vector3.right);
            Chair(parent, center + new Vector3(acrossOffset, 0f, along), Vector3.left);
        }

        Transform props = Group(parent, "Props");
        foreach (Prop prop in DiningTableProps)
            PlaceModel(props, prop.Model, center + new Vector3(prop.Z, bounds.max.y, prop.X), prop.Facing);

        // Two pendant lamps over the table.
        Material cord = Paint("Seam", new Color(0.2f, 0.2f, 0.2f));
        Material shade = PaintGlow("PendantShade", new Color(0.98f, 0.8f, 0.45f));
        foreach (float along in new[] { -0.55f, 0.55f })
        {
            Vector3 spot = center + Vector3.forward * along;
            Box(parent, "PendantCord", spot + new Vector3(-0.005f, RoomHeight - 0.75f, -0.005f), spot + new Vector3(0.005f, RoomHeight, 0.005f), cord);
            Cylinder(parent, "PendantShade", spot + Vector3.up * (RoomHeight - 0.95f), new Vector3(0.36f, 0.2f, 0.36f), shade);
            Light pendant = NewLight(parent, "PendantLight", spot + Vector3.up * (RoomHeight - 1f), LightType.Point);
            pendant.range = 5f * WorldScale;
            pendant.intensity = PendantIntensity;
        }
    }

    // facing is the direction someone sitting on the chair would look.
    void Chair(Transform parent, Vector3 position, Vector3 facing)
    {
        Transform chair = Group(parent, "Chair");
        chair.localPosition = position;
        chair.localRotation = Quaternion.LookRotation(facing);
        Material wood = Paint("Wood", new Color(0.58f, 0.4f, 0.26f), 0.3f);
        Box(chair, "Seat", new Vector3(-0.21f, 0.43f, -0.2f), new Vector3(0.21f, 0.47f, 0.2f), wood);
        Box(chair, "Back", new Vector3(-0.21f, 0.47f, -0.2f), new Vector3(0.21f, 0.95f, -0.17f), wood);
        Box(chair, "BackRail", new Vector3(-0.21f, 0.62f, -0.2f), new Vector3(0.21f, 0.66f, -0.15f), wood);
        foreach (float x in new[] { -0.19f, 0.16f })
        {
            foreach (float z in new[] { -0.18f, 0.15f })
                Box(chair, "Leg", new Vector3(x, 0f, z), new Vector3(x + 0.03f, 0.43f, z + 0.03f), wood);
        }
    }

    void BuildPrepTable(Transform parent)
    {
        // 90 degrees turns its long side east-west, along the south wall.
        PlaceModel(parent, "SM_KitchenTable", PrepTableSpot, 90f);
        PlaceProps(Group(parent, "Props"), PrepTableProps, PrepTableTop);
    }

    void BuildKitchenShelves(Transform parent)
    {
        Material wood = Paint("ShelfWood", new Color(0.62f, 0.45f, 0.3f));
        Transform props = Group(parent, "Props");
        for (int i = 0; i < KitchenShelfHeights.Length; i++)
        {
            float y = KitchenShelfHeights[i];
            Box(parent, "Shelf", new Vector3(PartitionX - 0.25f, y - 0.03f, 0.1f), new Vector3(PartitionX, y, 1.5f), wood);
            PlaceProps(props, KitchenShelfProps[i], y);
        }
        // Cookbooks lying in a pile at the end of the lower shelf.
        PlaceStack(props, new[] { "SM_Book_04", "SM_Book_05", "SM_Book_06" }, new Vector3(2.86f, KitchenShelfHeights[0], 1.28f), 90f);
    }
}
