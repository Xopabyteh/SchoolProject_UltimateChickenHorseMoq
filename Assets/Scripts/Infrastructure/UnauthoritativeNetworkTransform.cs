using Unity.Netcode.Components;

public class UnauthoritativeNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative()
    {
        return false;
    }
}
