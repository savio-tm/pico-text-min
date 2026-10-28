// Switches on objects that were inactive when the scene was exported, 1 s after launch.
using System.Collections;
using UnityEngine;

public class ShowAtStart : MonoBehaviour
{
    public GameObject[] Targets;

    IEnumerator Start()
    {
        yield return new WaitForSeconds(1f);
        foreach (var t in Targets) t.SetActive(true);
    }
}
