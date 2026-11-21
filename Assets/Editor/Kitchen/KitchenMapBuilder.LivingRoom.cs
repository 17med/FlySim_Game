using UnityEngine;

// The living room, east of the kitchen through the doorway: two armchairs facing the TV on the north
// wall across a coffee table, a bookshelf on the south wall, a window on the east wall and lamps.
public partial class KitchenMapBuilder
{
    const float LivingCenterX = (LivingWest + LivingEast) * 0.5f;
    const float CeilingLightIntensity = 160f;
    const float FloorLampIntensity = 35f;
    // The bookshelf stands against the south wall behind the armchairs.
    const float BookshelfWest = 5f;
    const float BookshelfEast = 7.2f;
    const float BookshelfDepth = 0.35f;
    const float BookshelfHeight = 2.1f;
    const float BookshelfBoard = 0.03f;
    // Top surface of each board, bottom first.
    static readonly float[] BookshelfLevels = { 0.08f, 0.5f, 0.92f, 1.34f, 1.76f };
    static readonly Color LivingWallColor = new Color(0.66f, 0.74f, 0.72f);

    static readonly Prop[] LivingSillProps =
    {
        new("SM_FlowersPlant_03", LivingEast - SillDepth * 0.5f, -0.35f), new("SM_FlowerPot_09", LivingEast - SillDepth * 0.5f, 0.35f),
    };

    static readonly Prop[] LivingFloorProps =
    {
        new("SM_FlowerPot_07", 8.65f, -2.12f), new("SM_FlowerPot_04", 3.55f, -2.15f),
    };

    // Around the coffee table, which stands long side east-west on the carpet.
    static readonly Prop[] CoffeeTableProps =
    {
        new("SM_BookOpen_01", 6.55f, 0.42f, -20f), new("SM_Candle", 6.12f, 0.3f), new("SM_FlowerPot_09", 6.32f, 0.12f),
        new("SM_Crayon02", 6.45f, 0.62f, 30f), new("SM_Crayon03", 6.52f, 0.66f, 55f), new("SM_Crayon07", 6.4f, 0.7f, 75f),
        new("SM_Scotch", 5.85f, 0.62f, 10f),
    };

    // Per bookshelf level (BookshelfLevels), single props; book piles are listed separately below.
    // Z is unused: everything sits in the middle of the shelf's depth.
    static readonly Prop[][] BookshelfProps =
    {
        new[] { new Prop("SM_FlowerPot_10", 6.1f, 0f) },
        new[] { new Prop("SM_Frames_08", 5.4f, 0f), new Prop("SM_FlowerPot_09", 6.8f, 0f) },
        new[] { new Prop("SM_Candle", 6.1f, 0f) },
        new[] { new Prop("SM_FlowerPot_05", 5.4f, 0f), new Prop("SM_Bottles_06", 6.8f, 0f) },
        new[] { new Prop("SM_FlowerPot_09", 6.1f, 0f) },
    };

    // Per bookshelf level: (x, the books in the pile from the bottom up).
    static readonly (float x, string[] books)[][] BookshelfPiles =
    {
        new[] { (5.35f, new[] { "SM_Book_01", "SM_Book_02", "SM_Book_03", "SM_Book_04" }), (6.8f, new[] { "SM_Book_05", "SM_Book_06", "SM_Book_02" }) },
        new[] { (6.1f, new[] { "SM_Book_03", "SM_Book_01", "SM_Book_05", "SM_Book_06", "SM_Book_04" }) },
        new[] { (5.35f, new[] { "SM_Book_06", "SM_Book_02", "SM_Book_01" }), (6.8f, new[] { "SM_Book_04", "SM_Book_05" }) },
        new[] { (6.1f, new[] { "SM_Book_01", "SM_Book_03", "SM_Book_02", "SM_Book_06" }) },
        new[] { (5.4f, new[] { "SM_Book_05", "SM_Book_04", "SM_Book_03" }), (6.8f, new[] { "SM_Book_02", "SM_Book_06" }) },
    };

    void BuildLivingRoom(Transform parent)
    {
        BuildLivingShell(Group(parent, "Shell"));
        BuildSeating(Group(parent, "Seating"));
        BuildTvWall(Group(parent, "TvWall"));
        BuildBookshelf(Group(parent, "Bookshelf"));
        PlaceProps(Group(parent, "Plants"), LivingFloorProps, 0f);
    }

    void BuildLivingShell(Transform parent)
    {
        float t = WallThickness, d = HalfDepth + t;
        Material wall = PaintSurface("LivingWall", LivingWallColor);
        Color[] planks =
        {
            new Color(0.62f, 0.43f, 0.28f), new Color(0.57f, 0.39f, 0.25f), new Color(0.66f, 0.47f, 0.31f), new Color(0.6f, 0.41f, 0.27f),
            new Color(0.55f, 0.37f, 0.24f), new Color(0.64f, 0.45f, 0.3f), new Color(0.58f, 0.4f, 0.26f), new Color(0.63f, 0.44f, 0.29f),
        };
        // Eight 20 cm planks running north-south, repeating every 1.6 m.
        Material floor = PaintPattern("LivingPlanks", planks, planks.Length, new Vector2((LivingEast + t - PartitionMiddle) / 1.6f, 1f));
        Box(parent, "Floor", new Vector3(PartitionMiddle, -t, -d), new Vector3(LivingEast + t, 0f, d), floor);
        Box(parent, "Wall_South", new Vector3(PartitionMiddle, 0f, -d), new Vector3(LivingEast + t, RoomHeight, -HalfDepth), wall);
        Box(parent, "Wall_North", new Vector3(PartitionMiddle, 0f, HalfDepth), new Vector3(LivingEast + t, RoomHeight, d), wall);
        // Turned to face east: the frame's x runs south from the north-east corner, the window centred at z = 0.
        Transform east = WallFrame(parent, "Wall_East", new Vector3(LivingEast, 0f, d), 90f);
        BuildWindowWall(east, 2f * d, d, wall);
        PartitionHalf(parent, LivingWest, PartitionMiddle, wall);
        PlaceProps(Group(parent, "SillProps"), LivingSillProps, SillHeight);

        // Counter-clockwise from the south-west corner.
        Baseboard(parent, new Vector3(LivingWest, 0f, -HalfDepth), new Vector3(LivingEast, 0f, -HalfDepth));
        Baseboard(parent, new Vector3(LivingEast, 0f, -HalfDepth), new Vector3(LivingEast, 0f, HalfDepth));
        Baseboard(parent, new Vector3(LivingEast, 0f, HalfDepth), new Vector3(LivingWest, 0f, HalfDepth));
        Baseboard(parent, new Vector3(LivingWest, 0f, HalfDepth), new Vector3(LivingWest, 0f, DoorwayNorth + TrimWidth));
        Baseboard(parent, new Vector3(LivingWest, 0f, DoorwaySouth - TrimWidth), new Vector3(LivingWest, 0f, -HalfDepth));

        Transform pictures = Group(parent, "Pictures");
        HangPicture(pictures, "SM_Frames_01", new Vector3(LivingWest, 1.3f, 0.6f), 90f);
        HangPicture(pictures, "SM_Frames_05", new Vector3(LivingWest, 1.35f, 1.55f), 90f);

        Cylinder(parent, "CeilingLamp", new Vector3(LivingCenterX, RoomHeight - 0.08f, 0.2f), new Vector3(0.5f, 0.08f, 0.5f), PaintGlow("CeilingLamp", new Color(1f, 0.92f, 0.78f)));
        Light ceiling = NewLight(parent, "CeilingLight", new Vector3(LivingCenterX, RoomHeight - 0.25f, 0.2f), LightType.Point);
        ceiling.range = 7f * WorldScale;
        ceiling.intensity = CeilingLightIntensity;
    }

    void BuildSeating(Transform parent)
    {
        GameObject carpet = PlaceModel(parent, "SM_Carpet_05", new Vector3(LivingCenterX, 0f, 0.3f), 90f);
        float carpetTop = carpet != null ? LocalBounds(carpet).max.y : 0f;
        // The oval coffee table's long side runs east-west.
        GameObject coffeeTable = PlaceModel(parent, "SM_SmallTable_01", new Vector3(LivingCenterX, carpetTop, 0.35f), 90f);
        if (coffeeTable != null)
        {
            Transform props = Group(parent, "CoffeeTableProps");
            float top = LocalBounds(coffeeTable).max.y;
            PlaceProps(props, CoffeeTableProps, top);
            PlaceStack(props, new[] { "SM_Book_01", "SM_Book_02" }, new Vector3(5.65f, top, 0.35f), 10f);
        }

        // Turned slightly in towards the TV.
        PlaceModel(parent, "SM_LeatherCouch", new Vector3(4.85f, 0f, -1f), 20f);
        PlaceModel(parent, "SM_LeatherCouch", new Vector3(7.35f, 0f, -1f), -20f);
        GameObject sideTable = PlaceModel(parent, "SM_TableBasse_01", new Vector3(LivingCenterX, 0f, -1.25f), 0f);
        if (sideTable != null)
            PlaceModel(parent, "SM_Radio", new Vector3(LivingCenterX, LocalBounds(sideTable).max.y, -1.25f), 0f);
        FloorLamp(parent, "SM_LampTall_01", new Vector3(8.55f, 0f, -1.6f));
    }

    void BuildTvWall(Transform parent)
    {
        // The TV stands on its own legs; its screen faces south, at the armchairs.
        PlaceModel(parent, "SM_TVSet", new Vector3(LivingCenterX, 0f, 2.15f), 180f);
        FloorLamp(parent, "SM_LampTall_03", new Vector3(8.55f, 0f, 2.05f));
        PlaceModel(parent, "SM_LampTall_02", new Vector3(3.65f, 0f, 2.05f), 0f);
        HangPicture(parent, "SM_Frames_03", new Vector3(5f, 1.3f, HalfDepth), 180f);
        HangPicture(parent, "SM_Frames_04", new Vector3(7.2f, 1.3f, HalfDepth), 180f);
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

    void BuildBookshelf(Transform parent)
    {
        Material wood = Paint("BookshelfWood", new Color(0.42f, 0.28f, 0.18f), 0.3f);
        float back = -HalfDepth, front = back + BookshelfDepth, b = BookshelfBoard;
        Box(parent, "Side_West", new Vector3(BookshelfWest, 0f, back), new Vector3(BookshelfWest + b, BookshelfHeight, front), wood);
        Box(parent, "Side_East", new Vector3(BookshelfEast - b, 0f, back), new Vector3(BookshelfEast, BookshelfHeight, front), wood);
        Box(parent, "Back", new Vector3(BookshelfWest, 0f, back), new Vector3(BookshelfEast, BookshelfHeight, back + 0.015f), wood);
        Box(parent, "Top", new Vector3(BookshelfWest, BookshelfHeight - b, back), new Vector3(BookshelfEast, BookshelfHeight, front), wood);
        foreach (float level in BookshelfLevels)
            Box(parent, "Board", new Vector3(BookshelfWest + b, level - b, back), new Vector3(BookshelfEast - b, level, front), wood);

        Transform props = Group(parent, "Props");
        float z = back + BookshelfDepth * 0.5f;
        for (int i = 0; i < BookshelfLevels.Length; i++)
        {
            foreach (Prop prop in BookshelfProps[i])
                PlaceModel(props, prop.Model, new Vector3(prop.X, BookshelfLevels[i], z), prop.Facing);
            foreach ((float x, string[] books) in BookshelfPiles[i])
                PlaceStack(props, books, new Vector3(x, BookshelfLevels[i], z), 0f);
        }
        PlaceModel(props, "SM_FlowersPlant_02", new Vector3(LivingCenterX, BookshelfHeight, z), 0f);
    }
}
