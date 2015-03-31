using System.Threading.Tasks;
using StackExchange.Redis;

namespace CacheSleeve
{
    public sealed partial class CacheManager
    {
        public async static Task InitAsync(string redisHost, int redisPort = 6379, string redisPassword = null, int redisDb = 0, string keyPrefix = "cs.")
        {
            PopulateSettings(redisHost, redisPort, redisPassword, redisDb, keyPrefix);

            var configuration =
                ConfigurationOptions.Parse(string.Format("{0}:{1}", Settings.RedisHost, Settings.RedisPort));
            configuration.AllowAdmin = true;
            configuration.Password = redisPassword;
            configuration.AbortOnConnectFail = false;
            Settings._redisConnection = await ConnectionMultiplexer.ConnectAsync(configuration);

            // Setup pub/sub for cache syncing
            var subscriber = Settings._redisConnection.GetSubscriber();
            var removeSubscription = subscriber.SubscribeAsync("cacheSleeve.remove.*", (redisChannel, value) => Settings.LocalCacher.Remove(GetString(value)));
            var flushSubscription = subscriber.SubscribeAsync("cacheSleeve.flush*", (redisChannel, value) => Settings.LocalCacher.FlushAll());
            Task.WaitAll(removeSubscription, flushSubscription);
        }
    }
}