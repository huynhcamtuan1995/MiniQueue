using System;
using System.Threading;

namespace MiniQueue.Models
{
    public class ThreadRequest<TRequest, TResponse>
        where TRequest : notnull
        where TResponse : notnull
    {
        public AutoResetEvent Event = new AutoResetEvent(false);

        public ThreadRequest(TRequest requestData)
        {
            Guid = System.Guid.NewGuid().ToString("N");
            RequestData = requestData;
        }

        public string Guid { get; }

        public TRequest RequestData { get; }
        public DateTime CreateAt { get; set; } = DateTime.Now;

        public ThreadResponse<TResponse> Response { get; set; }

        public bool IsExpire()
        {
            return CreateAt.AddSeconds(30) <= DateTime.Now;
        }
    }
}