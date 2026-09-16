namespace FaceSlapper.Networking
{
    /// <summary>RPC 传输通道；不可靠通道不保证到达或顺序。</summary>
    public enum NetChannel
    {
        Reliable,
        Unreliable
    }
}
