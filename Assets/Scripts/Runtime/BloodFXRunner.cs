using UnityEngine;

/// <summary>
/// The frame tick for <see cref="BloodFX"/>, which is a static class and has none of its
/// own: it spreads the pools under bodies. Added to the level's BloodFX object when that
/// is built, and goes with it when the scene does.
/// </summary>
[AddComponentMenu("")]
public class BloodFXRunner : MonoBehaviour
{
    void Update() => BloodFX.Grow();
}
