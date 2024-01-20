using DG.Tweening;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
using UnityEngine;

public class SpinningSaw : MonoBehaviour
{
    [SerializeField] private Transform handPivot;
    [SerializeField] private Transform sawPivot;

    [SerializeField] private float handClockCycleDuration = 3f;
    [SerializeField] private float sawClockCycleDuration = 1f;
    private TweenerCore<Quaternion, Vector3, QuaternionOptions> handTween;
    private TweenerCore<Quaternion, Vector3, QuaternionOptions> sawTween;

    [SerializeField] private Collider2D sawCollider;

    public void InitializeTweens() //Call through event
    {
        handTween = handPivot
            .DOLocalRotate(new Vector3(0, 0, 360), handClockCycleDuration, RotateMode.FastBeyond360)
            .SetLoops(-1, LoopType.Incremental)
            .SetEase(Ease.Linear)
            .SetAutoKill(false);

        sawTween = sawPivot
            .DORotate(new Vector3(0, 0, 360), sawClockCycleDuration, RotateMode.FastBeyond360)
            .SetLoops(-1, LoopType.Incremental)
            .SetEase(Ease.Linear)
            .SetAutoKill(false);
    }

    public void OnDestroy()
    {
        handTween.Kill();
        sawTween.Kill();
    }


    public void StartSpinning() //Call through event
    {
        TimeManager.Singleton.OnAfterPhysicsTick += KillTouchingSaw;

        handTween.Restart();
        handTween.Play();

        sawTween.Play();
    }

    public void StopSpinning() //Call through event
    {
        TimeManager.Singleton.OnAfterPhysicsTick -= KillTouchingSaw;
        
        sawTween.Pause();

        handTween.Rewind();
    }

    private void KillTouchingSaw()
    {
        var isTouchingLocal = sawCollider.IsTouching(PlayerPresence.LocalPlayer.PlayerCharacter.PlayerCollider);
        if (!isTouchingLocal)
            return;

        PlayerPresence.LocalPlayer.PlayerCharacter.KillPlayerServerRpc();
    }
}
