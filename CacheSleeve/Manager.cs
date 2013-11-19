using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using BookSleeve;
using CacheSleeve.Utilities;

namespace CacheSleeve
{
    public sealed class Manager
    {
        private bool _setup;
        
        #region Singleton Setup

		private Manager()
		{
		}

		public static Manager Settings
		{
			get
			{
				return Nested.Settings;
			}
		}

		class Nested
		{
			static Nested()
			{
			}

			internal static readonly Manager Settings = new Manager();
		}

		#endregion

        public static void Init(string redisHost, int redisPort = 6379, string redisPassword = null, string keyPrefix = "cs.")
        {
            if (Settings._setup)
                if (!UnitTestDetector.IsRunningFromXunit) throw new InvalidOperationException("Cannot reinitialize CacheSleeve");
            Settings._setup = true;

            Settings.RedisHost = redisHost;
            Settings.RedisPort = redisPort;
            Settings.RedisPassword = redisPassword;
            Settings.KeyPrefix = keyPrefix;

            Settings.RemoteCacher = new RedisCacher();
            Settings.LocalCacher = new HttpContextCacher();

            // Setup pub/sub for cache syncing
            var connection = new RedisConnection(redisHost, redisPort, -1, redisPassword);
            var channel = connection.GetOpenSubscriberChannel();
            channel.PatternSubscribe("cacheSleeve.remove.*", (key, message) => Settings.LocalCacher.Remove(GetString(message)));
            channel.PatternSubscribe("cacheSleeve.flush*", (key, message) => Settings.LocalCacher.FlushAll());
        }

        /// <summary>
        /// The out of band caching service used as a backplane to share cache across servers.
        /// </summary>
        public RedisCacher RemoteCacher { get; private set; }

        /// <summary>
        /// The local in-memory cache.
        /// </summary>
        public HttpContextCacher LocalCacher { get; private set; }

        /// <summary>
        /// The prefix added to keys of items cached by CacheSleeve to prevent collisions.
        /// </summary>
        public string KeyPrefix { get; private set; }
        
        /// <summary>
        /// The url to the Redis backplane.
        /// </summary>
        public string RedisHost { get; private set; }

        /// <summary>
        /// The port for the Redis backplane.
        /// </summary>
        public int RedisPort { get; private set; }

        /// <summary>
        /// The password for the Redis backplane.
        /// </summary>
        public string RedisPassword { get; private set; }

        /// <summary>
        /// Adds the prefix to the key.
        /// </summary>
        /// <param name="key">The specified key value.</param>
        /// <returns>The specified key with the prefix attached.</returns>
        public string AddPrefix(string key)
        {
            return string.Format("{0}{1}", KeyPrefix, key);
        }

        /// <summary>
        /// Removes the prefix from the key.
        /// </summary>
        /// <param name="key">The internal key with the prefix attached.</param>
        /// <returns>The key without the prefix.</returns>
        public string StripPrefix(string key)
        {
            if (key == null)
                return null;
            var regex = new Regex(string.Format("^{0}", KeyPrefix));
            return regex.Replace(key, String.Empty);
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