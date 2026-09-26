using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;

namespace PartyTime
{
    [HarmonyPatch(typeof(MusicManager), "GetRandomProductiveMusic")]
    internal static class MusicManager_GetRandomProductiveMusic_Patch
    {
        private static bool Prefix(MusicManager __instance, ref AudioResource __result)
        {
            var mod = PartyTimeMod.Instance;
            if (mod == null) return true;
            try
            {
                if (!mod.Player.TryPick(__instance, out var song)) return true;
                __result = song;
                return false;
            }
            catch (Exception e)
            {
                mod.Report(e, "Picking a song");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(MusicManager), "Update")]
    internal static class MusicManager_Update_Patch
    {
        private static void Postfix(MusicManager __instance, AudioSource ___musicSource)
        {
            var mod = PartyTimeMod.Instance;
            if (mod == null || ___musicSource == null) return;
            try
            {
                mod.Player.AfterGameUpdate(__instance, ___musicSource);
            }
            catch (Exception e)
            {
                mod.Report(e, "Jukebox playback");
            }
        }
    }

    [HarmonyPatch]
    internal static class MusicManagerSwitchTo_Update_Patch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(AccessTools.Inner(typeof(MusicManager), "MusicManagerSwitchTo"), "Update");
        }

        private static void Prefix(ref float ___duration, bool ___fadedOut, AudioResource ___targetResource)
        {
            var mod = PartyTimeMod.Instance;
            if (mod == null || ___fadedOut) return;
            try
            {
                ___duration = mod.Player.FadeOutSeconds(___targetResource, ___duration);
            }
            catch (Exception e)
            {
                mod.Report(e, "Song change");
            }
        }
    }

    [HarmonyPatch]
    internal static class MusicManagerIdle_Update_Patch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(AccessTools.Inner(typeof(MusicManager), "MusicManagerIdle"), "Update");
        }

        private static void Prefix(ref float ___timer)
        {
            var mod = PartyTimeMod.Instance;
            if (mod == null) return;
            try
            {
                if (mod.Player.ShouldSkipIdleWait()) ___timer = JukeboxPlayer.GameIdleWaitSeconds;
            }
            catch (Exception e)
            {
                mod.Report(e, "Music start");
            }
        }
    }

    [HarmonyPatch(typeof(MusicManager), nameof(MusicManager.FadeOutMusic))]
    internal static class MusicManager_FadeOutMusic_Patch
    {
        private static void Postfix()
        {
            PartyTimeMod.Instance?.Player.KeepQuietForDialogue();
        }
    }

    [HarmonyPatch(typeof(InteractableJukebox), nameof(InteractableJukebox.Interact))]
    internal static class InteractableJukebox_Interact_Patch
    {
        private const float InteractTimeout = 1f;

        private static void Prefix(float ___lastInteractTime)
        {
            var mod = PartyTimeMod.Instance;
            if (mod == null || Time.time - ___lastInteractTime < InteractTimeout) return;
            try
            {
                mod.Player.BeforeJukeboxPress();
            }
            catch (Exception e)
            {
                mod.Report(e, "Jukebox press");
            }
        }
    }
}
