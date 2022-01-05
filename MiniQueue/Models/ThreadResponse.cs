using System.Net;

namespace MiniQueue.Models
{
    public class ThreadResponse<T> where T : notnull
    {
        public HttpStatusCode? Status { get; set; }
        public string Message { get; set; }
        public T Data { get; set; }
    }
}
