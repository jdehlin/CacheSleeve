using System.Collections.Generic;
using CacheSleeve.Tests.TestObjects;
using Xunit;
using System.Diagnostics;


[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CacheSleeve.Tests
{
    public static class TestSettings
    {
        public static string RedisHost = "localhost";
        public static int RedisPort = 6379;
        public static string RedisPassword = null;
        public static int RedisDb = 5;
        public static string KeyPrefix = "cs.";

        // don't mess with George.. you'll break a lot of tests
        public static Monkey George = new Monkey("George") { Bananas = new List<Banana> { new Banana(4, "yellow") }};

        static TestSettings()
        {
            StartRedis();
        }

        public static bool _redisStarted = false;

        public static void StartRedis()
        {            
            if (!_redisStarted)
            {
                Process.Start(@"..\..\..\packages\redis-64.3.0.503\tools\redis-server.exe");
                _redisStarted = true;
            }
        }

    }
}