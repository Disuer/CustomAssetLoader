using Il2CppInterop.Runtime;
using UnityEngine.Timeline;

namespace CustomAssetLoader.Managers.SFX;

/// <summary>
/// Reads Unity AudioTrack clips. FMOD event tracks are ignored, so bank playback stays on Studio.
/// </summary>
public static class TimelineCueExtractor
{
    public static List<SoundCue> Extract(TimelineAsset timeline, IReadOnlyDictionary<string, byte[]> audioByClipName)
    {
        var cues = new List<SoundCue>();
        if (timeline == null || audioByClipName == null || audioByClipName.Count == 0)
        {
            return cues;
        }

        foreach (TrackAsset track in timeline.flattenedTracks)
        {
            if (!IsUnityAudioTrack(track))
            {
                continue;
            }

            foreach (TimelineClip clip in track.clips)
            {
                if (clip == null || !TryResolveBytes(clip.displayName, audioByClipName, out byte[] bytes))
                {
                    continue;
                }

                cues.Add(new SoundCue
                {
                    StartTime = (float)clip.start,
                    ClipIn = (float)clip.clipIn,
                    Duration = (float)clip.duration,
                    WavData = bytes,
                });
            }
        }

        return cues;
    }

    private static bool IsUnityAudioTrack(TrackAsset track)
    {
        if (track == null)
        {
            return false;
        }

        Il2CppSystem.Type il2CppType = track.GetIl2CppType();
        if (il2CppType == null)
        {
            return false;
        }

        string name = il2CppType.Name;
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (name.IndexOf("FMOD", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        string fullName = il2CppType.FullName;
        if (!string.IsNullOrEmpty(fullName)
            && fullName.IndexOf("FMOD", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        return string.Equals(name, "AudioTrack", StringComparison.Ordinal);
    }

    private static bool TryResolveBytes(
        string displayName,
        IReadOnlyDictionary<string, byte[]> audioByClipName,
        out byte[] bytes)
    {
        bytes = null;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return false;
        }

        string key = displayName.Trim().ToLowerInvariant();
        if (audioByClipName.TryGetValue(key, out bytes))
        {
            return true;
        }

        if (key.EndsWith(".wav", StringComparison.Ordinal))
        {
            key = key.Substring(0, key.Length - 4);
        }
        else if (key.EndsWith(".ogg", StringComparison.Ordinal))
        {
            key = key.Substring(0, key.Length - 4);
        }

        return audioByClipName.TryGetValue(key, out bytes);
    }
}
