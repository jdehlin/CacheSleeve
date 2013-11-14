using System.Collections.Generic;

namespace CacheSleeve.Tests.TestObjects
{
    public class Monkey
    {
        public Monkey(string name)
        {
            Name = name;
        }

        public string Name { get; set; }
        public List<Banana> Bananas { get; set; } 
    }
}