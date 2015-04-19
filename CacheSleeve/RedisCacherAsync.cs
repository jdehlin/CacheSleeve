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
            if (typeof(T) == typeof(byte[]))
            {
                object byteResult = await conn.StringGetAsync(_cacheSleeve.AddPrefix(key));
                return (T)byteResult;
            }
            string result;
            try
            {
                result = await conn.StringGetAsync(_cacheSleeve.AddPrefix(key));
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

        public async Task<bool> SetAsync<T>(string key, T value, string parentKey = null)
        {
            if (await InternalSetAsync(key, value, parentKey))
            {
                await RemoveDependenciesAsync(_cacheSleeve.AddPrefix(key));
                await SetDependenciesAsync(_cacheSleeve.AddPrefix(key), _cacheSleeve.AddPrefix(parentKey));
            }
            return true;
        }

        public async Task<bool> SetAsync<T>(string key, T value, DateTime expiresAt, string parentKey = null)
        {
            var result = false;
            if (await InternalSetAsync(key, value))
            {
                var conn = _cacheSleeve.GetDatebase();
                var timeSpan = (expiresAt - DateTime.Now);
                result = await conn.KeyExpireAsync(_cacheSleeve.AddPrefix(key), timeSpan);
                await RemoveDependenciesAsync(_cacheSleeve.AddPrefix(key));
                await SetDependenciesAsync(_cacheSleeve.AddPrefix(key), _cacheSleeve.AddPrefix(parentKey));
            }
            return result;
        }

        public async Task<bool> SetAsync<T>(string key, T value, TimeSpan expiresIn, string parentKey = null)
        {
            var result = false;
            if (await InternalSetAsync(key, value))
            {
                var conn = _cacheSleeve.GetDatebase();
                result = await conn.KeyExpireAsync(_cacheSleeve.AddPrefix(key), expiresIn);
                await RemoveDependenciesAsync(_cacheSleeve.AddPrefix(key));
                await SetDependenciesAsync(_cacheSleeve.AddPrefix(key), _cacheSleeve.AddPrefix(parentKey));
            }
            return result;
        }

        public async Task<bool> RemoveAsync(string key)
        {
            var conn = _cacheSleeve.GetDatebase();
            if (await conn.KeyDeleteAsync(_cacheSleeve.AddPrefix(key)))
            {
                await RemoveDependenciesAsync(_cacheSleeve.AddPrefix(key));
                await conn.KeyDeleteAsync(_cacheSleeve.AddPrefix(key + ".parent"));
                if (_cacheSleeve.Debug)
                    Trace.WriteLine(string.Format("CS Redis: Removed cache item with key {0}", key));
                return true;
            }
            return false;
        }

        public async Task FlushAllAsync()
        {
            var keys = _cacheSleeve.GetAllKeys();
            var tasks = new List<Task>();
            foreach (var key in keys)
                tasks.Add(RemoveAsync(_cacheSleeve.StripPrefix(key)));
            await Task.WhenAll(tasks.ToArray());
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
            foreach (var keyString in keyStrings)
            {
                var ttl = tasks[keyString].Result;
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
        /// <param name="parentKey">The key of the item that this item is a child of.</param>
        private async Task<bool> InternalSetAsync<T>(string key, T value, string parentKey = null)
        {
            var conn = _cacheSleeve.GetDatebase();
            try
            {
                if (typeof(T) == typeof(byte[]))
                {
                    var bytesValue = value as byte[];
                    if (bytesValue != null)
                        await conn.StringSetAsync(_cacheSleeve.AddPrefix(key), bytesValue);
                }
                else if (typeof(T) == typeof(string))
                {
                    var stringValue = value as string;
                    if (stringValue != null)
                        await conn.StringSetAsync(_cacheSleeve.AddPrefix(key), stringValue);
                }
                else
                {
                    var valueString = JsonConvert.SerializeObject(value, this._jsonSettings);
                    await conn.StringSetAsync(_cacheSleeve.AddPrefix(key), valueString);
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
            if (string.IsNullOrWhiteSpace(_cacheSleeve.StripPrefix(childKey)) || string.IsNullOrWhiteSpace(_cacheSleeve.StripPrefix(parentKey)))
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
            if (string.IsNullOrWhiteSpace(_cacheSleeve.StripPrefix(key)))
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