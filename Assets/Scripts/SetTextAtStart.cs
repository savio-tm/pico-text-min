// 1 s after launch: sets a TMP text's string (TMP rebuilds its mesh at runtime), native texts' strings,
// and switches on objects that were inactive when the scene was exported.
using System.Collections;
using ByteDance.PICO.SpatialAdapter.Component;
using TMPro;
using UnityEngine;

public class SetTextAtStart : MonoBehaviour
{
    public TMP_Text Text;
    public string Value;
    public SpatialAdapterNativeText[] Natives = new SpatialAdapterNativeText[0];
    public string[] NativeValues = new string[0];
    public GameObject[] Show = new GameObject[0];

    IEnumerator Start()
    {
        yield return new WaitForSeconds(1f);
        if (Text) Text.text = Value;
        for (int i = 0; i < Natives.Length; i++) if (Natives[i]) Natives[i].Text = NativeValues[i];
        foreach (var go in Show) go.SetActive(true);
    }
}
