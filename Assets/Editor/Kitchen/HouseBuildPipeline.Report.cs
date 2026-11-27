using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// What HouseBuildPipeline says about a kitchen once it is saved: the summary in the Console and, from
// the command line, a top-down picture of the house. See HouseBuildPipeline.cs.
public static partial class HouseBuildPipeline
{
    const string ShotDirectoryArgument = "-houseShotDir";
    const int ShotWidth = 2048;
    // House metres of floor shown around the walls in the shot.
    const float ShotMargin = 0.5f;
    // Anything whose bottom is this close under the ceiling is hidden for the shot: the ceiling slab and
    // the lamps flush with it. Pendant shades hang lower and stay, small enough not to hide a room.
    const float CeilingBand = 0.3f;

    // ---- the report ----------------------------------------------------------------------------

    // Everything in house metres (KitchenMap's local space), and distances flat along the floor,
    // because that is how FoodDish measures the guard and how far the old man has to walk.
    static void LogSummary(Scene scene, HouseMarkers markers, int throwables)
    {
        Transform map = markers.Map;
        var dishes = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<FoodDish>(true))
            .Select(dish => (Name: dish.name, Spot: map.InverseTransformPoint(TryMeshBounds(dish.gameObject, out Bounds b) ? b.center : dish.transform.position)))
            .ToList();
        Vector3 human = map.InverseTransformPoint(markers.Human.position);
        var log = new StringBuilder($"Build House: {scene.name} saved. {throwables} throwable(s). Human spawn {Format(human)}.");
        foreach ((string name, Vector3 spot) in dishes)
        {
            log.Append($"\n  dish '{name}' {Format(spot)}: {Flat(human, spot):0.00} m from the human spawn");
        }
        foreach (Transform fly in markers.Flies)
        {
            Vector3 spawn = map.InverseTransformPoint(fly.position);
            string nearest = "no dish in the scene";
            if (dishes.Count > 0)
            {
                (string name, Vector3 spot) = dishes.OrderBy(dish => Flat(spawn, dish.Spot)).First();
                nearest = $"{Flat(spawn, spot):0.00} m from the nearest dish, '{name}'";
            }
            log.Append($"\n  {fly.name} {Format(spawn)}: {nearest}");
        }
        Debug.Log(log.ToString());
    }

    static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    static string Format(Vector3 metres) => $"({metres.x:0.00}, {metres.y:0.00}, {metres.z:0.00})";

    // ---- the top-down shot ---------------------------------------------------------------------

    // Fails open: the shot is for looking the layout over, and the scene is already saved by now.
    static void RenderTopDown(Scene scene, Transform map, string directory)
    {
        float scale = map.lossyScale.x;
        if (!TryFrameHouse(map, out Bounds house, out List<Renderer> ceiling))
        {
            Debug.LogWarning($"Build House: nothing to photograph in {scene.name}; no shot taken.");
            return;
        }
        RenderTexture shot = null;
        try
        {
            ceiling.ForEach(r => r.enabled = false);
            shot = RenderFromAbove(house, ShotMargin * scale, house.max.y - map.position.y);
            WritePng(shot, Path.Combine(directory, $"{scene.name}_topdown.png"));
        }
        catch (IOException e)
        {
            Debug.LogWarning($"Build House: could not write the shot of {scene.name} to {directory}: {e.Message}");
        }
        catch (UnauthorizedAccessException e)
        {
            Debug.LogWarning($"Build House: not allowed to write the shot of {scene.name} to {directory}: {e.Message}");
        }
        finally
        {
            ceiling.ForEach(r => r.enabled = true);
            if (shot != null)
            {
                shot.Release();
                UnityEngine.Object.DestroyImmediate(shot);
            }
        }
    }

    // The house as seen from above, and what has to be hidden to see into it. The ceiling covers the
    // house exactly, so it frames the picture; the whole map if there is none. Shared with
    // MinimapWiring, whose map picture has to frame the house the same way.
    internal static bool TryFrameHouse(Transform map, out Bounds house, out List<Renderer> ceiling)
    {
        float scale = map.lossyScale.x;
        ceiling = map.GetComponentsInChildren<Renderer>(true)
            .Where(r => r.enabled && r.bounds.min.y >= map.position.y + (KitchenMapBuilder.RoomHeight - CeilingBand) * scale)
            .ToList();
        return TryUnion(ceiling.Count > 0 ? ceiling : map.GetComponentsInChildren<Renderer>(true).ToList(), out house);
    }

    // An orthographic camera straight above the house, looking down with north (+z) at the top of the
    // picture, ShotWidth pixels wide and as tall as the house's proportions make it.
    internal static RenderTexture RenderFromAbove(Bounds house, float margin, float heightAboveFloor)
    {
        float width = house.size.x + 2f * margin;
        float depth = house.size.z + 2f * margin;
        int height = Mathf.Max(1, Mathf.RoundToInt(ShotWidth * depth / width));
        var target = new RenderTexture(ShotWidth, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var cameraObject = new GameObject("HouseShotCamera") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = depth * 0.5f;
            camera.aspect = (float)ShotWidth / height;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.transform.SetPositionAndRotation(new Vector3(house.center.x, house.max.y + margin, house.center.z), Quaternion.Euler(90f, 0f, 0f));
            camera.nearClipPlane = 0.01f * margin;
            camera.farClipPlane = heightAboveFloor + 2f * margin;
            camera.targetTexture = target;
            // Batch mode compiles shaders in the background and draws anything not ready yet in a flat
            // placeholder colour, which turned the first scene's picture solid blue; compiled up front instead.
            bool wasAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                // Twice: the first pass of a freshly opened scene can still come out with the toon
                // materials black even with compiling forced, as the first scene's picture did.
                Render(camera, target);
                Render(camera, target);
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = wasAsync;
            }
            camera.targetTexture = null;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
        return target;
    }

    // URP renders a single camera on request; Camera.Render is the fallback for any other pipeline.
    static void Render(Camera camera, RenderTexture target)
    {
        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
        if (RenderPipeline.SupportsRenderRequest(camera, request))
        {
            RenderPipeline.SubmitRenderRequest(camera, request);
            return;
        }
        camera.Render();
    }

    internal static void WritePng(RenderTexture source, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = source;
        var pixels = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
        pixels.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        pixels.Apply();
        RenderTexture.active = previous;
        File.WriteAllBytes(path, pixels.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(pixels);
        Debug.Log($"Build House: wrote {path}.");
    }
}
