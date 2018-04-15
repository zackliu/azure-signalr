using System.Threading;

namespace Microsoft.Azure.SignalR
{
    public class MessageCounters : IMessageCounters
    {
        private long _incomingMessageCounter;
        private long _outgoingMessageCounter;

        public void AddIncomingMessageCount(long messageCount)
        {
            Interlocked.Add(ref _incomingMessageCounter, messageCount);
        }

        public void AddOutgoingMessageCount(long messageCount)
        {
            Interlocked.Add(ref _outgoingMessageCounter, messageCount);
        }

        public long GetIncomingMessageCount()
        {
            return Interlocked.Read(ref _incomingMessageCounter);
        }

        public long GetOutgoingMessageCount()
        {
            return Interlocked.Read(ref _outgoingMessageCounter);
        }
    }
}
