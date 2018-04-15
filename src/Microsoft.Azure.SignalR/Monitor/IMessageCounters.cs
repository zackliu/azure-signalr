namespace Microsoft.Azure.SignalR
{
    public interface IMessageCounters
    {
        void AddIncomingMessageCount(long messageCount);

        void AddOutgoingMessageCount(long messageCount);

        long GetIncomingMessageCount();

        long GetOutgoingMessageCount();
    }
}
