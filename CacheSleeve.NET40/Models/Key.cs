using System;

namespace CacheSleeve.Models
{
    public class Key
    {
        public Key(string keyName, DateTime? expirationDate = null)
        {
            KeyName = keyName;
            ExpirationDate = expirationDate;
        }


        public string KeyName { get; private set; }
        public DateTime? ExpirationDate { get; private set; }
    }
}