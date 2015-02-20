using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CacheSleeve.Models;

namespace CacheSleeve
{
    public class HybridCacher : ICacher, IAsyncCacher
    {
        private readonly CacheManager _cacheSleeve;
        private readonly RedisCacher _remoteCacher;
        private readonly HttpContextCacher _localCacher;

        public HybridCacher()
        {
            _cacheSleeve = CacheManager.Settings;

            _remoteCacher = _cacheSleeve.RemoteCacher;
            _localCacher = _cacheSleeve.LocalCacher;
        }


        public T Get<T>(string key)
        {
            var result = _localCacher.Get<T>(key);
            if (result != null)
                return result;
            result = _remoteCacher.Get<T>(key);
            if (result != null)
            {
                var ttl = (int) _remoteCacher.TimeToLive(key);
                var parentKey = _remoteCacher.Get<string>(key + ".parent");
                if (ttl > -1)
                    _localCacher.Set(key, result, TimeSpan.FromSeconds(ttl), _cacheSleeve.StripPrefix(parentKey));
                else
                    _localCacher.Set(key, result, _cacheSleeve.StripPrefix(parentKey));
                result = _localCacher.Get<T>(key);
            }
            return result;
        }

        public bool Set<T>(string key, T value, string parentKey = null)
        {
            try
            {
                _remoteCacher.Set(key, value, parentKey);
                _remoteCacher.PublishToKey("cacheSleeve.remove." + key, key);
                return true;
            }
            catch (Exception)
            {
                _localCacher.Remove(key);
                _remoteCacher.Remove(key);
                return false;
            }
        }

        public bool Set<T>(string key, T value, DateTime expiresAt, string parentKey = null)
        {
            try
            {
                _remoteCacher.Set(key, value, expiresAt, parentKey);
                _remoteCacher.PublishToKey("cacheSleeve.remove." + key, key);
                return true;
            }
            catch (Exception)
            {
                _localCacher.Remove(key);
                _remoteCacher.Remove(key);
                return false;
            }
        }

        public bool Set<T>(string key, T value, TimeSpan expiresIn, string parentKey = null)
        {
            try
            {
                _remoteCacher.Set(key, value, expiresIn, parentKey);
                _remoteCacher.PublishToKey("cacheSleeve.remove." + key, key);
                return true;
            }
            catch (Exception)
            {
                _localCacher.Remove(key);
                _remoteCacher.Remove(key);
                return false;
            }
        }

        public bool Remove(string key)
        {
            try
            {
                _remoteCacher.Remove(key);
                _remoteCacher.PublishToKey("cacheSleeve.remove." + key, key);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void FlushAll()
        {
            _remoteCacher.FlushAll();
            _remoteCacher.PublishToKey("cacheSleeve.flush", "");
        }

        public IEnumerable<Key> GetAllKeys()
        {
            var keys = _remoteCacher.GetAllKeys();
            keys = keys.Union(_localCacher.GetAllKeys());
            return keys.GroupBy(k => k.KeyName).Select(grp => grp.First());
        }

        public async Task<T> GetAsync<T>(string key)
        {
            var result = _localCacher.Get<T>(key);
            if (result != null)
                return result;
            result = await _remoteCacher.GetAsync<T>(key);
            if (result != null)
            {
                var ttl = (int)(await _remoteCacher.TimeToLiveAsync(key));
                var parentKey = await _remoteCacher.GetAsync<string>(key + ".parent");
                if (ttl > -1)
                    _localCacher.Set(key, result, TimeSpan.FromSeconds(ttl), _cacheSleeve.StripPrefix(parentKey));
                else
                    _localCacher.Set(key, result, _cacheSleeve.StripPrefix(parentKey));
                result = _localCacher.Get<T>(key);
            }
            return result;
        }

        public async Task<bool> SetAsync<T>(string key, T value, string parentKey = null)
        {
            try
            {
                await _remoteCacher.SetAsync(key, value, parentKey);
                await _remoteCacher.PublishToKeyAsync("cacheSleeve.remove." + key, key);
                return true;
            }
            catch (Exception)
            {
                _localCacher.Remove(key);
                _remoteCacher.RemoveAsync(key); // this might be a really bad idea
                return false;
            }
        }

        public async Task<bool> SetAsync<T>(string key, T value, DateTime expiresAt, string parentKey = null)
        {
            try
            {
                await _remoteCacher.SetAsync(key, value, expiresAt, parentKey);
                await _remoteCacher.PublishToKeyAsync("cacheSleeve.remove." + key, key);
                return true;
            }
            catch (Exception)
            {
                _localCacher.Remove(key);
                _remoteCacher.RemoveAsync(key); // this might be a really bad idea
                return false;
            }
        }

        public async Task<bool> SetAsync<T>(string key, T value, TimeSpan expiresIn, string parentKey = null)
        {
            try
            {
                await _remoteCacher.SetAsync(key, value, expiresIn, parentKey);
                await _remoteCacher.PublishToKeyAsync("cacheSleeve.remove." + key, key);
                return true;
            }
            catch (Exception)
            {
                _localCacher.Remove(key);
                _remoteCacher.RemoveAsync(key); // this might be a really bad idea
                return false;
            }
        }

        public async Task<bool> RemoveAsync(string key)
        {
            try
            {
                await _remoteCacher.RemoveAsync(key);
                await _remoteCacher.PublishToKeyAsync("cacheSleeve.remove." + key, key);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task FlushAllAsync()
        {
            await _remoteCacher.FlushAllAsync();
            await _remoteCacher.PublishToKeyAsync("cacheSleeve.flush", "");
        }

        public async Task<IEnumerable<Key>> GetAllKeysAsync()
        {
            var keys = await _remoteCacher.GetAllKeysAsync();
            keys = keys.Union(_localCacher.GetAllKeys());
            return keys.GroupBy(k => k.KeyName).Select(grp => grp.First());
        }
    }
}