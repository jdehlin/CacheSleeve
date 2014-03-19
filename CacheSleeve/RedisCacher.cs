using System;
using System.Linq;
using BookSleeve;
using Newtonsoft.Json;

namespace CacheSleeve
{
    public class RedisCacher : ICacher
    {
        private readonly CacheManager _cacheSleeve;

        public RedisCacher()
        {
            _cacheSleeve = CacheManager.Settings;
        }


        public T Get<T>(string key)
        {
            using (var conn = new RedisConnection(_cacheSleeve.RedisHost, _cacheSleeve.RedisPort, -1, _cacheSleeve.RedisPassword))
            {
                conn.Wait(conn.Open());
                if (typeof(T) == typeof(byte[]))
                    return (T)(object)conn.Strings.Get(_cacheSleeve.RedisDb, _cacheSleeve.AddPrefix(key)).Result;
                string result;
                try
                {
                    result = conn.Strings.GetString(_cacheSleeve.RedisDb, _cacheSleeve.AddPrefix(key)).Result;
                }
                catch (Exception)
                {
                    return default(T);
                }
                if (result == null || typeof(T) == typeof(string))
                    return (T)(object)result;
                try
                {
                    return JsonConvert.DeserializeObject<T>(result, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Objects });
                }
                catch (JsonReaderException)
                {
                    Remove(key);
                    return default(T);
                }
            }
        }

        public bool Set<T>(string key, T value, string parentKey = null)
        {
            if (InternalSet(key, value, parentKey))
            {
                RemoveDependencies(_cacheSleeve.AddPrefix(key));
                SetDependencies(_cacheSleeve.AddPrefix(key), _cacheSleeve.AddPrefix(parentKey));
            }
            return true;
        }

        public bool Set<T>(string key, T value, DateTime expiresAt, string parentKey = null)
        {
            var result = false;
            if (InternalSet(key, value))
            {
                using (var conn = new RedisConnection(_cacheSleeve.RedisHost, _cacheSleeve.RedisPort, -1, _cacheSleeve.RedisPassword))
                {
                    conn.Open();
                    var seconds = (int) (expiresAt - DateTime.Now).TotalSeconds;
                    result = conn.Keys.Expire(_cacheSleeve.RedisDb, _cacheSleeve.AddPrefix(key), seconds).Result;
                }
                RemoveDependencies(_cacheSleeve.AddPrefix(key));
                SetDependencies(_cacheSleeve.AddPrefix(key), _cacheSleeve.AddPrefix(parentKey));
            }
            return result;
        }

        public bool Set<T>(string key, T value, TimeSpan expiresIn, string parentKey = null)
        {
            var result = false;
            if (InternalSet(key, value))
            {
                using (var conn = new RedisConnection(_cacheSleeve.RedisHost, _cacheSleeve.RedisPort, -1, _cacheSleeve.RedisPassword))
                {
                    conn.Open();
                    result = conn.Keys.Expire(_cacheSleeve.RedisDb, _cacheSleeve.AddPrefix(key), (int)expiresIn.TotalSeconds).Result;
                }
                RemoveDependencies(_cacheSleeve.AddPrefix(key));
                SetDependencies(_cacheSleeve.AddPrefix(key), _cacheSleeve.AddPrefix(parentKey));
            }
            return result;
        }

        public bool Remove(string key)
        {
            using (var conn = new RedisConnection(_cacheSleeve.RedisHost, _cacheSleeve.RedisPort, -1, _cacheSleeve.RedisPassword))
            {
                conn.Open();
                if (conn.Keys.Remove(_cacheSleeve.RedisDb, _cacheSleeve.AddPrefix(key)).Result)
                {
                    RemoveDependencies(_cacheSleeve.AddPrefix(key));
                    conn.Keys.Remove(_cacheSleeve.RedisDb, _cacheSleeve.AddPrefix(key + ".parent"));
                    return true;
                }
                return false;
            }
        }

        public void FlushAll()
        {
            using (var conn = new RedisConnection(_cacheSleeve.RedisHost, _cacheSleeve.RedisPort, -1, _cacheSleeve.RedisPassword))
            {
                conn.Open();
                var keys = conn.Keys.Find(_cacheSleeve.RedisDb, _cacheSleeve.AddPrefix("*")).Result;
                foreach (var key in keys)
                    Remove(_cacheSleeve.StripPrefix(key));
            }
        }

        /// <summary>
        /// Gets the amount of time left before the item expires.
        /// </summary>
        /// <param name="key">The key to check.</param>
        /// <returns>The amount of time in seconds.</returns>
        public long TimeToLive(string key)
        {
            using (var conn = new RedisConnection(_cacheSleeve.RedisHost, _cacheSleeve.RedisPort, -1, _cacheSleeve.RedisPassword))
            {
                conn.Open();
                return conn.Keys.TimeToLive(_cacheSleeve.RedisDb, _cacheSleeve.AddPrefix(key)).Result;
            }
        }

        /// <summary>
        /// Publishes a message with a specified key.
        /// Any clients connected to the Redis server and subscribed to the key will recieve the message.
        /// </summary>
        /// <param name="key">The key that other clients subscribe to.</param>
        /// <param name="message">The message to send to subscribed clients.</param>
        public void PublishToKey(string key, string message)
        {
            using (var conn = new RedisConnection(_cacheSleeve.RedisHost, _cacheSleeve.RedisPort, -1, _cacheSleeve.RedisPassword))
            {
                conn.Open();
                conn.Publish(key, message);
            }
        }

        /// <summary>
        /// Shared insert for public wrappers.
        /// </summary>
        /// <typeparam name="T">The type of the item to insert.</typeparam>
        /// <param name="key">The key of the item to insert.</param>
        /// <param name="value">The value of the item to insert.</param>
        /// <param name="parentKey">The key of the item that this item is a child of.</param>
        /// <returns></returns>
        private bool InternalSet<T>(string key, T value, string parentKey = null)
        {
            using (var conn = new RedisConnection(_cacheSleeve.RedisHost, _cacheSleeve.RedisPort, -1, _cacheSleeve.RedisPassword))
            {
                conn.Open();
                var bytesValue = value as byte[];
                try
                {
                    if (bytesValue != null)
                        conn.Strings.Set(_cacheSleeve.RedisDb, _cacheSleeve.AddPrefix(key), bytesValue);
                    else if (typeof(T) == typeof(string))
                        conn.Strings.Set(_cacheSleeve.RedisDb, _cacheSleeve.AddPrefix(key), value as string);
                    else
                    {
                        var valueString = JsonConvert.SerializeObject(value, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Objects });
                        conn.Strings.Set(_cacheSleeve.RedisDb, _cacheSleeve.AddPrefix(key), valueString);                        
                    }
                }
                catch (Exception)
                {
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Adds a child key as a dependency of a parent key.
        /// When the parent is invalidated by remove, overwrite, or expiration the child will be removed.
        /// </summary>
        /// <param name="childKey">The key of the child item.</param>
        /// <param name="parentKey">The key of the parent item.</param>
        private void SetDependencies(string childKey, string parentKey)
        {
            if (string.IsNullOrWhiteSpace(_cacheSleeve.StripPrefix(childKey)) || string.IsNullOrWhiteSpace(_cacheSleeve.StripPrefix(parentKey)))
                return;

            using (var conn = new RedisConnection(_cacheSleeve.RedisHost, _cacheSleeve.RedisPort, -1, _cacheSleeve.RedisPassword))
            {
                conn.Open();
                conn.Lists.AddLast(_cacheSleeve.RedisDb, parentKey + ".children", childKey);
                conn.Strings.Set(_cacheSleeve.RedisDb, childKey + ".parent", parentKey);
                var ttl = (int)conn.Keys.TimeToLive(_cacheSleeve.RedisDb, parentKey).Result;                
                if (ttl > -1)
                {
                    var children = conn.Lists.RangeString(_cacheSleeve.RedisDb, parentKey + ".children", 0, (int)conn.Lists.GetLength(_cacheSleeve.RedisDb, parentKey + ".children").Result).Result.ToList();
                    conn.Keys.Expire(_cacheSleeve.RedisDb, parentKey + ".children", ttl);
                    conn.Keys.Expire(_cacheSleeve.RedisDb, childKey + ".parent", ttl);
                    foreach (var child in children)
                        conn.Keys.Expire(_cacheSleeve.RedisDb, child, ttl);
                }
            }
        }

        /// <summary>
        /// Removes all of the dependencies of the key from the cache.
        /// </summary>
        /// <param name="key">The key of the item to remove children for.</param>
        private void RemoveDependencies(string key)
        {
            if (string.IsNullOrWhiteSpace(_cacheSleeve.StripPrefix(key)))
                return;

            using (var conn = new RedisConnection(_cacheSleeve.RedisHost, _cacheSleeve.RedisPort, -1, _cacheSleeve.RedisPassword))
            {
                conn.Open();
                var children = conn.Lists.RangeString(_cacheSleeve.RedisDb, key + ".children", 0, (int)conn.Lists.GetLength(_cacheSleeve.RedisDb, key + ".children").Result).Result.ToList();
                foreach (var child in children)
                {
                    conn.Keys.Remove(_cacheSleeve.RedisDb, child);
                    conn.Keys.Remove(_cacheSleeve.RedisDb, child + ".parent");
                }

                conn.Keys.Remove(_cacheSleeve.RedisDb, key + ".children");
            }
        }
    }
}