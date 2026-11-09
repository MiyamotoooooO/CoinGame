using UnityEngine;

public class TitleCamera : MonoBehaviour
{
    public Transform target;

    public float distance = 2f;
    public float height = 2f;
    public float speed = 5f;

    private void Update()
    {
        transform.RotateAround(target.position, Vector3.up, speed * Time.deltaTime);

        transform.LookAt(target.position + Vector3.up * height);
    }

    private void Start()
    {
        transform.position = target.position + new Vector3(0, 2, -distance);
    }
}
