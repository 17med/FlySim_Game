using UnityEngine;
using UnityEngine.UI;

// Plays a click when this Button is pressed.
// The sound plays on its own short-lived object that survives a scene load, because several menu
// buttons load a scene on the same click, which would otherwise cut the sound off at once.
[RequireComponent(typeof(Button))]
public class ButtonClickSound : MonoBehaviour
{
    [SerializeField] AudioClip clickSound;
    [SerializeField, Range(0f, 1f)] float volume = 1f;

    Button button;

    void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(PlayClick);
    }

    void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(PlayClick);
    }

    void PlayClick()
    {
        if (clickSound == null)
            return;
        var player = new GameObject(nameof(ButtonClickSound));
        DontDestroyOnLoad(player);
        AudioSource source = player.AddComponent<AudioSource>();
        source.clip = clickSound;
        source.volume = GameAudio.Scale(volume);
        source.spatialBlend = 0f;
        source.Play();
        // Scaled time: on a menu paused with time scale 0 the object waits until time runs again.
        Destroy(player, clickSound.length);
    }
}
