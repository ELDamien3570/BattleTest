using Unity.VisualScripting;
using UnityEngine;

public class ClosablePanel : MonoBehaviour
{
    public void ClosePanel()
    {
        this.gameObject.SetActive(false);   
    }
}
