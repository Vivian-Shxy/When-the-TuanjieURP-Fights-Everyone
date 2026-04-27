using UnityEngine;

public class GrabSoundProp : MonoBehaviour
{
    [Header("Audio Manager")]
    public DemoAudioManager audioManager;

    [Header("Debug Controls")]
    public bool simulateGrabWithKey = true;
    public KeyCode grabKey = KeyCode.G;

    private bool isGrabbed = false;

    private void Update()
    {
        if (!simulateGrabWithKey)
        {
            return;
        }

        if (Input.GetKeyDown(grabKey))
        {
            if (isGrabbed)
            {
                ReleaseProp();
            }
            else
            {
                GrabProp();
            }
        }
    }

    public void GrabProp()
    {
        isGrabbed = true;

        if (audioManager != null)
        {
            audioManager.PlayBackstageAmbience();
        }
    }

    public void ReleaseProp()
    {
        isGrabbed = false;

        if (audioManager != null)
        {
            audioManager.StopAudio();
        }
    }

    public bool IsGrabbed()
    {
        return isGrabbed;
    }
}