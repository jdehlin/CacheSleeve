using System;
using System.Linq;
using System.Web;
using BookSleeve;
using CacheSleeve.Tests.TestObjects;
using Xunit;

namespace CacheSleeve.Tests
{
    public class RedisCacherTests : IDisposable
    {
        private RedisCacher _redisCacher;

        public RedisCacherTests()
        {
            // have to fake an http context to use http context cache
            HttpContext.Current = new HttpContext(new HttpRequest(null, "http://tempuri.org", null), new HttpResponse(null));

            CacheManager.Init(TestSettings.RedisHost, TestSettings.RedisPort, TestSettings.RedisPassword, TestSettings.KeyPrefix);

            _redisCacher = CacheManager.Settings.RemoteCacher;
        }

        public class Basics : RedisCacherTests
        {
            [Fact]
            public void SetReturnsTrueOnInsert()
            {
                var result = _redisCacher.Set("key", "value");
                Assert.Equal(true, result);
            }

            [Fact]
            public void CanSetAndGetStringValues()
            {
                _redisCacher.Set("key", "value");
                var result = _redisCacher.Get<string>("key");
                Assert.Equal("value", result);
            }

            [Fact]
            public void CanSetAndGetByteValues()
            {
                _redisCacher.Set("key", new byte[] { 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20 });
                var result = _redisCacher.Get<byte[]>("key");
                Assert.Equal(new byte[] { 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20 }, result);
            }

            [Fact]
            public void CanSetAndGetObjectValues()
            {
                _redisCacher.Set("key", TestSettings.George);
                var result = _redisCacher.Get<Monkey>("key");
                Assert.Equal(TestSettings.George.Name, result.Name);
                Assert.Equal(TestSettings.George.Bananas.First().Length, result.Bananas.First().Length);
            }

            [Fact]
            public void GetEmptyKeyReturnsNull()
            {
                var result = _redisCacher.Get<Monkey>("nonexistant");
                Assert.Equal(null, result);
            }

            [Fact]
            public void SetExistingKeyOverwrites()
            {
                var george = TestSettings.George;
                var georgeJr = new Monkey("George Jr.");
                _redisCacher.Set("key", george);
                _redisCacher.Set("key", georgeJr);
                var result = _redisCacher.Get<Monkey>("key");
                Assert.Equal(georgeJr.Name, result.Name);
            }

            [Fact]
            public void CanRemoveItem()
            {
                _redisCacher.Set("key", "value");
                var result = _redisCacher.Get<string>("key");
                Assert.Equal("value", result);
                _redisCacher.Remove("key");
                result = _redisCacher.Get<string>("key");
                Assert.Equal(null, result);
            }
        }

        public class Failsafes : RedisCacherTests
        {
            [Fact]
            public void RemovesAndReturnsDefaultIfGetItemNotOfValidTypeOfT()
            {
                _redisCacher.Set("key", TestSettings.George);
                var result = _redisCacher.Get<int>("key");
                Assert.Equal(0, result);
                var result2 = _redisCacher.Get<Monkey>("key");
                Assert.Equal(null, result2);
            }
        }

        public class Expiration : RedisCacherTests
        {
            [Fact]
            public void SetsTimeToLiveByDateTime()
            {
                _redisCacher.Set("key", "value", DateTime.Now.AddMinutes(1));
                var result = _redisCacher.TimeToLive("key");
                Assert.InRange(result, 50, 70);
            }

            [Fact]
            public void SetsTimeToLiveByTimeSpan()
            {
                _redisCacher.Set("key", "value", new TimeSpan(0, 1, 0));
                var result = _redisCacher.TimeToLive("key");
                Assert.InRange(result, 50, 70);
            }
        }

        public class Dependencies : RedisCacherTests
        {
            [Fact]
            public void SetWithParentAddsKeyToParentsChildren()
            {
                _redisCacher.Set("key1", "value1");
                _redisCacher.Set("key2", "value2", "key1");
                using (var conn = new RedisConnection(TestSettings.RedisHost, TestSettings.RedisPort, -1, TestSettings.RedisPassword))
                {
                    conn.Open();
                    var childrenKey = CacheManager.Settings.AddPrefix("key1.children");
                    var result = conn.Lists.RangeString(0, childrenKey, 0, (int)conn.Lists.GetLength(0, childrenKey).Result).Result;
                    Assert.Contains(TestSettings.KeyPrefix + "key2", result);
                }
            }

            [Fact]
            public void SetWithParentAddsParentReferenceForChild()
            {
                _redisCacher.Set("key1", "value1");
                _redisCacher.Set("key2", "value2", "key1");
                var result = _redisCacher.Get<string>("key2.parent");
                Assert.Equal(CacheManager.Settings.AddPrefix("key1"), result);
            }

            [Fact]
            public void ParentsTimeToLiveAddedToChildrenList()
            {
                _redisCacher.Set("key1", "value1", DateTime.Now.AddHours(1));
                _redisCacher.Set("key2", "value2", "key1");
                var result = _redisCacher.TimeToLive("key1.children");
                Assert.InRange(result, 3500, 3700);
            }

            [Fact]
            public void ParentsTimeToLiveAddedToChildren()
            {
                _redisCacher.Set("key1", "value1", DateTime.Now.AddHours(1));
                _redisCacher.Set("key2", "value2", "key1");
                var result = _redisCacher.TimeToLive("key2");
                Assert.InRange(result, 3500, 3700);
            }

            [Fact]
            public void OverwritingItemRemovesChildren()
            {
                _redisCacher.Set("key1", "value1");
                _redisCacher.Set("key2", "value2", "key1");
                var result = _redisCacher.Get<string>("key2");
                Assert.Equal("value2", result);
                _redisCacher.Set("key1", "value3");
                result = _redisCacher.Get<string>("key2");
                Assert.Equal(null, result);
            }

            [Fact]
            public void OverwritingItemRemovesChildList()
            {
                _redisCacher.Set("key1", "value1");
                _redisCacher.Set("key2", "value2", "key1");
                using (var conn = new RedisConnection(TestSettings.RedisHost, TestSettings.RedisPort, -1, TestSettings.RedisPassword))
                {
                    conn.Open();
                    var childrenKey = CacheManager.Settings.AddPrefix("key1.children");
                    var result = conn.Lists.RangeString(0, childrenKey, 0, (int)conn.Lists.GetLength(0, childrenKey).Result).Result;
                    Assert.Contains(TestSettings.KeyPrefix + "key2", result);
                    _redisCacher.Set("key1", "value3");
                    result = conn.Lists.RangeString(0, childrenKey, 0, (int)conn.Lists.GetLength(0, childrenKey).Result).Result;
                    Assert.Equal(0, result.Length);
                }
            }

            [Fact]
            public void RemovingItemRemovesChildren()
            {
                _redisCacher.Set("key1", "value1");
                _redisCacher.Set("key2", "value2", "key1");
                var result = _redisCacher.Get<string>("key2");
                Assert.Equal("value2", result);
                _redisCacher.Remove("key1");
                result = _redisCacher.Get<string>("key2");
                Assert.Equal(null, result);
            }

            [Fact]
            public void RemovingItemRemovesChildList()
            {
                _redisCacher.Set("key1", "value1");
                _redisCacher.Set("key2", "value2", "key1");
                using (var conn = new RedisConnection(TestSettings.RedisHost, TestSettings.RedisPort, -1, TestSettings.RedisPassword))
                {
                    conn.Open();
                    var childrenKey = CacheManager.Settings.AddPrefix("key1.children");
                    var result = conn.Lists.RangeString(0, childrenKey, 0, (int)conn.Lists.GetLength(0, childrenKey).Result).Result;
                    Assert.Contains(TestSettings.KeyPrefix + "key2", result);
                    _redisCacher.Remove("key1");
                    result = conn.Lists.RangeString(0, childrenKey, 0, (int)conn.Lists.GetLength(0, childrenKey).Result).Result;
                    Assert.Equal(0, result.Length);
                }
            }

            [Fact]
            public void RemovingItemRemovesParentReference()
            {
                _redisCacher.Set("key1", "value1");
                _redisCacher.Set("key2", "value2", "key1");
                var result = _redisCacher.Get<string>("key2.parent");
                Assert.Equal(CacheManager.Settings.AddPrefix("key1"), result);
                _redisCacher.Remove("key2");
                result = _redisCacher.Get<string>("key2.parent");
                Assert.Equal(null, result);
            }
        }

        public void Dispose()
        {
            _redisCacher.FlushAll();
            _redisCacher = null;
        }
    }
}