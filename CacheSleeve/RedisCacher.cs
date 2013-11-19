using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BookSleeve;
using Newtonsoft.Json;

namespace CacheSleeve
{
    public class RedisCacher : ICacher
    {
        private const int Db = 0;
        private readonly Manager _cacheSleeve;

        public RedisCacher()
        {
            _cacheSleeve = Manager.Settings;
        }


        public T Get<T>(string key)
        {
            using (var conn = new RedisConnection(_cacheSleeve.RedisHost, _cacheSleeve.RedisPort, -1, _cacheSleeve.RedisPassword))
            {
                conn.Wait(conn.Open());
                if (typeof(T) == typeof(byte[]))
                    return (T)(object)conn.Strings.Get(Db, _cacheSleeve.AddPrefix(key)).Result;
                string result;
                try
                {
                    result = conn.Strings.GetString(Db, _cacheSleeve.AddPrefix(key)).Result;
                }
                catch (Exception)
                {
                    return default(T);
                }
                if (result == null || typeof(T) == typeof(string))
                    return (T)(object)result;
                try
                {
                    return JsonConvert.DeserializeObject<T>(result);
                }
                catch (JsonReaderException)
                {
                    Remove(key);
                    return default(T);
                }
            }
        }

        public Dictionary<string, T> GetAll<T>(IEnumerable<string> keys = null)
        {
            if (keys == null)
                using (var conn = new RedisConnection(_cacheSleeve.RedisHost, _cacheSleeve.RedisPort, -1, _cacheSleeve.RedisPassword))
                {
                    conn.Open();
                    keys = conn.Keys.Find(Db, _cacheSleeve.AddPrefix("*")).Result;
                    keys = keys.Select(k => _cacheSleeve.StripPrefix(k));
                }

            var keyByteValues = default(byte[][]);
            var keyStringValues = default(string[]);
            var keysArray = keys.Select(k => _cacheSleeve.AddPrefix(k)).ToArray();
            var results = new Dictionary<string, T>();

            using (var conn = new RedisConnection(_cacheSleeve.RedisHost, _cacheSleeve.RedisPort, -1, _cacheSleeve.RedisPassword))
            {
                conn.Open();
                if (typeof (T) == typeof (byte[]))
                    keyByteValues = conn.Strings.Get(Db, keysArray).Result;
                else
                    keyStringValues = conn.Strings.GetString(Db, keysArray).Result;
            }

            var i = 0;
            if (keyByteValues != null)
                foreach (var keyValue in keyByteValues)
                {
                    var key = _cacheSleeve.StripPrefix(keysArray[i++]);
                    results[key] = (T)(object)keyValue;
                }
            else if (keyStringValues != null)
                foreach (var keyValue in keyStringValues)
                {
                    var key = _cacheSleeve.StripPrefix(keysArray[i++]);
                    if (keyValue == null)
                        results[key] = default(T);
                    else
                    {
                        var keyValueString = keyValue;
                        if (typeof(T) == typeof(string) || typeof(T) == typeof(String))
                            results[key] = (T)(object)keyValueString;
                        else
                            try
                            {
                                results[key] = JsonConvert.DeserializeObject<T>(keyValueString);
                            }
                            catch (JsonSerializationException) { }
                    }
                }
            return results;
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
                    result = conn.Keys.Expire(Db, _cacheSleeve.AddPrefix(key), seconds).Result;
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
                    result = conn.Keys.Expire(Db, _cacheSleeve.AddPrefix(key), (int) expiresIn.TotalSeconds).Result;
                }
                RemoveDependencies(_cacheSleeve.AddPrefix(key));
                SetDependencies(_cacheSleeve.AddPrefix(key), _cacheSleeve.AddPrefix(parentKey));
            }
            return result;
        }

        public void SetAll<T>(Dictionary<string, T> values)
        {
            var valBytes = new Dictionary<string, byte[]>();
            var valStrings = new Dictionary<string, string>();
            foreach (var item in values)
            {
                if (typeof(T) == typeof(string))
                    valStrings.Add(_cacheSleeve.AddPrefix(item.Key), item.Value.ToString());
                else if (typeof (T) == typeof (byte[]))
                    valBytes.Add(_cacheSleeve.AddPrefix(item.Key), (byte[]) (object) item.Value ?? new byte[] {});
                else
                {
                    var json = JsonConvert.SerializeObject(item.Value);
                    valStrings.Add(_cacheSleeve.AddPrefix(item.Key), json);
                }
                    
            }
            using (var conn = new RedisConnection(_cacheSleeve.RedisHost, _cacheSleeve.RedisPort, -1, _cacheSleeve.RedisPassword))
            {
                conn.Open();
                if (typeof(T) == typeof(byte[]))
                    conn.Strings.Set(Db, valBytes);
                else
                    conn.Strings.Set(Db, valStrings);
            }
            foreach (var key in values.Keys)
                RemoveDependencies(_cacheSleeve.AddPrefix(key));
        }

        public bool Remove(string key)
        {
            using (var conn = new RedisConnection(_cacheSleeve.RedisHost, _cacheSleeve.RedisPort, -1, _cacheSleeve.RedisPassword))
            {
                conn.Open();
                if (conn.Keys.Remove(Db, _cacheSleeve.AddPrefix(key)).Result)
                {
                    RemoveDependencies(_cacheSleeve.AddPrefix(key));
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
                var keys = conn.Keys.Find(Db, _cacheSleeve.AddPrefix("*")).Result;
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
                return conn.Keys.TimeToLive(Db, _cacheSleeve.AddPrefix(key)).Result;
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
                        conn.Strings.Set(Db, _cacheSleeve.AddPrefix(key), bytesValue);
                    else if (typeof(T) == typeof(string))
                        conn.Strings.Set(Db, _cacheSleeve.AddPrefix(key), value as string);
                    else
                    {
                        var valueString = JsonConvert.SerializeObject(value);
                        conn.Strings.Set(Db, _cacheSleeve.AddPrefix(key), valueString);                        
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
                conn.Lists.AddLast(Db, parentKey + ".children", childKey);
                var ttl = (int)conn.Keys.TimeToLive(Db, parentKey).Result;
                var children = conn.Lists.RangeString(Db, parentKey + ".children", 0, (int)conn.Lists.GetLength(Db, parentKey + ".children").Result).Result.ToList();
                if (ttl > -1)
                {
                    conn.Keys.Expire(Db, parentKey + ".children", ttl);
                    foreach (var child in children)
                        conn.Keys.Expire(Db, child, ttl);
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
                var children = conn.Lists.RangeString(Db, key + ".children", 0, (int)conn.Lists.GetLength(Db, key + ".children").Result).Result.ToList();
                foreach (var child in children)
                    conn.Keys.Remove(Db, child);
                conn.Keys.Remove(Db, key + ".children");
            }
        }
    }
}