namespace Microsoft.Azure.SignalR
{
    public interface IMessageCounters
    {
        void AddIncomingMessageCount(long messageCount);

        void AddOutgoingMessageCount(long messageCount);

        void AddPingCount(long messageCount);

        long GetIncomingMessageCount();

        long GetOutgoingMessageCount();

        long GetPingCount();
    }
}
