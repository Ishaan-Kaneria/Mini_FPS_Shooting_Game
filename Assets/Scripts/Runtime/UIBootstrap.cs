using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Gives every scene the same interface plumbing, whatever built it: every root canvas gets
/// a safe area (<see cref="SafeAreaCanvas"/>), the player's interface scale
/// (<see cref="UIScaleBinder"/>) and a gamepad focus ring (<see cref="UIFocusRing"/>); the
/// event system reads the game's own UI actions and gets a <see cref="UINavigator"/>.
///
/// <b>At runtime, not in the builders</b>, so the dashboard, seven arenas, the gallery and
/// an arena somebody built with Add Gameplay To Current Scene all get it identically, and
/// none of them needs rebuilding to pick it up. A builder that wants something different --
/// a full-bleed layer, a stated default selection -- says so with
/// <see cref="SafeAreaBleed"/> and <see cref="UIDefaultSelection"/>.
///
/// <b>The UI actions matter for B.</b> The module's default actions bind Cancel to Escape
/// and B already, but they are a second action asset, with their own idea of which scheme
/// is live; pointing the module at <see cref="GameInput"/>'s UI map means there is one
/// answer to "what does B do" and one place that says it.
/// </summary>
public static class UIBootstrap
{
    static bool _hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        if (_hooked) SceneManager.sceneLoaded -= OnSceneLoaded;
        _hooked = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (!_hooked)
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            _hooked = true;
        }
        Prepare();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Prepare();

    /// <summary>Applies everything to what is loaded now. Safe to call more than once.</summary>
    public static void Prepare()
    {
        foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
        {
            if (!canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace) continue;
            if (canvas.GetComponent<SafeAreaCanvas>() == null) canvas.gameObject.AddComponent<SafeAreaCanvas>();
            UIScaleBinder.For(canvas);
            if (canvas.GetComponent<UIFocusRing>() == null) canvas.gameObject.AddComponent<UIFocusRing>();
        }

        foreach (var es in Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include))
        {
            if (es.GetComponent<UINavigator>() == null) es.gameObject.AddComponent<UINavigator>();
            var module = es.GetComponent<InputSystemUIInputModule>();
            if (module == null) continue;
            module.actionsAsset = GameInput.Asset;
            module.point = InputActionReference.Create(GameInput.UIPoint);
            module.leftClick = InputActionReference.Create(GameInput.UIClick);
            module.rightClick = InputActionReference.Create(GameInput.UIRightClick);
            module.middleClick = InputActionReference.Create(GameInput.UIMiddleClick);
            module.scrollWheel = InputActionReference.Create(GameInput.UIScroll);
            module.move = InputActionReference.Create(GameInput.UINavigate);
            module.submit = InputActionReference.Create(GameInput.UISubmit);
            module.cancel = InputActionReference.Create(GameInput.UICancel);
        }
    }
}
