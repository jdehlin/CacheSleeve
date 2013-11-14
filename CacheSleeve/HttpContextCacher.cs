using System;
using System.Collections;
using System.Collections.Generic;
using System.Web.Caching;

namespace CacheSleeve
{
    public class HttpContextCacher : ICacher
    {
        private readonly Cache _cache;
        private readonly CacheSleeve _cacheSleeve;

        public HttpContextCacher()
        {
            _cache = System.Web.HttpContext.Current.Cache;
            _cacheSleeve = CacheSleeve.Manager;
        }


        public T Get<T>(string key)
        {
            var cacheEntry = (CacheEntry)_cache.Get(_cacheSleeve.AddPrefix(key));
            if (cacheEntry == null)
                return (T)(object)null;
            return (T)cacheEntry.Value;
        }

        public IDictionary<string, T> GetAll<T>(IEnumerable<string> keys)
        {
            var items = new Dictionary<string, T>();
            if (keys != null)
                foreach (var key in keys)
                    items[_cacheSleeve.StripPrefix(key)] = Get<T>(key);
            else
                foreach (DictionaryEntry item in _cache)
                    if (item.Value.GetType() == typeof(CacheEntry))
                    {
                        var cacheItem = (item.Value as CacheEntry).Value;
                        items.Add(_cacheSleeve.StripPrefix(item.Key.ToString()), (T)cacheItem);
                    }
            return items;
        }

        public bool Set<T>(string key, T value)
        {
            var entry = new CacheEntry(value, null);
            return Set(key, entry);
        }

        public bool Set<T>(string key, T value, DateTime expiresAt)
        {
            var entry = new CacheEntry(value, expiresAt);
            return Set(key, entry);
        }

        public bool Set<T>(string key, T value, TimeSpan expiresIn)
        {
            var entry = new CacheEntry(value, DateTime.UtcNow.Add(expiresIn));
            return Set(key, entry);
        }

        public void SetAll<T>(IDictionary<string, T> values)
        {
            foreach (var entry in values)
                Set(entry.Key, entry.Value);
        }

        public bool Remove(string key)
        {
            if (_cache.Get(key) != null)
                return false;
            try
            {
                _cache.Remove(_cacheSleeve.AddPrefix(key));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void FlushAll()
        {
            var enumerator = _cache.GetEnumerator();
            while (enumerator.MoveNext())
                _cache.Remove(_cacheSleeve.AddPrefix(enumerator.Key.ToString()));
        }

        /// <summary>
        /// Shared insert for public wrappers.
        /// </summary>
        /// <param name="key">The key of the item to insert.</param>
        /// <param name="entry">The internal CacheEntry object to insert.</param>
        private bool Set(string key, CacheEntry entry)
        {
            try
            {
                if (entry.ExpiresAt == null)
                    _cache.Insert(_cacheSleeve.AddPrefix(key), entry);
                else
                    _cache.Insert(_cacheSleeve.AddPrefix(key), entry, null, entry.ExpiresAt.Value, Cache.NoSlidingExpiration);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Private class for the wrapper around the cache items.
        /// </summary>
        private class CacheEntry
        {
            /// <summary>
            /// Creates a new instance of CacheEntry.
            /// </summary>
            /// <param name="value">The value being cached.</param>
            /// <param name="expiresAt">The UTC time at which CacheEntry expires.</param>
            public CacheEntry(object value, DateTime? expiresAt)
            {
                Value = value;
                ExpiresAt = expiresAt;
            }

            /// <summary>
            /// UTC time at which CacheEntry expires.
            /// </summary>
            internal DateTime? ExpiresAt { get; private set; }

            /// <summary>
            /// The value that is cached.
            /// </summary>
            internal object Value { get; private set; }
        }
    }
}