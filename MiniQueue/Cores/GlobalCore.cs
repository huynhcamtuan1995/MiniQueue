using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiniQueue.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniQueue.Cores
{
    public class GlobalCore
    {
        private readonly int _limitRequest;
        private static IServiceCollection _serviceCollection;

        private static Dictionary<Type, Type> _typeDictionary = new Dictionary<Type, Type>();

        public GlobalCore(
            IServiceCollection serviceCollection,
            IConfiguration configuration)
        {
            _serviceCollection = serviceCollection;
            _limitRequest = configuration.GetValue<int>("QueueRequest:Limit");
        }

        public static void AddQueueCore<TRequest, TResponse>(
            int maxThreads,
            Func<ThreadRequest<TRequest, TResponse>, ThreadResponse<TResponse>> func = null)
            where TRequest : notnull
            where TResponse : notnull
        {
            MiniQueueCore<TRequest, TResponse> queueCore =
                new MiniQueueCore<TRequest, TResponse>(func, maxThreads);
            _typeDictionary.Add(typeof(TRequest), typeof(TResponse));
            _serviceCollection.AddSingleton(typeof(TRequest), implementationFactory: _ => queueCore);
        }

        public bool IsLimitedQueue()
        {
            int currentThreads = _typeDictionary.Sum(selector: x => Invoke(x.Key));
            return currentThreads >= _limitRequest;
        }

        private int Invoke(Type key)
        {
            ServiceProvider provider = _serviceCollection.BuildServiceProvider();
            int? currentThreads = (int?) provider
                .GetRequiredService(key)
                .GetType()
                .GetMethod("GetQueueLength")
                !.Invoke(null, Array.Empty<object>());
            return currentThreads ?? 0;
        }
    }
}