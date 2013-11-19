using System;

namespace CacheSleeve
{
    public class HybridCacher : ICacher
    {
        private readonly RedisCacher _remoteCacher;
        private readonly HttpContextCacher _localCacher;

        public HybridCacher()
        {
            var cacheSleeve = Manager.Settings;

            _remoteCacher = cacheSleeve.RemoteCacher;
            _localCacher = cacheSleeve.LocalCacher;
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
                if (ttl > -1)
                    _localCacher.Set(key, result, TimeSpan.FromSeconds(ttl));
                else 
                    _localCacher.Set(key, result);
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
    }
}