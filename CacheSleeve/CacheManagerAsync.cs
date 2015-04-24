using System.Threading.Tasks;
using StackExchange.Redis;

namespace CacheSleeve
{
    public sealed partial class CacheManager
    {
        public async static void InitMultiAsync(string hosts, string redisPassword = null, int redisDb = 0, string keyPrefix = "cs.", int timeoutMilli = 5000)
        {
            var configuration =
                ConfigurationOptions.Parse(hosts);
            configuration.AllowAdmin = true;
            configuration.Password = redisPassword;
            configuration.AbortOnConnectFail = false;
            configuration.ConnectTimeout = timeoutMilli;

            await InitAsync(configuration, redisDb, keyPrefix);
        }

        public async static Task InitAsync(string redisHost, int redisPort = 6379, string redisPassword = null, int redisDb = 0, string keyPrefix = "cs.", int timeoutMilli = 5000)
        {
            var configuration =
                ConfigurationOptions.Parse(string.Format("{0}:{1}", redisHost, redisPort));
            configuration.AllowAdmin = true;
            configuration.Password = redisPassword;
            configuration.AbortOnConnectFail = false;
            configuration.ConnectTimeout = timeoutMilli; 

            await InitAsync(configuration, redisDb, keyPrefix);
        }

        public async static Task InitAsync(ConfigurationOptions config, int redisDb = 0, string keyPrefix = "cs.")
        {
            PopulateSettings(config, redisDb, keyPrefix);

            Settings._redisConnection = ConnectionMultiplexer.Connect(config);

            // Setup pub/sub for cache syncing
            var subscriber = Settings._redisConnection.GetSubscriber();
            var removeSubscription = subscriber.SubscribeAsync("cacheSleeve.remove.*", (redisChannel, value) => Settings.LocalCacher.Remove(GetString(value)));
            var flushSubscription = subscriber.SubscribeAsync("cacheSleeve.flush*", (redisChannel, value) => Settings.LocalCacher.FlushAll());
            await Task.WhenAll(removeSubscription, flushSubscription);
        }
    }
}