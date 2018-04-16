namespace Microsoft.Azure.SignalR
{
    public interface IMessageCounters
    {
        void AddIncomingMessageCount(long messageCount);

        void AddIncomingMessageItemsCount(long messageCount);

        void AddOutgoingMessageCount(long messageCount);

        void AddPingCount(long messageCount);

        long GetIncomingMessageCount();

        long GetIncomingMessageItemsCount();

        long GetOutgoingMessageCount();

        long GetPingCount();
    }
}
