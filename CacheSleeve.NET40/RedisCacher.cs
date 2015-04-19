using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CacheSleeve.Models;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace CacheSleeve
{
    public partial class RedisCacher : ICacher
    {
        private readonly CacheManager _cacheSleeve;
        private readonly JsonSerializerSettings _jsonSettings;

        public RedisCacher()
        {
            _cacheSleeve = CacheManager.Settings;
            _cacheSleeve.Debug = true;

             _jsonSettings = new JsonSerializerSettings
             {
                 TypeNameHandling = TypeNameHandling.Objects
             };
        }


        public T Get<T>(string key)
        {
            var conn = _cacheSleeve.GetDatebase();
            if (typeof (T) == typeof (byte[]))
            {
                dynamic byteResult = conn.StringGet(_cacheSleeve.AddPrefix(key));
                return (T)byteResult;
            }
            string result;
            try
            {
                result = conn.StringGet(_cacheSleeve.AddPrefix(key));
            }
            catch (Exception)
            {
                return default(T);
            }
            if (result == null || typeof(T) == typeof(string))
                return (T)(object)result;
            try
            {
                return JsonConvert.DeserializeObject<T>(result, _jsonSettings);
            }
            catch (JsonReaderException)
            {
                Remove(key);
                return default(T);
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
                var conn = _cacheSleeve.GetDatebase();
                var timeSpan = (expiresAt - DateTime.Now);
                result = conn.KeyExpire(_cacheSleeve.AddPrefix(key), timeSpan);
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
                var conn = _cacheSleeve.GetDatebase();
                result = conn.KeyExpire(_cacheSleeve.AddPrefix(key), expiresIn);
                RemoveDependencies(_cacheSleeve.AddPrefix(key));
                SetDependencies(_cacheSleeve.AddPrefix(key), _cacheSleeve.AddPrefix(parentKey));
            }
            return result;
        }

        public bool Remove(string key)
        {
            var conn = _cacheSleeve.GetDatebase();
            if (conn.KeyDelete(_cacheSleeve.AddPrefix(key)))
            {
                RemoveDependencies(_cacheSleeve.AddPrefix(key));
                conn.KeyDelete(_cacheSleeve.AddPrefix(key + ".parent"));
                if (_cacheSleeve.Debug)
                    Trace.WriteLine(string.Format("CS Redis: Removed cache item with key {0}", key));
                return true;
            }
            return false;
        }

        public void FlushAll()
        {
            var keys = _cacheSleeve.GetAllKeys();
            foreach (var key in keys)
                Remove(_cacheSleeve.StripPrefix(key));
        }

        public IEnumerable<Key> GetAllKeys()
        {
            var conn = _cacheSleeve.GetDatebase();
            var keys = new List<Key>();
            var keyStrings = _cacheSleeve.GetAllKeys();
            foreach (var keyString in keyStrings)
            {
                var ttl = conn.KeyTimeToLive(keyString);
                var expiration = default(DateTime?);
                if (ttl != null)
                    expiration = DateTime.Now.AddSeconds(ttl.Value.TotalSeconds);
                keys.Add(new Key(keyString, expiration));
            }
            return keys;
        }

        /// <summary>
        /// Gets the amount of time left before the item expires.
        /// </summary>
        /// <param name="key">The key to check.</param>
        /// <returns>The amount of time in seconds.</returns>
        public long TimeToLive(string key)
        {
            var conn = _cacheSleeve.GetDatebase();
            var ttl = conn.KeyTimeToLive(_cacheSleeve.AddPrefix(key));
            if (ttl == null)
                return -1;
            return (long)ttl.Value.TotalSeconds;
        }

        /// <summary>
        /// Publishes a message with a specified key.
        /// Any clients connected to the Redis server and subscribed to the key will recieve the message.
        /// </summary>
        /// <param name="key">The key that other clients subscribe to.</param>
        /// <param name="message">The message to send to subscribed clients.</param>
        public void PublishToKey(string key, string message)
        {
            var conn = _cacheSleeve.GetDatebase();
            conn.Publish(key, message);
        }
                

        /// <summary>
        /// Shared insert for public wrappers.
        /// </summary>
        /// <typeparam name="T">The type of the item to insert.</typeparam>
        /// <param name="key">The key of the item to insert.</param>
        /// <param name="value">The value of the item to insert.</param>
        /// <param name="parentKey">The key of the item that this item is a child of.</param>
        private bool InternalSet<T>(string key, T value, string parentKey = null)
        {
            var conn = _cacheSleeve.GetDatebase();
            try
            {
                if (typeof(T) == typeof(byte[]))
                {
                    var bytesValue = value as byte[];
                    if (bytesValue != null)
                        conn.StringSet(_cacheSleeve.AddPrefix(key), bytesValue);
                }
                else if (typeof(T) == typeof(string))
                {
                    var stringValue = value as string;
                    if (stringValue != null)
                        conn.StringSet(_cacheSleeve.AddPrefix(key), value as string);
                }
                else
                {
                    var valueString = JsonConvert.SerializeObject(value, this._jsonSettings);
                    conn.StringSet(_cacheSleeve.AddPrefix(key), valueString);
                }
                if (_cacheSleeve.Debug)
                    Trace.WriteLine(string.Format("CS Redis: Set cache item with key {0}", key));
            }
            catch (Exception)
            {
                return false;
            }
            return true;
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

            var conn = _cacheSleeve.GetDatebase();
            var parentDepKey = parentKey + ".children";
            var childDepKey = childKey + ".parent";

            conn.ListRightPush(parentDepKey, childKey);
            conn.StringSet(childDepKey, parentKey);
            var ttl = conn.KeyTimeToLive(parentKey);
            if (ttl != null && ttl.Value.TotalSeconds > -1)
            {
                var children = conn.ListRange(parentDepKey, 0, -1).ToList();
                conn.KeyExpire(parentDepKey, ttl);
                conn.KeyExpire(childDepKey, ttl);
                foreach (var child in children)
                    conn.KeyExpire(child.ToString(), ttl);
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

            var conn = _cacheSleeve.GetDatebase();
            var depKey = key + ".children";
            var children = conn.ListRange(depKey, 0, -1).ToList();
            if (children.Count > 0)
            {
                var keys = new List<RedisKey>(children.Count * 2 + 1);
                keys.Add(depKey);
                foreach (var child in children)
                {
                    keys.Add(child.ToString());
                    keys.Add(child + ".parent");
                }
                conn.KeyDelete(keys.ToArray());
            }
        }
    }
}