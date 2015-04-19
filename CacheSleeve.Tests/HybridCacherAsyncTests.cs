using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web;
using StackExchange.Redis;
using Xunit;

namespace CacheSleeve.Tests
{
    public class HybridCacherAsyncTests : IDisposable
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

        public HybridCacherAsyncTests()
        {
            // have to fake an http context to use http context cache
            HttpContext.Current = new HttpContext(new HttpRequest(null, "http://tempuri.org", null), new HttpResponse(null));

            CacheManager.Init(TestSettings.RedisHost, TestSettings.RedisPort, TestSettings.RedisPassword, TestSettings.RedisDb, TestSettings.KeyPrefix);
            _cacheSleeve = CacheManager.Settings;

            var cacheSleeve = CacheManager.Settings;

            var configuration =
                ConfigurationOptions.Parse(string.Format("{0}:{1}", TestSettings.RedisHost, TestSettings.RedisPort));
            configuration.AllowAdmin = true;
            var redisConnection = ConnectionMultiplexer.Connect(configuration);

            var subscriber = redisConnection.GetSubscriber();
            subscriber.Subscribe("cacheSleeve.remove.*", (redisChannel, value) => OnSubscriptionHit(redisChannel, GetString(value)));
            subscriber.Subscribe("cacheSleeve.flush*", (redisChannel, value) => OnSubscriptionHit(redisChannel, "flush"));

            _hybridCacher = new HybridCacher();
            _remoteCacher = cacheSleeve.RemoteCacher;
            _localCacher = cacheSleeve.LocalCacher;
        }


        public class Basics : HybridCacherAsyncTests
        {
            [Fact]
            public async void SetCachesRemote()
            {
                await _hybridCacher.SetAsync("key", "value");
                var result = await _remoteCacher.GetAsync<string>("key");
                Assert.Equal("value", result);
            }

            [Fact]
            public async void GetsFromLocalCacheFirst()
            {
                await _remoteCacher.SetAsync("key", "value1");
                _localCacher.Set("key", "value2");
                var result = await _hybridCacher.GetAsync<string>("key");
                Assert.Equal("value2", result);
            }

            [Fact]
            public async void GetsFromRemoteCacheIfNotInLocal()
            {
                await _remoteCacher.SetAsync("key", "value1");
                var result = await _hybridCacher.GetAsync<string>("key");
                Assert.Equal("value1", result);
            }

            [Fact]
            public async void SetsExpirationOfLocalByRemoteTimeToLive()
            {
                await _remoteCacher.SetAsync("key", "value1", DateTime.Now.AddSeconds(120));
                var hybridResult = await _hybridCacher.GetAsync<string>("key");
                var ttl = _localCacher.TimeToLive("key");
                Assert.InRange(ttl, 118, 122);
            }

            [Fact]
            public async void CanGetAllKeys()
            {
                await _remoteCacher.SetAsync("key1", "value");
                _localCacher.Set("key2", "value");
                var result = await _hybridCacher.GetAllKeysAsync();
                Assert.True(result.Select(k => k.KeyName).Contains(_cacheSleeve.AddPrefix("key1")));
                Assert.True(result.Select(k => k.KeyName).Contains(_cacheSleeve.AddPrefix("key2")));
            }

            [Fact]
            public async void ExpirationTransfersFromRemoteToLocal()
            {
                await _remoteCacher.SetAsync("key1", "value", DateTime.Now.AddSeconds(120));
                await _hybridCacher.GetAsync<string>("key1");
                var results = _localCacher.GetAllKeys();
                Assert.InRange(results.First().ExpirationDate.Value, DateTime.Now.AddSeconds(118), DateTime.Now.AddSeconds(122));
            }
        }

        public class PubSub : HybridCacherAsyncTests
        {
            [Fact]
            public async void SetCausesPublishRemove()
            {
                var lastMessage = default(string);
                SubscriptionHit += (key, message) => { lastMessage = message; };
                await _hybridCacher.SetAsync("key", "value");
                Thread.Sleep(30);
                Assert.Equal("key", lastMessage);
            }

            [Fact]
            public async void RemoveCausesPublishRemove()
            {
                var lastMessage = default(string);
                SubscriptionHit += (key, message) => { lastMessage = message; };
                await _hybridCacher.RemoveAsync("key");
                Thread.Sleep(30);
                Assert.Equal("key", lastMessage);
            }

            [Fact]
            public async void FlushCausesPublishFlush()
            {
                var lastMessage = default(string);
                SubscriptionHit += (key, message) => { lastMessage = message; };
                await _hybridCacher.FlushAllAsync();
                Thread.Sleep(30);
                Assert.Equal("flush", lastMessage);
            }
        }

        public class Dependencies : HybridCacherAsyncTests
        {
            [Fact]
            public async void GetSetsRemoteDependencyOnLocal()
            {
                await _hybridCacher.SetAsync("key1", "value1");
                await _hybridCacher.GetAsync<string>("key1");
                await _hybridCacher.SetAsync("key2", "value2", "key1");
                await _hybridCacher.GetAsync<string>("key2");
                var result = _localCacher.Get<string>("key2");
                Assert.Equal("value2", result);
                _localCacher.Remove("key1");
                result = _localCacher.Get<string>("key2");
                Assert.Equal(null, result);
            }
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