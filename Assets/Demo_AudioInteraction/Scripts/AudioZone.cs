using UnityEngine;

public class AudioZone : MonoBehaviour
{
    public enum ZoneType
    {
        Backstage,
        Stage
    }

    [Header("Zone Settings")]
    public ZoneType zoneType = ZoneType.Backstage;

    private void OnTriggerEnter(Collider other)
    {
        GrabSoundProp prop = other.GetComponent<GrabSoundProp>();

        if (prop == null || !prop.IsGrabbed())
        {
            return;
        }

        DemoAudioManager audioManager = prop.audioManager;

        if (audioManager == null)
        {
            return;
        }

        switch (zoneType)
        {
            case ZoneType.Backstage:
                audioManager.PlayBackstageAmbience();
                break;

            case ZoneType.Stage:
                audioManager.PlayStageMusic();
                break;
        }
    }
}