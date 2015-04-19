namespace CacheSleeve
{
    public interface IObjectSerializer
    {
        T DeserializeObject<T>(string serializedObj);
        string SerializeObject<T>(object obj);
    }
}