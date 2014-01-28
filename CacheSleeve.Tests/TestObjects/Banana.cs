namespace CacheSleeve.Tests.TestObjects
{
    public class Banana : Fruit
    {
        public Banana(int length, string color)
        {
            Length = length;
            Color = color;
        }

        public int Length { get; set; }
    }
}