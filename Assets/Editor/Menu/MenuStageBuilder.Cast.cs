using UnityEditor;
using UnityEngine;

// The flies, the old man and the three camera views of the lobby stage. See MenuStageBuilder.cs for
// the build and MenuStageBuilder.Set.cs for the room and its coordinates.
public partial class MenuStageBuilder
{
    // Far past life size, so a fly reads from across the room: the root is scaled until the longest
    // side of the fly, seen from above, is this long.
    const float FlyTargetLength = 0.18f;
    // Dancing in the menu, and sitting in the lobby, where it is tighter so that four seated flies
    // fit between the side panels in the fly view (about 24-76% of the width at this spacing).
    const float FlySpacing = 0.12f;
    const float LobbyFlySpacing = 0.10f;
    // How far the outer pair stands forward of the inner pair, so the four make a shallow arc open
    // towards the cameras, the way a lobby line-up is staged.
    const float FlyArcDepth = 0.05f;
    // Every fly turns towards this point on the floor plan (x, z), between the menu and fly cameras.
    static readonly Vector2 FlyFocus = new Vector2(0f, 1.1f);
    // The MenuFly model's front, as a yaw from its root's +z. 0 assumes the model faces +z; if it turns
    // out to face another way, this one number corrects every fly.
    const float FlyFrontYawOffset = 0f;
    // Seat order, as slots counted from the left: the inner pair first, then the outer pair, so a room
    // with one or two flyers keeps them near the middle of the table, where the cameras frame it.
    static readonly int[] FlySeatOrder = { 1, 2, 0, 3 };

    // Just inside the doorway, facing the table.
    const float OldManInset = 0.3f;
    // Assumed, not seen in a render: the humanoid FBX faces +z, as Unity imports humanoids. Correct here.
    const float OldManFrontYawOffset = 0f;

    // The flies' bodies, about 4 cm over the tabletop: what the human view aims at.
    static readonly Vector3 FlyBodies = new Vector3(0f, TableTop + 0.04f, 0f);
    // Close in front of the one dancing fly, looking down about 17 degrees from 0.45 m: the fly fills
    // about a fifth of the width, and its jumps (about 0.1 m) and raised front legs (0.05 m over the
    // head) still fit under the top of the frame. The aim is just over the tabletop, at the fly's body.
    static readonly Vector3 MenuViewSpot = new Vector3(0f, TableTop + 0.03f + 0.13f, 0.43f);
    static readonly Vector3 MenuViewAim = new Vector3(0f, TableTop + 0.03f, 0f);
    // Close and looking down about 19.5 degrees, facing away from the door (the old man is behind
    // it), on the middle of the table, where the director centres the seated flies whatever their
    // number. Four seated flies span about 24-76% of the width, three 32-68%, with their bodies at
    // 54-57% of the height.
    static readonly Vector3 FlyViewSpot = new Vector3(0f, 0.97f, 0.58f);
    static readonly Vector3 FlyViewAim = new Vector3(0f, TableTop + 0.005f, 0f);
    // Over the old man's right shoulder: this far behind him, this far to his right and this high
    // (his head is at about 1.62 m). The aim is moved to his left of the table so the back of his
    // head sits left of the flies instead of on top of them.
    const float HumanViewBack = 0.95f;
    const float HumanViewRight = 0.24f;
    const float HumanViewHeight = 1.95f;
    const float HumanViewAimShift = 0.18f;

    static Vector3 OldManSpot => new Vector3(DoorCenterX, 0f, BackWallZ - OldManInset);

    // Fails closed: a fly whose size cannot be measured would come out at any size at all.
    static bool TryMeasureFlyScale(GameObject flyPrefab, out float scale)
    {
        scale = 0f;
        var probe = Object.Instantiate(flyPrefab);
        probe.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            probe.transform.localScale = Vector3.one;
            float length = TryMeshBounds(probe, out Bounds bounds) ? Mathf.Max(bounds.size.x, bounds.size.z) : 0f;
            if (length < 1e-4f)
            {
                Debug.LogError($"Build Lobby Stage: {MenuFlyPrefabPath} has no mesh to measure; nothing was changed.");
                return false;
            }
            scale = FlyTargetLength / length;
            return true;
        }
        finally
        {
            Object.DestroyImmediate(probe);
        }
    }

    // In seat order: the director sits the first N of them for N flyers.
    MenuFlyDance[] PlaceFlies(Transform parent)
    {
        var flies = new MenuFlyDance[FlySeatOrder.Length];
        for (int seat = 0; seat < FlySeatOrder.Length; seat++)
        {
            Vector3 spot = FlySpot(FlySeatOrder[seat]);
            var fly = (GameObject)PrefabUtility.InstantiatePrefab(assets.FlyPrefab, parent);
            fly.name = $"MenuFly_{seat + 1}";
            fly.transform.localPosition = spot;
            fly.transform.localRotation = Quaternion.Euler(0f, FaceTowards(spot, FlyFocus) + FlyFrontYawOffset, 0f);
            fly.transform.localScale = Vector3.one * assets.FlyScale;
            StripColliders(fly);
            var dance = fly.GetComponent<MenuFlyDance>();
            if (dance == null)
            {
                dance = fly.AddComponent<MenuFlyDance>();
            }
            flies[seat] = dance;
        }
        return flies;
    }

    // The middle of the line-up, on the tabletop: MenuFlyDance stands each fly on its root.
    static Vector3 SeatCentre => new Vector3(0f, TableTop, 0f);

    // The dancer's place in the menu line-up, by the rule the director keeps the flies to.
    static Vector3 FlySpot(int slot) =>
        SeatCentre + MenuStageDirector.SeatOffset(slot, LobbyManager.MaxFlyers, FlySpacing, FlyArcDepth);

    // The yaw that turns +z at spot towards target on the floor plan (x, z).
    static float FaceTowards(Vector3 spot, Vector2 target) =>
        Mathf.Atan2(target.x - spot.x, target.y - spot.z) * Mathf.Rad2Deg;

    GameObject PlaceOldMan(Transform parent)
    {
        var oldMan = (GameObject)PrefabUtility.InstantiatePrefab(assets.OldManPrefab, parent);
        oldMan.name = "OldMan";
        Vector3 spot = OldManSpot;
        oldMan.transform.localPosition = spot;
        oldMan.transform.localRotation = Quaternion.Euler(0f, FaceTowards(spot, Vector2.zero) + OldManFrontYawOffset, 0f);
        // His capsule is for the match; nothing on this stage uses physics.
        StripColliders(oldMan);
        foreach (Transform part in oldMan.GetComponentsInChildren<Transform>(true))
        {
            part.gameObject.layer = assets.OldManLayer;
        }
        return oldMan;
    }

    void PlaceViews(Transform parent, StageCast cast)
    {
        cast.MenuView = View(parent, "MenuView", MenuViewSpot, MenuViewAim);
        cast.FlyLobbyView = View(parent, "FlyLobbyView", FlyViewSpot, FlyViewAim);
        Vector3 spot = OldManSpot;
        Vector3 forward = new Vector3(-spot.x, 0f, -spot.z).normalized;
        Vector3 right = new Vector3(forward.z, 0f, -forward.x);
        Vector3 eye = spot - forward * HumanViewBack + right * HumanViewRight + Vector3.up * HumanViewHeight;
        cast.HumanLobbyView = View(parent, "HumanLobbyView", eye, FlyBodies - right * HumanViewAimShift);
    }

    // An empty the camera is put on, looking from spot at aim (both in the set's space).
    static Transform View(Transform parent, string name, Vector3 spot, Vector3 aim)
    {
        Transform view = Group(parent, name);
        view.localPosition = spot;
        view.localRotation = Quaternion.LookRotation(aim - spot, Vector3.up);
        return view;
    }
}
