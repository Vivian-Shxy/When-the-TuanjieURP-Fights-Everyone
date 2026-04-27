using UnityEngine;

public class DemoAudioManager : MonoBehaviour
{
    [Header("Audio Clips")]
    public AudioClip backstageAmbience;
    public AudioClip stageMusic;

    [Header("Audio Source")]
    public AudioSource audioSource;

    private void Awake()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if (audioSource != null)
        {
            audioSource.playOnAwake = false;
            audioSource.loop = true;
        }
    }

    public void PlayBackstageAmbience()
    {
        PlayClip(backstageAmbience);
    }

    public void PlayStageMusic()
    {
        PlayClip(stageMusic);
    }

    public void StopAudio()
    {
        if (audioSource != null)
        {
            audioSource.Stop();
        }
    }

    private void PlayClip(AudioClip clip)
    {
        if (audioSource == null || clip == null)
        {
            return;
        }

        if (audioSource.clip == clip && audioSource.isPlaying)
        {
            return;
        }

        audioSource.clip = clip;
        audioSource.Play();
    }
}