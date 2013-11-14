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
        private readonly CacheSleeve _cacheSleeve;

        public RedisCacher()
        {
            _cacheSleeve = CacheSleeve.Manager;
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

        public IDictionary<string, T> GetAll<T>(IEnumerable<string> keys = null)
        {
            if (keys == null)
                using (var conn = new RedisConnection(_cacheSleeve.RedisHost, _cacheSleeve.RedisPort, -1, _cacheSleeve.RedisPassword))
                {
                    conn.Open();
                    keys = conn.Keys.Find(Db, _cacheSleeve.AddPrefix("*")).Result;
                    keys = keys.Select(k => _cacheSleeve.StripPrefix(k));
                }
            
            byte[][] keyValues;
            var keysArray = keys.Select(k => _cacheSleeve.AddPrefix(k)).ToArray();
            var results = new Dictionary<string, T>();

            using (var conn = new RedisConnection(_cacheSleeve.RedisHost, _cacheSleeve.RedisPort, -1, _cacheSleeve.RedisPassword))
            {
                conn.Open();
                keyValues = conn.Strings.Get(Db, keysArray).Result;
            }

            var i = 0;
            foreach (var keyValue in keyValues)
            {
                var key = _cacheSleeve.StripPrefix(keysArray[i++]);
                if (keyValue == null)
                    results[key] = default(T);
                else if (typeof(T) == typeof(byte[]))
                    results[key] = (T)(object)keyValue;
                else
                {
                    var keyValueString = GetString(keyValue);
                    if (typeof (T) == typeof (string))
                        results[key] = (T)(object)keyValueString;
                    try
                    {
                        results[key] = JsonConvert.DeserializeObject<T>(keyValueString);
                    }
                    catch (JsonSerializationException) {}
                }
            }
            return results;
        }

        public bool Set<T>(string key, T value)
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

        public bool Set<T>(string key, T value, DateTime expiresAt)
        {
            var result = false;
            if (Set(key, value))
                using (var conn = new RedisConnection(_cacheSleeve.RedisHost, _cacheSleeve.RedisPort, -1, _cacheSleeve.RedisPassword))
                {
                    conn.Open();
                    var seconds = (int) (expiresAt - DateTime.Now).TotalSeconds;
                    result = conn.Keys.Expire(Db, _cacheSleeve.AddPrefix(key), seconds).Result;
                }
            return result;
        }

        public bool Set<T>(string key, T value, TimeSpan expiresIn)
        {
            var result = false;
            if (Set(key, value))
                using (var conn = new RedisConnection(_cacheSleeve.RedisHost, _cacheSleeve.RedisPort, -1, _cacheSleeve.RedisPassword))
                {
                    conn.Open();
                    result = conn.Keys.Expire(Db, _cacheSleeve.AddPrefix(key), (int)expiresIn.TotalSeconds).Result;
                }
            return result;
        }

        public void SetAll<T>(IDictionary<string, T> values)
        {
            var valBytes = new Dictionary<string, byte[]>();
            foreach (var item in values)
            {
                if (typeof(T) != typeof(byte[]))
                {
                    var json = JsonConvert.SerializeObject(item.Value);
                    valBytes.Add(_cacheSleeve.AddPrefix(item.Key), json != null ? GetBytes(json) : new byte[] {});
                }
                else
                    valBytes.Add(_cacheSleeve.AddPrefix(item.Key), (byte[])(object)item.Value ?? new byte[] { });
            }
            using (var conn = new RedisConnection(_cacheSleeve.RedisHost, _cacheSleeve.RedisPort, -1, _cacheSleeve.RedisPassword))
            {
                conn.Open();
                conn.Strings.Set(Db, valBytes);
            }
        }

        public bool Remove(string key)
        {
            using (var conn = new RedisConnection(_cacheSleeve.RedisHost, _cacheSleeve.RedisPort, -1, _cacheSleeve.RedisPassword))
            {
                conn.Open();
                return conn.Keys.Remove(Db, _cacheSleeve.AddPrefix(key)).Result;
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
        /// Converts a string to a byte[].
        /// </summary>
        /// <param name="str">The string to convert.</param>
        /// <returns>The resulting byte[].</returns>
        private static byte[] GetBytes(string str)
        {
            var bytes = new byte[str.Length * sizeof(char)];
            Buffer.BlockCopy(str.ToCharArray(), 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private static string GetString(byte[] bytes)
        {
            var chars = new char[bytes.Length / sizeof(char)];
            Buffer.BlockCopy(bytes, 0, chars, 0, bytes.Length);
            return new string(chars);
        }
    }
}