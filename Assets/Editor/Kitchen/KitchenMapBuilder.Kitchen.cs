using UnityEngine;

// The kitchen, the north-west room: an L of counters along the north and west walls with the stove,
// the sink under the window and the microwave, the fridge by the pantry doorway, and a free-standing
// island in the middle that holds the kitchen's dish. The island has open floor on its south, west
// and east sides, so the old man can stand right at the dish from more than one side.
public partial class KitchenMapBuilder
{
    const float CounterHeight = 0.9f;
    const float NorthCounterFront = 2.9f;
    const float NorthCounterEast = -3f;
    const float WestCounterFront = HouseWest + 0.6f;
    const float StoveWest = -6f;
    const float StoveEast = -5.4f;
    const float SinkWest = -4.6f;
    const float SinkEast = -4f;
    const float FridgeWest = -2.85f;
    const float FridgeFront = 2.7f;
    const float FridgeTop = 1.85f;
    const float UpperCabinetBottom = 1.45f;
    const float UpperCabinetTop = 2.15f;
    const float UpperCabinetFront = 3.15f;
    // The upper cabinets stop short of the hood and of the window trim, so there are two of them.
    const float UpperCabinetWestEnd = StoveWest - 0.1f;
    const float UpperCabinetEastStart = KitchenWindowX + WindowHalfWidth + TrimWidth + 0.03f;
    const float IslandWest = -5f;
    const float IslandEast = -3.6f;
    const float IslandSouth = 1.5f;
    const float IslandNorth = 2.3f;
    static readonly Color CabinetColor = new Color(0.56f, 0.66f, 0.55f);
    static readonly Color MetalColor = new Color(0.75f, 0.75f, 0.72f);
    static readonly Color PlinthColor = new Color(0.25f, 0.27f, 0.25f);
    static readonly Color WorktopColor = new Color(0.92f, 0.9f, 0.86f);

    // Tall ones stay back from the window: its sill reaches 0.2 m out over the back of the counter.
    static readonly Prop[] NorthCounterThrowables =
    {
        new("SM_FoodPot_01", -6.85f, 3.3f), new("SM_FoodPot_04", -6.68f, 3.3f),
        new("SM_Bottles_01", -5.27f, 3.25f), new("SM_KitchenUstensil_Roll", -4.9f, 3.05f),
        new("SM_Sponge_01", -3.88f, 3.12f, 10f), new("SM_Glove", -3.62f, 3.3f, 25f), new("SM_KitchenUstensil_Whip", -3.35f, 3.02f, 15f),
    };

    // Clear of the microwave, which stands in the middle of the west counter.
    static readonly Prop[] WestCounterThrowables =
    {
        new("SM_Bottles_05", -6.85f, 0.85f), new("SM_BreakfastProps_Bread", -6.65f, 2.35f, 75f), new("SM_Food_SM_Onion", -6.62f, 2.65f),
    };

    static readonly Prop[] KitchenSillProps =
    {
        new("SM_FlowerPot_02", KitchenWindowX - 0.35f, HouseNorth - SillDepth * 0.5f), new("SM_FlowersPlant_01", KitchenWindowX + 0.35f, HouseNorth - SillDepth * 0.5f),
    };

    void BuildKitchen(Transform parent)
    {
        BuildCounters(Group(parent, "Counters"));
        BuildStove(Group(parent, "Stove"));
        BuildFridgeAndSink(Group(parent, "FridgeAndSink"));
        BuildIsland(Group(parent, "Island"));

        Transform decor = Group(parent, "Decor");
        PlaceProps(decor, KitchenSillProps, SillHeight);
        // On the hub wall, facing north into the kitchen.
        HangPicture(decor, "SM_Frames_02", new Vector3(-5.5f, 1.4f, NorthRoomsSouth), 0f);
        CeilingLamp(parent, -3f, 1.3f);
    }

    void BuildCounters(Transform parent)
    {
        Counter(parent, "Counter_North_West", new Vector3(HouseWest, 0f, NorthCounterFront), new Vector3(StoveWest, 0f, HouseNorth), Vector3.back);
        Counter(parent, "Counter_North_East", new Vector3(StoveEast, 0f, NorthCounterFront), new Vector3(NorthCounterEast, 0f, HouseNorth), Vector3.back);
        Counter(parent, "Counter_West", new Vector3(HouseWest, 0f, NorthRoomsSouth), new Vector3(WestCounterFront, 0f, NorthCounterFront), Vector3.right);

        Material cabinet = Paint("Cabinet", CabinetColor);
        Box(parent, "UpperCabinet_West", new Vector3(HouseWest, UpperCabinetBottom, UpperCabinetFront), new Vector3(UpperCabinetWestEnd, UpperCabinetTop, HouseNorth), cabinet);
        Box(parent, "UpperCabinet_East", new Vector3(UpperCabinetEastStart, UpperCabinetBottom, UpperCabinetFront), new Vector3(NorthCounterEast, UpperCabinetTop, HouseNorth), cabinet);

        Transform throwables = Group(parent, ThrowablesGroupName);
        PlaceProps(throwables, NorthCounterThrowables, CounterHeight);
        PlaceProps(throwables, WestCounterThrowables, CounterHeight);
        // Facing east, into the room. Too big to throw, so it stays outside the Throwables group.
        PlaceModel(parent, "SM_Microwave", new Vector3(-6.7f, CounterHeight, 1.6f), 90f);
    }

    // A run of base cabinets with an overhanging worktop, a dark plinth and a handle every 0.5 m along
    // the side that faces `front`. min and max are the footprint corners; their y is ignored.
    void Counter(Transform parent, string name, Vector3 min, Vector3 max, Vector3 front)
    {
        Transform run = Group(parent, name);
        // The plinth is set back 6 cm from the doors, like a real toe kick.
        Vector3 recess = -front * 0.06f;
        Box(run, "Plinth", new Vector3(min.x, 0f, min.z) + Vector3.Max(recess, Vector3.zero), new Vector3(max.x, 0.1f, max.z) + Vector3.Min(recess, Vector3.zero), Paint("Plinth", PlinthColor));
        Box(run, "Cabinets", new Vector3(min.x, 0.1f, min.z), new Vector3(max.x, CounterHeight - 0.04f, max.z), Paint("Cabinet", CabinetColor));
        Vector3 overhang = front * 0.02f;
        Vector3 topMin = new Vector3(min.x, CounterHeight - 0.04f, min.z) + Vector3.Min(overhang, Vector3.zero);
        Vector3 topMax = new Vector3(max.x, CounterHeight, max.z) + Vector3.Max(overhang, Vector3.zero);
        Box(run, "Worktop", topMin, topMax, Paint("Worktop", WorktopColor, 0.5f));

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
        float front = NorthCounterFront, back = HouseNorth;
        Material dark = Paint("StoveBody", new Color(0.18f, 0.18f, 0.19f), 0.6f);
        Material black = Paint("Burner", new Color(0.05f, 0.05f, 0.05f), 0.3f);
        Material metal = Paint("Metal", MetalColor, 0.8f);
        Box(parent, "Body", new Vector3(StoveWest, 0f, front), new Vector3(StoveEast, CounterHeight, back), dark);
        Box(parent, "OvenDoor", new Vector3(StoveWest + 0.05f, 0.12f, front - 0.01f), new Vector3(StoveEast - 0.05f, 0.7f, front), black);
        Box(parent, "OvenHandle", new Vector3(StoveWest + 0.08f, 0.74f, front - 0.04f), new Vector3(StoveEast - 0.08f, 0.76f, front - 0.01f), metal);
        foreach (float x in new[] { StoveWest + 0.15f, StoveEast - 0.15f })
        {
            foreach (float z in new[] { front + 0.15f, back - 0.15f })
                Cylinder(parent, "Burner", new Vector3(x, CounterHeight, z), new Vector3(0.18f, 0.01f, 0.18f), black);
        }

        Box(parent, "Hood", new Vector3(StoveWest - 0.05f, 1.6f, back - 0.5f), new Vector3(StoveEast + 0.05f, 1.75f, back), metal);
        Box(parent, "HoodChimney", new Vector3(StoveWest + 0.15f, 1.75f, back - 0.3f), new Vector3(StoveEast - 0.15f, RoomHeight, back), metal);
        // On the back-left burner with its handle over the counter to the west, out of the fly's way.
        PlaceModel(parent, "SM_SaucePan_Black_Pan_01", new Vector3(StoveWest + 0.15f, CounterHeight + 0.01f, back - 0.15f), 0f);
    }

    void BuildFridgeAndSink(Transform parent)
    {
        float back = HouseNorth, sinkMiddle = (SinkWest + SinkEast) * 0.5f;
        Material metal = Paint("Metal", MetalColor, 0.8f);
        Box(parent, "Sink", new Vector3(SinkWest, CounterHeight, NorthCounterFront + 0.1f), new Vector3(SinkEast, CounterHeight + 0.005f, back - 0.1f), metal);
        // A low faucet: the window sill is only 15 cm above the worktop, and a tall one would go through it.
        float faucetTop = SillHeight - 0.05f;
        Box(parent, "Faucet", new Vector3(sinkMiddle - 0.02f, CounterHeight, back - 0.08f), new Vector3(sinkMiddle + 0.02f, faucetTop, back - 0.04f), metal);
        Box(parent, "FaucetSpout", new Vector3(sinkMiddle - 0.02f, faucetTop - 0.04f, back - 0.25f), new Vector3(sinkMiddle + 0.02f, faucetTop, back - 0.08f), metal);

        Material white = Paint("Fridge", new Color(0.95f, 0.95f, 0.93f), 0.6f);
        Material seam = Paint("Seam", SeamColor);
        Box(parent, "Fridge", new Vector3(FridgeWest, 0f, FridgeFront), new Vector3(KitchenEast, FridgeTop, back), white);
        Box(parent, "FridgeDoorSplit", new Vector3(FridgeWest, 1.18f, FridgeFront - 0.01f), new Vector3(KitchenEast, 1.2f, FridgeFront), seam);
        Box(parent, "FridgeHandle_Low", new Vector3(FridgeWest + 0.08f, 0.5f, FridgeFront - 0.05f), new Vector3(FridgeWest + 0.11f, 1.1f, FridgeFront - 0.01f), metal);
        Box(parent, "FridgeHandle_High", new Vector3(FridgeWest + 0.08f, 1.28f, FridgeFront - 0.05f), new Vector3(FridgeWest + 0.11f, 1.7f, FridgeFront - 0.01f), metal);
        PlaceModel(parent, "SM_FlowerPot_05", new Vector3((FridgeWest + KitchenEast) * 0.5f, FridgeTop, 3.1f), 0f);
    }

    // A solid block of cupboards with a worktop all round, at counter height. Its top is kept clear
    // for the dish, and a pendant hangs over it.
    void BuildIsland(Transform parent)
    {
        Vector3 min = new Vector3(IslandWest, 0f, IslandSouth), max = new Vector3(IslandEast, 0f, IslandNorth);
        Vector3 doorInset = new Vector3(0.03f, 0f, 0.03f), toeKick = new Vector3(0.09f, 0f, 0.09f);
        Box(parent, "Plinth", min + toeKick, max - toeKick + Vector3.up * 0.1f, Paint("Plinth", PlinthColor));
        Box(parent, "Cabinets", min + doorInset + Vector3.up * 0.1f, max - doorInset + Vector3.up * (CounterHeight - 0.04f), Paint("Cabinet", CabinetColor));
        Box(parent, "Worktop", min + Vector3.up * (CounterHeight - 0.04f), max + Vector3.up * CounterHeight, Paint("Worktop", WorktopColor, 0.5f));

        Material metal = Paint("Metal", MetalColor, 0.8f);
        float doorFront = IslandSouth + 0.03f;
        foreach (float x in new[] { -4.65f, -3.95f })
            Box(parent, "Handle", new Vector3(x - 0.06f, CounterHeight - 0.13f, doorFront - 0.03f), new Vector3(x + 0.06f, CounterHeight - 0.11f, doorFront), metal);
        Pendant(parent, (IslandWest + IslandEast) * 0.5f, (IslandSouth + IslandNorth) * 0.5f);
    }
}
