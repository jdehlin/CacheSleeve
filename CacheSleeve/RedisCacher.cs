using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CacheSleeve.Models;
using Newtonsoft.Json;

namespace CacheSleeve
{
    public class RedisCacher : ICacher, IAsyncCacher
    {
        private readonly CacheManager _cacheSleeve;

        public RedisCacher()
        {
            _cacheSleeve = CacheManager.Settings;
            _cacheSleeve.Debug = true;
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
                return JsonConvert.DeserializeObject<T>(result, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Objects });
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

        public async Task<T> GetAsync<T>(string key)
        {
            var conn = _cacheSleeve.GetDatebase();
            if (typeof(T) == typeof(byte[]))
            {
                dynamic byteResult = await conn.StringGetAsync(_cacheSleeve.AddPrefix(key));
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
                return JsonConvert.DeserializeObject<T>(result, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Objects });
            }
            catch (JsonReaderException)
            {
                RemoveAsync(key); // this might be a really bad idea
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
            Task.WaitAll(tasks.ToArray());
        }

        public async Task<IEnumerable<Key>> GetAllKeysAsync()
        {
            var conn = _cacheSleeve.GetDatebase();
            var keys = new List<Key>();
            var keyStrings = _cacheSleeve.GetAllKeys().ToList();
            var tasks = new Dictionary<string, Task<TimeSpan?>>();
            foreach (var keyString in keyStrings)
                tasks.Add(keyString, conn.KeyTimeToLiveAsync(keyString));
            Task.WaitAll(tasks.Values.ToArray());
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
        /// <returns></returns>
        private bool InternalSet<T>(string key, T value, string parentKey = null)
        {
            var conn = _cacheSleeve.GetDatebase();
            var bytesValue = value as byte[];
            try
            {
                if (bytesValue != null)
                    conn.StringSet(_cacheSleeve.AddPrefix(key), bytesValue);
                else if (typeof(T) == typeof(string))
                    conn.StringSet(_cacheSleeve.AddPrefix(key), value as string);
                else
                {
                    var valueString = JsonConvert.SerializeObject(value, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Objects });
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
        /// Shared insert for public wrappers.
        /// </summary>
        /// <typeparam name="T">The type of the item to insert.</typeparam>
        /// <param name="key">The key of the item to insert.</param>
        /// <param name="value">The value of the item to insert.</param>
        /// <param name="parentKey">The key of the item that this item is a child of.</param>
        /// <returns></returns>
        private async Task<bool> InternalSetAsync<T>(string key, T value, string parentKey = null)
        {
            var conn = _cacheSleeve.GetDatebase();
            var bytesValue = value as byte[];
            try
            {
                if (bytesValue != null)
                    await conn.StringSetAsync(_cacheSleeve.AddPrefix(key), bytesValue);
                else if (typeof(T) == typeof(string))
                    await conn.StringSetAsync(_cacheSleeve.AddPrefix(key), value as string);
                else
                {
                    var valueString = JsonConvert.SerializeObject(value, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Objects });
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
        private void SetDependencies(string childKey, string parentKey)
        {
            if (string.IsNullOrWhiteSpace(_cacheSleeve.StripPrefix(childKey)) || string.IsNullOrWhiteSpace(_cacheSleeve.StripPrefix(parentKey)))
                return;

            var conn = _cacheSleeve.GetDatebase();
            conn.ListRightPush(parentKey + ".children", childKey);
            conn.StringSet(childKey + ".parent", parentKey);
            var ttl = conn.KeyTimeToLive(parentKey);
            if (ttl != null && ttl.Value.TotalSeconds > -1)
            {
                var children = conn.ListRange(parentKey + ".children", 0, (int)conn.ListLength(parentKey + ".children")).ToList();
                conn.KeyExpire(parentKey + ".children", ttl);
                conn.KeyExpire(childKey + ".parent", ttl);
                foreach (var child in children)
                    conn.KeyExpire(child.ToString(), ttl);
            }
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
            var parentKetPushTask = conn.ListRightPushAsync(parentKey + ".children", childKey);
            var childKeySetTask = conn.StringSetAsync(childKey + ".parent", parentKey);
            var ttlTask = conn.KeyTimeToLiveAsync(parentKey);
            Task.WaitAll(parentKetPushTask, childKeySetTask, ttlTask);
            var ttl = ttlTask.Result;
            if (ttl != null && ttl.Value.TotalSeconds > -1)
            {
                var children = (await conn.ListRangeAsync(parentKey + ".children", 0, (int) await conn.ListLengthAsync(parentKey + ".children"))).ToList();
                var parentKeyExpireTask = conn.KeyExpireAsync(parentKey + ".children", ttl);
                var childKeyExpireTask = conn.KeyExpireAsync(childKey + ".parent", ttl);
                Task.WaitAll(parentKeyExpireTask, childKeyExpireTask);
                var childExpirationTasks = new List<Task>();
                foreach (var child in children)
                    childExpirationTasks.Add(conn.KeyExpireAsync(child.ToString(), ttl));
                Task.WaitAll(childExpirationTasks.ToArray());
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
            var children = conn.ListRange(key + ".children", 0, (int)conn.ListLength(key + ".children")).ToList();
            foreach (var child in children)
            {
                conn.KeyDelete(child.ToString());
                conn.KeyDelete(child + ".parent");
            }
            conn.KeyDelete(key + ".children");
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
            var children = (await conn.ListRangeAsync(key + ".children", 0, (int)conn.ListLength(key + ".children"))).ToList();
            var tasks = new List<Task>();
            foreach (var child in children)
            {
                tasks.Add(conn.KeyDeleteAsync(child.ToString()));
                tasks.Add(conn.KeyDeleteAsync(child + ".parent"));
            }
            Task.WaitAll(tasks.ToArray());
            await conn.KeyDeleteAsync(key + ".children");
        }
    }
}