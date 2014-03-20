using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Web;
using CacheSleeve.Tests.TestObjects;
using Xunit;

namespace CacheSleeve.Tests
{
    public class HttpContextCacherTests : IDisposable
    {
        private HttpContextCacher _httpContextCacher;
        private readonly CacheManager _cacheSleeve;

        public HttpContextCacherTests()
        {
            // have to fake an http context to use http context cache
            HttpContext.Current = new HttpContext(new HttpRequest(null, "http://tempuri.org", null), new HttpResponse(null));

            CacheManager.Init(TestSettings.RedisHost, TestSettings.RedisPort, TestSettings.RedisPassword, TestSettings.RedisDb, TestSettings.KeyPrefix);
            _cacheSleeve = CacheManager.Settings;

            _httpContextCacher = CacheManager.Settings.LocalCacher;
        }

        public class Basics : HttpContextCacherTests
        {
            [Fact]
            public void SetReturnsTrueOnInsert()
            {
                var result = _httpContextCacher.Set("key", "value");
                Assert.Equal(true, result);
            }

            [Fact]
            public void CanSetAndGetStringValues()
            {
                _httpContextCacher.Set("key", "value");
                var result = _httpContextCacher.Get<string>("key");
                Assert.Equal("value", result);
            }

            [Fact]
            public void CanSetAndGetByteValues()
            {
                _httpContextCacher.Set("key", new byte[] { 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20 });
                var result = _httpContextCacher.Get<byte[]>("key");
                Assert.Equal(new byte[] { 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20 }, result);
            }

            [Fact]
            public void CanSetAndGetObjectValues()
            {
                _httpContextCacher.Set("key", TestSettings.George);
                var result = _httpContextCacher.Get<Monkey>("key");
                Assert.Equal(TestSettings.George.Name, result.Name);
                Assert.Equal(TestSettings.George.Bananas.First().Length, result.Bananas.First().Length);
            }

            [Fact]
            public void GetEmptyKeyReturnsNull()
            {
                var result = _httpContextCacher.Get<Monkey>("nonexistant");
                Assert.Equal(null, result);
            }

            [Fact]
            public void SetExistingKeyOverwrites()
            {
                var george = TestSettings.George;
                var georgeJr = new Monkey("George Jr.");
                _httpContextCacher.Set("key", george);
                _httpContextCacher.Set("key", georgeJr);
                var result = _httpContextCacher.Get<Monkey>("key");
                Assert.Equal(georgeJr.Name, result.Name);
            }

            [Fact]
            public void CanRemoveItem()
            {
                _httpContextCacher.Set("key", "value");
                var result = _httpContextCacher.Get<string>("key");
                Assert.Equal("value", result);
                _httpContextCacher.Remove("key");
                result = _httpContextCacher.Get<string>("key");
                Assert.Equal(null, result);
            }

            [Fact]
            public void CanGetAllKeys()
            {
                _httpContextCacher.Set("key1", "value");
                _httpContextCacher.Set("key2", "value");
                var result = _httpContextCacher.GetAllKeys();
                Assert.True(result.Select(k => k.KeyName).Contains(_cacheSleeve.AddPrefix("key1")));
                Assert.True(result.Select(k => k.KeyName).Contains(_cacheSleeve.AddPrefix("key2")));
            }

            [Fact]
            public void GetAllKeysIncludesExpiration()
            {
                _httpContextCacher.Set("key1", "value", DateTime.Now.AddMinutes(1));
                var result = _httpContextCacher.GetAllKeys();
                Assert.InRange(result.ToList()[0].ExpirationDate.Value, DateTime.Now.AddSeconds(58), DateTime.Now.AddSeconds(62));
            }
        }

        public class Failsafes : HttpContextCacherTests
        {
            [Fact]
            public void RemovesAndReturnsDefaultIfGetItemNotOfValidTypeOfT()
            {
                _httpContextCacher.Set("key", TestSettings.George);
                var result = _httpContextCacher.Get<int>("key");
                Assert.Equal(0, result);
                var result2 = _httpContextCacher.Get<Monkey>("key");
                Assert.Equal(null, result2);
            }
        }

        public class Expiration : HttpContextCacherTests
        {
            [Fact]
            public void SetsTimeToLiveByDateTime()
            {
                _httpContextCacher.Set("key", "value", DateTime.Now.AddMilliseconds(100));
                var result = _httpContextCacher.Get<string>("key");
                Assert.Equal("value", result);
                Thread.Sleep(110);
                result = _httpContextCacher.Get<string>("key");
                Assert.Equal(null, result);
            }

            [Fact]
            public void SetsTimeToLiveByTimeSpan()
            {
                _httpContextCacher.Set("key", "value", new TimeSpan(0, 0, 0, 0, 100));
                var result = _httpContextCacher.Get<string>("key");
                Assert.Equal("value", result);
                Thread.Sleep(110);
                result = _httpContextCacher.Get<string>("key");
                Assert.Equal(null, result);
            }

            [Fact]
            public void KeysHaveProperExpirationDates()
            {
                _httpContextCacher.Set("key", "value", DateTime.Now.AddMinutes(1));
                var result = _httpContextCacher.GetAllKeys();
                Assert.InRange(result.First().ExpirationDate.Value, DateTime.Now.AddSeconds(58), DateTime.Now.AddSeconds(62));
            }
        }

        public class Dependencies : HttpContextCacherTests
        {
            [Fact]
            public void DeleteParentAlsoDeletesChildrenForSet()
            {
                _httpContextCacher.Set("parent", "value1");
                _httpContextCacher.Set("child", "value2", "parent");
                var result = _httpContextCacher.Get<string>("child");
                Assert.Equal("value2", result);
                _httpContextCacher.Remove("parent");
                result = _httpContextCacher.Get<string>("child");
                Assert.Equal(null, result);
            }

            [Fact]
            public void DeleteParentAlsoDeletesChildrenForSetWithDateTime()
            {
                _httpContextCacher.Set("parent", "value1");
                _httpContextCacher.Set("child", "value2", DateTime.Now.AddHours(1), "parent");
                var result = _httpContextCacher.Get<string>("child");
                Assert.Equal("value2", result);
                _httpContextCacher.Remove("parent");
                result = _httpContextCacher.Get<string>("child");
                Assert.Equal(null, result);
            }

            [Fact]
            public void DeleteParentAlsoDeletesChildrenForSetWithTimeSpan()
            {
                _httpContextCacher.Set("parent", "value1");
                _httpContextCacher.Set("child", "value2", TimeSpan.FromHours(1), "parent");
                var result = _httpContextCacher.Get<string>("child");
                Assert.Equal("value2", result);
                _httpContextCacher.Remove("parent");
                result = _httpContextCacher.Get<string>("child");
                Assert.Equal(null, result);
            }

            [Fact]
            public void OverwritingParentRemovesChildren()
            {
                _httpContextCacher.Set("parent", "value1");
                _httpContextCacher.Set("child", "value2", "parent");
                var result = _httpContextCacher.Get<string>("child");
                Assert.Equal("value2", result);
                _httpContextCacher.Set("parent", "value3");
                result = _httpContextCacher.Get<string>("child");
                Assert.Equal(null, result);
            }
        }

        public class Polymorphism : HttpContextCacherTests
        {
            [Fact]
            public void ProperlySerializesAndDeserializesPolymorphicTypes()
            {
                var fruits = new List<Fruit>
                             {
                                 new Banana(4, "green")
                             };
                _httpContextCacher.Set("key", fruits);
                var result = _httpContextCacher.Get<List<Fruit>>("key");
                Assert.IsType<Banana>(result.First());
            }
        }

        public void Dispose()
        {
            _httpContextCacher.FlushAll();
            _httpContextCacher = null;
            HttpContext.Current = null;
        }
    }
}