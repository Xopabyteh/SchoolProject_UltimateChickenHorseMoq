using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DebugRandomizeColor : MonoBehaviour
{
    private static float _hue = 0f;
    void Start()
    {
        GetComponent<SpriteRenderer>().color = Color.HSVToRGB(_hue, 1f, 1f);
        _hue += 0.13f;
    }
}
