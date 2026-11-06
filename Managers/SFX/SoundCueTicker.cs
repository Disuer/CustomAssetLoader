using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.Playables;

namespace CustomAssetLoader.Managers.SFX;

/// <summary>
/// Plays Unity AudioTrack cues for one custom appearance. Only stops channels it started.
/// </summary>
public sealed class SoundCueTicker : MonoBehaviour
{
    private const int PendingFrames = 8;

    public SoundCueTicker(IntPtr ptr) : base(ptr) { }

    public PlayableDirector Director;
    public string BundleKey;
    public List<SoundCue> SoundCues = new();

    private bool _pending;
    private int _attemptsLeft;
    private bool _failed;
    private bool _channelsPaused;
    private double _lastTime;

    private void Update()
    {
        if (_failed)
        {
            return;
        }

        try
        {
            if (_pending)
            {
                if (TryBind())
                {
                    _pending = false;
                }
                else if (--_attemptsLeft <= 0)
                {
                    _pending = false;
                    BundleLog.Verbose("[TimelineSfx] director had no timeline asset after ChangeMotion.");
                }

                return;
            }

            if (Director == null || SoundCues == null || SoundCues.Count == 0)
            {
                return;
            }

            PlayState state = Director.state;
            double now = Director.time;
            if (state == PlayState.Paused)
            {
                SetPaused(true);
                _lastTime = now;
                return;
            }

            SetPaused(false);
            bool timeMoved = now > _lastTime + 0.0001d;
            if (state == PlayState.Playing || timeMoved)
            {
                Tick((float)now, (float)_lastTime);
            }

            _lastTime = now;
        }
        catch (Exception ex)
        {
            _failed = true;
            StopAllCues();
            BundleLog.Error($"[TimelineSfx] ticker disabled: {ex.Message}");
        }
    }

    private void OnDestroy()
    {
        StopAllCues();
    }

    [HideFromIl2Cpp]
    public void Arm(PlayableDirector director, string bundleKey, bool mute)
    {
        Director = director;
        BundleKey = bundleKey;
        _failed = false;
        _lastTime = director != null ? director.time : 0d;
        StopAllCues();
        if (mute || director == null)
        {
            _pending = false;
            SoundCues = new List<SoundCue>();
            return;
        }

        if (TryBind())
        {
            _pending = false;
            return;
        }

        PlayableAsset asset = director.playableAsset;
        if (asset != null)
        {
            _pending = false;
            SoundCues = new List<SoundCue>();
            return;
        }

        _pending = true;
        _attemptsLeft = PendingFrames;
    }

    [HideFromIl2Cpp]
    public void StopAllCues()
    {
        if (SoundCues == null)
        {
            return;
        }

        for (int i = 0; i < SoundCues.Count; i++)
        {
            SoundCue cue = SoundCues[i];
            cue.Playback?.Release();
            cue.Playback = null;
            SoundCues[i] = cue;
        }

        _channelsPaused = false;
    }

    private bool TryBind()
    {
        if (Director == null)
        {
            return false;
        }

        PlayableAsset asset = Director.playableAsset;
        if (asset == null)
        {
            return false;
        }

        if (!TimelineSfxLibrary.TryGet(asset, BundleKey, out List<SoundCue> cues) || cues.Count == 0)
        {
            SoundCues = new List<SoundCue>();
            return true;
        }

        SoundCues = TimelineSfxLibrary.Copy(cues);
        _channelsPaused = false;
        BundleLog.Verbose($"[TimelineSfx] bound {SoundCues.Count} cue(s) on '{asset.name}'.");
        return true;
    }

    private void Tick(float time, float previousTime)
    {
        float volume = FMODAudioUtil.ReadSfxVolume();
        for (int i = 0; i < SoundCues.Count; i++)
        {
            SoundCue cue = SoundCues[i];
            // Only cues the clock just reached. A director already past a clip does not replay it.
            bool reached = time >= cue.StartTime && cue.StartTime + 0.05f >= previousTime;
            if (!cue.Triggered && reached)
            {
                cue.Triggered = true;
                cue.Playback = FMODAudioUtil.Play(cue.WavData, cue.ClipIn, volume);
                SoundCues[i] = cue;
            }

            ActivePlayback playback = cue.Playback;
            if (!cue.Triggered || playback == null)
            {
                continue;
            }

            bool playing = playback.IsPlaying();
            bool durationElapsed = cue.Duration > 0f && time >= cue.StartTime + cue.Duration;
            bool finished = playback.HeardPlaying && !playing;
            if (durationElapsed || finished)
            {
                playback.Release();
                cue.Playback = null;
                SoundCues[i] = cue;
            }
        }
    }

    private void SetPaused(bool paused)
    {
        if (_channelsPaused == paused || SoundCues == null)
        {
            return;
        }

        _channelsPaused = paused;
        for (int i = 0; i < SoundCues.Count; i++)
        {
            SoundCues[i].Playback?.SetPaused(paused);
        }
    }
}
