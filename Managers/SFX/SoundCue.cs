using System.IO;

namespace CustomAssetLoader.Managers.SFX;

/// <summary>
/// One Unity AudioTrack clip converted to FMOD Core playback.
/// A struct so each ticker owns its trigger state when the list is copied.
/// </summary>
public struct SoundCue
{
    public float StartTime;
    public float ClipIn;
    public float Duration;
    public byte[] WavData;
    public bool Triggered;
    public ActivePlayback Playback;
}

/// <summary>
/// A Core sound this plugin created. Releasing it does not touch Studio events.
/// </summary>
public sealed class ActivePlayback
{
    public FMOD.Sound Sound;
    public FMOD.Channel Channel;
    public string TempPath;
    public bool HeardPlaying;

    public void SetPaused(bool paused)
    {
        if (Channel.hasHandle())
        {
            Channel.setPaused(paused);
        }
    }

    public bool IsPlaying()
    {
        if (!Channel.hasHandle())
        {
            return false;
        }

        Channel.isPlaying(out bool playing);
        if (playing)
        {
            HeardPlaying = true;
        }

        return playing;
    }

    public void Release()
    {
        if (Channel.hasHandle())
        {
            Channel.stop();
            Channel = default;
        }

        if (Sound.hasHandle())
        {
            Sound.release();
            Sound = default;
        }

        if (string.IsNullOrEmpty(TempPath))
        {
            return;
        }

        try
        {
            if (File.Exists(TempPath))
            {
                File.Delete(TempPath);
            }
        }
        catch
        {
            // The sample may still be opening the file. The next release retries.
        }

        if (!File.Exists(TempPath))
        {
            TempPath = null;
        }
    }
}
