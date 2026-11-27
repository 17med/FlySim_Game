using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Builds the lobby stage from the command line, saves MainMenu and photographs it through the menu's
// own camera at each view, staged by MenuStageDirector exactly as it stages the running menu:
//   Unity -batchmode -projectPath <project> -executeMethod MenuStageBuilder.RenderPreviewBatch -quit -menuShotDir <dir>
// Exit code 1 when anything failed, so a script running it can tell. See MenuStageBuilder.cs.
public partial class MenuStageBuilder
{
    const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
    const string ShotDirectoryArgument = "-menuShotDir";
    const int ShotWidth = 1920;
    const int ShotHeight = 1080;
    static readonly float[] DanceShotTimes = { 0f, 0.4f, 0.8f, 1.2f, 1.6f };
    const float SitShotTime = 1f;
    const int LobbyShotFlyers = 3;
    // Seconds into the old man's idle for his picture: in Edit mode his Animator has not run, and
    // without a step he stands in his bind pose.
    const float IdleShotTime = 0.5f;

    public static void RenderPreviewBatch()
    {
        bool succeeded;
        try
        {
            succeeded = BuildAndShoot(CommandLineValue(ShotDirectoryArgument));
        }
        catch (Exception e)
        {
            // Logged, not swallowed: this is the top of a batch run, and its only way to report an
            // unexpected failure to the script that started it is the exit code below.
            Debug.LogException(e);
            succeeded = false;
        }
        EditorApplication.Exit(succeeded ? 0 : 1);
    }

    // Fails closed: the pictures are what this run is for, so a scene that could not be built or
    // saved, or a picture that could not be written, is a failed run.
    static bool BuildAndShoot(string shotDirectory)
    {
        if (string.IsNullOrEmpty(shotDirectory))
        {
            Debug.LogError($"Build Lobby Stage: pass {ShotDirectoryArgument} <directory> for the pictures.");
            return false;
        }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScenePath) == null)
        {
            Debug.LogError($"Build Lobby Stage: no scene at {MainMenuScenePath}. Was it moved or renamed?");
            return false;
        }
        Scene scene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);
        if (!TryBuild(scene, out MenuStageDirector director))
        {
            Debug.LogError($"Build Lobby Stage: {scene.name} was left unsaved.");
            return false;
        }
        if (!EditorSceneManager.SaveScene(scene))
        {
            Debug.LogError($"Build Lobby Stage: could not save {scene.name}.");
            return false;
        }
        Debug.Log($"Build Lobby Stage: {scene.name} built and saved.");
        return ShootAll(director, shotDirectory);
    }

    static bool ShootAll(MenuStageDirector director, string directory)
    {
        var target = new RenderTexture(ShotWidth, ShotHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        // Batch mode compiles shaders in the background and draws anything not ready in a flat
        // placeholder colour (HouseBuildPipeline's first top-down shot came out solid blue): compiled
        // up front instead.
        bool wasAsync = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;
        try
        {
            director.ShowImmediate(MenuStageDirector.StageState.Menu);
            foreach (float time in DanceShotTimes)
            {
                PoseFlies(director, time);
                Shoot(director.stageCamera, target, Path.Combine(directory, $"menu_dance_t{time.ToString("0.#", CultureInfo.InvariantCulture)}.png"));
            }
            director.ShowImmediate(MenuStageDirector.StageState.Room(false, LobbyShotFlyers));
            PoseFlies(director, SitShotTime);
            Shoot(director.stageCamera, target, Path.Combine(directory, "lobby_fly.png"));
            director.ShowImmediate(MenuStageDirector.StageState.Room(true, LobbyShotFlyers));
            PoseFlies(director, SitShotTime);
            PoseOldMan(director.oldMan);
            Shoot(director.stageCamera, target, Path.Combine(directory, "lobby_human.png"));
            return true;
        }
        catch (IOException e)
        {
            Debug.LogError($"Build Lobby Stage: could not write a picture to {directory}: {e.Message}");
            return false;
        }
        catch (UnauthorizedAccessException e)
        {
            Debug.LogError($"Build Lobby Stage: not allowed to write pictures to {directory}: {e.Message}");
            return false;
        }
        finally
        {
            ShaderUtil.allowAsyncCompilation = wasAsync;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    // Only the flies on show: the director has already set each one's mode.
    static void PoseFlies(MenuStageDirector director, float time)
    {
        foreach (MenuFlyDance fly in director.flies)
        {
            if (fly != null && fly.gameObject.activeInHierarchy)
            {
                fly.Evaluate(time);
            }
        }
    }

    // Fails open: without his Animator he is photographed in his bind pose, which still shows where
    // he stands and how the view frames him.
    static void PoseOldMan(GameObject oldMan)
    {
        Animator animator = oldMan != null ? oldMan.GetComponentInChildren<Animator>() : null;
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning("Build Lobby Stage: the old man has no Animator with a controller; he is photographed in his bind pose.");
            return;
        }
        animator.Rebind();
        animator.Update(IdleShotTime);
    }

    static void Shoot(Camera camera, RenderTexture target, string path)
    {
        camera.targetTexture = target;
        camera.aspect = (float)ShotWidth / ShotHeight;
        try
        {
            // Twice: the first pass of a freshly opened scene can still come out with the toon
            // materials black even with compiling forced, as HouseBuildPipeline found.
            Render(camera, target);
            Render(camera, target);
            WritePng(target, path);
        }
        finally
        {
            camera.targetTexture = null;
            camera.ResetAspect();
        }
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

    static void WritePng(RenderTexture source, string path)
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
        Debug.Log($"Build Lobby Stage: wrote {path}.");
    }

    static string CommandLineValue(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
