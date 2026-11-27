using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// The small map in the top-right corner: a picture of the house from above with markers on it.
// What each player sees is decided by who they are on this machine, because the map is a fairness
// tool and the two sides need different things from it:
// - a fly sees the food, itself and the other flies, so the flies can work together - but not the old
//   man, who has to be looked for;
// - the old man sees the food and himself, and never the flies, or hunting them would be no work at all.
// The map always has north at the top; only the player's own marker turns, to show where they face.
// The picture is a still image made in the Editor (Tools > Match > Add Minimap To Open Scene), not a
// live camera, so the map costs nothing to draw; the markers are the only things that move.
// Goes on the minimap object under the scene's Match_HUD, which every player has on their own screen.
public class MinimapHud : MonoBehaviour
{
    // How often the scene is swept for players and dishes. They are spawned after the scene loads and
    // can leave at any time, so they are looked for again rather than found once.
    const float SearchInterval = 0.5f;

    [Header("Map")]
    [Tooltip("The picture of the house seen from above.")]
    [SerializeField] RawImage map;
    [Tooltip("The part of the world the picture shows, in world units seen from above: x and y are the west and south edges, width runs east and height runs north.")]
    [SerializeField] Rect worldArea;
    [Tooltip("Where the markers are drawn. Must cover exactly the same rectangle as the picture.")]
    [SerializeField] RectTransform markerLayer;

    [Header("Markers")]
    [Tooltip("This player when they are a fly. Should point up, so it can be turned to where the fly faces.")]
    [SerializeField] Texture selfFlyMarker;
    [Tooltip("This player when they are the old man. Should point up, like the fly's.")]
    [SerializeField] Texture selfHumanMarker;
    [Tooltip("The other flies, on a fly's map only.")]
    [SerializeField] Texture otherFlyMarker;
    [SerializeField] Texture foodMarker;
    [SerializeField, Min(1f)] float selfSize = 24f;
    [SerializeField, Min(1f)] float otherFlySize = 16f;
    [SerializeField, Min(1f)] float foodSize = 20f;
    [SerializeField] Color otherFlyColor = new Color(0.55f, 0.85f, 1f, 1f);
    [Tooltip("A dish that has been eaten empty fades to this, so the players can see at a glance which food is left.")]
    [SerializeField] Color emptyFoodColor = new Color(1f, 1f, 1f, 0.25f);

    readonly List<FlyHealth> flies = new List<FlyHealth>();
    readonly List<FoodDish> dishes = new List<FoodDish>();
    readonly List<RawImage> otherFlyMarkers = new List<RawImage>();
    readonly List<RawImage> foodMarkers = new List<RawImage>();
    RawImage selfMarker;
    // The part of this machine's own player that moves: the fly's body, or the old man himself.
    Transform localBody;
    FlyHealth localFly;
    float searchAt;

    void Awake()
    {
        // Fails open: without a picture or a place to draw, the map is switched off and the match plays
        // on without it - nothing is lost but the map, and the warning names what to fix.
        if (map == null || markerLayer == null || worldArea.width <= 0f || worldArea.height <= 0f)
        {
            Debug.LogWarning($"{nameof(MinimapHud)}: {name} has no map picture, marker layer or world area, so no minimap is shown. Run Tools > Match > Add Minimap To Open Scene.", this);
            enabled = false;
        }
    }

    void Update()
    {
        if (Time.unscaledTime >= searchAt)
        {
            searchAt = Time.unscaledTime + SearchInterval;
            Search();
        }
        ShowSelf();
        ShowOtherFlies();
        ShowFood();
    }

    void Search()
    {
        flies.Clear();
        flies.AddRange(FindObjectsByType<FlyHealth>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
        dishes.Clear();
        dishes.AddRange(FindObjectsByType<FoodDish>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
        // Kept once found: a dead fly's controller is switched off, which must not make the map forget
        // whose screen this is.
        if (localBody == null)
            FindLocalPlayer();
    }

    void FindLocalPlayer()
    {
        localFly = null;
        foreach (FlyHealth fly in flies)
        {
            if (IsThisMachinesPlayer(fly.GetComponent<FlyController>()))
            {
                localFly = fly;
                localBody = fly.transform;
                return;
            }
        }
        foreach (OldPersonFPSController human in FindObjectsByType<OldPersonFPSController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (IsThisMachinesPlayer(human))
            {
                localBody = human.transform;
                return;
            }
        }
    }

    // Online the body's owner is this player; in the single player Kitchen, where nothing is spawned,
    // the one body whose controls are switched on is.
    static bool IsThisMachinesPlayer(Behaviour controls)
    {
        if (controls == null)
            return false;
        NetworkObject body = controls.GetComponentInParent<NetworkObject>();
        if (body != null && body.IsSpawned)
            return body.IsOwner;
        return controls.isActiveAndEnabled;
    }

    void ShowSelf()
    {
        bool isShown = localBody != null && (localFly == null || !localFly.IsDead);
        if (selfMarker == null && isShown)
            selfMarker = NewMarker("Self", localFly != null ? selfFlyMarker : selfHumanMarker, selfSize, Color.white);
        if (selfMarker == null)
            return;
        selfMarker.gameObject.SetActive(isShown);
        if (!isShown)
            return;
        Place(selfMarker, localBody.position);
        // North-up map: a yaw of 0 faces north, which is up, and Unity's yaw turns clockwise seen from
        // above while UI rotation turns anticlockwise.
        selfMarker.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -localBody.eulerAngles.y);
    }

    void ShowOtherFlies()
    {
        int shown = 0;
        // Only a fly sees the other flies; the old man's map never shows them.
        if (localFly != null)
        {
            foreach (FlyHealth fly in flies)
            {
                if (fly == null || fly == localFly || fly.IsDead)
                    continue;
                RawImage marker = MarkerAt(otherFlyMarkers, shown++, "OtherFly", otherFlyMarker, otherFlySize, otherFlyColor);
                Place(marker, fly.transform.position);
                marker.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -fly.transform.eulerAngles.y);
            }
        }
        HideFrom(otherFlyMarkers, shown);
    }

    void ShowFood()
    {
        int shown = 0;
        foreach (FoodDish dish in dishes)
        {
            if (dish == null)
                continue;
            RawImage marker = MarkerAt(foodMarkers, shown++, "Food", foodMarker, foodSize, Color.white);
            Place(marker, dish.BitePoint);
            marker.color = dish.IsEmpty ? emptyFoodColor : Color.white;
        }
        HideFrom(foodMarkers, shown);
    }

    // Puts a marker over a world position, through its anchors: an anchor of (0.5, 0.5) is the middle
    // of the picture, so a position's share of the world area is its place on the map.
    void Place(RawImage marker, Vector3 worldPosition)
    {
        var onMap = new Vector2(Mathf.InverseLerp(worldArea.xMin, worldArea.xMax, worldPosition.x),
                                Mathf.InverseLerp(worldArea.yMin, worldArea.yMax, worldPosition.z));
        RectTransform rect = marker.rectTransform;
        rect.anchorMin = rect.anchorMax = onMap;
        rect.anchoredPosition = Vector2.zero;
    }

    RawImage MarkerAt(List<RawImage> pool, int index, string markerName, Texture texture, float size, Color color)
    {
        while (pool.Count <= index)
            pool.Add(NewMarker(markerName, texture, size, color));
        pool[index].gameObject.SetActive(true);
        return pool[index];
    }

    static void HideFrom(List<RawImage> pool, int firstHidden)
    {
        for (int i = firstHidden; i < pool.Count; i++)
            pool[i].gameObject.SetActive(false);
    }

    RawImage NewMarker(string markerName, Texture texture, float size, Color color)
    {
        var marker = new GameObject(markerName, typeof(RectTransform)).AddComponent<RawImage>();
        marker.transform.SetParent(markerLayer, false);
        marker.texture = texture;
        marker.color = color;
        // A marker is not clickable, and the HUD has nothing to click.
        marker.raycastTarget = false;
        marker.rectTransform.sizeDelta = Vector2.one * size;
        return marker;
    }
}
