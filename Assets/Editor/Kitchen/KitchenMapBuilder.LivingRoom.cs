using UnityEngine;

// The living room, the north-east room: a sofa under the north window facing a coffee table on a
// carpet, the TV against the east wall with a low bench beside it, a small bookcase by the pantry
// doorway and a floor lamp in the north-east corner.
// The living room's dish is the coffee mug, which Tools > Match > Place Coffee In Living Room
// (CoffeeCupSetup) stands on the coffee table. It finds the table as Seating/SM_SmallTable_01 and
// keeps the mug clear of whatever is in Seating/CoffeeTableProps, so those names stay; the props
// group is left empty so the whole table top is the dish's.
public partial class KitchenMapBuilder
{
    const float FloorLampIntensity = 35f;
    static readonly Vector3 CoffeeTableSpot = new Vector3(4.6f, 0f, 2f);
    // Seat 0.45 m and back 0.9 m high, against the north wall below the window sill.
    const float SofaWest = 3.4f;
    const float SofaEast = 5.8f;
    const float SofaFront = 3f;
    static readonly Color SofaColor = new Color(0.42f, 0.5f, 0.62f);
    static readonly Color SofaCushionColor = new Color(0.47f, 0.56f, 0.69f);
    // The TV stands on its own legs (1.13 m tall), facing west across the room.
    static readonly Vector3 TvSpot = new Vector3(6.7f, 0f, 2f);
    // A low bench against the east wall, south of the TV.
    const float BenchWest = 6.5f;
    const float BenchSouth = 0.7f;
    const float BenchNorth = 1.45f;
    const float BenchTop = 0.5f;
    // The bookcase stands against the pantry wall, north of its doorway.
    const float BookcaseFront = LivingWest + 0.35f;
    const float BookcaseSouth = 2.75f;
    const float BookcaseNorth = 3.45f;
    const float BookcaseBoard = 0.03f;
    // Top surface of each board, bottom first; the last is the bookcase's top.
    static readonly float[] BookcaseLevels = { 0.06f, 0.48f, 0.9f, 1.3f };
    static readonly Color BookcaseColor = new Color(0.42f, 0.28f, 0.18f);

    static readonly Prop[] LivingSillProps =
    {
        new("SM_FlowersPlant_03", LivingWindowX - 0.35f, HouseNorth - SillDepth * 0.5f), new("SM_FlowerPot_09", LivingWindowX + 0.35f, HouseNorth - SillDepth * 0.5f),
    };

    // Per bookcase level (BookcaseLevels): single props as (model, z), then book piles as (z, books
    // from the bottom up). Everything stands in the middle of the bookcase's depth.
    static readonly (string model, float z)[][] BookcaseSingles =
    {
        new (string, float)[0],
        new[] { ("SM_Candle", 3.25f) },
        new[] { ("SM_BookOpen_01", 3f), ("SM_Bottles_06", 3.3f) },
        new (string, float)[0],
    };
    static readonly (float z, string[] books)[][] BookcasePiles =
    {
        new[] { (3.1f, new[] { "SM_Book_04", "SM_Book_06" }) },
        new[] { (2.95f, new[] { "SM_Book_01", "SM_Book_02" }) },
        new (float, string[])[0],
        new[] { (2.95f, new[] { "SM_Book_03", "SM_Book_05" }) },
    };

    void BuildLivingRoom(Transform parent)
    {
        BuildSeating(Group(parent, "Seating"));
        BuildSofa(Group(parent, "Sofa"));
        BuildTvCorner(Group(parent, "TvCorner"));
        BuildBookcase(Group(parent, "Bookcase"));

        Transform decor = Group(parent, "Decor");
        PlaceProps(decor, LivingSillProps, SillHeight);
        // On the hub wall facing north, and on the east wall above the bench facing west.
        HangPicture(decor, "SM_Frames_01", new Vector3(5.2f, 1.3f, NorthRoomsSouth), 0f);
        HangPicture(decor, "SM_Frames_05", new Vector3(HouseEast, 1.3f, (BenchSouth + BenchNorth) * 0.5f), -90f);
        FloorLamp(parent, "SM_LampTall_03", new Vector3(6.7f, 0f, 3.2f));
        CeilingLamp(parent, 4.6f, 1.3f);
    }

    // The carpet, the coffee table on it and the (empty) group for what stands on the table: the
    // names and nesting are what CoffeeCupSetup looks for (see the file comment).
    void BuildSeating(Transform parent)
    {
        GameObject carpet = PlaceModel(parent, "SM_Carpet_05", CoffeeTableSpot, 90f);
        float carpetTop = carpet != null ? LocalBounds(carpet).max.y : 0f;
        // The oval coffee table's long side runs east-west.
        PlaceModel(parent, "SM_SmallTable_01", new Vector3(CoffeeTableSpot.x, carpetTop, CoffeeTableSpot.z), 90f);
        Group(parent, "CoffeeTableProps");
    }

    // Blocks rather than the pack's couch, whose collision hull is a solid box far bigger than the couch.
    void BuildSofa(Transform parent)
    {
        Material fabric = Paint("Sofa", SofaColor, 0.1f);
        Material cushion = Paint("SofaCushion", SofaCushionColor, 0.1f);
        float back = HouseNorth, backRest = back - 0.18f, armWidth = 0.15f;
        Box(parent, "Base", new Vector3(SofaWest, 0f, SofaFront), new Vector3(SofaEast, 0.3f, back), fabric);
        Box(parent, "SeatCushion", new Vector3(SofaWest + armWidth, 0.3f, SofaFront), new Vector3(SofaEast - armWidth, 0.45f, backRest), cushion);
        Box(parent, "BackRest", new Vector3(SofaWest, 0.3f, backRest), new Vector3(SofaEast, 0.9f, back), fabric);
        Box(parent, "Arm_West", new Vector3(SofaWest, 0.3f, SofaFront), new Vector3(SofaWest + armWidth, 0.6f, backRest), fabric);
        Box(parent, "Arm_East", new Vector3(SofaEast - armWidth, 0.3f, SofaFront), new Vector3(SofaEast, 0.6f, backRest), fabric);
    }

    void BuildTvCorner(Transform parent)
    {
        PlaceModel(parent, "SM_TVSet", TvSpot, -90f);

        Material wood = Paint("BookcaseWood", BookcaseColor, 0.3f);
        Box(parent, "Bench", new Vector3(BenchWest, BenchTop - 0.05f, BenchSouth), new Vector3(HouseEast, BenchTop, BenchNorth), wood);
        foreach (float z in new[] { BenchSouth, BenchNorth - 0.05f })
            Box(parent, "BenchSide", new Vector3(BenchWest, 0f, z), new Vector3(HouseEast, BenchTop - 0.05f, z + 0.05f), wood);
        // The radio is too big to throw and stays outside the Throwables group.
        PlaceModel(parent, "SM_Radio", new Vector3(6.78f, BenchTop, 1.2f), -90f);
        PlaceModel(Group(parent, ThrowablesGroupName), "SM_Candle", new Vector3(6.75f, BenchTop, 0.85f), 0f);
    }

    void FloorLamp(Transform parent, string model, Vector3 position)
    {
        GameObject lamp = PlaceModel(parent, model, position, 0f);
        if (lamp == null)
            return;
        Light light = NewLight(parent, model + "_Light", new Vector3(position.x, LocalBounds(lamp).max.y - 0.15f, position.z), LightType.Point);
        light.range = 3.5f * WorldScale;
        light.intensity = FloorLampIntensity;
    }

    void BuildBookcase(Transform parent)
    {
        Material wood = Paint("BookcaseWood", BookcaseColor, 0.3f);
        float back = LivingWest, front = BookcaseFront, b = BookcaseBoard;
        float height = BookcaseLevels[BookcaseLevels.Length - 1];
        Box(parent, "Side_South", new Vector3(back, 0f, BookcaseSouth), new Vector3(front, height, BookcaseSouth + b), wood);
        Box(parent, "Side_North", new Vector3(back, 0f, BookcaseNorth - b), new Vector3(front, height, BookcaseNorth), wood);
        Box(parent, "Back", new Vector3(back, 0f, BookcaseSouth), new Vector3(back + 0.015f, height, BookcaseNorth), wood);
        foreach (float level in BookcaseLevels)
            Box(parent, "Board", new Vector3(back, level - b, BookcaseSouth), new Vector3(front, level, BookcaseNorth), wood);

        Transform throwables = Group(parent, ThrowablesGroupName);
        float x = (back + front) * 0.5f;
        for (int i = 0; i < BookcaseLevels.Length; i++)
        {
            foreach ((string model, float z) in BookcaseSingles[i])
                PlaceModel(throwables, model, new Vector3(x, BookcaseLevels[i], z), 0f);
            foreach ((float z, string[] books) in BookcasePiles[i])
                PlaceStack(throwables, books, new Vector3(x, BookcaseLevels[i], z), 0f);
        }
        // A pot plant on top, too awkward to throw.
        PlaceModel(parent, "SM_FlowerPot_05", new Vector3(x, BookcaseLevels[BookcaseLevels.Length - 1], 3.25f), 0f);
    }
}
