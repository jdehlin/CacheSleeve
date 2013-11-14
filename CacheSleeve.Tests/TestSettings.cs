using System.Collections.Generic;
using CacheSleeve.Tests.TestObjects;

namespace CacheSleeve.Tests
{
    public static class TestSettings
    {
        public static string RedisHost = "localhost";
        public static int RedisPort = 6379;
        public static string RedisPassword = null;
        public static string KeyPrefix = "cs.";

        // don't mess with George.. you'll break a lot of tests
        public static Monkey George = new Monkey("George") { Bananas = new List<Banana> { new Banana(4, "yellow") }};
    }
}