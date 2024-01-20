using DG.Tweening;
using UnityEngine;

public class SpinningSquare : MonoBehaviour
{
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private float quarterRotationDuration = 1f;
    [SerializeField] private float delayBetweenRotations = 1f;
    private Sequence sequence;


    public void Initialize() //Called through event
    {
        sequence = DOTween.Sequence();

        sequence.AppendInterval(delayBetweenRotations);
        sequence.Append(rb
            .DORotate(90, quarterRotationDuration)
            .ChangeStartValue(0));
        sequence.AppendInterval(delayBetweenRotations);
        sequence.Append(rb
            .DORotate(180, quarterRotationDuration)
            .ChangeStartValue(90));
        sequence.AppendInterval(delayBetweenRotations);
        sequence.Append(rb
            .DORotate(270, quarterRotationDuration)
            .ChangeStartValue(180));
        sequence.AppendInterval(delayBetweenRotations);
        sequence.Append(rb
            .DORotate(360, quarterRotationDuration)
            .ChangeStartValue(270));

        sequence.SetLoops(-1);
    }
    public void StartSpinning() //Called through event
    {
        //StartCoroutine(SpinningEnumerator());
        sequence.Restart();
    }

    public void StopSpinning() //Called through event
    {
        sequence.Rewind();

        rb.rotation = 0;
    }
}
