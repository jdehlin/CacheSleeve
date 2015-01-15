using System;
using System.Text;
using System.Web;
using StackExchange.Redis;
using Xunit;
using System.Linq;

namespace CacheSleeve.Tests
{
    public class CacheManagerTests : IDisposable
    {
        private HybridCacher _hybridCacher;
        private RedisCacher _remoteCacher;
        private HttpContextCacher _localCacher;
        private readonly CacheManager _cacheSleeve;

        private delegate void SubscriptionHitHandler(string key, string message);
        private event SubscriptionHitHandler SubscriptionHit;
        private void OnSubscriptionHit(string key, string message)
        {
            if (SubscriptionHit != null)
                SubscriptionHit(key, message);
        }

        public CacheManagerTests()
        {
            // have to fake an http context to use http context cache
            HttpContext.Current = new HttpContext(new HttpRequest(null, "http://tempuri.org", null), new HttpResponse(null));

            CacheManager.Init(TestSettings.RedisHost, TestSettings.RedisPort, TestSettings.RedisPassword, TestSettings.RedisDb, TestSettings.KeyPrefix);
            _cacheSleeve = CacheManager.Settings;

            var configuration =
                ConfigurationOptions.Parse(string.Format("{0}:{1}", _cacheSleeve.RedisHost, _cacheSleeve.RedisPort));
            configuration.AllowAdmin = true;
            var redisConnection = ConnectionMultiplexer.Connect(configuration);
            
            var subscriber = redisConnection.GetSubscriber();
            subscriber.Subscribe("cacheSleeve.remove.*", (redisChannel, value) => _cacheSleeve.LocalCacher.Remove(GetString(value)));
            subscriber.Subscribe("cacheSleeve.flush*", (redisChannel, value) => _cacheSleeve.LocalCacher.FlushAll());

            _hybridCacher = new HybridCacher();
            _remoteCacher = _cacheSleeve.RemoteCacher;
            _localCacher = _cacheSleeve.LocalCacher;
        }


        [Fact]
        public void GeneratesOverview()
        {
            var result = _cacheSleeve.GenerateOverview();
            Assert.False(string.IsNullOrWhiteSpace(result));
        }

        [Fact]
        public void OverviewContainsKeys()
        {
            _remoteCacher.Set("key1", "value1", DateTime.Now.AddSeconds(30));
            _localCacher.Set("key2", "value2", DateTime.Now.AddMinutes(5));
            var result = _cacheSleeve.GenerateOverview();
            Assert.Equal(1, result.Select((c, i) => result.Substring(i)).Count(sub => sub.StartsWith("key1")));
            Assert.Equal(1, result.Select((c, i) => result.Substring(i)).Count(sub => sub.StartsWith("key2")));
        }
        

        public void Dispose()
        {
            _hybridCacher.FlushAll();
            _hybridCacher = null;
            _remoteCacher = null;
            _localCacher = null;
        }

        /// <summary>
        /// Converts a byte[] to a string.
        /// </summary>
        /// <param name="bytes">The bytes to convert.</param>
        /// <returns>The resulting string.</returns>
        private static string GetString(byte[] bytes)
        {
            var buffer = Encoding.Convert(Encoding.GetEncoding("iso-8859-1"), Encoding.UTF8, bytes);
            return Encoding.UTF8.GetString(buffer, 0, bytes.Count());
        }
    }
}