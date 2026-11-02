using UnityEngine;

public class rotate : MonoBehaviour
{
    [Header("Axes to rotate on")]
    public bool rotateX = false;
    public bool rotateY = true;
    public bool rotateZ = false;

    [Header("Speed per axis (degrees per second, negative = reverse)")]
    public float speedX = 90f;
    public float speedY = 90f;
    public float speedZ = 90f;

    [Tooltip("Self = the object's own axes, World = the scene's axes")]
    public Space rotationSpace = Space.Self;

    void Update()
    {
        Vector3 degreesPerSecond = new Vector3(
            rotateX ? speedX : 0f,
            rotateY ? speedY : 0f,
            rotateZ ? speedZ : 0f);

        transform.Rotate(degreesPerSecond * Time.deltaTime, rotationSpace);
    }
}
