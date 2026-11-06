using System.IO;

namespace CustomAssetLoader.Managers.SFX;

/// <summary>
/// Plays a wav/ogg byte buffer through FMOD Core.
/// Does not load banks and does not create Studio events.
/// </summary>
public static class FMODAudioUtil
{
    public static ActivePlayback Play(byte[] audioData, float clipIn, float volume)
    {
        if (audioData == null || audioData.Length == 0)
        {
            return null;
        }

        string ext = DetectExtension(audioData);
        string tempPath = Path.Combine(
            Path.GetTempPath(),
            "CustomAssetLoader_" + Guid.NewGuid().ToString("N") + ext);
        var playback = new ActivePlayback { TempPath = tempPath };

        try
        {
            File.WriteAllBytes(tempPath, audioData);

            if (!FMODUnity.RuntimeManager.IsInitialized)
            {
                BundleLog.VerboseWarn("[TimelineSfx] FMOD is not initialized; skipped a cue.");
                return null;
            }

            FMOD.System core = FMODUnity.RuntimeManager.CoreSystem;
            if (!core.hasHandle())
            {
                return null;
            }

            // CREATESAMPLE reads the whole file before returning, so playback
            // does not depend on the temp file staying on disk.
            FMOD.MODE mode = FMOD.MODE.CREATESAMPLE | FMOD.MODE.LOOP_OFF | FMOD.MODE._2D;
            FMOD.RESULT result = core.createSound(tempPath, mode, out FMOD.Sound sound);
            if (result != FMOD.RESULT.OK)
            {
                BundleLog.Error($"[TimelineSfx] createSound failed ({result}) for {ext} ({audioData.Length} bytes).");
                return null;
            }

            playback.Sound = sound;
            core.getMasterChannelGroup(out FMOD.ChannelGroup group);
            result = core.playSound(sound, group, false, out FMOD.Channel channel);
            if (result != FMOD.RESULT.OK)
            {
                BundleLog.Error($"[TimelineSfx] playSound failed ({result}).");
                playback.Release();
                return null;
            }

            playback.Channel = channel;
            channel.setVolume(volume);
            if (clipIn > 0f)
            {
                channel.setPosition((uint)(clipIn * 1000f), FMOD.TIMEUNIT.MS);
            }

            return playback;
        }
        catch (Exception ex)
        {
            BundleLog.Error($"[TimelineSfx] playback failed: {ex.Message}");
            playback.Release();
            return null;
        }
        finally
        {
            // A live Core sound deletes its file from Release, after Sound.release.
            if (!playback.Sound.hasHandle())
            {
                TryDeleteTemp(playback);
            }
        }
    }

    public static float ReadSfxVolume()
    {
        try
        {
            SoundManager manager = SoundManager.Instance;
            if (manager != null)
            {
                return manager.Volume_SFX;
            }
        }
        catch
        {
            // SoundManager may not exist yet. Play at full volume rather than drop the cue.
        }

        return 1f;
    }

    private static void TryDeleteTemp(ActivePlayback playback)
    {
        if (playback == null || string.IsNullOrEmpty(playback.TempPath))
        {
            return;
        }

        try
        {
            if (File.Exists(playback.TempPath))
            {
                File.Delete(playback.TempPath);
            }
        }
        catch
        {
            return;
        }

        if (!File.Exists(playback.TempPath))
        {
            playback.TempPath = null;
        }
    }

    private static string DetectExtension(byte[] data)
    {
        if (data.Length >= 4
            && data[0] == (byte)'R'
            && data[1] == (byte)'I'
            && data[2] == (byte)'F'
            && data[3] == (byte)'F')
        {
            return ".wav";
        }

        if (data.Length >= 4
            && data[0] == (byte)'O'
            && data[1] == (byte)'g'
            && data[2] == (byte)'g'
            && data[3] == (byte)'S')
        {
            return ".ogg";
        }

        return ".dat";
    }
}
