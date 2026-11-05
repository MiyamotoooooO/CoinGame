using UnityEngine;

public class CSVReader : MonoBehaviour
{
    private void Start()
    {
        TextAsset csv = Resources.Load<TextAsset>("RecordData");

        if (csv == null)
        {
            Debug.LogError("CSV‚ªŒ©‚Â‚©‚ç‚È‚¢");
            return;
        }

        Debug.Log(csv.text);
    }
}
