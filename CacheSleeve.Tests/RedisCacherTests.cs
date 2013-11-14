using System;
using Xunit;

namespace CacheSleeve.Tests
{
    public class RedisCacherTests : IDisposable
    {
        private ICacher _redisCacher;

        public RedisCacherTests()
        {
            CacheSleeve.Init(TestSettings.RedisHost, TestSettings.RedisPort, TestSettings.RedisPassword, TestSettings.KeyPrefix);

            _redisCacher = new RedisCacher();
        }


        [Fact]
        public void CanRetrieveAddedKey()
        {
            _redisCacher.Set("key", "value");
            var result = _redisCacher.Get<string>("key");
            Assert.True(result == "value");
        }

        public void Dispose()
        {
            _redisCacher = null;
        }
    }
}