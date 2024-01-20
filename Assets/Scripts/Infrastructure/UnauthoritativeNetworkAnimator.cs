using Unity.Netcode.Components;

public class UnauthoritativeNetworkAnimator : NetworkAnimator
{
    protected override bool OnIsServerAuthoritative()
    {
        return false;
    }
}
