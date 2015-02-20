using System.Collections;
using System.Collections.Generic;

namespace CacheSleeve.Models
{
    public class Overview
    {
        public IEnumerable<Key> RemoteKeys { get; set; }
        public IEnumerable<Key> LocalKeys { get; set; }
    }
}