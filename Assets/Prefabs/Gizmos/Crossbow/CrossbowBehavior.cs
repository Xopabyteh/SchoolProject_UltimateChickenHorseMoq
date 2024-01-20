using UnityEngine;

public class CrossbowBehavior : MonoBehaviour
{
    [SerializeField] private GameObject arrowPrefab;
    [SerializeField] private Transform arrowSpawnPoint;
    [SerializeField] private float arrowSpeed;
    [SerializeField] private float arrowLifetime;
    [SerializeField] private float shootCooldownSeconds;
    private float shootTimer;
    [SerializeField] private Collider2D shooterCollider;

    public void StartShooting() //Call through event
    {
        shootTimer = shootCooldownSeconds;
        TimeManager.Singleton.OnTick += OnTick;
    }

    public void StopShooting() //Call through event
    {
        TimeManager.Singleton.OnTick -= OnTick;
    }

    private void OnTick()
    {
        shootTimer -= TimeManager.Singleton.DelayBetweenTicks;
        if (shootTimer <= 0)
        {
            shootTimer = shootCooldownSeconds;
            Shoot();
        }
    }

    private void Shoot()
    {
        var arrow = Instantiate(arrowPrefab, arrowSpawnPoint.position, transform.rotation);
        arrow.GetComponent<Rigidbody2D>().velocity = transform.up * arrowSpeed;

        Physics2D.IgnoreCollision(arrow.GetComponent<Collider2D>(), shooterCollider);

        Destroy(arrow, arrowLifetime);
    }
}
