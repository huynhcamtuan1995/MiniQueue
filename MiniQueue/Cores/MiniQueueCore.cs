using MiniQueue.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Timers;
using Timer = System.Timers.Timer;

namespace MiniQueue.Cores
{
    internal class MiniQueueCore<TRequest, TResponse>
        where TRequest : notnull
        where TResponse : notnull
    {
        private readonly ConcurrentDictionary<string, ThreadRequest<TRequest, TResponse>> _concurrentDictionary =
            new ConcurrentDictionary<string, ThreadRequest<TRequest, TResponse>>();

        private readonly ConcurrentQueue<string> _concurrentQueue = new ConcurrentQueue<string>();

        private readonly AutoResetEvent _queueEvent = new AutoResetEvent(false);
        private readonly Semaphore _semaphoreQueue;
        private readonly Timer _timerExpired;

        private readonly Func<ThreadRequest<TRequest, TResponse>, ThreadResponse<TResponse>> _function;

        public MiniQueueCore(
            Func<ThreadRequest<TRequest, TResponse>, ThreadResponse<TResponse>> func,
            int maxThreads = 2) : this()
        {
            _function = func ?? throw new Exception("Func cannot be null");
            _semaphoreQueue = new Semaphore(maxThreads, maxThreads, Guid.NewGuid().ToString("N"));

            _timerExpired = new Timer(1000 * 5);
            _timerExpired.Elapsed += ProcessOnRemoveExpired;
            _timerExpired.AutoReset = true;
            _timerExpired.Start();
        }

        private MiniQueueCore()
        {
            new Thread(StartThreads).Start();
        }

        public int GetQueueLength()
        {
            return _concurrentQueue.Count;
        }



        /// <summary>
        ///     isolate thread to running thread
        /// </summary>
        private void StartThreads()
        {
            while (true)
            {
                if (_concurrentQueue.IsEmpty)
                {
                    // Make this thread sleep while request in queue empty
                    _queueEvent.WaitOne();
                }

                // Dequeue from queue and progress 
                if (_concurrentQueue.TryDequeue(out string guid) &&
                    _concurrentDictionary.TryRemove(guid, out ThreadRequest<TRequest, TResponse> model))
                {
                    Thread t = CreateThread(_semaphoreQueue,
                        threadStart: () => ProgressAsync(model));
                    t.Start();
                }
            }
        }

        /// <summary>
        ///     managed max thread to progress by semaphore
        /// </summary>
        /// <param name="semaphore"></param>
        /// <param name="threadStart"></param>
        /// <returns></returns>
        private static Thread CreateThread(Semaphore semaphore, ThreadStart threadStart)
        {
            semaphore.WaitOne();
            return new Thread(threadStart);
        }

        /// <summary>
        ///     function process request
        /// </summary>
        /// <param name="model"></param>
        private void ProgressAsync(ThreadRequest<TRequest, TResponse> model)
        {
            try
            {
                model.Response = _function.Invoke(model);
            }
            catch (Exception)
            {
                // Log ex
            }
            finally
            {
                model.Event.Set();

                // Release thread from critical section
                _semaphoreQueue.Release();
            }
        }

        /// <summary>
        ///     Remove expired request
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        private void ProcessOnRemoveExpired(object source, ElapsedEventArgs e)
        {
            // Stop the timer to run until end of request
            _timerExpired.Stop();
            try
            {
                Next:
                {
                    // Get all expired request in Dictionary store
                    List<string> expired = _concurrentDictionary.Values
                        .Where(predicate: x => x.IsExpire())
                        .OrderBy(keySelector: x => x.CreateAt)
                        .Select(selector: x => x.Guid)
                        .ToList();

                    if (expired.Count == 0)
                    {
                        goto End;
                    }

                    foreach (string key in expired)
                    {
                        if (false == _concurrentDictionary.TryRemove(key, out ThreadRequest<TRequest, TResponse> model))
                        {
                            continue;
                        }

                        //set value for expire item
                        if (model.Response == null)
                        {
                            // Response stats response to timeout or something...
                            // Then set request event to continue response 
                            ThreadResponse<TResponse> response = new ThreadResponse<TResponse>();
                            response.Status = HttpStatusCode.RequestTimeout;
                            response.Message = nameof(HttpStatusCode.RequestTimeout);

                            model.Response = response;
                        }

                        model.Event.Set();
                    }
                }

                // Repeat
                goto Next;

                End:
                {
                }
            }
            catch (Exception)
            {
                // Log ex
            }
            finally
            {
                // Start timer after stop
                _timerExpired.Start();
            }
        }


        /// <summary>
        ///     adding new request to queue
        /// </summary>
        /// <param name="request"></param>
        /// <param name="model"></param>
        /// <returns></returns>
        public bool AddRequest(TRequest request, out ThreadRequest<TRequest, TResponse> model)
        {
            model = new ThreadRequest<TRequest, TResponse>(request);
            if (_concurrentDictionary.TryAdd(model.Guid, model) == false)
            {
                return false;
            }

            _concurrentQueue.Enqueue(model.Guid);
            _queueEvent.Set();

            return true;
        }
    }
}