using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// STOP IT! — GameAudio
/// Tiny self-bootstrapping SFX helper. On first use it loads every AudioClip under
/// <c>Resources/Audio</c> and plays them by name. Gameplay scripts just call:
///   • <see cref="Play"/>       — 2D one-shot (UI / global beats, e.g. the game-over cry).
///   • <see cref="PlayAt"/>     — 3D one-shot at a world position (in-world beats).
///   • <see cref="Loop"/> / <see cref="StopLoop"/> — a continuous sound (e.g. skating).
/// No scene wiring is required: it spawns its own persistent GameObject and auto-loads the clips,
/// and every call is null-safe (a missing clip just logs a warning).
/// </summary>
public class GameAudio : MonoBehaviour
{
    private static GameAudio _instance;
    private readonly Dictionary<string, AudioClip> _clips = new();
    private readonly Dictionary<string, AudioSource> _loops = new();
    private AudioSource _oneShot;

    public static GameAudio Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("GameAudio");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<GameAudio>();
                _instance.Init();
            }
            return _instance;
        }
    }

    private void Init()
    {
        _oneShot = gameObject.AddComponent<AudioSource>();
        _oneShot.playOnAwake = false;
        _oneShot.spatialBlend = 0f; // 2D
        foreach (var c in Resources.LoadAll<AudioClip>("Audio"))
            if (c != null) _clips[c.name] = c;
        Debug.Log($"[GameAudio] Loaded {_clips.Count} clips from Resources/Audio.");
    }

    private AudioClip Get(string clipName)
    {
        if (_clips.TryGetValue(clipName, out var c)) return c;
        Debug.LogWarning($"[GameAudio] Clip '{clipName}' not found in Resources/Audio.");
        return null;
    }

    /// <summary>2D one-shot — for UI / global events (no world position).</summary>
    public static void Play(string clipName, float volume = 1f)
    {
        var c = Instance.Get(clipName);
        if (c != null) Instance._oneShot.PlayOneShot(c, volume);
    }

    /// <summary>3D one-shot at a world position — for in-world events.</summary>
    public static void PlayAt(string clipName, Vector3 pos, float volume = 1f)
    {
        var c = Instance.Get(clipName);
        if (c != null) AudioSource.PlayClipAtPoint(c, pos, volume);
    }

    /// <summary>Start (or restart) a looping sound. Idempotent per name.</summary>
    public static void Loop(string clipName, float volume = 1f)
    {
        var inst = Instance;
        var c = inst.Get(clipName);
        if (c == null) return;
        if (!inst._loops.TryGetValue(clipName, out var src) || src == null)
        {
            src = inst.gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = true;
            src.spatialBlend = 0f;
            inst._loops[clipName] = src;
        }
        src.clip = c;
        src.volume = volume;
        if (!src.isPlaying) src.Play();
    }

    /// <summary>Stop a looping sound started with <see cref="Loop"/>.</summary>
    public static void StopLoop(string clipName)
    {
        if (_instance != null && _instance._loops.TryGetValue(clipName, out var src) && src != null)
            src.Stop();
    }
}
