namespace Pixie
{
    public static class ExtensionMethods
    {
        public static bool IsExiting(this ConnectionState state) =>
            state == ConnectionState.PendingDisconnect || state == ConnectionState.Disconnected;
    }
}