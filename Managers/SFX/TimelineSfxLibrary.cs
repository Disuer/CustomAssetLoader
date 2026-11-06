using System.IO;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CustomAssetLoader.Managers.SFX;

/// <summary>
/// Cues extracted from mod bundles. Lookup is by timeline instance id, then by
/// bundle key plus timeline name. A name that exists twice in one bundle is not guessed.
/// </summary>
public static class TimelineSfxLibrary
{
    private sealed class NameEntry
    {
        public int InstanceId;
        public List<SoundCue> Cues;
        public bool Ambiguous;
    }

    private static readonly Dictionary<int, List<SoundCue>> ByInstanceId = new();
    private static readonly Dictionary<string, Dictionary<string, NameEntry>> ByBundleAndName =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Dictionary<string, byte[]>> AudioByBundle =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> LoggedAmbiguous = new(StringComparer.OrdinalIgnoreCase);

    public static void Clear()
    {
        ByInstanceId.Clear();
        ByBundleAndName.Clear();
        AudioByBundle.Clear();
        LoggedAmbiguous.Clear();
    }

    public static void ScanBundle(AssetBundle bundle, string bundleKey)
    {
        if (bundle == null || string.IsNullOrEmpty(bundleKey))
        {
            return;
        }

        string[] names = bundle.GetAllAssetNames();
        if (names == null || names.Length == 0)
        {
            return;
        }

        var audio = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var playables = new List<string>();
        foreach (string assetName in names)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                continue;
            }

            if (assetName.EndsWith(".playable", StringComparison.OrdinalIgnoreCase))
            {
                playables.Add(assetName);
                continue;
            }

            if (!TryGetAudioClipKey(assetName, out string clipKey))
            {
                continue;
            }

            byte[] bytes = LoadAudioBytes(bundle, assetName);
            if (bytes == null)
            {
                continue;
            }

            if (!audio.ContainsKey(clipKey))
            {
                audio[clipKey] = bytes;
            }
            else
            {
                BundleLog.Warn(
                    $"[TimelineSfx] '{bundleKey}' has more than one audio file named '{clipKey}'. Keeping the first.");
            }
        }

        AudioByBundle[bundleKey] = audio;
        int registered = 0;
        foreach (string playableName in playables)
        {
            TimelineAsset timeline = LoadTimeline(bundle, playableName);
            if (timeline != null && Register(bundleKey, timeline, audio))
            {
                registered++;
            }
        }

        BundleLog.Verbose(
            $"[TimelineSfx] '{bundleKey}': {audio.Count} audio file(s), {registered} timeline(s) with Unity audio cues.");
    }

    public static void ScanPrefab(GameObject prefab, string bundleKey)
    {
        if (prefab == null || string.IsNullOrEmpty(bundleKey))
        {
            return;
        }

        if (!AudioByBundle.TryGetValue(bundleKey, out Dictionary<string, byte[]> audio) || audio.Count == 0)
        {
            return;
        }

        SD.CharacterAppearance[] appearances = prefab.GetComponentsInChildren<SD.CharacterAppearance>(true);
        if (appearances == null)
        {
            return;
        }

        foreach (SD.CharacterAppearance appearance in appearances)
        {
            if (appearance == null)
            {
                continue;
            }

            PlayableDirector director = appearance._playableDirector;
            TimelineAsset directorTimeline = director?.playableAsset?.TryCast<TimelineAsset>();
            if (directorTimeline != null)
            {
                Register(bundleKey, directorTimeline, audio);
            }

            if (appearance.motionList == null)
            {
                continue;
            }

            foreach (SD.CharacterMotion motion in appearance.motionList)
            {
                if (motion?.timelineAssets == null)
                {
                    continue;
                }

                foreach (TimelineAsset timeline in motion.timelineAssets)
                {
                    Register(bundleKey, timeline, audio);
                }
            }
        }
    }

    public static bool TryGet(PlayableAsset asset, string bundleKey, out List<SoundCue> cues)
    {
        cues = null;
        if (asset == null)
        {
            return false;
        }

        if (ByInstanceId.TryGetValue(asset.GetInstanceID(), out cues))
        {
            return true;
        }

        if (string.IsNullOrEmpty(bundleKey) || string.IsNullOrEmpty(asset.name))
        {
            return false;
        }

        if (!ByBundleAndName.TryGetValue(bundleKey, out Dictionary<string, NameEntry> names)
            || !names.TryGetValue(asset.name, out NameEntry entry)
            || entry == null)
        {
            return false;
        }

        if (entry.Ambiguous || entry.Cues == null)
        {
            string logKey = bundleKey + "\0" + asset.name;
            if (LoggedAmbiguous.Add(logKey))
            {
                BundleLog.Warn(
                    $"[TimelineSfx] '{asset.name}' is used by more than one timeline in '{bundleKey}'. " +
                    "Those cues are not played, so the wrong bundle cannot be chosen.");
            }

            return false;
        }

        cues = entry.Cues;
        return true;
    }

    public static List<SoundCue> Copy(List<SoundCue> source)
    {
        var copy = new List<SoundCue>(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            SoundCue cue = source[i];
            cue.Triggered = false;
            cue.Playback = null;
            copy.Add(cue);
        }

        return copy;
    }

    private static bool Register(string bundleKey, TimelineAsset timeline, Dictionary<string, byte[]> audio)
    {
        if (timeline == null)
        {
            return false;
        }

        List<SoundCue> cues = TimelineCueExtractor.Extract(timeline, audio);
        if (cues.Count == 0)
        {
            return false;
        }

        int id = timeline.GetInstanceID();
        ByInstanceId[id] = cues;

        if (string.IsNullOrEmpty(timeline.name))
        {
            return true;
        }

        if (!ByBundleAndName.TryGetValue(bundleKey, out Dictionary<string, NameEntry> names))
        {
            names = new Dictionary<string, NameEntry>(StringComparer.OrdinalIgnoreCase);
            ByBundleAndName[bundleKey] = names;
        }

        if (names.TryGetValue(timeline.name, out NameEntry existing))
        {
            if (existing.InstanceId != id)
            {
                existing.Ambiguous = true;
                existing.Cues = null;
                BundleLog.Warn(
                    $"[TimelineSfx] '{bundleKey}' contains multiple timelines named '{timeline.name}'. " +
                    "Name fallback is disabled for that name.");
            }
            else
            {
                existing.Cues = cues;
            }
        }
        else
        {
            names[timeline.name] = new NameEntry
            {
                InstanceId = id,
                Cues = cues,
            };
        }

        return true;
    }

    private static bool TryGetAudioClipKey(string assetPath, out string clipKey)
    {
        clipKey = null;
        string fileName = Path.GetFileName(assetPath);
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        fileName = fileName.ToLowerInvariant();
        const string wavSuffix = ".wav.bytes";
        const string oggSuffix = ".ogg.bytes";
        if (fileName.EndsWith(wavSuffix, StringComparison.Ordinal))
        {
            clipKey = fileName.Substring(0, fileName.Length - wavSuffix.Length);
        }
        else if (fileName.EndsWith(oggSuffix, StringComparison.Ordinal))
        {
            clipKey = fileName.Substring(0, fileName.Length - oggSuffix.Length);
        }

        return !string.IsNullOrEmpty(clipKey);
    }

    private static byte[] LoadAudioBytes(AssetBundle bundle, string assetName)
    {
        try
        {
            UnityEngine.Object loaded = bundle.LoadAsset(assetName, Il2CppType.Of<TextAsset>());
            TextAsset text = loaded?.TryCast<TextAsset>();
            if (text == null || text.bytes == null || text.bytes.Length == 0)
            {
                return null;
            }

            var copy = new byte[text.bytes.Length];
            text.bytes.CopyTo(copy, 0);
            if (!LooksLikeWavOrOgg(copy))
            {
                BundleLog.Warn($"[TimelineSfx] '{assetName}' is not wav or ogg. It will not be played.");
                return null;
            }

            return copy;
        }
        catch (Exception ex)
        {
            BundleLog.Warn($"[TimelineSfx] failed to read '{assetName}': {ex.Message}");
            return null;
        }
    }

    private static TimelineAsset LoadTimeline(AssetBundle bundle, string assetName)
    {
        try
        {
            UnityEngine.Object loaded = bundle.LoadAsset(assetName, Il2CppType.Of<TimelineAsset>());
            return loaded?.TryCast<TimelineAsset>();
        }
        catch (Exception ex)
        {
            BundleLog.Warn($"[TimelineSfx] failed to read timeline '{assetName}': {ex.Message}");
            return null;
        }
    }

    private static bool LooksLikeWavOrOgg(byte[] data)
    {
        if (data.Length < 4)
        {
            return false;
        }

        bool wav = data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F';
        bool ogg = data[0] == (byte)'O' && data[1] == (byte)'g' && data[2] == (byte)'g' && data[3] == (byte)'S';
        return wav || ogg;
    }
}
