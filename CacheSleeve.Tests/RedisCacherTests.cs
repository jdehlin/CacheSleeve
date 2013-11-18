using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Web;
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

            CacheSleeve.Init(TestSettings.RedisHost, TestSettings.RedisPort, TestSettings.RedisPassword, TestSettings.KeyPrefix);

            _redisCacher = CacheSleeve.Manager.RemoteCacher;
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

        public class BulkOperations : RedisCacherTests
        {
            [Fact]
            public void CanSetAndGetMultipleStringItems()
            {
                var input = new Dictionary<string, string> {{"key1", "value1"}, {"key2", "value2"}};
                _redisCacher.SetAll(input);
                var result = _redisCacher.GetAll<string>(input.Keys);
                Assert.Equal("value1", result["key1"]);
                Assert.Equal("value2", result["key2"]);
            }

            [Fact]
            public void CanSetAndGetMultipleByteArrayItems()
            {
                var input = new Dictionary<string, byte[]> { { "key1", new byte[] { 0x20, 0x20, 0x20 } }, { "key2", new byte[] { 0x20, 0x20 } } };
                _redisCacher.SetAll(input);
                var result = _redisCacher.GetAll<byte[]>(input.Keys);
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
                _redisCacher.SetAll(input);
                var result = _redisCacher.GetAll<Monkey>(input.Keys);
                Assert.Equal("George", result["key1"].Name);
                Assert.Equal("George Jr.", result["key2"].Name);
                Thread.Sleep(100);
            }

            [Fact]
            public void FlushRemovesAllItems()
            {
                var input = new Dictionary<string, string> { { "key1", "value1" }, { "key2", "value2" } };
                _redisCacher.SetAll(input);
                _redisCacher.FlushAll();
                var result = _redisCacher.GetAll<string>(input.Keys);
                Assert.Equal(null, result["key1"]);
                Assert.Equal(null, result["key2"]);
            }

            [Fact]
            public void GetAllWithNoKeyListReturnsAllItems()
            {
                var input = new Dictionary<string, string> { { "key1", "value1" }, { "key2", "value2" } };
                _redisCacher.SetAll(input);
                var result = _redisCacher.GetAll<string>();
                Assert.Equal("value1", result["key1"]);
                Assert.Equal("value2", result["key2"]);
            }
        }

        public void Dispose()
        {
            _redisCacher.FlushAll();
            _redisCacher = null;
        }
    }
}