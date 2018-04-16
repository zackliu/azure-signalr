using System.Threading;

namespace Microsoft.Azure.SignalR
{
    public class MessageCounters : IMessageCounters
    {
        private long _incomingMessageCounter;
        private long _incomingMessageItemsCounter;
        private long _outgoingMessageCounter;
        private long _pingCounter;

        public void AddIncomingMessageCount(long messageCount)
        {
            Interlocked.Add(ref _incomingMessageCounter, messageCount);
        }

        public void AddIncomingMessageItemsCount(long messageCount)
        {
            Interlocked.Add(ref _incomingMessageItemsCounter, messageCount);
        }

        public void AddOutgoingMessageCount(long messageCount)
        {
            Interlocked.Add(ref _outgoingMessageCounter, messageCount);
        }

        public void AddPingCount(long messageCount)
        {
            Interlocked.Add(ref _pingCounter, messageCount);
        }

        public long GetIncomingMessageCount()
        {
            return Interlocked.Read(ref _incomingMessageCounter);
        }

        public long GetIncomingMessageItemsCount()
        {
            return Interlocked.Read(ref _incomingMessageItemsCounter);
        }

        public long GetOutgoingMessageCount()
        {
            return Interlocked.Read(ref _outgoingMessageCounter);
        }

        public long GetPingCount()
        {
            return Interlocked.Read(ref _pingCounter);
        }
    }
}
