using DG.Tweening;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
using UnityEngine;
using UnityEngine.Serialization;

public class SwingingAxe : MonoBehaviour
{
    [SerializeField] private Rigidbody2D handPivotRb;

    [SerializeField] private float swingDuration = 3f;
    [FormerlySerializedAs("swingAngle")] [SerializeField] private float swingAngleDeg = 43f;
    private float startAngleDeg; //Angle at which the part was placed

    private TweenerCore<float, float, FloatOptions> handTween;

    [SerializeField] private Collider2D deathCollider;

    public void InitializeTweens() //Call through event
    {
        startAngleDeg = transform.eulerAngles.z;

        handTween = handPivotRb
            .DORotate( startAngleDeg - swingAngleDeg, swingDuration)
            .ChangeStartValue(startAngleDeg + swingAngleDeg)
            .SetLoops(-1, LoopType.Yoyo)
            .SetEase(Ease.InOutSine)
            .SetAutoKill(false);
    }

    public void OnDestroy()
    {
        handTween.Kill();
    }


    public void StartSpinning() //Call through event
    {
        TimeManager.Singleton.OnAfterPhysicsTick += KillTouching;

        handTween.Restart();
    }

    public void StopSpinning() //Call through event
    {
        TimeManager.Singleton.OnAfterPhysicsTick -= KillTouching;
        
        handTween.Pause();
        
        //Go to start angle
        handPivotRb.rotation = 0;
    }

    private void KillTouching()
    {
        var isTouchingLocal = deathCollider.IsTouching(PlayerPresence.LocalPlayer.PlayerCharacter.PlayerCollider);
        if (!isTouchingLocal)
            return;

        PlayerPresence.LocalPlayer.PlayerCharacter.KillPlayerServerRpc();
    }
}