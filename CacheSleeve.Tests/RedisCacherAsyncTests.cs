using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using CacheSleeve.Tests.TestObjects;
using Xunit;

namespace CacheSleeve.Tests
{
    public class RedisCacherAsyncTests : IDisposable
    {
        private RedisCacher _redisCacher;
        private readonly CacheManager _cacheSleeve;

        public RedisCacherAsyncTests()
        {
            // have to fake an http context to use http context cache
            HttpContext.Current = new HttpContext(new HttpRequest(null, "http://tempuri.org", null), new HttpResponse(null));

            CacheManager.Init(TestSettings.RedisHost, TestSettings.RedisPort, TestSettings.RedisPassword, TestSettings.RedisDb, TestSettings.KeyPrefix);
            _cacheSleeve = CacheManager.Settings;

            _redisCacher = CacheManager.Settings.RemoteCacher;
        }

        public class Basics : RedisCacherAsyncTests
        {
            [Fact]
            public async void SetReturnsTrueOnInsert()
            {
                var result = await _redisCacher.SetAsync("key", "value");
                Assert.Equal(true, result);
            }

            [Fact]
            public async void CanSetAndGetStringValues()
            {
                await _redisCacher.SetAsync("key", "value");
                var result = await _redisCacher.GetAsync<string>("key");
                Assert.Equal("value", result);
            }

            [Fact]
            public async void CanSetAndGetByteValues()
            {
                await _redisCacher.SetAsync("key", new byte[] { 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20 });
                var result = await _redisCacher.GetAsync<byte[]>("key");
                Assert.Equal(new byte[] { 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20 }, result);
            }

            [Fact]
            public async void CanSetAndGetObjectValues()
            {
                await _redisCacher.SetAsync("key", TestSettings.George);
                var result = await _redisCacher.GetAsync<Monkey>("key");
                Assert.Equal(TestSettings.George.Name, result.Name);
                Assert.Equal(TestSettings.George.Bananas.First().Length, result.Bananas.First().Length);
            }

            [Fact]
            public async void GetEmptyKeyReturnsNull()
            {
                var result = await _redisCacher.GetAsync<Monkey>("nonexistant");
                Assert.Equal(null, result);
            }

            [Fact]
            public async void SetExistingKeyOverwrites()
            {
                var george = TestSettings.George;
                var georgeJr = new Monkey("George Jr.");
                var task1 = _redisCacher.SetAsync("key", george);
                var task2 = _redisCacher.SetAsync("key", georgeJr);
                await Task.WhenAll(task1, task2);
                var result = await _redisCacher.GetAsync<Monkey>("key");
                Assert.Equal(georgeJr.Name, result.Name);
            }

            [Fact]
            public async void CanRemoveItem()
            {
                await _redisCacher.SetAsync("key", "value");
                var result = await _redisCacher.GetAsync<string>("key");
                Assert.Equal("value", result);
                await _redisCacher.RemoveAsync("key");
                result = await _redisCacher.GetAsync<string>("key");
                Assert.Equal(null, result);
            }

            [Fact]
            public async void CanGetAllKeys()
            {
                await _redisCacher.SetAsync("key1", "value");
                await _redisCacher.SetAsync("key2", "value");
                var result = await _redisCacher.GetAllKeysAsync();
                Assert.True(result.Select(k => k.KeyName).Contains(_cacheSleeve.AddPrefix("key1")));
                Assert.True(result.Select(k => k.KeyName).Contains(_cacheSleeve.AddPrefix("key2")));
            }

            [Fact]
            public async void GetAllKeysIncludesExpiration()
            {
                await _redisCacher.SetAsync("key1", "value", DateTime.Now.AddMinutes(1));
                var result = await _redisCacher.GetAllKeysAsync();
                Assert.InRange(result.ToList()[0].ExpirationDate.Value, DateTime.Now.AddSeconds(58), DateTime.Now.AddSeconds(62));
            }

            [Fact]
            public async void IfTimeToLiveIsNegative1ThenExpirationIsNull()
            {
                await _redisCacher.SetAsync("key1", "value");
                var result = await _redisCacher.GetAllKeysAsync();
                Assert.Equal(null, result.First().ExpirationDate);
            }
        }

        public class Failsafes : RedisCacherAsyncTests
        {
            [Fact]
            public async void RemovesAndReturnsDefaultIfGetItemNotOfValidTypeOfT()
            {
                await _redisCacher.SetAsync("key", TestSettings.George);
                var result = await _redisCacher.GetAsync<int>("key");
                Assert.Equal(0, result);
                var result2 = await _redisCacher.GetAsync<Monkey>("key");
                Assert.Equal(null, result2);
            }
        }

        public class Expiration : RedisCacherAsyncTests
        {
            [Fact]
            public async void SetsTimeToLiveByDateTime()
            {
                await _redisCacher.SetAsync("key", "value", DateTime.Now.AddMinutes(1));
                var result = await _redisCacher.TimeToLiveAsync("key");
                Assert.InRange(result, 50, 70);
            }

            [Fact]
            public async void SetsTimeToLiveByTimeSpan()
            {
                await _redisCacher.SetAsync("key", "value", new TimeSpan(0, 1, 0));
                var result = await _redisCacher.TimeToLiveAsync("key");
                Assert.InRange(result, 50, 70);
            }

            [Fact]
            public async void KeysHaveProperExpirationDates()
            {
                await _redisCacher.SetAsync("key", "value", DateTime.Now.AddMinutes(1));
                var result = await _redisCacher.GetAllKeysAsync();
                Assert.InRange(result.First().ExpirationDate.Value, DateTime.Now.AddSeconds(58), DateTime.Now.AddSeconds(62));
            }
        }

        public class Dependencies : RedisCacherAsyncTests
        {
            [Fact]
            public async void SetWithParentAddsKeyToParentsChildren()
            {
                await _redisCacher.SetAsync("key1", "value1");
                await  _redisCacher.SetAsync("key2", "value2", "key1");
                var conn = _cacheSleeve.GetDatebase();
                var childrenKey = CacheManager.Settings.AddPrefix("key1.children");
                var result = conn.ListRange(childrenKey, 0, (int)conn.ListLength(childrenKey));
                Assert.Contains(TestSettings.KeyPrefix + "key2", result.Select(x => x.ToString()));
            }

            [Fact]
            public async void SetWithParentAddsParentReferenceForChild()
            {
                await _redisCacher.SetAsync("key1", "value1");
                await _redisCacher.SetAsync("key2", "value2", "key1");
                var result = await _redisCacher.GetAsync<string>("key2.parent");
                Assert.Equal(CacheManager.Settings.AddPrefix("key1"), result);
            }

            [Fact]
            public async void ParentsTimeToLiveAddedToChildrenList()
            {
                await _redisCacher.SetAsync("key1", "value1", DateTime.Now.AddHours(1));
                await _redisCacher.SetAsync("key2", "value2", "key1");
                var result = await _redisCacher.TimeToLiveAsync("key1.children");
                Assert.InRange(result, 3500, 3700);
            }

            [Fact]
            public async void ParentsTimeToLiveAddedToChildren()
            {
                await _redisCacher.SetAsync("key1", "value1", DateTime.Now.AddHours(1));
                await _redisCacher.SetAsync("key2", "value2", "key1");
                var result = await _redisCacher.TimeToLiveAsync("key2");
                Assert.InRange(result, 3500, 3700);
            }

            [Fact]
            public async void OverwritingItemRemovesChildren()
            {
                await _redisCacher.SetAsync("key1", "value1");
                await _redisCacher.SetAsync("key2", "value2", "key1");
                var result = await _redisCacher.GetAsync<string>("key2");
                Assert.Equal("value2", result);
                await _redisCacher.SetAsync("key1", "value3");
                result = await _redisCacher.GetAsync<string>("key2");
                Assert.Equal(null, result);
            }

            [Fact]
            public async void OverwritingItemRemovesChildList()
            {
                await _redisCacher.SetAsync("key1", "value1");
                await _redisCacher.SetAsync("key2", "value2", "key1");
                var conn = _cacheSleeve.GetDatebase();
                var childrenKey = CacheManager.Settings.AddPrefix("key1.children");
                var result = conn.ListRange(childrenKey, 0, (int)conn.ListLength(childrenKey));
                Assert.Contains(TestSettings.KeyPrefix + "key2", result.Select(x => x.ToString()));
                await _redisCacher.SetAsync("key1", "value3");
                result = conn.ListRange(childrenKey, 0, (int)conn.ListLength(childrenKey));
                Assert.Equal(0, result.Length);
            }

            [Fact]
            public async void RemovingItemRemovesChildren()
            {
                await _redisCacher.SetAsync("key1", "value1");
                await _redisCacher.SetAsync("key2", "value2", "key1");
                var result = _redisCacher.Get<string>("key2");
                Assert.Equal("value2", result);
                await _redisCacher.RemoveAsync("key1");
                result = await _redisCacher.GetAsync<string>("key2");
                Assert.Equal(null, result);
            }

            [Fact]
            public async void RemovingItemRemovesChildList()
            {
                await _redisCacher.SetAsync("key1", "value1");
                await _redisCacher.SetAsync("key2", "value2", "key1");
                var conn = _cacheSleeve.GetDatebase();
                var childrenKey = CacheManager.Settings.AddPrefix("key1.children");
                var result = conn.ListRange(childrenKey, 0, (int)conn.ListLength(childrenKey));
                Assert.Contains(TestSettings.KeyPrefix + "key2", result.Select(x => x.ToString()));
                await _redisCacher.RemoveAsync("key1");
                result = conn.ListRange(childrenKey, 0, (int)conn.ListLength(childrenKey));
                Assert.Equal(0, result.Length);
            }

            [Fact]
            public async void RemovingItemRemovesParentReference()
            {
                await _redisCacher.SetAsync("key1", "value1");
                await _redisCacher.SetAsync("key2", "value2", "key1");
                var result = await _redisCacher.GetAsync<string>("key2.parent");
                Assert.Equal(CacheManager.Settings.AddPrefix("key1"), result);
                await _redisCacher.RemoveAsync("key2");
                result = await _redisCacher.GetAsync<string>("key2.parent");
                Assert.Equal(null, result);
            }

            [Fact]
            public async void SettingDependenciesDoesNotScrewUpTimeToLive()
            {
                await _redisCacher.SetAsync("parent1", "value1", DateTime.Now.AddMinutes(1));
                var parentTtl = await _redisCacher.TimeToLiveAsync("parent1");
                Assert.InRange(parentTtl, 58, 62);
                await _redisCacher.SetAsync("key1", "value1", DateTime.Now.AddMinutes(10), "parent1");
                var childTtl = await _redisCacher.TimeToLiveAsync("key1");
                parentTtl = await _redisCacher.TimeToLiveAsync("parent1");
                Assert.InRange(childTtl, 58, 62); // this is not a 10 minute range because when the parent expires so will the child
                Assert.InRange(parentTtl, 58, 62);
            }
        }

        public class Polymorphism : RedisCacherAsyncTests
        {
            [Fact]
            public async void ProperlySerializesAndDeserializesPolymorphicTypes()
            {
                var fruits = new List<Fruit>
                             {
                                 new Banana(4, "green")
                             };
                await _redisCacher.SetAsync("key", fruits);
                var result = await _redisCacher.GetAsync<List<Fruit>>("key");
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