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

        public HttpContextCacherTests()
        {
            // have to fake an http context to use http context cache
            HttpContext.Current = new HttpContext(new HttpRequest(null, "http://tempuri.org", null), new HttpResponse(null));

            CacheSleeve.Init(TestSettings.RedisHost, TestSettings.RedisPort, TestSettings.RedisPassword, TestSettings.KeyPrefix);

            _httpContextCacher = CacheSleeve.Manager.LocalCacher;
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
        }

        public class BulkOperations : HttpContextCacherTests
        {
            [Fact]
            public void CanSetAndGetMultipleStringItems()
            {
                var input = new Dictionary<string, string> { { "key1", "value1" }, { "key2", "value2" } };
                _httpContextCacher.SetAll(input);
                var result = _httpContextCacher.GetAll<string>(input.Keys);
                Assert.Equal("value1", result["key1"]);
                Assert.Equal("value2", result["key2"]);
            }

            [Fact]
            public void CanSetAndGetMultipleByteArrayItems()
            {
                var input = new Dictionary<string, byte[]> { { "key1", new byte[] { 0x20, 0x20, 0x20 } }, { "key2", new byte[] { 0x20, 0x20 } } };
                _httpContextCacher.SetAll(input);
                var result = _httpContextCacher.GetAll<byte[]>(input.Keys);
                Assert.Equal(new byte[] { 0x20, 0x20, 0x20 }, result["key1"]);
                Assert.Equal(new byte[] { 0x20, 0x20 }, result["key2"]);
                Thread.Sleep(100);
            }

            [Fact]
            public void CanSetAndGetMultipleObjectItems()
            {
                var george = TestSettings.George;
                var georgeJr = new Monkey("George Jr.");
                var input = new Dictionary<string, Monkey> { { "key1", george }, { "key2", georgeJr } };
                _httpContextCacher.SetAll(input);
                var result = _httpContextCacher.GetAll<Monkey>(input.Keys);
                Assert.Equal("George", result["key1"].Name);
                Assert.Equal("George Jr.", result["key2"].Name);
                Thread.Sleep(100);
            }

            [Fact]
            public void FlushRemovesAllItems()
            {
                var input = new Dictionary<string, string> { { "key1", "value1" }, { "key2", "value2" } };
                _httpContextCacher.SetAll(input);
                _httpContextCacher.FlushAll();
                var result = _httpContextCacher.GetAll<string>(input.Keys);
                Assert.Equal(null, result["key1"]);
                Assert.Equal(null, result["key2"]);
            }

            [Fact]
            public void GetAllWithNoKeyListReturnsAllItems()
            {
                var input = new Dictionary<string, string> { { "key1", "value1" }, { "key2", "value2" } };
                _httpContextCacher.SetAll(input);
                var result = _httpContextCacher.GetAll<string>();
                Assert.Equal("value1", result["key1"]);
                Assert.Equal("value2", result["key2"]);
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