using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Web.Caching;
using CacheSleeve.Models;

namespace CacheSleeve
{
    public class HttpContextCacher : ICacher
    {
        private readonly Cache _cache;
        private readonly CacheManager _cacheSleeve;

        public HttpContextCacher()
        {
            _cache = System.Web.HttpContext.Current.Cache;
            _cacheSleeve = CacheManager.Settings;
            _cacheSleeve.Debug = true;
        }


        public T Get<T>(string key)
        {
            var cacheEntry = (CacheEntry)_cache.Get(_cacheSleeve.AddPrefix(key));
            if (cacheEntry == null || cacheEntry.Value.GetType() != typeof (T))
            {
                Remove(key);
                return default(T);   
            }
            return (T)cacheEntry.Value;
        }

        public bool Set<T>(string key, T value, string parentKey = null)
        {
            var entry = new CacheEntry(value, null);
            return InternalSet(key, entry, parentKey);
        }

        public bool Set<T>(string key, T value, DateTime expiresAt, string parentKey = null)
        {
            var entry = new CacheEntry(value, expiresAt);
            return InternalSet(key, entry, parentKey);
        }

        public bool Set<T>(string key, T value, TimeSpan expiresIn, string parentKey = null)
        {
            var entry = new CacheEntry(value, DateTime.UtcNow.Add(expiresIn));
            return InternalSet(key, entry, parentKey);
        }
        
        public bool Remove(string key)
        {
            if (_cache.Get(_cacheSleeve.AddPrefix(key)) == null)
                return false;
            try
            {
                _cache.Remove(_cacheSleeve.AddPrefix(key));
                _cache.Remove(_cacheSleeve.AddPrefix(key + ".parent"));
                if (_cacheSleeve.Debug)
                    Trace.WriteLine(string.Format("CS HttpContext: Removed cache item with key {0}", key));
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
                _cache.Remove(enumerator.Key.ToString());
        }

        public IEnumerable<Key> GetAllKeys()
        {
            var keys = _cache.Cast<DictionaryEntry>()
                .Where(de => de.Value.GetType() == typeof(CacheEntry))
                .Select(de => new Key((de.Key as string), (de.Value as CacheEntry).ExpiresAt));
            return keys;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public int TimeToLive(string key)
        {
            var result = (CacheEntry)_cache.Get(_cacheSleeve.AddPrefix(key));
            if (result == null || result.ExpiresAt == null)
                return -1;
            return (int)(result.ExpiresAt.Value - DateTime.UtcNow).TotalSeconds;
        }

        /// <summary>
        /// Shared insert for public wrappers.
        /// </summary>
        /// <param name="key">The key of the item to insert.</param>
        /// <param name="entry">The internal CacheEntry object to insert.</param>
        /// <param name="parentKey">The key of the item that this item is a child of.</param>
        private bool InternalSet(string key, CacheEntry entry, string parentKey = null)
        {
            CacheDependency cacheDependency = null;
            if (!string.IsNullOrWhiteSpace(parentKey))
                cacheDependency = new CacheDependency(null, new[] { _cacheSleeve.AddPrefix(parentKey) });
            try
            {
                if (entry.ExpiresAt == null)
                    _cache.Insert(_cacheSleeve.AddPrefix(key), entry, cacheDependency);
                else
                    _cache.Insert(_cacheSleeve.AddPrefix(key), entry, cacheDependency, entry.ExpiresAt.Value, Cache.NoSlidingExpiration);
                if (_cacheSleeve.Debug)
                    Trace.WriteLine(string.Format("CS HttpContext: Set cache item with key {0}", key));
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