using UnityEngine;

// The pantry, the middle north room, between the kitchen and the living room: tall shelving full of
// jars along the north wall, sacks and crates in the south corners, and a table in the middle that
// holds the pantry's dish. The table stands clear of the shelves on its north side and has open
// floor on its other three, so the old man can guard the dish from the doorway side.
public partial class KitchenMapBuilder
{
    const float PantryTableHalfWidth = 0.6f;
    const float PantryTableSouth = 2.25f;
    const float PantryTableNorth = 2.95f;
    const float PantryTableTop = 0.9f;
    const float PantryShelfFront = 3.15f;
    const float PantryShelfHalfWidth = 1.85f;
    const float PantryShelfBoard = 0.03f;
    // Top surface of each board, bottom first. The old man can reach up to the fourth (1.9 m), so what
    // stands on those is throwable; the top board is out of his reach and its jars stay put.
    static readonly float[] PantryShelfLevels = { 0.4f, 0.9f, 1.4f, 1.9f, 2.4f };
    const float PantryShelfTop = 2.45f;
    const int PantryReachableBoards = 4;

    // One list per board, bottom first, matching PantryShelfLevels. Z is unused: everything stands in
    // the middle of the shelf's depth. Nothing stands on the middle upright at x = 0.
    static readonly Prop[][] PantryShelfProps =
    {
        new[] { new Prop("SM_FoodPot_14", -1.3f, 0f), new Prop("SM_BreakfastProps_Bread", 0.5f, 0f, 20f) },
        new[] { new Prop("SM_FoodPot_02", -1f, 0f), new Prop("SM_FoodPot_03", 0.25f, 0f), new Prop("SM_Bottles_02", 1.3f, 0f) },
        new[] { new Prop("SM_FoodPot_05", -1.4f, 0f), new Prop("SM_FoodPot_09", -0.35f, 0f), new Prop("SM_Bottles_04", 0.9f, 0f) },
        new[] { new Prop("SM_FoodPot_12", -0.8f, 0f), new Prop("SM_FoodPot_15", 1.1f, 0f) },
        new[] { new Prop("SM_FoodPot_13", -1f, 0f), new Prop("SM_FoodPot_11", 0.3f, 0f), new Prop("SM_Bottles_06", 1.2f, 0f) },
    };

    void BuildPantry(Transform parent)
    {
        BuildPantryTable(Group(parent, "Table"));
        BuildPantryShelving(Group(parent, "Shelving"));
        BuildPantryStores(Group(parent, "Stores"));
        // On the hub wall, facing north into the pantry, beside the doorway.
        HangPicture(parent, "SM_Frames_06", new Vector3(1.2f, 1.3f, NorthRoomsSouth), 0f);
        CeilingLamp(parent, 0f, 1.6f);
    }

    // A top on four legs, open underneath. Its top is kept clear for the dish.
    void BuildPantryTable(Transform parent)
    {
        Material wood = Paint("PantryWood", new Color(0.68f, 0.52f, 0.36f), 0.3f);
        float west = -PantryTableHalfWidth, east = PantryTableHalfWidth;
        Box(parent, "TableTop", new Vector3(west, PantryTableTop - 0.04f, PantryTableSouth), new Vector3(east, PantryTableTop, PantryTableNorth), wood);
        foreach (float x in new[] { west + 0.04f, east - 0.09f })
        {
            foreach (float z in new[] { PantryTableSouth + 0.04f, PantryTableNorth - 0.09f })
                Box(parent, "TableLeg", new Vector3(x, 0f, z), new Vector3(x + 0.05f, PantryTableTop - 0.04f, z + 0.05f), wood);
        }
    }

    void BuildPantryShelving(Transform parent)
    {
        Material wood = Paint("ShelfWood", new Color(0.62f, 0.45f, 0.3f));
        float west = -PantryShelfHalfWidth, east = PantryShelfHalfWidth, back = HouseNorth, b = PantryShelfBoard;
        Box(parent, "Back", new Vector3(west, 0f, back - 0.015f), new Vector3(east, PantryShelfTop, back), wood);
        foreach (float x in new[] { west, -b * 0.5f, east - b })
            Box(parent, "Upright", new Vector3(x, 0f, PantryShelfFront), new Vector3(x + b, PantryShelfTop, back), wood);
        foreach (float level in PantryShelfLevels)
            Box(parent, "Board", new Vector3(west, level - b, PantryShelfFront), new Vector3(east, level, back - 0.015f), wood);

        Transform throwables = Group(parent, ThrowablesGroupName);
        Transform fixedProps = Group(parent, "Props");
        float z = (PantryShelfFront + back) * 0.5f;
        for (int i = 0; i < PantryShelfLevels.Length; i++)
        {
            Transform shelfGroup = i < PantryReachableBoards ? throwables : fixedProps;
            foreach (Prop prop in PantryShelfProps[i])
                PlaceModel(shelfGroup, prop.Model, new Vector3(prop.X, PantryShelfLevels[i], z), prop.Facing);
        }
    }

    // Sacks and a crate in the two south corners, against the hub wall, clear of both doorways.
    void BuildPantryStores(Transform parent)
    {
        Material crate = Paint("CrateWood", new Color(0.6f, 0.46f, 0.3f), 0.25f);
        Material sack = Paint("Sack", new Color(0.78f, 0.69f, 0.52f), 0.1f);
        float south = NorthRoomsSouth, north = NorthRoomsSouth + 0.5f;
        Box(parent, "Crate", new Vector3(PantryWest, 0f, south), new Vector3(-1.4f, 0.45f, north), crate);
        Box(parent, "Sack", new Vector3(PantryWest + 0.05f, 0.45f, south + 0.05f), new Vector3(-1.45f, 0.6f, north - 0.05f), sack);
        Box(parent, "Sack", new Vector3(1.4f, 0f, south), new Vector3(PantryEast, 0.4f, north), sack);
        Box(parent, "Sack", new Vector3(1.45f, 0.4f, south + 0.05f), new Vector3(PantryEast - 0.05f, 0.6f, north - 0.05f), sack);
    }
}
