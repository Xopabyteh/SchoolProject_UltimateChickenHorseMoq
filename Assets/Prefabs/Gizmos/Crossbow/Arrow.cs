using UnityEngine;

public class Arrow : MonoBehaviour
{
    private void OnCollisionEnter2D(Collision2D other)
    {
        //If we hit player, that we own local player, kill it
        //Break arrow (whatever, whoever we hit)

        if (other.gameObject.TryGetComponent<PlayerCharacter>(out var character))
        {
            if (character.IsOwner)
            {
                character.KillPlayerServerRpc();
            }
        }

        Destroy(gameObject);
    }
}
