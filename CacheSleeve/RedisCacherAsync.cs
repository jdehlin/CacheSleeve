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
    public partial class RedisCacher : IAsyncCacher
    {
        public async Task<T> GetAsync<T>(string key)
        {
            var conn = _cacheSleeve.GetDatebase();

            var redisKey = _cacheSleeve.AddPrefix(key);
            if (typeof(T) == typeof(string) || typeof(T) == typeof(byte[]))
                return (T)(dynamic)(await conn.StringGetAsync(redisKey));
            string result;
            try
            {
                result = await conn.StringGetAsync(_cacheSleeve.AddPrefix(key));
            }
            catch (Exception)
            {
                return default(T);
            }
            if (result != null)
                return _objectSerializer.DeserializeObject<T>(result);
            return default(T);
        }

        public async Task<bool> SetAsync<T>(string key, T value, string parentKey = null)
        {
            var redisKey = _cacheSleeve.AddPrefix(key);
            if (await InternalSetAsync(redisKey, value))
            {
                await RemoveDependenciesAsync(redisKey);
                await SetDependenciesAsync(redisKey, _cacheSleeve.AddPrefix(parentKey));
            }
            return true;
        }

        public Task<bool> SetAsync<T>(string key, T value, DateTime expiresAt, string parentKey = null)
        {
            return SetAsync(key, value, expiresAt - DateTime.Now, parentKey);
        }

        public async Task<bool> SetAsync<T>(string key, T value, TimeSpan expiresIn, string parentKey = null)
        {
            var redisKey = _cacheSleeve.AddPrefix(key);
            var result = await InternalSetAsync(redisKey, value);
            if (result)
            {
                var conn = _cacheSleeve.GetDatebase();
                result = await conn.KeyExpireAsync(redisKey, expiresIn);
                await RemoveDependenciesAsync(redisKey);
                await SetDependenciesAsync(redisKey, _cacheSleeve.AddPrefix(parentKey));
            }
            return result;
        }

        public async Task<bool> RemoveAsync(string key)
        {
            var conn = _cacheSleeve.GetDatebase();
            var redisKey = _cacheSleeve.AddPrefix(key);
            if (await conn.KeyDeleteAsync(redisKey))
            {
                await RemoveDependenciesAsync(redisKey);
                await conn.KeyDeleteAsync(redisKey + ".parent");
                if (_cacheSleeve.Debug)
                    Trace.WriteLine(string.Format("CS Redis: Removed cache item with key {0}", key));
                return true;
            }
            return false;
        }

        public async Task FlushAllAsync()
        {
            var keys = _cacheSleeve.GetAllKeys();
            await _cacheSleeve.GetDatebase().KeyDeleteAsync(keys.ToString());
        }

        public async Task<IEnumerable<Key>> GetAllKeysAsync()
        {
            var conn = _cacheSleeve.GetDatebase();
            var keys = new List<Key>();
            var keyStrings = _cacheSleeve.GetAllKeys().ToList();
            var tasks = new Dictionary<string, Task<TimeSpan?>>();
            foreach (var keyString in keyStrings)
                tasks.Add(keyString, conn.KeyTimeToLiveAsync(keyString));
            await Task.WhenAll(tasks.Values.ToArray());
            foreach (var taskResult in tasks)
            {
                var key = taskResult.Key;
                var ttl = taskResult.Value.Result;
                keys.Add(new Key(key, ttl != null ? DateTime.Now.AddSeconds(ttl.Value.TotalSeconds) : (DateTime?)null));
            }
            return keys;
        }

        /// <summary>
        /// Gets the amount of time left before the item expires.
        /// </summary>
        /// <param name="key">The key to check.</param>
        /// <returns>The amount of time in seconds.</returns>
        public async Task<long> TimeToLiveAsync(string key)
        {
            var conn = _cacheSleeve.GetDatebase();
            var ttl = await conn.KeyTimeToLiveAsync(_cacheSleeve.AddPrefix(key));
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
        public async Task PublishToKeyAsync(string key, string message)
        {
            var conn = _cacheSleeve.GetDatebase();
            await conn.PublishAsync(key, message);
        }


        /// <summary>
        /// Shared insert for public wrappers.
        /// </summary>
        /// <typeparam name="T">The type of the item to insert.</typeparam>
        /// <param name="key">The key of the item to insert.</param>
        /// <param name="value">The value of the item to insert.</param>
        /// <returns></returns>
        private async Task<bool> InternalSetAsync<T>(string key, T value)
        {
            var conn = _cacheSleeve.GetDatebase();
            try
            {
                if (typeof(T) == typeof(byte[]))
                {
                    await conn.StringSetAsync(key, value as byte[]);
                }
                else if (typeof(T) == typeof(string))
                {
                    await conn.StringSetAsync(key, value as string);
                }
                else
                {
                    var serializedValue = _objectSerializer.SerializeObject<T>(value);
                    await conn.StringSetAsync(key, serializedValue);
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
        private async Task SetDependenciesAsync(string childKey, string parentKey)
        {
            if (childKey.Length <= _cacheSleeve.KeyPrefix.Length || parentKey.Length <= _cacheSleeve.KeyPrefix.Length)
                return;

            var conn = _cacheSleeve.GetDatebase();
            var parentDepKey = parentKey + ".children";
            var childDepKey = childKey + ".parent";
            var parentKetPushTask = conn.ListRightPushAsync(parentDepKey, childKey);
            var childKeySetTask = conn.StringSetAsync(childDepKey, parentKey);
            var ttlTask = conn.KeyTimeToLiveAsync(parentKey);
            await Task.WhenAll(parentKetPushTask, childKeySetTask, ttlTask);
            var ttl = ttlTask.Result;
            if (ttl != null && ttl.Value.TotalSeconds > -1)
            {
                var children = (await conn.ListRangeAsync(parentDepKey, 0, -1)).ToList();
                var expirationTasks = new List<Task>(children.Count + 2);
                expirationTasks.Add(conn.KeyExpireAsync(parentDepKey, ttl));
                expirationTasks.Add(conn.KeyExpireAsync(childDepKey, ttl));
                foreach (var child in children)
                    expirationTasks.Add(conn.KeyExpireAsync(child.ToString(), ttl));
                await Task.WhenAll(expirationTasks.ToArray());
            }
        }
        
        /// <summary>
        /// Removes all of the dependencies of the key from the cache.
        /// </summary>
        /// <param name="key">The key of the item to remove children for.</param>
        private async Task RemoveDependenciesAsync(string key)
        {
            if (key.Length <= _cacheSleeve.KeyPrefix.Length)
                return;

            var conn = _cacheSleeve.GetDatebase();
            var depKey = key + ".children";
            var children = (await conn.ListRangeAsync(depKey, 0, -1)).ToList();
            if (children.Count > 0)
            {
                var keys = new List<RedisKey>(children.Count * 2 + 1);
                keys.Add(depKey);
                foreach (var child in children)
                {
                    keys.Add(child.ToString());
                    keys.Add(child + ".parent");
                }
                await conn.KeyDeleteAsync(keys.ToArray());
            }
        }
    }
}