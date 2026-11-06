using System.Reflection;
using CustomAssetLoader.Managers.SFX;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine.Playables;

namespace CustomAssetLoader.Patches.Features;

/// <summary>
/// Binds wav/ogg Timeline cues on custom appearances.
/// FMOD Studio event tracks and custom banks are left to the game.
/// </summary>
public static class TimelineSfxPatches
{
    public static void Apply(Harmony harmony)
    {
        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<SoundCueTicker>();
        }
        catch (Exception ex)
        {
            BundleLog.Error($"[TimelineSfx] failed to register SoundCueTicker: {ex.Message}");
            return;
        }

        MethodInfo changeMotion = AccessTools.Method(
            typeof(SD.CharacterAppearance),
            nameof(SD.CharacterAppearance.ChangeMotion));
        if (changeMotion == null)
        {
            BundleLog.Error("[TimelineSfx] patch target missing: CharacterAppearance.ChangeMotion");
            return;
        }

        MethodInfo stop = AccessTools.Method(typeof(PlayableDirector), nameof(PlayableDirector.Stop), Type.EmptyTypes);
        harmony.Patch(changeMotion, postfix: new HarmonyMethod(typeof(TimelineSfxPatches), nameof(Postfix_ChangeMotion)));
        if (stop != null)
        {
            harmony.Patch(stop, postfix: new HarmonyMethod(typeof(TimelineSfxPatches), nameof(Postfix_Stop)));
        }

        BundleLog.Startup("[TimelineSfx] Unity AudioTrack cues enabled. FMOD event tracks are unchanged.");
    }

    public static void Postfix_ChangeMotion(
        SD.CharacterAppearance __instance,
        bool muteSound)
    {
        if (__instance == null)
        {
            return;
        }

        BundleInstanceTag tag = __instance.GetComponentInParent<BundleInstanceTag>();
        if (tag == null)
        {
            return;
        }

        try
        {
            PlayableDirector director = __instance._playableDirector;
            if (director == null)
            {
                return;
            }

            SoundCueTicker ticker = director.GetComponent<SoundCueTicker>();
            if (ticker == null)
            {
                ticker = director.gameObject.AddComponent<SoundCueTicker>();
            }

            ticker.Arm(director, tag.BundleKey, muteSound);
        }
        catch (Exception ex)
        {
            BundleLog.Error($"[TimelineSfx] ChangeMotion failed: {ex.Message}");
        }
    }

    public static void Postfix_Stop(PlayableDirector __instance)
    {
        if (__instance == null)
        {
            return;
        }

        try
        {
            SoundCueTicker ticker = __instance.GetComponent<SoundCueTicker>();
            ticker?.StopAllCues();
        }
        catch (Exception ex)
        {
            BundleLog.Error($"[TimelineSfx] Stop failed: {ex.Message}");
        }
    }
}
