using UnityEngine;

// The hub, the dining room that runs the width of the house along the south wall, and the markers.
// The old man starts in the middle of it, with a clear 2 m round him and a clear walk to each of the
// three doorways north. The dining table and its chairs fill the west end, a sideboard stands under
// the east window, and a pot plant stands in each corner.
public partial class KitchenMapBuilder
{
    // Long side east-west, top 0.76 m, open underneath.
    const float DiningTableWest = -5f;
    const float DiningTableEast = -3f;
    const float DiningTableSouth = -2.3f;
    const float DiningTableNorth = -1.3f;
    const float DiningTableTop = 0.76f;
    const float SideboardWest = 2f;
    const float SideboardEast = 5f;
    const float SideboardFront = -3f;
    const float SideboardTop = 0.9f;
    // A chair's middle is this far out from the table edge, so the seat just clears it.
    const float ChairOffset = 0.22f;
    // Kept off the window sills, which reach 0.2 m into the room from the south wall.
    const float SideboardPropZ = -3.2f;

    // The markers the match wiring reads by name: where the players start and where the dishes go.
    // Positions in house metres; yaw is the compass direction the marker faces.
    static readonly Vector3 HumanSpawn = new Vector3(0f, 0f, -1f);
    // High (above the old man's 2.72 m slap) and in the far corners, away from every dish.
    static readonly Vector3 FirstFlySpawn = new Vector3(-6.3f, 3.3f, -3f);
    static readonly (string name, Vector3 position, float yaw)[] MarkerSpots =
    {
        ("HumanSpawn", HumanSpawn, 0f),
        ("FlySpawn_1", FirstFlySpawn, 45f),
        ("FlySpawn_2", new Vector3(6.3f, 3.3f, -3f), -45f),
        ("FlySpawn_3", new Vector3(-6.5f, 3.3f, 3f), 135f),
        ("FlySpawn_4", new Vector3(6.5f, 3.3f, 3f), -135f),
        // On the middle of the kitchen island and of the pantry table, at their tops.
        ("DishSpot_Kitchen", new Vector3((IslandWest + IslandEast) * 0.5f, CounterHeight, (IslandSouth + IslandNorth) * 0.5f), 0f),
        ("DishSpot_Pantry", new Vector3(0f, PantryTableTop, (PantryTableSouth + PantryTableNorth) * 0.5f), 0f),
    };

    // Along (x) and across (z) the dining table from its middle.
    static readonly Prop[] DiningTableThrowables =
    {
        new("SM_Food_SM_Apple_01", 0.04f, 0.05f), new("SM_Food_SM_Apple_02", 0.1f, -0.06f), new("SM_Food_SM_Orange", -0.05f, 0.13f),
        new("SM_Food_SM_Pear", -0.07f, -0.11f), new("SM_Food_SM_Banana", 0.19f, 0f, 30f), new("SM_Food_SM_Clementine", 0.11f, 0.2f),
        new("SM_Candle", 0.55f, 0f), new("SM_BreakfastProps_Bread", -0.35f, -0.28f, 40f),
        new("SM_Food_SM_Waffle", 0.75f, 0.22f, -10f), new("SM_Bottles_07", 0.8f, -0.25f),
    };

    // Z is unused: everything stands at SideboardPropZ.
    static readonly Prop[] SideboardThrowables =
    {
        new("SM_Candle", 2.75f, 0f), new("SM_BookOpen_01", 3.25f, 0f, 10f),
        new("SM_Bottles_02", 4.6f, 0f), new("SM_Bottles_06", 4.78f, 0f),
    };

    void BuildHub(Transform parent)
    {
        BuildDining(Group(parent, "Dining"));
        BuildSideboard(Group(parent, "Sideboard"));
        BuildHubDecor(Group(parent, "Decor"));
        CeilingLamp(parent, 3.5f, -1.5f);
    }

    void BuildDining(Transform parent)
    {
        Vector3 center = new Vector3((DiningTableWest + DiningTableEast) * 0.5f, 0f, (DiningTableSouth + DiningTableNorth) * 0.5f);
        // On the floor under the whole table and its chairs, long side east-west.
        PlaceModel(parent, "SM_Carpet_02", center, 90f);
        BuildDiningTable(parent);

        foreach (float x in new[] { center.x - 0.5f, center.x + 0.5f })
        {
            Chair(parent, new Vector3(x, 0f, DiningTableSouth - ChairOffset), Vector3.forward);
            Chair(parent, new Vector3(x, 0f, DiningTableNorth + ChairOffset), Vector3.back);
        }
        Chair(parent, new Vector3(DiningTableWest - ChairOffset, 0f, center.z), Vector3.right);
        Chair(parent, new Vector3(DiningTableEast + ChairOffset, 0f, center.z), Vector3.left);

        Transform throwables = Group(parent, ThrowablesGroupName);
        foreach (Prop prop in DiningTableThrowables)
            PlaceModel(throwables, prop.Model, center + new Vector3(prop.X, DiningTableTop, prop.Z), prop.Facing);

        Pendant(parent, center.x - 0.5f, center.z);
        Pendant(parent, center.x + 0.5f, center.z);
    }

    // A top on four legs, open underneath so a fly can hide under it and the old man has to stoop.
    void BuildDiningTable(Transform parent)
    {
        Material wood = Paint("Wood", new Color(0.58f, 0.4f, 0.26f), 0.3f);
        Box(parent, "TableTop", new Vector3(DiningTableWest, DiningTableTop - 0.04f, DiningTableSouth), new Vector3(DiningTableEast, DiningTableTop, DiningTableNorth), wood);
        Box(parent, "TableApron", new Vector3(DiningTableWest + 0.05f, DiningTableTop - 0.12f, DiningTableSouth + 0.05f),
            new Vector3(DiningTableEast - 0.05f, DiningTableTop - 0.04f, DiningTableNorth - 0.05f), wood);
        foreach (float x in new[] { DiningTableWest + 0.05f, DiningTableEast - 0.11f })
        {
            foreach (float z in new[] { DiningTableSouth + 0.05f, DiningTableNorth - 0.11f })
                Box(parent, "TableLeg", new Vector3(x, 0f, z), new Vector3(x + 0.06f, DiningTableTop - 0.04f, z + 0.06f), wood);
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

    // A low cupboard against the south wall under the east window, with three doors.
    void BuildSideboard(Transform parent)
    {
        Material wood = Paint("SideboardWood", new Color(0.45f, 0.3f, 0.2f), 0.3f);
        float back = HouseSouth;
        Box(parent, "Plinth", new Vector3(SideboardWest + 0.04f, 0f, back), new Vector3(SideboardEast - 0.04f, 0.1f, SideboardFront - 0.04f), Paint("Plinth", new Color(0.25f, 0.27f, 0.25f)));
        Box(parent, "Body", new Vector3(SideboardWest, 0.1f, back), new Vector3(SideboardEast, SideboardTop - 0.04f, SideboardFront), wood);
        Box(parent, "Top", new Vector3(SideboardWest - 0.02f, SideboardTop - 0.04f, back), new Vector3(SideboardEast + 0.02f, SideboardTop, SideboardFront + 0.02f), wood);
        Material seam = Paint("Seam", SeamColor), metal = Paint("Metal", MetalColor, 0.8f);
        float doorWidth = (SideboardEast - SideboardWest) / 3f;
        for (int i = 0; i < 3; i++)
        {
            float x = SideboardWest + doorWidth * (i + 0.5f);
            if (i > 0)
                Box(parent, "DoorSeam", new Vector3(x - doorWidth * 0.5f - 0.005f, 0.12f, SideboardFront), new Vector3(x - doorWidth * 0.5f + 0.005f, SideboardTop - 0.06f, SideboardFront + 0.005f), seam);
            Box(parent, "Knob", new Vector3(x - 0.02f, 0.62f, SideboardFront), new Vector3(x + 0.02f, 0.66f, SideboardFront + 0.03f), metal);
        }

        Transform throwables = Group(parent, ThrowablesGroupName);
        foreach (Prop prop in SideboardThrowables)
            PlaceModel(throwables, prop.Model, new Vector3(prop.X, SideboardTop, SideboardPropZ), prop.Facing);
        PlaceStack(throwables, new[] { "SM_Book_01", "SM_Book_03" }, new Vector3(2.3f, SideboardTop, SideboardPropZ), 0f);
    }

    void BuildHubDecor(Transform parent)
    {
        // The corner plants have no collision hull in the pack, so they keep an exact mesh collider.
        Transform plants = Group(parent, "Plants");
        foreach (float x in new[] { HouseWest + 0.2f, HouseEast - 0.2f })
        {
            foreach (float z in new[] { HouseSouth + 0.2f, HubNorth - 0.2f })
                PlaceModel(plants, "SM_FlowersPlant_01", new Vector3(x, 0f, z), 0f);
        }

        // On the hub wall between its doorways, facing south, and on the south wall between the windows.
        Transform pictures = Group(parent, "Pictures");
        HangPicture(pictures, "SM_Frames_01", new Vector3(-5.2f, 1.4f, HubNorth), 180f);
        HangPicture(pictures, "SM_Frames_06", new Vector3(-1.4f, 1.5f, HubNorth), 180f);
        HangPicture(pictures, "SM_Frames_03", new Vector3(1.4f, 1.5f, HubNorth), 180f);
        HangPicture(pictures, "SM_Frames_02", new Vector3(5.2f, 1.4f, HubNorth), 180f);
        HangPicture(pictures, "SM_Frames_04", new Vector3(-1f, 1.4f, HouseSouth), 0f);
        HangPicture(pictures, "SM_Frames_05", new Vector3(1f, 1.4f, HouseSouth), 0f);
    }

    // Empty objects, so they can be found by name and moved by hand without touching the house.
    void BuildMarkers(Transform parent)
    {
        foreach ((string name, Vector3 position, float yaw) in MarkerSpots)
        {
            Transform marker = Group(parent, name);
            marker.localPosition = position;
            marker.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }
    }
}
