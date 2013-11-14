using System;
using System.Text.RegularExpressions;
using BookSleeve;

namespace CacheSleeve
{
    public sealed class CacheSleeve
    {
        private bool _setup;
        
        #region Singleton Setup

		private CacheSleeve()
		{
		}

		public static CacheSleeve Manager
		{
			get
			{
				return Nested.Manager;
			}
		}

		class Nested
		{
			static Nested()
			{
			}

			internal static readonly CacheSleeve Manager = new CacheSleeve();
		}

		#endregion

        public static void Init(string redisHost, int redisPort = 6379, string redisPassword = null, string keyPrefix = "cs.")
        {
            if (Manager._setup)
                throw new InvalidOperationException("Cannot reinitialize CacheSleeve");
            Manager._setup = true;

            Manager.RedisHost = redisHost;
            Manager.RedisPort = redisPort;
            Manager.RedisPassword = redisPassword;
            Manager.KeyPrefix = keyPrefix;
        }

        /// <summary>
        /// The prefix added to keys of items cached by CacheSleeve to prevent collisions.
        /// </summary>
        public string KeyPrefix { get; set; }
        
        /// <summary>
        /// The url to the Redis backplane.
        /// </summary>
        public string RedisHost { get; set; }

        /// <summary>
        /// The port for the Redis backplane.
        /// </summary>
        public int RedisPort { get; set; }

        /// <summary>
        /// The password for the Redis backplane.
        /// </summary>
        public string RedisPassword { get; set; }

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
            var regex = new Regex(string.Format("^{0}", KeyPrefix));
            return regex.Replace(key, String.Empty);
        }
    }
}