using UnityEngine;
using UnityEngine.SceneManagement;

// The one place the game changes scenes. Buttons call LoadKitchen / LoadMainMenu / QuitGame straight
// from their OnClick, and gameplay code goes through GameSceneManager.Instance so no script has to
// know scene names or build indices.
// Named GameSceneManager, not SceneManager, because UnityEngine.SceneManagement.SceneManager already
// owns that name and the two would shadow each other in every file that loads a scene.
// It survives the load (DontDestroyOnLoad), so the single instance created in the main menu is still
// there in the Kitchen to take the player back.
public class GameSceneManager : MonoBehaviour
{
    // Both must stay listed in File > Build Profiles > Scene List; LoadScene says so when one is missing.
    public const string MainMenuScene = "MainMenu";
    public const string KitchenScene = "Kitchen";

    static GameSceneManager instance;

    // Creates the manager on demand, so a scene played on its own (entering the Kitchen directly from
    // the Editor) can still ask for a scene change.
    public static GameSceneManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<GameSceneManager>();
            }
            if (instance == null)
            {
                instance = new GameObject(nameof(GameSceneManager)).AddComponent<GameSceneManager>();
            }
            return instance;
        }
    }

    bool isLoading;

    // 0 to 1 while a load is running, so a loading bar can read it later. 1 when nothing is loading.
    public float LoadProgress { get; private set; } = 1f;

    public bool IsLoading => isLoading;

    void Awake()
    {
        // Loading the main menu a second time brings in a fresh manager with the scene; the one that
        // survived the previous load wins and the newcomer removes itself.
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void LoadKitchen()
    {
        LoadScene(KitchenScene);
    }

    public void LoadMainMenu()
    {
        LoadScene(MainMenuScene);
    }

    public void ReloadCurrentScene()
    {
        LoadScene(SceneManager.GetActiveScene().name);
    }

    public void LoadScene(string sceneName)
    {
        // A double click on a button would otherwise start the same load twice.
        if (isLoading)
        {
            return;
        }
        // Fails closed with the reason: Unity's own error for a scene missing from the build only says
        // the name could not be loaded.
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"{nameof(GameSceneManager)}: scene '{sceneName}' is not in the build scene list, so it cannot be loaded. Add it in File > Build Profiles > Scene List.");
            return;
        }

        isLoading = true;
        LoadProgress = 0f;
        // A scene change while the game is paused would leave the next scene frozen.
        Time.timeScale = 1f;
        // Gameplay locks the cursor (OrbitCamera, OldPersonFPSController); menus need it back.
        if (sceneName == MainMenuScene)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        StartCoroutine(LoadRoutine(sceneName));
    }

    public void QuitGame()
    {
        // Application.Quit does nothing in the Editor, so stop play mode there instead.
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    System.Collections.IEnumerator LoadRoutine(string sceneName)
    {
        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
        while (!load.isDone)
        {
            LoadProgress = load.progress;
            yield return null;
        }
        LoadProgress = 1f;
        isLoading = false;
    }
}
