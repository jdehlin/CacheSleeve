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
        private readonly ICacheManager _cacheSleeve;
        private readonly IObjectSerializer _objectSerializer;

        public RedisCacher()
        {
            _cacheSleeve = CacheManager.Settings;
            _cacheSleeve.Debug = true;

            _objectSerializer = new JsonObjectSerializer();
        }

        public RedisCacher(
            ICacheManager cacheManger,
            IObjectSerializer serializer)
        {
            _cacheSleeve = cacheManger;
            _objectSerializer = serializer;
        }


        public T Get<T>(string key)
        {
            var conn = _cacheSleeve.GetDatebase();
            var redisKey = _cacheSleeve.AddPrefix(key);
            if (typeof(T) == typeof(string) || typeof(T) == typeof(byte[]))
                return (T)(dynamic)conn.StringGet(redisKey);
            string result;
            try
            {
                result = conn.StringGet(redisKey);
            }
            catch (Exception)
            {
                return default(T);
            }
            if (result != null)
                return _objectSerializer.DeserializeObject<T>(result);
            return default(T);
        }
        
        public bool Set<T>(string key, T value, string parentKey = null)
        {
            var redisKey = _cacheSleeve.AddPrefix(key);
            if (InternalSet(redisKey, value))
            {
                RemoveDependencies(redisKey);
                SetDependencies(redisKey, _cacheSleeve.AddPrefix(parentKey));
            }
            return true;
        }

        public bool Set<T>(string key, T value, DateTime expiresAt, string parentKey = null)
        {
            return Set(key, value, expiresAt - DateTime.Now, parentKey);
        }
        
        public bool Set<T>(string key, T value, TimeSpan expiresIn, string parentKey = null)
        {

            var redisKey = _cacheSleeve.AddPrefix(key);
            var result = InternalSet(redisKey, value);
            if (result)
            {
                var conn = _cacheSleeve.GetDatebase();
                result = conn.KeyExpire(redisKey, expiresIn);
                RemoveDependencies(redisKey);
                SetDependencies(redisKey, _cacheSleeve.AddPrefix(parentKey));
            }
            return result;
        }

        public bool Remove(string key)
        {
            var conn = _cacheSleeve.GetDatebase();
            var redisKey = _cacheSleeve.AddPrefix(key);
            if (conn.KeyDelete(redisKey))
            {
                RemoveDependencies(redisKey);
                conn.KeyDelete(redisKey + ".parent");
                if (_cacheSleeve.Debug)
                    Trace.WriteLine(string.Format("CS Redis: Removed cache item with key {0}", key));
                return true;
            }
            return false;
        }

        public void FlushAll()
        {
            var keys = _cacheSleeve.GetAllKeys();
            _cacheSleeve.GetDatebase().KeyDelete(keys.ToArray());
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
        /// <returns></returns>
        private bool InternalSet<T>(string key, T value)
        {
            var conn = _cacheSleeve.GetDatebase();
            try
            {
                if (typeof(T) == typeof(byte[]))
                {
                    conn.StringSet(key, value as byte[]);
                }
                else if (typeof(T) == typeof(string))
                {
                    conn.StringSet(key, value as string);
                }
                else
                {
                    var serializedValue = _objectSerializer.SerializeObject<T>(value);
                    conn.StringSet(key, serializedValue);
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
            if (childKey.Length <= _cacheSleeve.KeyPrefix.Length || parentKey.Length <= _cacheSleeve.KeyPrefix.Length)
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
            if (key.Length <= _cacheSleeve.KeyPrefix.Length)
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