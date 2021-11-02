using System;

namespace QuikBridge.Entities
{
    public class MessageIndexer
    {
        const int maxId = 400;
        int LastTimeIndex = 0;
        object _locker = new Object();

        public int GetIndex(int traderId = 0)
        {
            int currenttimeindex = Convert.ToInt32((DateTime.Now - DateTime.Today).TotalMilliseconds / 10.0) - 3570000;
            lock (_locker)
            {
                currenttimeindex = (currenttimeindex <= LastTimeIndex) ? LastTimeIndex + 1 : currenttimeindex;
                LastTimeIndex = currenttimeindex;
            }
            return maxId * currenttimeindex + traderId;
        }

        public int GetNumberFromMsgId(int msgId)
        {
            return msgId % maxId;
        }
    }
}