using System;
using DG.Tweening;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
using UnityEngine;

public class SlidingPlatform : MonoBehaviour, IDisposable
{
    [SerializeField] private Transform pointA;
    [SerializeField] private Transform pointB;
    [SerializeField] private Rigidbody2D body;

    private float slideTime = 5f;

    TweenerCore<Vector2, Vector2, VectorOptions> tween;

    private Vector2 beforeUpdatePos;

    public void Initialize() //Called through event
    {
        // Cache
        var pointAPosition = pointA.position;

        tween = body
            .DOMove(pointB.position, slideTime)
            .ChangeStartValue(pointAPosition)
            .SetEase(Ease.Linear)
            .SetLoops(-1, LoopType.Yoyo)
            .SetUpdate(UpdateType.Manual);

        TimeManager.Singleton.OnBeforePhysicsTick += OnBeforePhysics;
    }

    private void OnBeforePhysics()
    {
        beforeUpdatePos = body.position;
        
        tween.ManualUpdate(TimeManager.Singleton.DelayBetweenPhysicsTicks, TimeManager.Singleton.DelayBetweenPhysicsTicks);

        var afterMovePos = body.position;
        var deltaPos = afterMovePos - beforeUpdatePos;

        if (body.IsTouching(PlayerPresence.LocalPlayer.PlayerCharacter.PlayerCollider))
        {
            var deltaPosInTime = deltaPos / TimeManager.Singleton.DelayBetweenPhysicsTicks;

            var velocityToAdd = new Vector2(deltaPosInTime.x, 0); // ignore y
            PlayerPresence.LocalPlayer.PlayerCharacter.PlayerMotor.AddExternalForceForThisTick(velocityToAdd);
        }
    }

    public void StartSliding() //Called through event
    {
        // Go to point A
        body.transform.position = pointA.position;

        tween.Restart();
    }

    public void StopSliding() //Called through event
    {
        tween.Pause();

        // Go back to middle
        body.transform.localPosition = Vector3.zero;
    }

    public void Dispose()
    {
        TimeManager.Singleton.OnBeforePhysicsTick -= OnBeforePhysics;
    }
}