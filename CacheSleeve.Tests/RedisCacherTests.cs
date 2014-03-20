using System;
using System.Collections.Generic;
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
        private readonly CacheManager _cacheSleeve;

        public RedisCacherTests()
        {
            // have to fake an http context to use http context cache
            HttpContext.Current = new HttpContext(new HttpRequest(null, "http://tempuri.org", null), new HttpResponse(null));

            CacheManager.Init(TestSettings.RedisHost, TestSettings.RedisPort, TestSettings.RedisPassword, TestSettings.RedisDb, TestSettings.KeyPrefix);
            _cacheSleeve = CacheManager.Settings;

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

            [Fact]
            public void CanGetAllKeys()
            {
                _redisCacher.Set("key1", "value");
                _redisCacher.Set("key2", "value");
                var result = _redisCacher.GetAllKeys();
                Assert.True(result.Select(k => k.KeyName).Contains(_cacheSleeve.AddPrefix("key1")));
                Assert.True(result.Select(k => k.KeyName).Contains(_cacheSleeve.AddPrefix("key2")));
            }

            [Fact]
            public void GetAllKeysIncludesExpiration()
            {
                _redisCacher.Set("key1", "value", DateTime.Now.AddMinutes(1));
                var result = _redisCacher.GetAllKeys();
                Assert.InRange(result.ToList()[0].ExpirationDate.Value, DateTime.Now.AddSeconds(58), DateTime.Now.AddSeconds(62));
            }

            [Fact]
            public void IfTimeToLiveIsNegative1ThenExpirationIsNull()
            {
                _redisCacher.Set("key1", "value");
                var result = _redisCacher.GetAllKeys();
                Assert.Equal(null, result.First().ExpirationDate);
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

            [Fact]
            public void KeysHaveProperExpirationDates()
            {
                _redisCacher.Set("key", "value", DateTime.Now.AddMinutes(1));
                var result = _redisCacher.GetAllKeys();
                Assert.InRange(result.First().ExpirationDate.Value, DateTime.Now.AddSeconds(58), DateTime.Now.AddSeconds(62));
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
                    var result = conn.Lists.RangeString(_cacheSleeve.RedisDb, childrenKey, 0, (int)conn.Lists.GetLength(0, childrenKey).Result).Result;
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
                    var result = conn.Lists.RangeString(_cacheSleeve.RedisDb, childrenKey, 0, (int)conn.Lists.GetLength(_cacheSleeve.RedisDb, childrenKey).Result).Result;
                    Assert.Contains(TestSettings.KeyPrefix + "key2", result);
                    _redisCacher.Set("key1", "value3");
                    result = conn.Lists.RangeString(_cacheSleeve.RedisDb, childrenKey, 0, (int)conn.Lists.GetLength(_cacheSleeve.RedisDb, childrenKey).Result).Result;
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
                    var result = conn.Lists.RangeString(_cacheSleeve.RedisDb, childrenKey, 0, (int)conn.Lists.GetLength(0, childrenKey).Result).Result;
                    Assert.Contains(TestSettings.KeyPrefix + "key2", result);
                    _redisCacher.Remove("key1");
                    result = conn.Lists.RangeString(_cacheSleeve.RedisDb, childrenKey, 0, (int)conn.Lists.GetLength(0, childrenKey).Result).Result;
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

            [Fact]
            public void SettingDependenciesDoesNotScrewUpTimeToLive()
            {
                _redisCacher.Set("parent1", "value1", DateTime.Now.AddMinutes(1));
                var parentTtl = _redisCacher.TimeToLive("parent1");
                Assert.InRange(parentTtl, 58, 62);
                _redisCacher.Set("key1", "value1", DateTime.Now.AddMinutes(10), "parent1");
                var childTtl = _redisCacher.TimeToLive("key1");
                parentTtl = _redisCacher.TimeToLive("parent1");
                Assert.InRange(childTtl, 58, 62); // this is not a 10 minute range because when the parent expires so will the child
                Assert.InRange(parentTtl, 58, 62);
            }
        }

        public class Polymorphism : RedisCacherTests
        {
            [Fact]
            public void ProperlySerializesAndDeserializesPolymorphicTypes()
            {
                var fruits = new List<Fruit>
                             {
                                 new Banana(4, "green")
                             };
                _redisCacher.Set("key", fruits);
                var result = _redisCacher.Get<List<Fruit>>("key");
                Assert.IsType<Banana>(result.First());
            }
        }

        public void Dispose()
        {
            _redisCacher.FlushAll();
            _redisCacher = null;
        }
    }
}