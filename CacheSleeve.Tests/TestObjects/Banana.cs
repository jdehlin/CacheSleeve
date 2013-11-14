namespace CacheSleeve.Tests.TestObjects
{
    public class Banana
    {
        public Banana(int length, string color)
        {
            Length = length;
            Color = color;
        }

        public int Length { get; set; }
        public string Color { get; set; }
    }
}