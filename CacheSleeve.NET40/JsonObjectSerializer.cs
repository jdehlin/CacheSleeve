using Newtonsoft.Json;

namespace CacheSleeve
{
    public class JsonObjectSerializer : IObjectSerializer
    {
        private readonly JsonSerializerSettings _jsonSettings;

        public JsonObjectSerializer()
        {
            _jsonSettings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Objects
            };
        }

        public T DeserializeObject<T>(string serializedObj)
        {
            return JsonConvert.DeserializeObject<T>(serializedObj, _jsonSettings);
        }

        public string SerializeObject<T>(object obj)
        {
            return JsonConvert.SerializeObject(obj, _jsonSettings);
        }
    }
}